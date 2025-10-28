using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Comfizen
{
    /// <summary>
    /// Handles advanced wildcard and syntax processing in prompts.
    /// Supports file-based wildcards, glob patterns, dynamic lists, and quantifiers.
    /// </summary>
    public class WildcardProcessor
    {
        private readonly Random _random;
        private const int MaxIterations = 100;

        public WildcardProcessor(long seed)
        {
            // Seed the random number generator for deterministic results for a given prompt + seed
            _random = new Random((int)(seed & 0xFFFFFFFF));
        }

        /// <summary>
        /// Processes the input string iteratively, replacing all supported wildcard and dynamic syntaxes, including nested ones.
        /// The processing continues until no more replacements can be made or a maximum iteration limit is reached.
        /// </summary>
        /// <param name="input">The prompt string to process.</param>
        /// <returns>The processed string with all syntaxes resolved.</returns>
        public string Process(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            string currentResult = input;
            int iterations = 0;

            while (iterations < MaxIterations)
            {
                string previousResult = currentResult;

                // --- STAGE 1: Process brace constructs {...} ---
                // This handles quantifiers and lists, e.g., {2$$a|__b__}.
                currentResult = Regex.Replace(currentResult, @"\{([^{}]+)\}", m => ProcessBraceContent(m.Groups[1].Value));

                // --- STAGE 2: Process simple __...__ wildcards ---
                // This resolves all __...__ patterns, including those generated from the previous stage or from other wildcards.
                currentResult = Regex.Replace(currentResult, @"__([\s\S]+?)__", m => ProcessSimpleWildcard(m.Groups[1].Value.Trim()));

                // If no changes were made in this iteration, the string is fully processed.
                if (currentResult == previousResult)
                {
                    break;
                }
                
                iterations++;
            }

            if (iterations >= MaxIterations)
            {
                Logger.Log($"[WildcardProcessor] Max processing iterations ({MaxIterations}) reached. Possible infinite loop in prompt: '{input}'. Final result: '{currentResult}'", LogLevel.Warning);
            }
            
            return currentResult;
        }

        /// <summary>
        /// Processes the content within {..} braces.
        /// Handles quantifiers (e.g., {2$$..}) and list expansion with custom separators.
        /// This method is now safer against malformed input.
        /// </summary>
        private string ProcessBraceContent(string content)
        {
            int minChoices = 1, maxChoices = 1;
            string separator = ", "; // Default separator

            var quantifierMatch = Regex.Match(content, @"^(.+?)\$\$(.+)");
            if (quantifierMatch.Success)
            {
                string quantifierPart = quantifierMatch.Groups[1].Value;
                string restOfContent = quantifierMatch.Groups[2].Value;

                var rangeMatch = Regex.Match(quantifierPart, @"^(\d+)(?:-(\d+))?$");
                if (rangeMatch.Success)
                {
                    // Safely parse numbers
                    int.TryParse(rangeMatch.Groups[1].Value, out minChoices);
                    maxChoices = minChoices;
                    if (rangeMatch.Groups[2].Success)
                    {
                        int.TryParse(rangeMatch.Groups[2].Value, out maxChoices);
                    }

                    // Check for a custom separator
                    var separatorParts = restOfContent.Split(new[] { "$$" }, 2, StringSplitOptions.None);
                    if (separatorParts.Length == 2)
                    {
                        separator = separatorParts[0];
                        content = separatorParts[1];
                    }
                    else
                    {
                        content = restOfContent;
                    }
                }
            }
            
            var items = content.Split('|')
                .Select(item => item.Trim())
                .ToList();

            var finalChoices = new List<string>();
            foreach (var item in items)
            {
                var wildcardMatch = Regex.Match(item, @"^__([\s\S]+?)__$");
                if (wildcardMatch.Success)
                {
                    var wildcardName = wildcardMatch.Groups[1].Value;
                    finalChoices.AddRange(WildcardFileHandler.GetLines(wildcardName));
                }
                else
                {
                    finalChoices.Add(item);
                }
            }
            
            if (finalChoices.Count == 0) return "";

            if (minChoices > maxChoices) (minChoices, maxChoices) = (maxChoices, minChoices);
            
            int numToTake = _random.Next(Math.Min(minChoices, finalChoices.Count), Math.Min(maxChoices, finalChoices.Count) + 1);
            
            var shuffled = finalChoices.OrderBy(x => _random.Next()).ToList();
            
            return string.Join(separator, shuffled.Take(numToTake));
        }

        /// <summary>
        /// Processes a simple, standalone wildcard like __creature__.
        /// </summary>
        private string ProcessSimpleWildcard(string wildcardName)
        {
            var lines = WildcardFileHandler.GetLines(wildcardName);
            if (lines.Length == 0)
            {
                return $"__{wildcardName}__"; 
            }
            return lines[_random.Next(lines.Length)];
        }
    }
}