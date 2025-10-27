using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace Comfizen;

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
    
    private static bool _yamlParsed = false;
    private static readonly object _yamlParseLock = new object();

    // New methods for testing, will only be compiled in DEBUG mode.
#if DEBUG
    /// <summary>
    /// Overrides the wildcard directory for unit testing and clears all caches.
    /// This method is only available in DEBUG builds.
    /// </summary>
    public static void SetTestDirectory(string path)
    {
        _testOverrideDirectory = path;
        // CRITICAL FIX: Reset all caches and flags when the directory changes.
        _contentCache.Clear();
        _allWildcardNamesCache = null;
        _yamlParsed = false;
    }

    /// <summary>
    /// Resets the wildcard directory to the production default and clears all caches.
    /// This method is only available in DEBUG builds.
    /// </summary>
    public static void ResetDirectory()
    {
        _testOverrideDirectory = null;
        // CRITICAL FIX: Reset all caches and flags when the directory changes.
        _contentCache.Clear();
        _allWildcardNamesCache = null;
        _yamlParsed = false;
    }
#endif

    static WildcardFileHandler()
    {
        // The static constructor now ensures the *production* directory exists,
        // which is fine even during tests.
        Directory.CreateDirectory(WildcardsDirectory);
    }

    /// <summary>
    /// Gets the lines from a wildcard file or a set of files matching a glob pattern.
    /// Also handles wildcards defined in YAML files.
    /// Results are cached.
    /// </summary>
    public static string[] GetLines(string wildcardPattern)
    {
        // This ensures that YAML files are parsed before any wildcard is requested.
        EnsureYamlIsParsed();

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
        // Note: Caching an empty result for a non-existent wildcard is important to avoid re-scans.
        _contentCache.TryAdd(wildcardPattern, lines);
        return lines;
    }
    
    private static void EnsureYamlIsParsed()
    {
        if (_yamlParsed) return;
        lock (_yamlParseLock)
        {
            if (_yamlParsed) return;
                
            if (!Directory.Exists(WildcardsDirectory))
            {
                Directory.CreateDirectory(WildcardsDirectory);
                _yamlParsed = true;
                return;
            }

            var yamlFiles = Directory.GetFiles(WildcardsDirectory, "*.yaml", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(WildcardsDirectory, "*.yml", SearchOption.AllDirectories));

            foreach (var file in yamlFiles)
            {
                ParseAndCacheYamlFile(file);
            }
                
            _yamlParsed = true;
        }
    }

    private static void ParseAndCacheYamlFile(string filePath)
    {
        try
        {
            var yamlContent = File.ReadAllText(filePath);
            var deserializer = new DeserializerBuilder()
                .WithAttemptingUnquotedStringTypeDeserialization() // Important for values that aren't quoted
                .Build();

            // Deserialize into a generic object structure
            var root = deserializer.Deserialize<Dictionary<object, object>>(yamlContent);

            if (root != null)
            {
                FlattenAndCacheYamlNode(root, "");
            }
        }
        catch (Exception ex)
        {
            Logger.Log(ex, $"Failed to parse YAML wildcard file: {filePath}. It will be skipped.");
        }
    }
    
    /// <summary>
    /// Recursively traverses the deserialized YAML object, flattens it into key/value pairs,
    /// and adds them to the content cache.
    /// </summary>
    private static void FlattenAndCacheYamlNode(object node, string currentPath)
    {
        // Handle dictionary nodes (nested structures)
        if (node is Dictionary<object, object> dict)
        {
            foreach (var kvp in dict)
            {
                var newPath = string.IsNullOrEmpty(currentPath) ? kvp.Key.ToString() : $"{currentPath}/{kvp.Key}";
                FlattenAndCacheYamlNode(kvp.Value, newPath);
            }
        }
        // Handle list nodes (the actual wildcard lines)
        else if (node is List<object> list)
        {
            var lines = list.Select(item => item.ToString()).ToArray();
            // Add the flattened path and its lines to the cache. This will override any .txt file with the same name.
            _contentCache[currentPath] = lines;
        }
    }

    public static List<string> GetAllWildcardNames()
    {
        // Ensure YAMLs are loaded into cache before we build the name list
        EnsureYamlIsParsed();

        if (_allWildcardNamesCache != null) return _allWildcardNamesCache;

        lock (_listCacheLock)
        {
            if (_allWildcardNamesCache != null) return _allWildcardNamesCache;

            var names = new HashSet<string>(); // Use HashSet to handle duplicates gracefully
            try
            {
                if (Directory.Exists(WildcardsDirectory))
                {
                    // 1. Add names from .txt files
                    var files = Directory.GetFiles(WildcardsDirectory, "*.txt", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var relativePath = Path.GetRelativePath(WildcardsDirectory, file);
                        var wildcardName = Path.ChangeExtension(relativePath, null).Replace(Path.DirectorySeparatorChar, '/');
                        names.Add(wildcardName);
                    }
                }
                    
                // 2. Add names from the cache (which now includes YAML keys)
                foreach (var key in _contentCache.Keys)
                {
                    names.Add(key);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Failed to scan wildcard directory. Wildcards may not be available.");
                return new List<string>(); 
            }
            _allWildcardNamesCache = names.ToList();
            _allWildcardNamesCache.Sort(); // Keep it sorted
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