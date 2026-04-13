namespace BraillePrinter.Models
{
    /// <summary>
    /// 점자 셀 하나를 나타냅니다.
    ///
    /// 6비트 패턴 인코딩:
    ///   bit0 = 점1 (좌상)   bit3 = 점4 (우상)
    ///   bit1 = 점2 (좌중)   bit4 = 점5 (우중)
    ///   bit2 = 점3 (좌하)   bit5 = 점6 (우하)
    ///
    /// 물리 배치:
    ///   점1 ● ● 점4
    ///   점2 ● ● 점5
    ///   점3 ● ● 점6
    /// </summary>
    public class BrailleCell
    {
        /// <summary>6비트 점자 패턴</summary>
        public byte DotPattern { get; set; }

        /// <summary>페이지 내 열 인덱스 (0-based)</summary>
        public int Column { get; set; }

        /// <summary>페이지 내 행 인덱스 (0-based)</summary>
        public int Row { get; set; }

        /// <summary>지정한 점 번호(1~6)가 활성화되어 있는지 반환합니다.</summary>
        public bool HasDot(int dotNumber) =>
            dotNumber is >= 1 and <= 6 &&
            (DotPattern & (1 << (dotNumber - 1))) != 0;

        public bool IsEmpty => DotPattern == 0;
    }
}
