using System.Xml.Serialization;

namespace BraillePrinter.Models
{
    /// <summary>
    /// 점자 출력 물리 파라미터 (2020 한국 점자 규정 기반)
    /// ParameterManager에 의해 XML로 저장/복원됩니다.
    /// </summary>
    [XmlRoot("BrailleParameters")]
    public class BrailleParameters
    {
        // ── 점자 물리 규격 (단위: mm) ──────────────────────────────────────

        /// <summary>셀 내부 점간 거리 (규격: 2.3~2.5mm)</summary>
        [XmlElement] public double DotSpacing { get; set; } = 2.5;

        /// <summary>자간 거리 – 셀과 셀 사이 (규격: 5.5~6.9mm)</summary>
        [XmlElement] public double CellSpacing { get; set; } = 6.0;

        /// <summary>줄간 거리 (규격: ~10.0mm)</summary>
        [XmlElement] public double LineSpacing { get; set; } = 10.0;

        // ── 용지 여백 (단위: mm) ──────────────────────────────────────────

        [XmlElement] public double MarginLeft   { get; set; } = 10.0;
        [XmlElement] public double MarginTop    { get; set; } = 10.0;
        [XmlElement] public double MarginRight  { get; set; } = 10.0;
        [XmlElement] public double MarginBottom { get; set; } = 10.0;

        // ── 용지 크기 (단위: mm) ──────────────────────────────────────────

        /// <summary>용지 너비 (A4 기본: 210mm)</summary>
        [XmlElement] public double PaperWidth  { get; set; } = 210.0;

        /// <summary>용지 높이 (A4 기본: 297mm)</summary>
        [XmlElement] public double PaperHeight { get; set; } = 297.0;

        // ── 화면 표시 설정 ────────────────────────────────────────────────

        /// <summary>화면 표시 배율 (px/mm)</summary>
        [XmlElement] public double DisplayScale { get; set; } = 2.5;

        // ── 계산값 (저장되지 않음) ────────────────────────────────────────

        [XmlIgnore]
        public int MaxCellsPerLine =>
            (int)((PaperWidth - MarginLeft - MarginRight) / CellSpacing);

        [XmlIgnore]
        public int MaxLines =>
            (int)((PaperHeight - MarginTop - MarginBottom) / LineSpacing);

        [XmlIgnore]
        public int TotalCapacity => MaxCellsPerLine * MaxLines;
    }
}
