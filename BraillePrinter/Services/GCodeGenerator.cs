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
            bool leftToRight = true;

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
                string x = (dot.X + P.OriginOffsetX).ToString("F3", CultureInfo.InvariantCulture);
                string y = (dot.Y + P.OriginOffsetY).ToString("F3", CultureInfo.InvariantCulture);

                lines.Add($"G0 X{x} Y{y} F{f}");
                lines.Add(PinOnCmd);
                lines.Add($"G4 P{punchDwell}");
                lines.Add(PinOffCmd);
                if (P.RetractDwellSeconds > 0)
                    lines.Add($"G4 P{retractDwell}");
            }

            string ox = P.OriginOffsetX.ToString("F3", CultureInfo.InvariantCulture);
            string oy = P.OriginOffsetY.ToString("F3", CultureInfo.InvariantCulture);
            lines.Add($"G0 X{ox} Y{oy}");
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

            bool leftToRight = true;
            foreach (var row in rows)
            {
                var sorted = leftToRight
                    ? row.OrderBy(d => d.X).ToList()
                    : row.OrderByDescending(d => d.X).ToList();

                bool firstInRow = true;
                foreach (var dot in sorted)
                {
                    double baseX = dot.X + P.OriginOffsetX;
                    double baseY = dot.Y + P.OriginOffsetY;

                    // 이동 방향 기준으로 M3/M5 위치 계산
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

            string ox = P.OriginOffsetX.ToString("F3", CultureInfo.InvariantCulture);
            string oy = P.OriginOffsetY.ToString("F3", CultureInfo.InvariantCulture);
            lines.Add($"G0 X{ox} Y{oy}");
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
            sb.AppendLine("  #  |   X (mm)  |   Y (mm)  | Cell(R,C) | Dot | 방향");
            sb.AppendLine("-----+-----------+-----------+-----------+-----+------");

            var ordered = BuildZigzagOrder(dots);

            double? prevY = null;
            bool leftToRight = true;

            for (int i = 0; i < ordered.Count; i++)
            {
                var d = ordered[i];

                if (prevY == null || Math.Abs(d.Y - prevY.Value) > 0.01)
                {
                    if (prevY != null) leftToRight = !leftToRight;
                    else leftToRight = true;
                    prevY = d.Y;
                }

                string dir = leftToRight ? " ->" : " <-";

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,4} | {1,9:F3} | {2,9:F3} | ({3,2},{4,2})  |  {5}  | {6}",
                    i + 1, d.X, d.Y, d.CellRow, d.CellColumn, d.DotNumber, dir));
            }

            return sb.ToString();
        }
    }
}
