using System.Windows;
using System.Windows.Media;
using BraillePrinter.Converters;
using BraillePrinter.Managers;
using BraillePrinter.Models;
using static BraillePrinter.Models.PrintMode;

namespace BraillePrinter.Views
{
    public partial class ParameterWindow : Window
    {
        public ParameterWindow()
        {
            InitializeComponent();
            LoadFromManager();
            WireTextChangedHandlers();
            RefreshLibLouisStatus();
        }

        // ── 초기화 ───────────────────────────────────────────────────────

        private void LoadFromManager() => LoadParameters(ParameterManager.Instance.Parameters);

        private void LoadParameters(BrailleParameters p)
        {
            TbDotSpacing.Text   = p.DotSpacing.ToString("F2");
            TbCellSpacing.Text  = p.CellSpacing.ToString("F2");
            TbLineSpacing.Text  = p.LineSpacing.ToString("F2");
            TbMarginLeft.Text   = p.MarginLeft.ToString("F2");
            TbMarginTop.Text    = p.MarginTop.ToString("F2");
            TbMarginRight.Text  = p.MarginRight.ToString("F2");
            TbMarginBottom.Text = p.MarginBottom.ToString("F2");
            TbPaperWidth.Text   = p.PaperWidth.ToString("F2");
            TbPaperHeight.Text  = p.PaperHeight.ToString("F2");
            TbDisplayScale.Text  = p.DisplayScale.ToString("F2");
            TbFeedRate.Text      = p.GCodeFeedRate.ToString("F0");
            TbPunchDwell.Text    = p.PunchDwellSeconds.ToString("F2");
            TbRetractDwell.Text  = p.RetractDwellSeconds.ToString("F2");

            // 연속 스캔 파라미터
            TbScanFeedRate.Text  = p.ScanFeedRate.ToString("F0");
            TbScanM3Offset.Text  = p.ScanM3OffsetMm.ToString("F3");
            TbScanM5Offset.Text  = p.ScanM5OffsetMm.ToString("F3");

            // 기계 원점 오프셋
            TbOriginOffsetX.Text = p.OriginOffsetX.ToString("F3");
            TbOriginOffsetY.Text = p.OriginOffsetY.ToString("F3");

            // 솔레노이드
            ChkSolenoidInvert.IsChecked = p.SolenoidInvert;

            // 출력 모드
            RbStopAndPunch.IsChecked   = p.PrintMode == PrintMode.StopAndPunch;
            RbContinuousScan.IsChecked = p.PrintMode == PrintMode.ContinuousScan;

            // 엔진 선택
            RbManual.IsChecked   = p.ConverterType == ConverterType.Manual;
            RbLibLouis.IsChecked = p.ConverterType == ConverterType.LibLouis;

            // liblouis 테이블 콤보박스
            SelectLibLouisTableItem(p.LibLouisTable);

            UpdateCalcFields(p);
            UpdateScanPanelState();
            ClearError();
        }

        private void SelectLibLouisTableItem(string tableName)
        {
            foreach (System.Windows.Controls.ComboBoxItem item in CbLibLouisTable.Items)
            {
                if (item.Tag?.ToString() == tableName)
                {
                    CbLibLouisTable.SelectedItem = item;
                    return;
                }
            }
            CbLibLouisTable.SelectedIndex = 0; // 기본값: ko-g2.ctb
        }

        private void WireTextChangedHandlers()
        {
            foreach (var tb in new[]
            {
                TbDotSpacing, TbCellSpacing, TbLineSpacing,
                TbMarginLeft, TbMarginTop, TbMarginRight, TbMarginBottom,
                TbPaperWidth, TbPaperHeight, TbDisplayScale,
                TbFeedRate, TbPunchDwell, TbRetractDwell,
                TbScanFeedRate, TbScanM3Offset, TbScanM5Offset,
                TbOriginOffsetX, TbOriginOffsetY
            })
            {
                tb.TextChanged += (_, _) =>
                {
                    if (TryBuildParameters(out var preview))
                        UpdateCalcFields(preview);
                };
            }
        }

        private void UpdateCalcFields(BrailleParameters p)
        {
            TbCalcCellsPerLine.Text = p.MaxCellsPerLine.ToString();
            TbCalcMaxLines.Text     = p.MaxLines.ToString();
            TbCalcTotalCells.Text   = p.TotalCapacity.ToString();
        }

        // ── liblouis 상태 UI ─────────────────────────────────────────────

        private void RefreshLibLouisStatus()
        {
            bool available = LibLouisConverter.Instance.IsAvailable;

            if (available)
            {
                TbLibLouisStatus.Text       = "✔ 사용 가능";
                TbLibLouisStatus.Foreground  = Brushes.Green;
                PanelLibLouisGuide.Visibility = Visibility.Collapsed;
            }
            else
            {
                TbLibLouisStatus.Text        = "✘ DLL 없음 — Manual로 자동 대체";
                TbLibLouisStatus.Foreground   = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
                PanelLibLouisGuide.Visibility = Visibility.Visible;
            }

            // 테이블 콤보박스 / 안내 패널은 liblouis 선택 시에만 활성
            bool libLouisSelected = RbLibLouis.IsChecked == true;
            PanelLibLouisTable.IsEnabled = libLouisSelected && available;
        }

        // ── 출력 모드 라디오 버튼 ─────────────────────────────────────────

        private void PrintModeRadio_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelScanSettings == null) return;
            UpdateScanPanelState();
        }

        private void UpdateScanPanelState()
        {
            bool scan = RbContinuousScan?.IsChecked == true;
            PanelScanSettings.IsEnabled = scan;
            PanelScanSettings.Opacity   = scan ? 1.0 : 0.45;
        }

        // ── 엔진 라디오 버튼 ────────────────────────────────────────────

        private void EngineRadio_Changed(object sender, RoutedEventArgs e)
        {
            // 초기화 전에 호출될 수 있으므로 null 체크
            if (PanelLibLouisTable == null) return;
            RefreshLibLouisStatus();
        }

        // ── 버튼 이벤트 ──────────────────────────────────────────────────

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBuildParameters(out var newParams))
                return;

            ParameterManager.Instance.UpdateParameters(newParams);
            DialogResult = true;
            Close();
        }

        private void BtnDefault_Click(object sender, RoutedEventArgs e)
        {
            LoadParameters(new BrailleParameters());
            RefreshLibLouisStatus();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ── 파라미터 파싱 ────────────────────────────────────────────────

        private bool TryBuildParameters(out BrailleParameters p)
        {
            p = new BrailleParameters();
            var errors = new List<string>();

            double dotSpacing        = ParsePositive(TbDotSpacing.Text,       "점간 거리",              errors);
            double cellSpacing       = ParsePositive(TbCellSpacing.Text,      "자간 거리",              errors);
            double lineSpacing       = ParsePositive(TbLineSpacing.Text,      "줄간 거리",              errors);
            double marginLeft        = ParseNonNeg(TbMarginLeft.Text,         "좌측 여백",              errors);
            double marginTop         = ParseNonNeg(TbMarginTop.Text,          "상단 여백",              errors);
            double marginRight       = ParseNonNeg(TbMarginRight.Text,        "우측 여백",              errors);
            double marginBottom      = ParseNonNeg(TbMarginBottom.Text,       "하단 여백",              errors);
            double paperWidth        = ParsePositive(TbPaperWidth.Text,       "용지 너비",              errors);
            double paperHeight       = ParsePositive(TbPaperHeight.Text,      "용지 높이",              errors);
            double displayScale      = ParsePositive(TbDisplayScale.Text,     "표시 배율",              errors);
            double feedRate          = ParsePositive(TbFeedRate.Text,         "급속 이동 속도",          errors);
            double punchDwell        = ParsePositive(TbPunchDwell.Text,       "핀 내려찍기 대기",        errors);
            double retractDwell      = ParseNonNeg(TbRetractDwell.Text,       "핀 복귀 대기",            errors);
            double scanFeedRate  = ParsePositive(TbScanFeedRate.Text,  "스캔 이동 속도",    errors);
            double scanM3Offset  = ParseNonNeg(TbScanM3Offset.Text,   "M3 선발사 Offset", errors);
            double scanM5Offset  = ParseNonNeg(TbScanM5Offset.Text,   "M5 후발사 Offset", errors);
            double originOffsetX = ParseAny(TbOriginOffsetX.Text, "X 오프셋", errors);
            double originOffsetY = ParseAny(TbOriginOffsetY.Text, "Y 오프셋", errors);
            bool solenoidInvert  = ChkSolenoidInvert.IsChecked == true;

            if (errors.Count > 0) { ShowError(string.Join("\n", errors)); return false; }

            if (marginLeft + marginRight >= paperWidth)
            {
                ShowError("좌우 여백의 합이 용지 너비보다 크거나 같습니다.");
                return false;
            }
            if (marginTop + marginBottom >= paperHeight)
            {
                ShowError("상하 여백의 합이 용지 높이보다 크거나 같습니다.");
                return false;
            }

            // 출력 모드
            var printMode = RbContinuousScan.IsChecked == true
                ? PrintMode.ContinuousScan
                : PrintMode.StopAndPunch;

            // 엔진 선택
            var converterType = RbLibLouis.IsChecked == true
                ? ConverterType.LibLouis
                : ConverterType.Manual;

            // liblouis 테이블
            string libLouisTable = "ko-g2.ctb";
            if (CbLibLouisTable.SelectedItem is System.Windows.Controls.ComboBoxItem selected
                && selected.Tag is string tag)
                libLouisTable = tag;

            p = new BrailleParameters
            {
                DotSpacing              = dotSpacing,
                CellSpacing             = cellSpacing,
                LineSpacing             = lineSpacing,
                MarginLeft              = marginLeft,
                MarginTop               = marginTop,
                MarginRight             = marginRight,
                MarginBottom            = marginBottom,
                PaperWidth              = paperWidth,
                PaperHeight             = paperHeight,
                DisplayScale            = displayScale,
                PrintMode               = printMode,
                GCodeFeedRate           = feedRate,
                PunchDwellSeconds       = punchDwell,
                RetractDwellSeconds     = retractDwell,
                ScanFeedRate    = scanFeedRate,
                ScanM3OffsetMm  = scanM3Offset,
                ScanM5OffsetMm  = scanM5Offset,
                OriginOffsetX   = originOffsetX,
                OriginOffsetY   = originOffsetY,
                SolenoidInvert  = solenoidInvert,
                ConverterType           = converterType,
                LibLouisTable           = libLouisTable,
            };

            ClearError();
            return true;
        }

        private static double ParsePositive(string text, string name, List<string> errors)
        {
            if (double.TryParse(text, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double v)
                && v > 0) return v;
            errors.Add($"'{name}'에 양수 숫자를 입력하세요.");
            return 0;
        }

        private static double ParseAny(string text, string name, List<string> errors)
        {
            if (double.TryParse(text, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double v)) return v;
            errors.Add($"'{name}'에 숫자를 입력하세요.");
            return 0;
        }

        private static double ParseNonNeg(string text, string name, List<string> errors)
        {
            if (double.TryParse(text, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double v)
                && v >= 0) return v;
            errors.Add($"'{name}'에 0 이상의 숫자를 입력하세요.");
            return 0;
        }

        private void ShowError(string message)
        {
            TbError.Text       = message;
            TbError.Visibility = Visibility.Visible;
        }

        private void ClearError()
        {
            TbError.Text       = string.Empty;
            TbError.Visibility = Visibility.Collapsed;
        }
    }
}
