using BraillePrinter.Converters;
using BraillePrinter.Models;

namespace BraillePrinter.Managers
{
    /// <summary>
    /// 점자 변환 및 물리 좌표 계산을 담당하는 싱글톤 매니저.
    ///
    /// 변환 엔진은 ParameterManager.Parameters.ConverterType으로 선택됩니다.
    ///   - ConverterType.Manual   → ManualBrailleConverter (내장 테이블)
    ///   - ConverterType.LibLouis → LibLouisConverter      (liblouis DLL)
    ///
    /// LibLouis가 선택되었지만 DLL이 없으면 자동으로 Manual로 폴백합니다.
    /// CurrentDotCoordinates는 하드웨어(G-code 생성 등)에서 직접 사용 가능합니다.
    /// </summary>
    public sealed class BrailleManager
    {
        // ── 싱글톤 ───────────────────────────────────────────────────────
        public static readonly BrailleManager Instance = new();

        // ── 공개 상태 ─────────────────────────────────────────────────────

        /// <summary>현재 실제로 사용 중인 변환기</summary>
        public IBrailleConverter ActiveConverter { get; private set; }
            = new ManualBrailleConverter();

        /// <summary>마지막으로 변환된 점자 셀 목록</summary>
        public IReadOnlyList<BrailleCell> CurrentCells { get; private set; }
            = Array.Empty<BrailleCell>();

        /// <summary>
        /// 마지막으로 계산된 물리 좌표 목록 (mm, 용지 좌상단 기준).
        /// 하드웨어 제어 시 이 목록을 직접 사용하세요.
        /// </summary>
        public IReadOnlyList<DotCoordinate> CurrentDotCoordinates { get; private set; }
            = Array.Empty<DotCoordinate>();

        /// <summary>변환 완료 시 발생합니다.</summary>
        public event Action? BrailleUpdated;

        private BrailleManager() { }

        // ── 공개 API ─────────────────────────────────────────────────────

        /// <summary>텍스트를 점자로 변환하고 CurrentCells / CurrentDotCoordinates를 갱신합니다.</summary>
        public void Convert(string text)
        {
            // ParameterManager에서 선택된 엔진 결정
            ActiveConverter = ResolveConverter();

            // 1단계: 변환기 → 점자 패턴 플랫 리스트
            var patterns = ActiveConverter.Convert(text);

            // 2단계: 레이아웃 (셀 위치 배정)
            var cells = LayoutCells(patterns);
            CurrentCells = cells.AsReadOnly();

            // 3단계: 물리 좌표 계산
            CurrentDotCoordinates = CalculateCoordinates(cells).AsReadOnly();

            BrailleUpdated?.Invoke();
        }

        // ── 엔진 선택 ────────────────────────────────────────────────────

        private static IBrailleConverter ResolveConverter()
        {
            var p = ParameterManager.Instance.Parameters;

            if (p.ConverterType == ConverterType.LibLouis)
            {
                var ll = LibLouisConverter.Instance;
                ll.TableName = p.LibLouisTable;

                if (ll.IsAvailable)
                    return ll;

                // DLL 없음 → 자동 폴백 (UI에서 별도 경고 표시)
                System.Diagnostics.Debug.WriteLine(
                    $"[BrailleManager] LibLouis 사용 불가, Manual로 대체. " +
                    $"사유: {ll.UnavailableReason}");
            }

            return new ManualBrailleConverter();
        }

        // ── 레이아웃 (패턴 → 셀 위치 배정) ─────────────────────────────

        private static List<BrailleCell> LayoutCells(List<byte> patterns)
        {
            var p            = ParameterManager.Instance.Parameters;
            int cellsPerLine = p.MaxCellsPerLine;
            int col = 0, row = 0;
            var cells = new List<BrailleCell>();

            foreach (byte pattern in patterns)
            {
                if (pattern == 0xFE)            // 줄바꿈
                {
                    col = 0;
                    row++;
                    continue;
                }

                if (pattern == 0x00 && col == 0)   // 줄 시작 공백 무시
                    continue;

                if (col >= cellsPerLine)           // 자동 줄 바꿈
                {
                    col = 0;
                    row++;
                }

                cells.Add(new BrailleCell
                {
                    DotPattern = pattern,
                    Column     = col,
                    Row        = row,
                });
                col++;
            }

            return cells;
        }

        // ── 물리 좌표 계산 ────────────────────────────────────────────────

        private static List<DotCoordinate> CalculateCoordinates(List<BrailleCell> cells)
        {
            var p    = ParameterManager.Instance.Parameters;
            var dots = new List<DotCoordinate>();

            foreach (var cell in cells)
            {
                // 셀 기준점 (점1의 절대 좌표, mm)
                double cellX = p.MarginLeft + cell.Column * p.CellSpacing;
                double cellY = p.MarginTop  + cell.Row    * p.LineSpacing;

                for (int dotNum = 1; dotNum <= 6; dotNum++)
                {
                    if (!cell.HasDot(dotNum)) continue;

                    // 점 배치: 1,2,3=왼쪽열 / 4,5,6=오른쪽열
                    double offsetX = (dotNum <= 3) ? 0.0 : p.DotSpacing;
                    double offsetY = ((dotNum - 1) % 3) * p.DotSpacing;

                    dots.Add(new DotCoordinate(
                        x:          cellX + offsetX,
                        y:          cellY + offsetY,
                        cellColumn: cell.Column,
                        cellRow:    cell.Row,
                        dotNumber:  dotNum));
                }
            }

            return dots;
        }
    }
}
