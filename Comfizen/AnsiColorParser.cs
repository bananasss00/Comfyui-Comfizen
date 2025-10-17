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
        private static readonly Regex AnsiRegex = new Regex(@"(\[\d+(?:;\d+)*m)", RegexOptions.Compiled);

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

                if (AnsiRegex.IsMatch(part))
                {
                    var codesString = part.Trim('[', 'm');
                    
                    // The code "0" is always a reset command.
                    if (codesString == "0")
                    {
                        currentColor = null;
                    }
                    else
                    {
                        // Split by semicolon to handle codes like [33;20m
                        // and find the first valid color code in the sequence.
                        var codes = codesString.Split(';');
                        foreach (var code in codes)
                        {
                            if (AnsiColorMap.TryGetValue(code, out var color))
                            {
                                currentColor = color; // Found a color, apply it.
                                // We could break here, but continuing allows the last color in a sequence to win, which is standard behavior.
                            }
                        }
                    }
                }
                else // This part is plain text
                {
                    segments.Add(new LogMessageSegment { Text = part, Color = currentColor });
                }
            }
            
            // If the entire string contained no color codes, create a single segment for it.
            if (!segments.Any() && !string.IsNullOrEmpty(message))
            {
                 segments.Add(new LogMessageSegment { Text = message, Color = null });
            }

            return segments;
        }
    }
}