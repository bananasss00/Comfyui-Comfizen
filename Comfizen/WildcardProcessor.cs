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
        private const int MaxIterations = 100;

        public WildcardProcessor(long seed)
        {
            // Seed the random number generator for deterministic results for a given prompt + seed
            _random = new Random((int)(seed & 0xFFFFFFFF));
        }

        /// <summary>
        /// Processes the input string iteratively, replacing all supported wildcard and dynamic syntaxes, including nested ones.
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

                // First, process the innermost complex {..} blocks.
                // The regex replacement is greedy, but since we re-evaluate in a loop, it works for nesting.
                currentResult = Regex.Replace(currentResult, @"\{([^{}]+)\}", m => ProcessBraceContent(m.Groups[1].Value));
                
                // Then, process simple __...__ wildcards. This handles cases like __folder/{__file__}__ after the inner part is resolved.
                currentResult = Regex.Replace(currentResult, @"__([\s\S]+?)__", m => ProcessSimpleWildcard(m.Groups[1].Value.Trim()));

                // If no changes were made in this iteration, we are done.
                if (currentResult == previousResult)
                {
                    break;
                }
                
                iterations++;
            }

            if (iterations >= MaxIterations)
            {
                Logger.Log($"[WildcardProcessor] Max processing iterations ({MaxIterations}) reached. Possible infinite loop detected in prompt: '{input}'. Returning intermediate result.");
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
    
    public static class WildcardFileHandler
    {
        // Change: The production directory is now in a separate readonly field.
        private static readonly string _productionDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wildcards");
        // Change: This allows us to override the path for testing purposes.
        private static string _testOverrideDirectory = null;

        // Change: WildcardsDirectory is now a property that checks if an override is set.
        private static string WildcardsDirectory => _testOverrideDirectory ?? _productionDirectory;
        
        // Cache for file content (key: wildcard name, value: array of lines)
        private static readonly ConcurrentDictionary<string, string[]> _contentCache = new ConcurrentDictionary<string, string[]>();
        // Cache for the complete list of all wildcard names found on disk
        private static List<string> _allWildcardNamesCache;
        private static readonly object _listCacheLock = new object();

        // New methods for testing, will only be compiled in DEBUG mode.
#if DEBUG
        /// <summary>
        /// Overrides the wildcard directory for unit testing.
        /// This method is only available in DEBUG builds.
        /// </summary>
        public static void SetTestDirectory(string path) => _testOverrideDirectory = path;

        /// <summary>
        /// Resets the wildcard directory to the production default.
        /// This method is only available in DEBUG builds.
        /// </summary>
        public static void ResetDirectory() => _testOverrideDirectory = null;
#endif

        static WildcardFileHandler()
        {
            // The static constructor now ensures the *production* directory exists,
            // which is fine even during tests.
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
            if (_allWildcardNamesCache != null) return _allWildcardNamesCache;

            lock (_listCacheLock)
            {
                if (_allWildcardNamesCache != null) return _allWildcardNamesCache;

                var names = new List<string>();
                try
                {
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
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Failed to scan wildcard directory. Wildcards may not be available.");
                    // Return an empty list but don't cache it, maybe the issue is temporary
                    return new List<string>(); 
                }
                _allWildcardNamesCache = names;
                return _allWildcardNamesCache;
            }
        }

        private static string[] GetLinesFromFile(string wildcardName)
        {
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

            try
            {
                var lines = File.ReadAllLines(fullPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                    .ToArray();
            
                _contentCache.TryAdd(wildcardName, lines);
                return lines;
            }
            catch (Exception ex)
            {
                Logger.Log(ex, $"Failed to read wildcard file: {fullPath}. It will be treated as empty.");
                // Cache the empty result to avoid re-reading a problematic file
                _contentCache.TryAdd(wildcardName, Array.Empty<string>());
                return Array.Empty<string>();
            }
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