namespace BraillePrinter.Models
{
    /// <summary>
    /// 점자 점 하나의 물리적 좌표입니다 (단위: mm, 용지 좌상단 기준).
    ///
    /// 하드웨어(G-code/스테퍼 모터)에서 직접 사용할 수 있도록
    /// 모든 계산을 마친 절대 좌표로 저장합니다.
    /// </summary>
    public class DotCoordinate
    {
        /// <summary>X 좌표 (mm) — 용지 좌측 기준, 오른쪽 방향 양수</summary>
        public double X { get; }

        /// <summary>Y 좌표 (mm) — 용지 상단 기준, 아래쪽 방향 양수</summary>
        public double Y { get; }

        /// <summary>소속 셀의 열 인덱스</summary>
        public int CellColumn { get; }

        /// <summary>소속 셀의 행 인덱스</summary>
        public int CellRow { get; }

        /// <summary>셀 내 점 번호 (1~6)</summary>
        public int DotNumber { get; }

        public DotCoordinate(double x, double y, int cellColumn, int cellRow, int dotNumber)
        {
            X          = x;
            Y          = y;
            CellColumn = cellColumn;
            CellRow    = cellRow;
            DotNumber  = dotNumber;
        }

        public override string ToString() =>
            $"({X:F2}, {Y:F2}) [셀 {CellColumn},{CellRow} 점{DotNumber}]";
    }
}
