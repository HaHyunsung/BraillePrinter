using System.Windows;
using System.Windows.Media;
using BraillePrinter.Converters;
using BraillePrinter.Managers;
using BraillePrinter.Models;

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
            TbDisplayScale.Text = p.DisplayScale.ToString("F2");

            // 엔진 선택
            RbManual.IsChecked   = p.ConverterType == ConverterType.Manual;
            RbLibLouis.IsChecked = p.ConverterType == ConverterType.LibLouis;

            // liblouis 테이블 콤보박스
            SelectLibLouisTableItem(p.LibLouisTable);

            UpdateCalcFields(p);
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
                TbPaperWidth, TbPaperHeight, TbDisplayScale
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

            double dotSpacing   = ParsePositive(TbDotSpacing.Text,   "점간 거리",  errors);
            double cellSpacing  = ParsePositive(TbCellSpacing.Text,  "자간 거리",  errors);
            double lineSpacing  = ParsePositive(TbLineSpacing.Text,  "줄간 거리",  errors);
            double marginLeft   = ParseNonNeg(TbMarginLeft.Text,     "좌측 여백",  errors);
            double marginTop    = ParseNonNeg(TbMarginTop.Text,      "상단 여백",  errors);
            double marginRight  = ParseNonNeg(TbMarginRight.Text,    "우측 여백",  errors);
            double marginBottom = ParseNonNeg(TbMarginBottom.Text,   "하단 여백",  errors);
            double paperWidth   = ParsePositive(TbPaperWidth.Text,   "용지 너비",  errors);
            double paperHeight  = ParsePositive(TbPaperHeight.Text,  "용지 높이",  errors);
            double displayScale = ParsePositive(TbDisplayScale.Text, "표시 배율",  errors);

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
                DotSpacing    = dotSpacing,
                CellSpacing   = cellSpacing,
                LineSpacing   = lineSpacing,
                MarginLeft    = marginLeft,
                MarginTop     = marginTop,
                MarginRight   = marginRight,
                MarginBottom  = marginBottom,
                PaperWidth    = paperWidth,
                PaperHeight   = paperHeight,
                DisplayScale  = displayScale,
                ConverterType = converterType,
                LibLouisTable = libLouisTable,
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
