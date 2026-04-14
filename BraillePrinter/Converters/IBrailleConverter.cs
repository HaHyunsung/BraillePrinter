namespace BraillePrinter.Converters
{
    /// <summary>
    /// 텍스트 → 점자 패턴 변환기 인터페이스.
    /// ManualBrailleConverter(테이블 직접 선언)와
    /// LibLouisConverter(liblouis DLL) 두 구현체가 동일하게 교체 가능합니다.
    ///
    /// Convert() 반환값 규칙:
    ///   0x00      = 공백 셀
    ///   0x01~0x3F = 6비트 점자 패턴  (bit0=점1 … bit5=점6)
    ///   0xFE      = 줄바꿈 마커 (레이아웃 단계에서 처리)
    /// </summary>
    public interface IBrailleConverter
    {
        string Name        { get; }
        string Description { get; }

        /// <summary>
        /// 이 변환기를 실제로 사용할 수 있는지 여부.
        /// LibLouisConverter의 경우 DLL 파일 존재 여부로 결정됩니다.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>사용 불가 이유 (IsAvailable=false일 때 UI에 표시)</summary>
        string UnavailableReason { get; }

        /// <summary>
        /// 텍스트를 점자 패턴 목록으로 변환합니다.
        /// </summary>
        List<byte> Convert(string text);
    }
}
