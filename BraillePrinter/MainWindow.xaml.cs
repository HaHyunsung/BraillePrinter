using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BraillePrinter.Managers;
using BraillePrinter.Models;
using BraillePrinter.Services;
using BraillePrinter.Views;

namespace BraillePrinter
{
    public partial class MainWindow : Window
    {
        private const int AutoConvertDelayMs = 500;

        private readonly DispatcherTimer _convertTimer;
        private readonly GrblService _grbl = GrblService.Instance;
        private List<string> _currentGCode = new();
        private bool _isReverting;
        private bool _alarmDialogOpen;   // 다이얼로그 중복 오픈 방지

        public MainWindow()
        {
            InitializeComponent();

            _convertTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AutoConvertDelayMs)
            };
            _convertTimer.Tick += (_, _) =>
            {
                _convertTimer.Stop();
                RunConvert();
            };

            ParameterManager.Instance.ParametersChanged += OnParametersChanged;
            BrailleManager.Instance.BrailleUpdated += OnBrailleUpdated;

            _grbl.StateChanged += OnGrblStateChanged;
            _grbl.LineReceived += OnGrblLineReceived;
            _grbl.LineSent += OnGrblLineSent;
            _grbl.PollReceived += OnGrblPollReceived;
            _grbl.ErrorOccurred += OnGrblError;
            _grbl.AlarmOccurred += OnGrblAlarm;

            ApplyCanvasSize();
            UpdateStatusBar();
            UpdatePinButtonLabels();
            RefreshPorts();
        }

        // ── 툴바 이벤트 ──────────────────────────────────────

        private void BtnConvert_Click(object sender, RoutedEventArgs e)
        {
            _convertTimer.Stop();
            RunConvert();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _isReverting = true;
            InputTextBox.Clear();
            _isReverting = false;
            BrailleCanvas.Children.Clear();
            PathCanvas.Children.Clear();
            TxtGCode.Clear();
            TxtCoordinates.Clear();
            _currentGCode.Clear();
            UpdateInfoPanel(0, 0, 0, 0);
            StatusMessage.Text = "지워졌습니다.";
        }

        private void BtnParam_Click(object sender, RoutedEventArgs e)
        {
            var win = new ParameterWindow { Owner = this };
            win.ShowDialog();
        }

        private LogWindow? _logWindow;

        private void BtnLog_Click(object sender, RoutedEventArgs e)
        {
            if (_logWindow != null && _logWindow.IsLoaded)
            {
                _logWindow.Activate();
                return;
            }

            _logWindow = new LogWindow { Owner = this };
            _logWindow.Show();
        }

        // ── 텍스트 입력 ──────────────────────────────────────

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isReverting) return;

            _convertTimer.Stop();
            _convertTimer.Start();

            int len = InputTextBox.Text.Length;
            UpdateInfoPanel(len, 0, 0, 0);
        }

        // ── 변환 및 렌더링 ───────────────────────────────────

        private void RunConvert()
        {
            string text = InputTextBox.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                BrailleCanvas.Children.Clear();
                PathCanvas.Children.Clear();
                TxtGCode.Clear();
                TxtCoordinates.Clear();
                _currentGCode.Clear();
                UpdateInfoPanel(0, 0, 0, 0);
                StatusMessage.Text = "텍스트를 입력하세요.";
                return;
            }

            StatusMessage.Text = "변환 중...";
            BrailleManager.Instance.Convert(text);
        }

        private void OnBrailleUpdated()
        {
            Dispatcher.InvokeAsync(RenderBraille);
        }

        private void OnParametersChanged()
        {
            Dispatcher.InvokeAsync(() =>
            {
                ApplyCanvasSize();
                RunConvert();
                UpdateStatusBar();
                UpdatePinButtonLabels();
            });
        }

        private void UpdatePinButtonLabels()
        {
            bool inv = ParameterManager.Instance.Parameters.SolenoidInvert;
            string onCmd  = inv ? "M5" : "M3";
            string offCmd = inv ? "M3" : "M5";

            BtnPinDown.Content = $"▼  핀 다운 ({onCmd})";
            BtnPinUp.Content   = $"▲  핀 업 ({offCmd})";
            TxtPinHint.Text    = inv
                ? $"※ {onCmd} = 솔레노이드 ON (펀칭), {offCmd} = 솔레노이드 OFF (복귀)  [반전 모드]"
                : $"※ {onCmd} = 솔레노이드 ON (펀칭), {offCmd} = 솔레노이드 OFF (복귀)";
        }

        // ── 캔버스 렌더링 ────────────────────────────────────

        private void RenderBraille()
        {
            var manager = BrailleManager.Instance;

            if (manager.IsOverflow)
            {
                string truncated = FindMaxFitText(InputTextBox.Text);

                _isReverting = true;
                InputTextBox.Text = truncated;
                InputTextBox.CaretIndex = truncated.Length;
                _isReverting = false;

                StatusMessage.Text = $"용지가 꽉 찼습니다. 초과 내용을 잘라냈습니다 ({truncated.Length}자).";

                BrailleManager.Instance.Convert(truncated);
                return;
            }

            BrailleCanvas.Children.Clear();

            var p = ParameterManager.Instance.Parameters;
            double scale = p.DisplayScale;

            double dotRadius = Math.Max(0.75 * scale, 2.0);

            DrawCellGuides(p, scale);

            foreach (var dot in manager.CurrentDotCoordinates)
            {
                double cx = dot.X * scale;
                double cy = dot.Y * scale;

                var ellipse = new Ellipse
                {
                    Width = dotRadius * 2,
                    Height = dotRadius * 2,
                    Fill = Brushes.DimGray,
                };

                Canvas.SetLeft(ellipse, cx - dotRadius);
                Canvas.SetTop(ellipse, cy - dotRadius);
                BrailleCanvas.Children.Add(ellipse);
            }

            _currentGCode = GCodeGenerator.Generate(manager.CurrentDotCoordinates);
            TxtGCode.Text = GCodeGenerator.GenerateText(manager.CurrentDotCoordinates);
            TxtCoordinates.Text = GCodeGenerator.FormatCoordinateTable(manager.CurrentDotCoordinates);

            RenderPathPreview(manager.CurrentDotCoordinates, p, scale, dotRadius);

            int usedLines = manager.CurrentCells.Count > 0
                ? manager.CurrentCells.Max(c => c.Row) + 1
                : 0;

            UpdateInfoPanel(
                charCount: InputTextBox.Text.Length,
                cellCount: manager.CurrentCells.Count,
                dotCount: manager.CurrentDotCoordinates.Count,
                lineCount: usedLines);

            if (manager.ActiveConverter is Converters.LibLouisConverter llc && llc.LastError != null)
                StatusMessage.Text = $"[liblouis 오류] {llc.LastError}";
            else
                StatusMessage.Text = $"변환 완료 [{manager.ActiveConverter.Name}] — {manager.CurrentCells.Count}셀, {manager.CurrentDotCoordinates.Count}점, G-Code {_currentGCode.Count}줄";

            UpdatePrintButtonState();
        }

        private void DrawCellGuides(BrailleParameters p, double scale, Canvas? target = null)
        {
            target ??= BrailleCanvas;

            var guideBrush = new SolidColorBrush(Color.FromArgb(25, 0, 100, 200));
            var dotAreaFill = new SolidColorBrush(Color.FromArgb(4, 0, 80, 200));
            var dotAreaStroke = new SolidColorBrush(Color.FromArgb(18, 0, 100, 200));
            int cellsPerLine = p.MaxCellsPerLine;
            int maxLines = p.MaxLines;

            double padX = (p.CellSpacing - p.DotSpacing) / 2.0;
            double padY = (p.LineSpacing - p.DotSpacing * 2) / 2.0;

            for (int row = 0; row < maxLines; row++)
            {
                for (int col = 0; col < cellsPerLine; col++)
                {
                    double x = (p.EffectiveMarginLeft + col * p.CellSpacing) * scale;
                    double y = (p.EffectiveMarginTop + row * p.LineSpacing) * scale;

                    var cellRect = new Rectangle
                    {
                        Width = p.CellSpacing * scale,
                        Height = p.LineSpacing * scale,
                        Stroke = guideBrush,
                        StrokeThickness = 0.5,
                        Fill = Brushes.Transparent,
                    };
                    Canvas.SetLeft(cellRect, x);
                    Canvas.SetTop(cellRect, y);
                    target.Children.Add(cellRect);

                    var dotAreaRect = new Rectangle
                    {
                        Width = p.DotSpacing * scale,
                        Height = p.DotSpacing * 2 * scale,
                        Stroke = dotAreaStroke,
                        StrokeThickness = 0.5,
                        StrokeDashArray = new DoubleCollection { 2, 3 },
                        Fill = dotAreaFill,
                    };
                    Canvas.SetLeft(dotAreaRect, x + padX * scale);
                    Canvas.SetTop(dotAreaRect, y + padY * scale);
                    target.Children.Add(dotAreaRect);
                }
            }
        }

        private void RenderPathPreview(IReadOnlyList<DotCoordinate> dots,
                                       BrailleParameters p, double scale, double dotRadius)
        {
            PathCanvas.Children.Clear();
            PathCanvas.Width = p.PaperWidth * scale;
            PathCanvas.Height = p.PaperHeight * scale;

            DrawCellGuides(p, scale, PathCanvas);

            foreach (var dot in dots)
            {
                var ellipse = new Ellipse
                {
                    Width = dotRadius * 2,
                    Height = dotRadius * 2,
                    Fill = Brushes.DimGray,
                };
                Canvas.SetLeft(ellipse, dot.X * scale - dotRadius);
                Canvas.SetTop(ellipse, dot.Y * scale - dotRadius);
                PathCanvas.Children.Add(ellipse);
            }

            var zigzag = GCodeGenerator.BuildZigzagOrder(dots);
            if (zigzag.Count == 0) return;

            var pathBrush = new SolidColorBrush(Color.FromArgb(140, 220, 50, 50));

            double prevX = zigzag[0].X * scale;
            double prevY = zigzag[0].Y * scale;

            for (int i = 1; i < zigzag.Count; i++)
            {
                double cx = zigzag[i].X * scale;
                double cy = zigzag[i].Y * scale;

                PathCanvas.Children.Add(new Line
                {
                    X1 = prevX, Y1 = prevY,
                    X2 = cx, Y2 = cy,
                    Stroke = pathBrush,
                    StrokeThickness = 1.0,
                });

                prevX = cx;
                prevY = cy;
            }
        }

        private static string FindMaxFitText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (!BrailleManager.Instance.TestOverflow(text)) return text;

            int lo = 0, hi = text.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (BrailleManager.Instance.TestOverflow(text[..mid]))
                    hi = mid - 1;
                else
                    lo = mid;
            }
            return text[..lo];
        }

        // ── 확대/축소 ────────────────────────────────────────

        private void OnPreviewZoom(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (!System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftCtrl) &&
                !System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.RightCtrl))
                return;

            e.Handled = true;

            double delta = e.Delta > 0 ? 1.15 : 1.0 / 1.15;

            if (sender == BrailleScrollViewer)
                ApplyZoom(BrailleScale, delta);
            else if (sender == PathScrollViewer)
                ApplyZoom(PathScale, delta);
        }

        private static void ApplyZoom(ScaleTransform scale, double factor)
        {
            const double min = 0.3, max = 5.0;
            double next = scale.ScaleX * factor;
            next = Math.Max(min, Math.Min(max, next));
            scale.ScaleX = next;
            scale.ScaleY = next;
        }

        // ── 캔버스 크기 ──────────────────────────────────────

        private void ApplyCanvasSize()
        {
            var p = ParameterManager.Instance.Parameters;
            double w = p.PaperWidth * p.DisplayScale;
            double h = p.PaperHeight * p.DisplayScale;

            BrailleCanvas.Width = w;
            BrailleCanvas.Height = h;
        }

        // ── 정보 패널 ────────────────────────────────────────

        private void UpdateInfoPanel(int charCount, int cellCount, int dotCount, int lineCount)
        {
            var p = ParameterManager.Instance.Parameters;

            InfoCharCount.Text = charCount.ToString();
            InfoCellCount.Text = cellCount.ToString();
            InfoDotCount.Text = dotCount.ToString();
            InfoLineCount.Text = lineCount.ToString();
            InfoCapacity.Text = $"{cellCount} / {p.TotalCapacity}";
            InfoGCodeLines.Text = _currentGCode.Count.ToString();

            if (cellCount > p.TotalCapacity && p.TotalCapacity > 0)
            {
                InfoCapacity.Foreground = Brushes.Red;
                StatusMessage.Text = $"셀 용량 초과! ({cellCount}/{p.TotalCapacity})";
            }
            else
            {
                InfoCapacity.Foreground = Brushes.DimGray;
            }
        }

        private void UpdateStatusBar()
        {
            var p = ParameterManager.Instance.Parameters;

            string engineLabel;
            if (p.ConverterType == ConverterType.LibLouis)
            {
                engineLabel = Converters.LibLouisConverter.Instance.IsAvailable
                    ? $"liblouis [{p.LibLouisTable}]"
                    : "Manual (liblouis DLL 없음)";
            }
            else
            {
                engineLabel = "Manual";
            }

            StatusParams.Text =
                $"엔진: {engineLabel}  |  " +
                $"점간: {p.DotSpacing}mm  자간: {p.CellSpacing}mm  줄간: {p.LineSpacing}mm  " +
                $"용지: {p.PaperWidth}x{p.PaperHeight}mm  한 줄 {p.MaxCellsPerLine}셀";
        }

        // ── 탭 복사 버튼 ─────────────────────────────────────

        private void BtnCopyGCode_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtGCode.Text))
            {
                Clipboard.SetText(TxtGCode.Text);
                StatusMessage.Text = "G-Code가 클립보드에 복사되었습니다.";
            }
        }

        private void BtnCopyCoords_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtCoordinates.Text))
            {
                Clipboard.SetText(TxtCoordinates.Text);
                StatusMessage.Text = "좌표 테이블이 클립보드에 복사되었습니다.";
            }
        }

        private void BtnClearSerialLog_Click(object sender, RoutedEventArgs e) => TxtSerialLog.Clear();

        // ══════════════════════════════════════════════════════
        //  시리얼 통신 / GRBL
        // ══════════════════════════════════════════════════════

        private void RefreshPorts()
        {
            CmbPort.Items.Clear();
            foreach (var port in GrblService.GetAvailablePorts())
                CmbPort.Items.Add(port);
            if (CmbPort.Items.Count > 0)
                CmbPort.SelectedIndex = 0;
        }

        private void BtnRefreshPorts_Click(object sender, RoutedEventArgs e)
        {
            RefreshPorts();
        }

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_grbl.IsConnected)
            {
                _grbl.Disconnect();
                BtnConnect.Content = "연결";
                BtnConnect.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#388E3C"));
                StatusMessage.Text = "연결 해제됨";
                return;
            }

            if (CmbPort.SelectedItem is not string portName)
            {
                StatusMessage.Text = "COM 포트를 선택하세요.";
                return;
            }

            StatusMessage.Text = $"{portName} 연결 중...";

            if (_grbl.Connect(portName))
            {
                BtnConnect.Content = "연결 해제";
                BtnConnect.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#757575"));
                StatusMessage.Text = $"{portName} 연결됨 — HOME을 실행하세요.";
            }
        }

        private void OnGrblStateChanged(GrblState state)
        {
            Dispatcher.InvokeAsync(() =>
            {
                TxtGrblState.Text = state.ToString();

                StateIndicator.Background = state switch
                {
                    GrblState.Idle => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#388E3C")),
                    GrblState.Run  => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1565C0")),
                    GrblState.Hold => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F57C00")),
                    GrblState.Alarm => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D32F2F")),
                    GrblState.Home => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7B1FA2")),
                    _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9E9E9E")),
                };

                string[] parts = _grbl.MachinePosition.Split(',');
                if (parts.Length >= 2)
                    TxtMachinePos.Text = $"X:{parts[0]}  Y:{parts[1]}";

                BtnHome.IsEnabled = _grbl.IsConnected && state != GrblState.Run && state != GrblState.Hold;
                TxtHomedStatus.Text = _grbl.IsHomed ? "[HOME 완료]" : "[HOME 필요]";
                TxtHomedStatus.Foreground = _grbl.IsHomed
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#388E3C"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D32F2F"));

                BtnPause.IsEnabled  = _grbl.IsConnected && state == GrblState.Run;
                BtnResume.IsEnabled = _grbl.IsConnected && state == GrblState.Hold;

                UpdatePrintButtonState();
            });
        }

        private void UpdatePrintButtonState()
        {
            bool jobIdle = _grbl.State != GrblState.Run && _grbl.State != GrblState.Hold;
            BtnPrint.IsEnabled = _grbl.IsConnected
                                 && _grbl.IsHomed
                                 && _grbl.State == GrblState.Idle
                                 && _currentGCode.Count > 0;

            if (jobIdle)
            {
                BtnPause.IsEnabled  = false;
                BtnResume.IsEnabled = false;
            }
        }

        private void OnGrblLineReceived(string line)
        {
            Dispatcher.InvokeAsync(() =>
            {
                AppendSerialLog($"<< {line}");
            });
        }

        private void OnGrblLineSent(string line)
        {
            Dispatcher.InvokeAsync(() =>
            {
                AppendSerialLog($">> {line}");
            });
        }

        private void OnGrblError(string msg)
        {
            Dispatcher.InvokeAsync(() =>
            {
                AppendSerialLog($"[ERROR] {msg}");
                StatusMessage.Text = msg;
            });
        }

        private void OnGrblAlarm(int alarmCode)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                AppendSerialLog($"[ALARM] code={alarmCode}");
                StatusMessage.Text = alarmCode > 0
                    ? $"ALARM:{alarmCode} 발생 — 다이얼로그를 확인하세요."
                    : "ALARM 상태 감지 — 다이얼로그를 확인하세요.";

                // 진행 중 작업 즉시 중단 및 토글 갱신
                BtnPause.IsEnabled = false;
                BtnResume.IsEnabled = false;
                _grbl.CancelJob();   // jobCts cancel + soft reset (이미 알람이면 즉시 반환)

                // 중복 오픈 방지
                if (_alarmDialogOpen) return;
                _alarmDialogOpen = true;

                try
                {
                    var dlg = new AlarmDialog(alarmCode) { Owner = this };
                    dlg.ShowDialog();

                    if (dlg.ShouldRunHome)
                    {
                        // 다이얼로그에서 Soft Reset은 이미 전송됨 → 약간 대기 후 Home 실행
                        await Task.Delay(1000);
                        BtnHome.IsEnabled = false;
                        BtnPrint.IsEnabled = false;
                        StatusMessage.Text = "리셋 후 홈잉 진행 중...";

                        bool ok = await _grbl.HomeAsync();
                        StatusMessage.Text = ok ? "홈잉 완료 — 인쇄 준비됨" : "홈잉 실패";

                        BtnHome.IsEnabled = _grbl.IsConnected;
                        UpdatePrintButtonState();
                    }
                }
                finally
                {
                    _alarmDialogOpen = false;
                }
            });
        }

        private void AppendSerialLog(string text)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {text}\n";
            TxtSerialLog.AppendText(line);
            TxtSerialLog.ScrollToEnd();
            TxtSerialLog2.AppendText(line);
            TxtSerialLog2.ScrollToEnd();
        }

        private void AppendPollLog(string text)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {text}\n";
            TxtPollLog.AppendText(line);
            TxtPollLog.ScrollToEnd();
            TxtPollLog2.AppendText(line);
            TxtPollLog2.ScrollToEnd();
        }

        private void OnGrblPollReceived(string line)
        {
            Dispatcher.InvokeAsync(() => AppendPollLog(line));
        }

        private void BtnClearSerialLog2_Click(object sender, RoutedEventArgs e) => TxtSerialLog2.Clear();
        private void BtnClearPollLog_Click(object sender, RoutedEventArgs e)    => TxtPollLog.Clear();
        private void BtnClearPollLog2_Click(object sender, RoutedEventArgs e)   => TxtPollLog2.Clear();

        // ── Home ─────────────────────────────────────────────

        private async void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            BtnHome.IsEnabled = false;
            BtnPrint.IsEnabled = false;
            StatusMessage.Text = "홈잉 중... (리밋 스위치 탐색)";

            bool success = await _grbl.HomeAsync();

            if (success)
            {
                StatusMessage.Text = "홈잉 완료 — 인쇄 준비됨";
            }
            else
            {
                StatusMessage.Text = "홈잉 실패 — 상태를 확인하세요.";
            }

            BtnHome.IsEnabled = _grbl.IsConnected;
            UpdatePrintButtonState();
        }

        // ── Print ────────────────────────────────────────────

        private async void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGCode.Count == 0)
            {
                StatusMessage.Text = "변환된 G-Code가 없습니다.";
                return;
            }

            var result = MessageBox.Show(
                $"총 {_currentGCode.Count}줄의 G-Code를 전송합니다.\n" +
                $"펀칭 점 수: {BrailleManager.Instance.CurrentDotCoordinates.Count}\n\n" +
                "인쇄를 시작할까요?",
                "인쇄 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            BtnPrint.IsEnabled   = false;
            BtnHome.IsEnabled    = false;
            BtnStop.IsEnabled    = true;
            BtnPause.IsEnabled   = true;
            BtnResume.IsEnabled  = false;
            BtnConvert.IsEnabled = false;

            JobProgress.Visibility = Visibility.Visible;
            JobProgress.Maximum = _currentGCode.Count;
            JobProgress.Value = 0;

            var progress = new Progress<int>(lineNum =>
            {
                JobProgress.Value = lineNum;
                TxtJobProgress.Text = $"{lineNum}/{_currentGCode.Count}";
            });

            StatusMessage.Text = "인쇄 중...";

            bool success = await _grbl.RunJobAsync(_currentGCode, progress);

            JobProgress.Visibility = Visibility.Collapsed;
            TxtJobProgress.Text   = "";
            BtnStop.IsEnabled     = false;
            BtnPause.IsEnabled    = false;
            BtnResume.IsEnabled   = false;
            BtnConvert.IsEnabled  = true;
            BtnHome.IsEnabled     = _grbl.IsConnected;

            StatusMessage.Text = success
                ? "인쇄 완료!"
                : "인쇄 중단됨 — 상태를 확인하세요.";

            UpdatePrintButtonState();
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            _grbl.FeedHold();
            StatusMessage.Text = "일시정지 명령 전송됨 (Feed Hold)";
        }

        private void BtnResume_Click(object sender, RoutedEventArgs e)
        {
            _grbl.CycleStart();
            StatusMessage.Text = "재개 명령 전송됨 (Cycle Start)";
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _grbl.CancelJob();
            BtnStop.IsEnabled   = false;
            BtnPause.IsEnabled  = false;
            BtnResume.IsEnabled = false;
            StatusMessage.Text  = "정지 명령 전송됨 (Soft Reset)";
        }

        // ── Jog 컨트롤 ───────────────────────────────────────

        private double GetJogStep()
        {
            foreach (var child in JogStepGroup.Children)
            {
                if (child is RadioButton rb && rb.IsChecked == true && rb.Tag is string tag
                    && double.TryParse(tag, System.Globalization.NumberStyles.Any,
                                       System.Globalization.CultureInfo.InvariantCulture,
                                       out double v))
                    return v;
            }
            return 1.0;
        }

        private double GetJogFeed()
        {
            if (double.TryParse(TbJogFeed.Text, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out double v) && v > 0)
                return v;
            return 1500;
        }

        private void DoJog(string axis, int sign)
        {
            if (!_grbl.IsConnected)
            {
                StatusMessage.Text = "GRBL 미연결 상태입니다.";
                return;
            }
            if (_grbl.State == GrblState.Run || _grbl.State == GrblState.Hold)
            {
                StatusMessage.Text = "작업 중에는 Jog 할 수 없습니다.";
                return;
            }

            double step = GetJogStep() * sign;
            double feed = GetJogFeed();
            string? resp = _grbl.Jog(axis, step, feed);

            if (resp == null)
                StatusMessage.Text = "Jog 명령 응답 없음";
            else if (resp.StartsWith("error"))
                StatusMessage.Text = $"Jog 오류: {resp}";
            else
                StatusMessage.Text = $"Jog: {axis}{(step >= 0 ? "+" : "")}{step:F2}mm @ F{feed:F0}";
        }

        private void BtnJogXPlus_Click(object sender, RoutedEventArgs e)   => DoJog("X", +1);
        private void BtnJogXMinus_Click(object sender, RoutedEventArgs e)  => DoJog("X", -1);
        private void BtnJogYPlus_Click(object sender, RoutedEventArgs e)   => DoJog("Y", +1);
        private void BtnJogYMinus_Click(object sender, RoutedEventArgs e)  => DoJog("Y", -1);

        private async void BtnPinDown_Click(object sender, RoutedEventArgs e)
        {
            if (!_grbl.IsConnected) { StatusMessage.Text = "GRBL 미연결 상태입니다."; return; }
            string cmd = GCodeGenerator.PinOnCmd;
            string? resp = await Task.Run(() => _grbl.SendLine(cmd));
            StatusMessage.Text = resp == null ? $"{cmd} 응답 없음" : $"핀 다운 ({cmd}): {resp}";
        }

        private async void BtnPinUp_Click(object sender, RoutedEventArgs e)
        {
            if (!_grbl.IsConnected) { StatusMessage.Text = "GRBL 미연결 상태입니다."; return; }
            string cmd = GCodeGenerator.PinOffCmd;
            string? resp = await Task.Run(() => _grbl.SendLine(cmd));
            StatusMessage.Text = resp == null ? $"{cmd} 응답 없음" : $"핀 업 ({cmd}): {resp}";
        }

        private void BtnJogCancel_Click(object sender, RoutedEventArgs e)
        {
            _grbl.JogCancel();
            StatusMessage.Text = "Jog 중지 명령 전송됨";
        }

        private void BtnGoOrigin_Click(object sender, RoutedEventArgs e)
        {
            if (!_grbl.IsConnected) { StatusMessage.Text = "GRBL 미연결 상태입니다."; return; }
            if (!_grbl.IsHomed)
            {
                MessageBox.Show("HOME이 완료되지 않은 상태에서는 원점 이동이 위험할 수 있습니다.\n먼저 HOME을 실행하세요.",
                                "원점 이동", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string? resp = _grbl.SendLine("G90 G0 X0 Y0");
            StatusMessage.Text = resp == null ? "응답 없음" : $"원점 이동: {resp}";
        }

        private void BtnKillAlarmLock_Click(object sender, RoutedEventArgs e)
        {
            if (!_grbl.IsConnected) { StatusMessage.Text = "GRBL 미연결 상태입니다."; return; }
            var result = MessageBox.Show(
                "$X 는 홈잉 없이 알람만 해제합니다. 기계 위치가 부정확할 수 있어 충돌 위험이 있습니다.\n계속할까요?",
                "알람 해제", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            string? resp = _grbl.KillAlarmLock();
            StatusMessage.Text = resp == null ? "응답 없음" : $"$X: {resp}";
        }

        // ── GRBL 진단 명령 ───────────────────────────────────

        private async void BtnDiagBuildInfo_Click(object sender, RoutedEventArgs e)
        {
            if (!_grbl.IsConnected) { StatusMessage.Text = "GRBL 미연결 상태입니다."; return; }
            StatusMessage.Text = "$I 전송 중...";
            await Task.Run(() => _grbl.SendQuery("$I"));
            StatusMessage.Text = "$I 완료 — 통신 로그 확인";
        }

        private async void BtnDiagSettings_Click(object sender, RoutedEventArgs e)
        {
            if (!_grbl.IsConnected) { StatusMessage.Text = "GRBL 미연결 상태입니다."; return; }
            StatusMessage.Text = "$$ 전송 중...";
            await Task.Run(() => _grbl.SendQuery("$$"));
            StatusMessage.Text = "$$ 완료 — 통신 로그 확인";
        }

        private async void BtnDiagParserState_Click(object sender, RoutedEventArgs e)
        {
            if (!_grbl.IsConnected) { StatusMessage.Text = "GRBL 미연결 상태입니다."; return; }
            StatusMessage.Text = "$G 전송 중...";
            await Task.Run(() => _grbl.SendQuery("$G"));
            StatusMessage.Text = "$G 완료 — 통신 로그 확인";
        }

        private async void BtnDiagCoords_Click(object sender, RoutedEventArgs e)
        {
            if (!_grbl.IsConnected) { StatusMessage.Text = "GRBL 미연결 상태입니다."; return; }
            StatusMessage.Text = "$# 전송 중...";
            await Task.Run(() => _grbl.SendQuery("$#"));
            StatusMessage.Text = "$# 완료 — 통신 로그 확인";
        }

        private void BtnDiagSoftReset_Click(object sender, RoutedEventArgs e)
        {
            if (!_grbl.IsConnected) { StatusMessage.Text = "GRBL 미연결 상태입니다."; return; }
            _grbl.SoftReset();
            StatusMessage.Text = "소프트 리셋 전송됨 (Ctrl-X)";
        }

        // ── Window close cleanup ─────────────────────────────

        protected override void OnClosed(EventArgs e)
        {
            _grbl.Dispose();
            base.OnClosed(e);
        }
    }
}
