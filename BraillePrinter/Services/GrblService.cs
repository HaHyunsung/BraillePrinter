using System.IO.Ports;
using System.Text.RegularExpressions;

namespace BraillePrinter.Services
{
    public enum GrblState
    {
        Unknown, Idle, Run, Hold, Jog, Alarm, Home, Check, Door, Sleep, Disconnected
    }

    public sealed class GrblService : IDisposable
    {
        public static readonly GrblService Instance = new();

        private SerialPort? _port;
        private readonly object _portLock = new();
        private CancellationTokenSource? _pollCts;
        private CancellationTokenSource? _jobCts;

        public GrblState State { get; private set; } = GrblState.Disconnected;
        public string MachinePosition { get; private set; } = "0,0,0";
        public bool IsConnected => _port is { IsOpen: true };
        public bool IsHomed { get; private set; }

        public event Action<string>? LineReceived;
        public event Action<string>? LineSent;
        public event Action<GrblState>? StateChanged;
        public event Action<string>? ErrorOccurred;
        public event Action<int>? AlarmOccurred;   // arg = alarm code (0 = unknown)

        public int AlarmCode { get; private set; }

        private static readonly Regex StateRegex =
            new(@"<(\w+)(?::[\d])?[|]MPos:([\-\d.,]+)", RegexOptions.Compiled);

        private static readonly Regex AlarmLineRegex =
            new(@"ALARM:(\d+)", RegexOptions.Compiled);

        private GrblService() { }

        public static string[] GetAvailablePorts() => SerialPort.GetPortNames();

        public bool Connect(string portName)
        {
            Disconnect();

            try
            {
                _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
                {
                    NewLine = "\n",
                    ReadTimeout = 2000,
                    WriteTimeout = 2000,
                    DtrEnable = true
                };
                _port.Open();

                Thread.Sleep(500);
                if (_port.BytesToRead > 0)
                {
                    string greeting = _port.ReadExisting();
                    LineReceived?.Invoke(greeting.Trim());
                    foreach (var greetLine in greeting.Split('\n'))
                        TryParseAlarmLine(greetLine.Trim());
                }

                AlarmCode = 0;
                UpdateState(GrblState.Unknown);
                IsHomed = false;
                StartPolling();
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"연결 실패: {ex.Message}");
                _port?.Dispose();
                _port = null;
                UpdateState(GrblState.Disconnected);
                return false;
            }
        }

        public void Disconnect()
        {
            StopPolling();
            _jobCts?.Cancel();

            lock (_portLock)
            {
                if (_port is { IsOpen: true })
                {
                    try { _port.Close(); } catch { }
                }
                _port?.Dispose();
                _port = null;
            }

            IsHomed = false;
            AlarmCode = 0;
            UpdateState(GrblState.Disconnected);
        }

        public string? SendLine(string line)
        {
            lock (_portLock)
            {
                if (_port is not { IsOpen: true }) return null;

                try
                {
                    _port.WriteLine(line);
                    LineSent?.Invoke(line);

                    string response = _port.ReadLine().Trim();
                    LineReceived?.Invoke(response);
                    TryParseAlarmLine(response);
                    return response;
                }
                catch (TimeoutException)
                {
                    ErrorOccurred?.Invoke($"응답 타임아웃: {line}");
                    return null;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"송신 오류: {ex.Message}");
                    return null;
                }
            }
        }

        public void SendRealtimeCommand(byte cmd)
        {
            lock (_portLock)
            {
                if (_port is not { IsOpen: true }) return;
                try
                {
                    _port.BaseStream.WriteByte(cmd);
                    LineSent?.Invoke($"[RT] 0x{cmd:X2}");
                }
                catch { }
            }
        }

        public string? QueryStatus()
        {
            lock (_portLock)
            {
                if (_port is not { IsOpen: true }) return null;

                try
                {
                    _port.BaseStream.WriteByte(0x3F); // '?'

                    string response = _port.ReadLine().Trim();

                    if (response.StartsWith('<') && response.EndsWith('>'))
                    {
                        ParseStatusResponse(response);
                        LineReceived?.Invoke(response);
                    }
                    else
                    {
                        LineReceived?.Invoke(response);
                        TryParseAlarmLine(response);
                    }

                    return response;
                }
                catch { return null; }
            }
        }

        private void ParseStatusResponse(string response)
        {
            var match = StateRegex.Match(response);
            if (!match.Success) return;

            MachinePosition = match.Groups[2].Value;

            var newState = match.Groups[1].Value switch
            {
                "Idle" => GrblState.Idle,
                "Run" => GrblState.Run,
                "Hold" => GrblState.Hold,
                "Jog" => GrblState.Jog,
                "Alarm" => GrblState.Alarm,
                "Home" => GrblState.Home,
                "Check" => GrblState.Check,
                "Door" => GrblState.Door,
                "Sleep" => GrblState.Sleep,
                _ => GrblState.Unknown
            };

            UpdateState(newState);
        }

        private void UpdateState(GrblState newState)
        {
            if (State == newState) return;

            bool enteringAlarm = newState == GrblState.Alarm;
            State = newState;
            StateChanged?.Invoke(newState);

            if (enteringAlarm)
                AlarmOccurred?.Invoke(AlarmCode);
        }

        // Parse "ALARM:N" lines that GRBL sends proactively (greeting, mid-job)
        private void TryParseAlarmLine(string line)
        {
            var m = AlarmLineRegex.Match(line);
            if (!m.Success) return;

            int code = int.Parse(m.Groups[1].Value);
            AlarmCode = code;

            // If we're already in Alarm state the transition won't re-fire, so fire directly.
            if (State == GrblState.Alarm)
                AlarmOccurred?.Invoke(code);
            else
                UpdateState(GrblState.Alarm); // fires AlarmOccurred inside
        }

        private void StartPolling()
        {
            StopPolling();
            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    QueryStatus();
                    await Task.Delay(300, token).ConfigureAwait(false);
                }
            }, token);
        }

        private void StopPolling()
        {
            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _pollCts = null;
        }

        // ── Homing ─────────────────────────────────────────

        public async Task<bool> HomeAsync()
        {
            if (!IsConnected)
            {
                ErrorOccurred?.Invoke("미연결 상태에서 홈잉 불가");
                return false;
            }

            if (State == GrblState.Alarm)
            {
                SendRealtimeCommand(0x18); // Ctrl-X soft reset
                await Task.Delay(1000);
            }

            string? resp = SendLine("$H");

            if (resp == null)
            {
                ErrorOccurred?.Invoke("홈잉 명령 응답 없음");
                return false;
            }

            if (resp.StartsWith("error"))
            {
                ErrorOccurred?.Invoke($"홈잉 오류: {resp}");
                return false;
            }

            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(200);
                QueryStatus();
                if (State == GrblState.Idle)
                {
                    IsHomed = true;
                    return true;
                }
            }

            ErrorOccurred?.Invoke("홈잉 타임아웃 (20초)");
            return false;
        }

        // ── Job execution (Simple Send-Response) ────────────

        public async Task<bool> RunJobAsync(List<string> gcodeLines, IProgress<int>? progress = null)
        {
            if (!IsConnected || !IsHomed)
            {
                ErrorOccurred?.Invoke("프린트 불가: 연결 또는 홈잉 미완료");
                return false;
            }

            if (State != GrblState.Idle)
            {
                ErrorOccurred?.Invoke($"프린트 불가: 현재 상태 = {State}");
                return false;
            }

            _jobCts = new CancellationTokenSource();
            var token = _jobCts.Token;

            StopPolling();

            try
            {
                for (int i = 0; i < gcodeLines.Count; i++)
                {
                    if (token.IsCancellationRequested) return false;

                    string line = gcodeLines[i].Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith(';')) continue;

                    string? resp = SendLine(line);

                    if (resp == null)
                    {
                        ErrorOccurred?.Invoke($"라인 {i + 1} 응답 없음: {line}");
                        return false;
                    }

                    if (resp.StartsWith("error"))
                    {
                        ErrorOccurred?.Invoke($"라인 {i + 1} 오류: {resp} ({line})");
                        return false;
                    }

                    progress?.Report(i + 1);
                }

                // Wait for machine to reach idle
                for (int i = 0; i < 300; i++)
                {
                    await Task.Delay(200, token);
                    QueryStatus();
                    if (State == GrblState.Idle) break;
                }

                return State == GrblState.Idle;
            }
            finally
            {
                StartPolling();
            }
        }

        public void CancelJob()
        {
            _jobCts?.Cancel();
            SendRealtimeCommand(0x18); // Ctrl-X soft reset
        }

        public void FeedHold() => SendRealtimeCommand(0x21);   // '!'
        public void CycleStart() => SendRealtimeCommand(0x7E); // '~'
        public void SoftReset() => SendRealtimeCommand(0x18);  // Ctrl-X
        public void JogCancel() => SendRealtimeCommand(0x85);  // Jog cancel

        // ── Jog ───────────────────────────────────────────
        // axis: "X" or "Y", distance: signed mm, feed: mm/min
        public string? Jog(string axis, double distance, double feed)
        {
            if (!IsConnected) return null;
            if (State == GrblState.Alarm)
            {
                ErrorOccurred?.Invoke("Alarm 상태에서는 Jog 불가 — 먼저 리셋 & 홈을 실행하세요.");
                return null;
            }

            string d = distance.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            string f = feed.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            // G91 incremental jog: $J=G91 X<d> F<f>
            string cmd = $"$J=G91 {axis}{d} F{f}";
            return SendLine(cmd);
        }

        // Kill alarm lock without rehoming ($X). Use with caution — position becomes unreliable.
        public string? KillAlarmLock() => SendLine("$X");

        public void Dispose()
        {
            Disconnect();
        }
    }
}
