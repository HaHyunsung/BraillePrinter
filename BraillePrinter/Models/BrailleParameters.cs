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

        // ── GRBL 기계 설정 ($1xx, 연결 시 자동 전송) ─────────────────────────

        /// <summary>스텝 펄스 폭 μs ($0). 드라이버 최소 인식 시간. 기본 10.</summary>
        [XmlElement] public int StepPulseUs     { get; set; } = 10;

        /// <summary>정지 후 모터 코일 유지 ms ($1). 255 = 항상 통전 (위치 유지).</summary>
        [XmlElement] public int StepIdleDelayMs { get; set; } = 255;

        /// <summary>하드 리밋 스위치 사용 여부 ($21). 스위치 없으면 0.</summary>
        [XmlElement] public int HardLimitsEnable { get; set; } = 0;

        /// <summary>홈잉 사이클 사용 여부 ($22). 홈 센서 없으면 0.</summary>
        [XmlElement] public int HomingEnable { get; set; } = 0;

        /// <summary>홈 방향 반전 마스크 ($23). X=1, Y=2, XY=3. 0이면 Min 방향(기본).</summary>
        [XmlElement] public int HomingDirMask { get; set; } = 0;

        /// <summary>홈 위치 확인 속도 mm/min ($24). 저속 2차 접근.</summary>
        [XmlElement] public double HomingFeedRate { get; set; } = 25.0;

        /// <summary>홈 탐색 속도 mm/min ($25). 고속 1차 탐색.</summary>
        [XmlElement] public double HomingSeekRate { get; set; } = 500.0;

        /// <summary>홈 스위치 디바운스 ms ($26).</summary>
        [XmlElement] public int HomingDebounce { get; set; } = 250;

        /// <summary>홈 풀오프 거리 mm ($27). 스위치에서 물러나는 거리.</summary>
        [XmlElement] public double HomingPullOff { get; set; } = 1.0;

        /// <summary>X축 분해능 steps/mm ($100). 계산: 모터스텝×마이크로스텝 / 피치.</summary>
        [XmlElement] public double StepsPerMmX  { get; set; } = 80.0;

        /// <summary>Y축 분해능 steps/mm ($101).</summary>
        [XmlElement] public double StepsPerMmY  { get; set; } = 80.0;

        /// <summary>X축 최대 속도 mm/min ($110).</summary>
        [XmlElement] public double MaxRateX     { get; set; } = 3000.0;

        /// <summary>Y축 최대 속도 mm/min ($111).</summary>
        [XmlElement] public double MaxRateY     { get; set; } = 3000.0;

        /// <summary>X축 가속도 mm/sec² ($120).</summary>
        [XmlElement] public double AccelerationX { get; set; } = 300.0;

        /// <summary>Y축 가속도 mm/sec² ($121).</summary>
        [XmlElement] public double AccelerationY { get; set; } = 300.0;

        /// <summary>X축 최대 이동 거리 mm ($130).</summary>
        [XmlElement] public double MaxTravelX   { get; set; } = 220.0;

        /// <summary>Y축 최대 이동 거리 mm ($131).</summary>
        [XmlElement] public double MaxTravelY   { get; set; } = 310.0;

        /// <summary>축 방향 반전 비트마스크 ($3). X=1, Y=2, XY=3.</summary>
        [XmlElement] public int DirectionInvert { get; set; } = 0;

        /// <summary>리밋 핀 반전 비트마스크 ($5). NO=0, NC=반전. X=1, Y=2, XY=3.</summary>
        [XmlElement] public int LimitPinsInvert { get; set; } = 0;

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

        // 홈 = 용지 우측 상단 코너 기준.
        // 근거리(우측·상단) 여백을 설정값과 정확히 일치시키고,
        // 정수 셀 절삭으로 생기는 잔여 공간은 원거리(좌측·하단)로 보낸다.

        /// <summary>실제 좌측 여백 (mm). 우측 여백 = MarginRight 고정, 잔여는 좌측으로.</summary>
        [XmlIgnore]
        public double EffectiveMarginLeft =>
            PaperWidth - MarginRight - MaxCellsPerLine * CellSpacing;

        /// <summary>실제 상단 여백 (mm). 상단 여백 = MarginTop 고정, 잔여는 하단으로.</summary>
        [XmlIgnore]
        public double EffectiveMarginTop => MarginTop;
    }
}
