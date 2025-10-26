using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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

        public WildcardProcessor(long seed)
        {
            // Seed the random number generator for deterministic results for a given prompt + seed
            _random = new Random((int)(seed & 0xFFFFFFFF));
        }

        /// <summary>
        /// Processes the input string, replacing all supported wildcard and dynamic syntaxes.
        /// </summary>
        /// <param name="input">The prompt string to process.</param>
        /// <returns>The processed string with all syntaxes resolved.</returns>
        public string Process(string input)
        {
            // First pass: Process complex {..} blocks which can contain wildcards and quantifiers.
            // This ensures that lists are built and chosen from correctly before simple wildcards are replaced.
            string pass1 = Regex.Replace(input, @"\{([^{}]+)\}", m => ProcessBraceContent(m.Groups[1].Value));
            
            // Second pass: Process simple __...__ wildcards that are not inside {..} blocks.
            // FIX: The original regex incorrectly forbade underscores in names. This is a more robust, lazy match.
            string pass2 = Regex.Replace(pass1, @"__([\s\S]+?)__", m => ProcessSimpleWildcard(m.Groups[1].Value.Trim()));

            return pass2;
        }

        /// <summary>
        /// Processes the content within {..} braces.
        /// Handles quantifiers (e.g., {2$$..}) and list expansion with custom separators.
        /// </summary>
        private string ProcessBraceContent(string content)
        {
            int minChoices = 1, maxChoices = 1;
            string separator = ", "; // Default separator

            // Check for quantifier syntax {N-M$$...} or {N$$...}
            var quantifierMatch = Regex.Match(content, @"^(\d+)(?:-(\d+))?\$\$(.+)");
            if (quantifierMatch.Success)
            {
                minChoices = int.Parse(quantifierMatch.Groups[1].Value);
                maxChoices = minChoices;
                if (quantifierMatch.Groups[2].Success)
                {
                    maxChoices = int.Parse(quantifierMatch.Groups[2].Value);
                }
                content = quantifierMatch.Groups[3].Value; // The rest of the string after the quantifier

                // Check for a custom separator {..$$separator$$..}
                var separatorParts = content.Split(new[] { "$$" }, 2, StringSplitOptions.None);
                if (separatorParts.Length == 2)
                {
                    separator = separatorParts[0];
                    content = separatorParts[1];
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
                    // It's a wildcard, expand it
                    var wildcardName = wildcardMatch.Groups[1].Value;
                    finalChoices.AddRange(WildcardFileHandler.GetLines(wildcardName));
                }
                else
                {
                    // It's a literal value
                    finalChoices.Add(item);
                }
            }
            
            if (finalChoices.Count == 0) return "";

            // Ensure minChoices is not greater than maxChoices
            if (minChoices > maxChoices) (minChoices, maxChoices) = (maxChoices, minChoices);
            
            int numToTake = _random.Next(Math.Min(minChoices, finalChoices.Count), Math.Min(maxChoices, finalChoices.Count) + 1);
            
            // Fisher-Yates shuffle to pick unique items
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
                // Return original text if wildcard is not found
                return $"__{wildcardName}__"; 
            }
            return lines[_random.Next(lines.Length)];
        }
    }
    
    public static class WildcardFileHandler
    {
        private static readonly string WildcardsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wildcards");
        // Cache for file content (key: wildcard name, value: array of lines)
        private static readonly ConcurrentDictionary<string, string[]> _contentCache = new ConcurrentDictionary<string, string[]>();
        // Cache for the complete list of all wildcard names found on disk
        private static List<string> _allWildcardNamesCache;
        private static readonly object _listCacheLock = new object();

        static WildcardFileHandler()
        {
            Directory.CreateDirectory(WildcardsDirectory);
        }

        /// <summary>
        /// Gets the lines from a wildcard file or a set of files matching a glob pattern.
        /// Results are cached.
        /// </summary>
        /// <param name="wildcardPattern">The name of the wildcard, e.g., "colors" or "poses/pose_*"</param>
        /// <returns>An array of strings from the file(s).</returns>
        public static string[] GetLines(string wildcardPattern)
        {
            if (_contentCache.TryGetValue(wildcardPattern, out var cachedLines))
            {
                return cachedLines;
            }

            string[] lines;
            if (wildcardPattern.Contains('*'))
            {
                lines = GetLinesFromGlob(wildcardPattern);
            }
            else
            {
                lines = GetLinesFromFile(wildcardPattern);
            }

            // Cache the final aggregated result for the pattern
            _contentCache.TryAdd(wildcardPattern, lines);
            return lines;
        }

        private static List<string> GetAllWildcardNames()
        {
            // Double-checked locking for thread-safe lazy initialization
            if (_allWildcardNamesCache != null) return _allWildcardNamesCache;

            lock (_listCacheLock)
            {
                if (_allWildcardNamesCache != null) return _allWildcardNamesCache;

                var names = new List<string>();
                if (Directory.Exists(WildcardsDirectory))
                {
                    var files = Directory.GetFiles(WildcardsDirectory, "*.txt", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var relativePath = Path.GetRelativePath(WildcardsDirectory, file);
                        var wildcardName = Path.ChangeExtension(relativePath, null).Replace(Path.DirectorySeparatorChar, '/');
                        names.Add(wildcardName);
                    }
                }
                _allWildcardNamesCache = names;
                return _allWildcardNamesCache;
            }
        }

        private static string[] GetLinesFromFile(string wildcardName)
        {
            // Attempt to get from cache first for individual files
            if (_contentCache.TryGetValue(wildcardName, out var cachedLines))
            {
                return cachedLines;
            }

            var relativePath = wildcardName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar) + ".txt";
            var fullPath = Path.Combine(WildcardsDirectory, relativePath);

            if (!File.Exists(fullPath))
            {
                return Array.Empty<string>();
            }

            var lines = File.ReadAllLines(fullPath)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .ToArray();
            
            _contentCache.TryAdd(wildcardName, lines);
            return lines;
        }

        private static string[] GetLinesFromGlob(string globPattern)
        {
            var allLines = new List<string>();
            var allNames = GetAllWildcardNames();
            var regex = new Regex(WildcardToRegex(globPattern), RegexOptions.IgnoreCase);

            var matchingNames = allNames.Where(name => regex.IsMatch(name));

            foreach (var name in matchingNames)
            {
                allLines.AddRange(GetLinesFromFile(name));
            }
            
            return allLines.ToArray();
        }

        private static string WildcardToRegex(string pattern)
        {
            // Convert glob pattern to a regex pattern that matches the whole string
            return "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        }
    }
}