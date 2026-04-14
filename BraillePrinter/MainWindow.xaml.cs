using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BraillePrinter.Managers;
using BraillePrinter.Models;
using BraillePrinter.Views;

namespace BraillePrinter
{
    public partial class MainWindow : Window
    {
        // 텍스트 변경 후 자동 변환 딜레이 (ms)
        private const int AutoConvertDelayMs = 500;

        private readonly DispatcherTimer _convertTimer;

        public MainWindow()
        {
            InitializeComponent();

            // 자동 변환 타이머 설정
            _convertTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AutoConvertDelayMs)
            };
            _convertTimer.Tick += (_, _) =>
            {
                _convertTimer.Stop();
                RunConvert();
            };

            // 파라미터 변경 시 자동 재변환
            ParameterManager.Instance.ParametersChanged += OnParametersChanged;

            // 점자 변환 완료 시 캔버스 갱신
            BrailleManager.Instance.BrailleUpdated += OnBrailleUpdated;

            // 초기 캔버스 크기 및 상태바 표시
            ApplyCanvasSize();
            UpdateStatusBar();
        }

        // ── 툴바 이벤트 ──────────────────────────────────────────────────

        private void BtnConvert_Click(object sender, RoutedEventArgs e)
        {
            _convertTimer.Stop();
            RunConvert();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            InputTextBox.Clear();
            BrailleCanvas.Children.Clear();
            UpdateInfoPanel(0, 0, 0, 0);
            StatusMessage.Text = "지워졌습니다.";
        }

        private void BtnParam_Click(object sender, RoutedEventArgs e)
        {
            var win = new ParameterWindow { Owner = this };
            win.ShowDialog();
            // 저장 시 ParameterManager.ParametersChanged 이벤트로 자동 처리
        }

        // ── 텍스트 입력 이벤트 ───────────────────────────────────────────

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _convertTimer.Stop();
            _convertTimer.Start();

            int len = InputTextBox.Text.Length;
            UpdateInfoPanel(len, 0, 0, 0);
        }

        // ── 변환 및 렌더링 ────────────────────────────────────────────────

        private void RunConvert()
        {
            string text = InputTextBox.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                BrailleCanvas.Children.Clear();
                UpdateInfoPanel(0, 0, 0, 0);
                StatusMessage.Text = "텍스트를 입력하세요.";
                return;
            }

            StatusMessage.Text = "변환 중...";
            BrailleManager.Instance.Convert(text);
            // 렌더링은 BrailleUpdated 이벤트에서 처리
        }

        private void OnBrailleUpdated()
        {
            // BrailleManager 이벤트는 비-UI 스레드에서도 올 수 있으므로 Dispatcher 사용
            Dispatcher.InvokeAsync(RenderBraille);
        }

        private void OnParametersChanged()
        {
            Dispatcher.InvokeAsync(() =>
            {
                ApplyCanvasSize();
                RunConvert();
                UpdateStatusBar();
            });
        }

        // ── 캔버스 렌더링 ────────────────────────────────────────────────

        private void RenderBraille()
        {
            BrailleCanvas.Children.Clear();

            var manager = BrailleManager.Instance;
            var p       = ParameterManager.Instance.Parameters;
            double scale = p.DisplayScale;

            // 점 크기: 실물 지름 1.5~1.6mm → 반지름 = DotSpacing * 0.28 (화면 가독성 확보)
            double dotRadius = Math.Max(p.DotSpacing * 0.28 * scale, 2.5);

            // 셀 가이드 그리기 (옅은 격자)
            DrawCellGuides(p, scale);

            // 점 그리기
            foreach (var dot in manager.CurrentDotCoordinates)
            {
                double cx = dot.X * scale;
                double cy = dot.Y * scale;

                var ellipse = new Ellipse
                {
                    Width  = dotRadius * 2,
                    Height = dotRadius * 2,
                    Fill   = Brushes.DimGray,
                };

                Canvas.SetLeft(ellipse, cx - dotRadius);
                Canvas.SetTop(ellipse, cy - dotRadius);
                BrailleCanvas.Children.Add(ellipse);
            }

            // 정보 패널 갱신
            int usedLines = manager.CurrentCells.Count > 0
                ? manager.CurrentCells.Max(c => c.Row) + 1
                : 0;

            UpdateInfoPanel(
                charCount: InputTextBox.Text.Length,
                cellCount: manager.CurrentCells.Count,
                dotCount:  manager.CurrentDotCoordinates.Count,
                lineCount: usedLines);

            // liblouis 사용 중 변환 실패 시 오류 표시
            if (manager.ActiveConverter is Converters.LibLouisConverter llc && llc.LastError != null)
                StatusMessage.Text = $"[liblouis 오류] {llc.LastError}";
            else
                StatusMessage.Text = $"변환 완료 [{manager.ActiveConverter.Name}] — {manager.CurrentCells.Count}셀, {manager.CurrentDotCoordinates.Count}점";
        }

        private void DrawCellGuides(BrailleParameters p, double scale)
        {
            var guideBrush = new SolidColorBrush(Color.FromArgb(30, 0, 100, 200));
            int cellsPerLine = p.MaxCellsPerLine;
            int maxLines     = p.MaxLines;

            for (int row = 0; row < maxLines; row++)
            {
                for (int col = 0; col < cellsPerLine; col++)
                {
                    double x = p.MarginLeft  * scale + col * p.CellSpacing * scale;
                    double y = p.MarginTop   * scale + row * p.LineSpacing * scale;

                    // 셀 영역 표시 (너비: dotSpacing, 높이: dotSpacing*2)
                    var rect = new Rectangle
                    {
                        Width           = p.DotSpacing * scale * 2 + p.DotSpacing * scale * 0.2,
                        Height          = p.DotSpacing * scale * 2,
                        Stroke          = guideBrush,
                        StrokeThickness = 0.5,
                        Fill            = Brushes.Transparent,
                    };

                    Canvas.SetLeft(rect, x - p.DotSpacing * scale * 0.1);
                    Canvas.SetTop(rect, y);
                    BrailleCanvas.Children.Add(rect);
                }
            }
        }

        // ── 캔버스 크기 적용 ─────────────────────────────────────────────

        private void ApplyCanvasSize()
        {
            var p = ParameterManager.Instance.Parameters;
            double w = p.PaperWidth  * p.DisplayScale;
            double h = p.PaperHeight * p.DisplayScale;

            BrailleCanvas.Width  = w;
            BrailleCanvas.Height = h;
        }

        // ── 정보 패널 갱신 ────────────────────────────────────────────────

        private void UpdateInfoPanel(int charCount, int cellCount, int dotCount, int lineCount)
        {
            var p = ParameterManager.Instance.Parameters;

            InfoCharCount.Text = charCount.ToString();
            InfoCellCount.Text = cellCount.ToString();
            InfoDotCount.Text  = dotCount.ToString();
            InfoLineCount.Text = lineCount.ToString();
            InfoCapacity.Text  = $"{cellCount} / {p.TotalCapacity}";

            if (cellCount > p.TotalCapacity && p.TotalCapacity > 0)
            {
                InfoCapacity.Foreground = Brushes.Red;
                StatusMessage.Text = $"⚠ 셀 용량 초과! ({cellCount}/{p.TotalCapacity})";
            }
            else
            {
                InfoCapacity.Foreground = Brushes.DimGray;
            }
        }

        private void UpdateStatusBar()
        {
            var p = ParameterManager.Instance.Parameters;

            // ActiveConverter는 Convert() 호출 후에만 갱신되므로
            // 상태바는 파라미터 설정값 + DLL 가용성으로 직접 판단한다
            string engineLabel;
            if (p.ConverterType == Models.ConverterType.LibLouis)
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
                $"엔진: {engineLabel}  │  " +
                $"점간: {p.DotSpacing}mm  자간: {p.CellSpacing}mm  줄간: {p.LineSpacing}mm  " +
                $"용지: {p.PaperWidth}×{p.PaperHeight}mm  한 줄 {p.MaxCellsPerLine}셀";
        }
    }
}
