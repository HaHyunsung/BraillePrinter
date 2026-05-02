using BraillePrinter.Managers;
using BraillePrinter.Models;
using System.Globalization;
using System.Text;

namespace BraillePrinter.Services
{
    public static class GCodeGenerator
    {
        private static double FeedRate => ParameterManager.Instance.Parameters.GCodeFeedRate;
        private static double Dwell    => ParameterManager.Instance.Parameters.GCodeDwellSeconds;

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
            var lines = new List<string>();

            lines.Add("G90");
            lines.Add("G21");

            string f = FeedRate.ToString("F0", CultureInfo.InvariantCulture);
            string dwell = Dwell.ToString("F2", CultureInfo.InvariantCulture);

            var ordered = BuildZigzagOrder(dots);

            foreach (var dot in ordered)
            {
                string x = dot.X.ToString("F3", CultureInfo.InvariantCulture);
                string y = dot.Y.ToString("F3", CultureInfo.InvariantCulture);

                lines.Add($"G0 X{x} Y{y} F{f}");
                lines.Add("M3");
                lines.Add($"G4 P{dwell}");
                lines.Add("M5");
            }

            lines.Add("G0 X0 Y0");

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
