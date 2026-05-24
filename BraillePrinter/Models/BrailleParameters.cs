using System.Xml.Serialization;

namespace BraillePrinter.Models
{
    /// <summary>변환 엔진 선택</summary>
    public enum ConverterType
    {
        /// <summary>내장 테이블 (항상 사용 가능, 약자 미지원)</summary>
        Manual,
        /// <summary>liblouis DLL (약자·다국어 완전 지원, DLL 필요)</summary>
        LibLouis,
    }

    /// <summary>G-Code 출력 모드</summary>
    public enum PrintMode
    {
        /// <summary>각 점 위치에 정지 후 찍기</summary>
        StopAndPunch,
        /// <summary>행을 스캔하면서 연속 찍기 (솔레노이드 선발사 Offset 적용)</summary>
        ContinuousScan,
    }

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

        // ── G-Code 출력 설정 ─────────────────────────────────────────────

        /// <summary>출력 모드 (정지 찍기 / 연속 스캔)</summary>
        [XmlElement] public PrintMode PrintMode { get; set; } = PrintMode.StopAndPunch;

        /// <summary>급속이동 속도 (mm/min) — G0 F 값</summary>
        [XmlElement] public double GCodeFeedRate { get; set; } = 3000.0;

        /// <summary>핀 내려찍기 유지 시간 (초) — M3 후 G4 P 값 (XML 하위 호환: GCodeDwellSeconds)</summary>
        [XmlElement("GCodeDwellSeconds")] public double PunchDwellSeconds { get; set; } = 0.5;

        /// <summary>핀 복귀 대기 시간 (초) — M5 후 G4 P 값. 0이면 생략.</summary>
        [XmlElement] public double RetractDwellSeconds { get; set; } = 0.1;

        // ── 연속 스캔 모드 설정 ($32=1 레이저 모드 필요) ─────────────────────

        /// <summary>스캔 이동 속도 (mm/min) — G1 F 값</summary>
        [XmlElement] public double ScanFeedRate { get; set; } = 1000.0;

        /// <summary>
        /// M3 선발사 Offset (mm) — 목표 점 위치보다 이 거리만큼 앞에서 솔레노이드 ON.
        /// 솔레노이드 전기적 지연을 보정. 이동 방향 기준.
        /// </summary>
        [XmlElement] public double ScanM3OffsetMm { get; set; } = 0.1;

        /// <summary>
        /// M5 후발사 Offset (mm) — 목표 점 위치를 지나 이 거리만큼 뒤에서 솔레노이드 OFF.
        /// 핀이 충분히 종이를 눌렀다가 복귀하도록 보정. 이동 방향 기준.
        /// </summary>
        [XmlElement] public double ScanM5OffsetMm { get; set; } = 0.1;

        // ── 기계 원점 오프셋 (단위: mm) ───────────────────────────────────────

        /// <summary>홈 이후 X 원점 오프셋 (mm). G-Code 좌표 전체에 더해짐.</summary>
        [XmlElement] public double OriginOffsetX { get; set; } = 0.0;

        /// <summary>홈 이후 Y 원점 오프셋 (mm). G-Code 좌표 전체에 더해짐.</summary>
        [XmlElement] public double OriginOffsetY { get; set; } = 0.0;

        // ── 솔레노이드 설정 ───────────────────────────────────────────────

        /// <summary>
        /// 솔레노이드 반전 (B접점). true면 M3=OFF, M5=ON (NC 솔레노이드).
        /// false(기본값)면 M3=ON, M5=OFF (NO 솔레노이드).
        /// </summary>
        [XmlElement] public bool SolenoidInvert { get; set; } = false;

        // ── 변환 엔진 설정 ────────────────────────────────────────────────

        /// <summary>사용할 변환 엔진 (Manual / LibLouis)</summary>
        [XmlElement] public ConverterType ConverterType { get; set; } = ConverterType.LibLouis;

        /// <summary>
        /// LibLouis 엔진 사용 시 테이블 파일명.
        /// "ko-g1.ctb" = 정자(약자 없음) / "ko-g2.ctb" = 약자(기본값)
        /// </summary>
        [XmlElement] public string LibLouisTable { get; set; } = "ko-g2.ctb";

        // ── 계산값 (저장되지 않음) ────────────────────────────────────────

        [XmlIgnore]
        public int MaxCellsPerLine =>
            (int)((PaperWidth - MarginLeft - MarginRight) / CellSpacing);

        [XmlIgnore]
        public int MaxLines =>
            (int)((PaperHeight - MarginTop - MarginBottom) / LineSpacing);

        [XmlIgnore]
        public int TotalCapacity => MaxCellsPerLine * MaxLines;

        // ── 균등 여백 (좌우·상하 대칭) ───────────────────────────────────────
        // MaxCellsPerLine은 정수 절삭으로 인해 우/하단에 잉여 공간이 생깁니다.
        // Effective 여백은 전체 점자 영역을 용지 중앙에 배치하여 좌=우, 상=하 를 보장합니다.

        /// <summary>실제 좌측 여백 (좌우 균등 배분 후, mm)</summary>
        [XmlIgnore]
        public double EffectiveMarginLeft =>
            (PaperWidth - MaxCellsPerLine * CellSpacing) / 2.0;

        /// <summary>실제 상단 여백 (상하 균등 배분 후, mm)</summary>
        [XmlIgnore]
        public double EffectiveMarginTop =>
            (PaperHeight - MaxLines * LineSpacing) / 2.0;
    }
}
