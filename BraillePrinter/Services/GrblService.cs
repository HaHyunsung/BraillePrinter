using System.IO.Ports;
using System.Text.RegularExpressions;
using BraillePrinter.Models;

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

        public event Action<string>? LineReceived;     // 명령 응답 (SendLine/SendQuery)
        public event Action<string>? LineSent;         // 명령 전송
        public event Action<string>? PollReceived;     // 폴링 상태 응답 (? 쿼리)
        public event Action<GrblState>? StateChanged;
        public event Action<string>? ErrorOccurred;
        public event Action<int>? AlarmOccurred;   // arg = alarm code (0 = unknown)
        public event Action<bool, bool>? HomePinsChanged;  // (xTriggered, yTriggered)

        public bool HomePinX { get; private set; }
        public bool HomePinY { get; private set; }

        public int AlarmCode { get; private set; }

        private static readonly Regex StateRegex =
            new(@"<(\w+)(?::[\d])?[|][MW]Pos:([\-\d.,]+)", RegexOptions.Compiled);

        private static readonly Regex PinRegex =
            new(@"\|Pn:([XYZPDHS]+)", RegexOptions.Compiled);

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

                // Arduino DTR 리셋 후 GRBL 부팅 대기 (최대 2초)
                for (int i = 0; i < 8; i++)
                {
                    Thread.Sleep(250);
                    if (_port.BytesToRead > 0) break;
                }
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

        // Sends a command and reads all lines until "ok" or "error" — logs each via LineReceived
        public List<string>? SendQuery(string cmd)
        {
            lock (_portLock)
            {
                if (_port is not { IsOpen: true }) return null;

                try
                {
                    _port.WriteLine(cmd);
                    LineSent?.Invoke(cmd);

                    var lines = new List<string>();
                    while (true)
                    {
                        string line = _port.ReadLine().Trim();
                        LineReceived?.Invoke(line);
                        TryParseAlarmLine(line);
                        lines.Add(line);
                        if (line == "ok" || line.StartsWith("error")) break;
                    }
                    return lines;
                }
                catch (TimeoutException)
                {
                    ErrorOccurred?.Invoke($"응답 타임아웃: {cmd}");
                    return null;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"송신 오류: {ex.Message}");
                    return null;
                }
            }
        }

        // 실시간 명령(0x18 리셋, 0x21 홀드, 0x7E 사이클스타트, 0x85 조그취소, 0x3F 상태)은
        // _portLock을 잡지 않고 BaseStream에 직접 1바이트를 쓴다.
        // 작업 전송 루프가 SendLine의 ReadLine에서 ok를 기다리며 _portLock을 쥔 채 블로킹 중이어도
        // 일시정지·재개·정지가 즉시 GRBL에 전달되어야 하기 때문. (SerialPort는 읽기/쓰기 동시 수행 안전)
        public void SendRealtimeCommand(byte cmd)
        {
            var port = _port;
            if (port is not { IsOpen: true }) return;
            try
            {
                port.BaseStream.WriteByte(cmd);
                LineSent?.Invoke($"[RT] 0x{cmd:X2}");
            }
            catch { }
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
                        PollReceived?.Invoke(response);
                    }
                    else
                    {
                        // 폴링 중 예상치 못한 줄 (알람 메시지 등) → 명령 로그로
                        LineReceived?.Invoke(response);
                        TryParseAlarmLine(response);
                    }

                    return response;
                }
                catch (TimeoutException)
                {
                    PollReceived?.Invoke("[TIMEOUT] GRBL 응답 없음");
                    return null;
                }
                catch { return null; }
            }
        }

        private void ParseStatusResponse(string response)
        {
            var match = StateRegex.Match(response);
            if (!match.Success) return;

            MachinePosition = match.Groups[2].Value;

            // Parse Pn: pin state field
            var pinMatch = PinRegex.Match(response);
            string pins = pinMatch.Success ? pinMatch.Groups[1].Value : "";
            bool newX = pins.Contains('X');
            bool newY = pins.Contains('Y');
            if (newX != HomePinX || newY != HomePinY)
            {
                HomePinX = newX;
                HomePinY = newY;
                HomePinsChanged?.Invoke(newX, newY);
            }

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
                await Task.Delay(2000);   // GRBL 재부팅 대기
            }

            StopPolling();
            try
            {
                // 수신 버퍼에 남은 폴링 응답 제거
                lock (_portLock)
                {
                    if (_port is { IsOpen: true })
                        _port.DiscardInBuffer();
                }

                // $H 전송 후 ok/error/ALARM 까지 대기 (최대 120초)
                string? result = await Task.Run(() =>
                {
                    lock (_portLock)
                    {
                        if (_port is not { IsOpen: true }) return null;
                        try
                        {
                            int prevTimeout = _port.ReadTimeout;
                            _port.ReadTimeout = 120_000;
                            _port.WriteLine("$H");
                            LineSent?.Invoke("$H");

                            while (true)
                            {
                                string line = _port.ReadLine().Trim();
                                if (string.IsNullOrEmpty(line)) continue;
                                LineReceived?.Invoke(line);
                                TryParseAlarmLine(line);
                                if (line == "ok" || line.StartsWith("error") || line.StartsWith("ALARM"))
                                {
                                    _port.ReadTimeout = prevTimeout;
                                    return line;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ErrorOccurred?.Invoke($"홈잉 오류: {ex.Message}");
                            return null;
                        }
                    }
                });

                if (result == null)                   { ErrorOccurred?.Invoke("홈잉 응답 없음"); return false; }
                if (result.StartsWith("error"))       { ErrorOccurred?.Invoke($"홈잉 오류: {result}"); return false; }
                if (result.StartsWith("ALARM"))       { ErrorOccurred?.Invoke($"홈잉 실패: {result}"); return false; }

                // result == "ok" → 홈잉 완료, 현재 위치(용지 우측 상단 코너)를 작업 원점으로 설정 후
                // 옵셋 지점(용지 코너, 여백 제외)으로 이동해 대기. 인쇄 시작 시 G-Code가 여백만큼 이동한다.
                await Task.Run(() =>
                {
                    SendLine("G10 L20 P1 X0 Y0");
                    var ic = System.Globalization.CultureInfo.InvariantCulture;
                    string px = GCodeGenerator.ParkX.ToString("F3", ic);
                    string py = GCodeGenerator.ParkY.ToString("F3", ic);
                    SendLine($"G90 G0 X{px} Y{py}");
                });
                IsHomed = true;
                UpdateState(GrblState.Idle);
                return true;
            }
            finally
            {
                StartPolling();
            }
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

            // 작업 중에는 M3/M5 동기화·긴 급속이동·드웰 때문에 ok 응답이 늦게 올 수 있으므로
            // 평소 2초 타임아웃을 크게 올린다 (홈잉과 동일한 방식). finally에서 원복.
            int prevReadTimeout;
            lock (_portLock)
            {
                prevReadTimeout = _port?.ReadTimeout ?? 2000;
                if (_port is { IsOpen: true }) _port.ReadTimeout = 60_000;
            }

            try
            {
                return await Task.Run(() =>
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
                        if (token.IsCancellationRequested) break;
                        Thread.Sleep(200);
                        QueryStatus();
                        if (State == GrblState.Idle) break;
                    }

                    return State == GrblState.Idle;
                }, token);
            }
            finally
            {
                lock (_portLock)
                {
                    if (_port is { IsOpen: true }) _port.ReadTimeout = prevReadTimeout;
                }
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

        // ── GRBL Settings ─────────────────────────────────────────────────

        // Sends $0~$1 and $100~$131 machine parameters from BrailleParameters to GRBL EEPROM.
        // Waits for GRBL to finish booting (leave Unknown state) before writing.
        public async Task<bool> ApplySettingsAsync(BrailleParameters p)
        {
            if (!IsConnected)
            {
                ErrorOccurred?.Invoke("설정 전송 실패: GRBL 미연결");
                return false;
            }

            // Wait up to 10 seconds for GRBL to leave Unknown state.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (State == GrblState.Unknown && DateTime.UtcNow < deadline)
                await Task.Delay(200);

            if (!IsConnected || State == GrblState.Unknown)
            {
                ErrorOccurred?.Invoke("설정 전송 실패: GRBL 초기화 타임아웃 (10초)");
                return false;
            }

            // Alarm 상태여도 $ 설정 명령은 수락됨 — 그대로 진행


            StopPolling();
            try
            {
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                string[] cmds =
                [
                    "$10=0",                                    // status report: WPos
                    $"$0={p.StepPulseUs}",
                    $"$1={p.StepIdleDelayMs}",
                    $"$3={p.DirectionInvert}",
                    $"$5={p.LimitPinsInvert}",
                    $"$21={p.HardLimitsEnable}",
                    $"$22={p.HomingEnable}",
                    $"$23={p.HomingDirMask}",
                    $"$24={p.HomingFeedRate.ToString("F0", ic)}",
                    $"$25={p.HomingSeekRate.ToString("F0", ic)}",
                    $"$26={p.HomingDebounce}",
                    $"$27={p.HomingPullOff.ToString("F1", ic)}",
                    $"$100={p.StepsPerMmX.ToString("F6", ic)}",
                    $"$101={p.StepsPerMmY.ToString("F6", ic)}",
                    $"$110={p.MaxRateX.ToString("F0", ic)}",
                    $"$111={p.MaxRateY.ToString("F0", ic)}",
                    $"$120={p.AccelerationX.ToString("F0", ic)}",
                    $"$121={p.AccelerationY.ToString("F0", ic)}",
                    $"$130={p.MaxTravelX.ToString("F0", ic)}",
                    $"$131={p.MaxTravelY.ToString("F0", ic)}",
                ];

                foreach (string cmd in cmds)
                {
                    string? resp = await Task.Run(() => SendLine(cmd));
                    if (resp == null || resp.StartsWith("error"))
                    {
                        ErrorOccurred?.Invoke($"설정 실패 [{cmd}]: {resp ?? "응답 없음"}");
                        return false;
                    }
                }

                LineReceived?.Invoke("[설정 완료] GRBL 파라미터가 EEPROM에 저장되었습니다.");
                return true;
            }
            finally
            {
                StartPolling();
            }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
