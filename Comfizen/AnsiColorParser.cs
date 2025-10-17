using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Comfizen
{
    public static class AnsiColorParser
    {
        // Map for converting codes to colors. You can extend it if necessary.
        private static readonly Dictionary<string, Color> AnsiColorMap = new Dictionary<string, Color>
        {
            { "90", Colors.DarkGray },      // Bright Black
            { "91", (Color)ColorConverter.ConvertFromString("#FF8080") }, // Bright Red
            { "92", Colors.LightGreen },    // Bright Green
            { "93", Colors.LightYellow },   // Bright Yellow
            { "94", Colors.LightBlue },     // Bright Blue
            { "95", Colors.Violet },        // Bright Magenta
            { "96", Colors.Cyan },          // Bright Cyan
            { "97", Colors.White },         // Bright White
            { "30", Colors.Black },
            { "31", Colors.Red },
            { "32", Colors.Green },
            { "33", Colors.Yellow },
            { "34", Colors.Blue },
            { "35", Colors.Magenta },
            { "36", Colors.DarkCyan },
            { "37", Colors.WhiteSmoke },
        };

        // Regular expression to find codes like [96m or [0m
        private static readonly Regex AnsiRegex = new Regex(@"(\[\d+m)", RegexOptions.Compiled);

        /// <summary>
        /// Parses a string with ANSI-like color codes into a list of segments with colors.
        /// </summary>
        public static List<LogMessageSegment> Parse(string message)
        {
            var segments = new List<LogMessageSegment>();
            var parts = AnsiRegex.Split(message);
            Color? currentColor = null;

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;

                // If the part is a color code
                if (AnsiRegex.IsMatch(part))
                {
                    var code = part.Trim('[', 'm');
                    if (code == "0")
                    {
                        currentColor = null; // Reset color
                    }
                    else if (AnsiColorMap.TryGetValue(code, out var color))
                    {
                        currentColor = color;
                    }
                }
                else // Otherwise, it's regular text
                {
                    segments.Add(new LogMessageSegment { Text = part, Color = currentColor });
                }
            }
            
            // If there were no codes in the entire string, create a single segment
            if (!segments.Any() && !string.IsNullOrEmpty(message))
            {
                 segments.Add(new LogMessageSegment { Text = message, Color = null });
            }

            return segments;
        }
    }
}