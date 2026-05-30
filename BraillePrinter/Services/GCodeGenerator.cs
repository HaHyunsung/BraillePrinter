using BraillePrinter.Managers;
using BraillePrinter.Models;
using System.Globalization;
using System.Text;

namespace BraillePrinter.Services
{
    public static class GCodeGenerator
    {
        private static BrailleParameters P => ParameterManager.Instance.Parameters;

        // 솔레노이드 반전 여부에 따라 실제 전송할 명령 결정
        public static string PinOnCmd  => P.SolenoidInvert ? "M5" : "M3";
        public static string PinOffCmd => P.SolenoidInvert ? "M3" : "M5";

        public static List<DotCoordinate> BuildZigzagOrder(IReadOnlyList<DotCoordinate> dots)
        {
            if (dots.Count == 0) return new List<DotCoordinate>();

            var rows = dots.GroupBy(d => d.Y)
                           .OrderBy(g => g.Key)
                           .ToList();

            var result = new List<DotCoordinate>();
            // 홈(용지 우측 상단=작은 머신 X)에서 시작하도록 첫 행을 용지 우측부터(paper X 내림차순) 출발.
            // → 헤드가 홈에서 여백만큼만 이동해 인쇄 시작 (X·Y 통일), 이후 +X로 가로 진행.
            bool leftToRight = false;

            foreach (var row in rows)
            {
                var sorted = leftToRight
                    ? row.OrderBy(d => d.X).ToList()
                    : row.OrderByDescending(d => d.X).ToList();

                result.AddRange(sorted);
                leftToRight = !leftToRight;
            }

            return result;
        }

        // 홈 = 용지 우측 상단 코너(머신 X0), 종이는 홈에서 −X(왼쪽) 방향에 위치.
        // 용지 우측 끝(dot.X=PaperWidth)이 머신 X0, 좌측으로 갈수록 X가 음수로 감소.
        // → 우측 글자(머신 X≈0)부터 시작해 X 감소(−)하며 왼쪽으로 찍음 (앞면에서 정상 판독).
        private static double MirrorX(double dotX) => dotX - P.PaperWidth;

        // 대기 위치 = 용지 우측 상단 코너(원점 오프셋 지점, 여백 제외).
        // 홈 직후·작업 종료 후 여기로 이동해 대기. 여백은 인쇄 G-Code 첫 이동이 처리한다.
        public static double ParkX => P.OriginOffsetX;
        public static double ParkY => P.OriginOffsetY;

        public static List<string> Generate(IReadOnlyList<DotCoordinate> dots)
        {
            return P.PrintMode == PrintMode.ContinuousScan
                ? GenerateContinuousScan(dots)
                : GenerateStopAndPunch(dots);
        }

        // ── 정지 찍기 모드 ────────────────────────────────────────────────
        // 각 점 위치로 G0 급속 이동 후 M3 → G4(punch) → M5 → G4(retract)

        private static List<string> GenerateStopAndPunch(IReadOnlyList<DotCoordinate> dots)
        {
            var lines = new List<string>();
            lines.Add("$32=0");   // 레이저 모드 OFF (정지 찍기는 플래너 동기화 필요)
            lines.Add("G90");
            lines.Add("G21");

            string f            = P.GCodeFeedRate.ToString("F0", CultureInfo.InvariantCulture);
            string punchDwell   = P.PunchDwellSeconds.ToString("F2", CultureInfo.InvariantCulture);
            string retractDwell = P.RetractDwellSeconds.ToString("F2", CultureInfo.InvariantCulture);

            foreach (var dot in BuildZigzagOrder(dots))
            {
                string x = (MirrorX(dot.X) + P.OriginOffsetX).ToString("F3", CultureInfo.InvariantCulture);
                string y = (dot.Y + P.OriginOffsetY).ToString("F3", CultureInfo.InvariantCulture);

                lines.Add($"G0 X{x} Y{y} F{f}");
                lines.Add(PinOnCmd);
                lines.Add($"G4 P{punchDwell}");
                lines.Add(PinOffCmd);
                if (P.RetractDwellSeconds > 0)
                    lines.Add($"G4 P{retractDwell}");
            }

            // 작업 종료 후 대기 위치(용지 우측 상단 코너, 옵셋 지점)로 복귀
            string px = ParkX.ToString("F3", CultureInfo.InvariantCulture);
            string py = ParkY.ToString("F3", CultureInfo.InvariantCulture);
            lines.Add($"G0 X{px} Y{py}");
            return lines;
        }

        // ── 연속 스캔 모드 ────────────────────────────────────────────────
        // GRBL 레이저 모드($32=1) 필요: M3/M5가 플래너 동기화를 하지 않아 헤드가 멈추지 않음.
        //
        // 각 점에 대해:
        //   M3OnX  = dot.X - ScanM3OffsetMm  (→방향) : 목표 위치보다 앞에서 솔레노이드 ON
        //   M5OffX = dot.X + ScanM5OffsetMm  (→방향) : 목표 위치를 지나서 솔레노이드 OFF
        //
        //   실제 점 타격 시점은 ON~OFF 사이 어딘가 → 두 offset으로 조정 가능.
        //
        //   핀 복귀는 M5 이후 다음 M3 위치로 이동하는 동안 자연 완료.
        //   점 간격 ≥ ScanM5OffsetMm + ScanM3OffsetMm + 복귀에 필요한 최소 거리 를 확인할 것.

        private static List<string> GenerateContinuousScan(IReadOnlyList<DotCoordinate> dots)
        {
            var lines = new List<string>();
            lines.Add("$32=1");   // 레이저 모드 ON (M3/M5가 플래너 동기화 안 함 → 연속 이동)
            lines.Add("G90");
            lines.Add("G21");

            string rf    = P.GCodeFeedRate.ToString("F0", CultureInfo.InvariantCulture);
            string sf    = P.ScanFeedRate.ToString("F0", CultureInfo.InvariantCulture);
            double m3Off = P.ScanM3OffsetMm;
            double m5Off = P.ScanM5OffsetMm;

            var rows = dots.GroupBy(d => d.Y)
                           .OrderBy(g => g.Key)
                           .ToList();

            // 홈(용지 우측=작은 머신 X)에서 시작하도록 첫 행을 용지 우측부터(paper X 내림차순) 출발
            bool leftToRight = false;
            foreach (var row in rows)
            {
                var sorted = leftToRight
                    ? row.OrderBy(d => d.X).ToList()
                    : row.OrderByDescending(d => d.X).ToList();

                bool firstInRow = true;
                foreach (var dot in sorted)
                {
                    double baseX = MirrorX(dot.X) + P.OriginOffsetX;
                    double baseY = dot.Y + P.OriginOffsetY;

                    // leftToRight(paper 좌→우) = machine +X 이동, 반대는 −X 이동. 이동 방향 기준 M3/M5 위치
                    double m3X = leftToRight ? baseX - m3Off : baseX + m3Off;
                    double m5X = leftToRight ? baseX + m5Off : baseX - m5Off;

                    string m3Str = m3X.ToString("F3", CultureInfo.InvariantCulture);
                    string m5Str = m5X.ToString("F3", CultureInfo.InvariantCulture);
                    string y     = baseY.ToString("F3", CultureInfo.InvariantCulture);

                    if (firstInRow)
                    {
                        lines.Add($"G0 X{m3Str} Y{y} F{rf}");  // 행 첫 M3 위치: Y 포함 급속
                        firstInRow = false;
                    }
                    else
                    {
                        lines.Add($"G1 X{m3Str} F{sf}");  // 다음 M3 위치까지 이동 (핀 복귀 중)
                    }

                    lines.Add(PinOnCmd);                   // 솔레노이드 ON (목표 위치 m3Off 앞)
                    lines.Add($"G1 X{m5Str} F{sf}");      // 목표 위치 통과 후 m5Off까지 이동
                    lines.Add(PinOffCmd);                  // 솔레노이드 OFF
                }

                leftToRight = !leftToRight;
            }

            // 작업 종료 후 대기 위치(용지 우측 상단 코너, 옵셋 지점)로 복귀
            string px = ParkX.ToString("F3", CultureInfo.InvariantCulture);
            string py = ParkY.ToString("F3", CultureInfo.InvariantCulture);
            lines.Add($"G0 X{px} Y{py}");
            return lines;
        }

        public static string GenerateText(IReadOnlyList<DotCoordinate> dots)
        {
            var sb = new StringBuilder();
            foreach (var line in Generate(dots))
                sb.AppendLine(line);
            return sb.ToString();
        }

        public static string FormatCoordinateTable(IReadOnlyList<DotCoordinate> dots)
        {
            var sb = new StringBuilder();
            // X·Y = 실제 머신 좌표 (X 미러 + 원점 오프셋 적용) — G-Code와 동일
            sb.AppendLine("  #  | MachX(mm) | MachY(mm) | Cell(R,C) | Dot | 방향");
            sb.AppendLine("-----+-----------+-----------+-----------+-----+------");

            var ordered = BuildZigzagOrder(dots);

            double? prevY = null;
            bool leftToRight = false;

            for (int i = 0; i < ordered.Count; i++)
            {
                var d = ordered[i];

                if (prevY == null || Math.Abs(d.Y - prevY.Value) > 0.01)
                {
                    if (prevY != null) leftToRight = !leftToRight;
                    else leftToRight = false;
                    prevY = d.Y;
                }

                // 표시 좌표를 머신 기준으로 미러 → 진행 방향도 머신 X 기준으로 표기
                string dir = leftToRight ? " <-" : " ->";

                double machX = MirrorX(d.X) + P.OriginOffsetX;
                double machY = d.Y + P.OriginOffsetY;

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,4} | {1,9:F3} | {2,9:F3} | ({3,2},{4,2})  |  {5}  | {6}",
                    i + 1, machX, machY, d.CellRow, d.CellColumn, d.DotNumber, dir));
            }

            return sb.ToString();
        }
    }
}
