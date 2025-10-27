// WildcardConverter.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Comfizen
{
    /// <summary>
    /// Provides static methods to convert between a directory structure of .txt wildcard files
    /// and a single consolidated .yaml file.
    /// </summary>
    public static class WildcardConverter
    {
        /// <summary>
        /// Converts a directory of .txt wildcard files into a single .yaml file.
        /// </summary>
        /// <param name="sourceDirectory">The root directory of the wildcards (e.g., "wildcards").</param>
        /// <param name="outputYamlFile">The path to save the resulting .yaml file.</param>
        public static void ConvertDirectoryToYaml(string sourceDirectory, string outputYamlFile)
        {
            if (!Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");
            }

            var root = new Dictionary<string, object>();
            var allFiles = Directory.GetFiles(sourceDirectory, "*.txt", SearchOption.AllDirectories);

            foreach (var file in allFiles)
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, file);
                var wildcardName = Path.ChangeExtension(relativePath, null).Replace(Path.DirectorySeparatorChar, '/');
                var pathParts = wildcardName.Split('/');

                var lines = File.ReadAllLines(file)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                    .ToList();

                if (lines.Any())
                {
                    AddToNestedDictionary(root, pathParts, lines);
                }
            }

            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance) // Optional: for cleaner YAML keys
                .Build();

            var yaml = serializer.Serialize(root);
            File.WriteAllText(outputYamlFile, yaml);
        }

        /// <summary>
        /// Recursively adds a list of strings to a nested dictionary based on path parts.
        /// </summary>
        private static void AddToNestedDictionary(Dictionary<string, object> dict, string[] pathParts, List<string> lines)
        {
            var currentDict = dict;
            for (int i = 0; i < pathParts.Length - 1; i++)
            {
                var part = pathParts[i];
                if (!currentDict.ContainsKey(part))
                {
                    currentDict[part] = new Dictionary<string, object>();
                }
                currentDict = (Dictionary<string, object>)currentDict[part];
            }
            currentDict[pathParts.Last()] = lines;
        }

        /// <summary>
        /// Converts a single .yaml file into a directory structure of .txt wildcard files.
        /// </summary>
        /// <param name="sourceYamlFile">The path to the source .yaml file.</param>
        /// <param name="outputDirectory">The root directory to unpack the .txt files into.</param>
        public static void ConvertYamlToDirectory(string sourceYamlFile, string outputDirectory)
        {
            if (!File.Exists(sourceYamlFile))
            {
                throw new FileNotFoundException($"Source YAML file not found: {sourceYamlFile}");
            }

            var yamlContent = File.ReadAllText(sourceYamlFile);
            var deserializer = new DeserializerBuilder()
                // --- CHANGE: Removed naming convention for more robust parsing of different YAML styles ---
                .WithAttemptingUnquotedStringTypeDeserialization()
                .Build();

            // --- CHANGE: Deserialize to a more generic dictionary to handle nested objects correctly ---
            var root = deserializer.Deserialize<Dictionary<object, object>>(yamlContent);

            Directory.CreateDirectory(outputDirectory);
            UnpackYamlNode(root, "", outputDirectory);
        }

        /// <summary>
        /// Recursively traverses the deserialized YAML object and writes .txt files.
        /// </summary>
        private static void UnpackYamlNode(object node, string currentPath, string baseOutputDir)
        {
            // --- CHANGE: Check for the generic Dictionary<object, object> type ---
            if (node is Dictionary<object, object> dict)
            {
                foreach (var kvp in dict)
                {
                    // --- CHANGE: Convert the key to a string ---
                    var newPath = string.IsNullOrEmpty(currentPath) ? kvp.Key.ToString() : $"{currentPath}/{kvp.Key.ToString()}";
                    UnpackYamlNode(kvp.Value, newPath, baseOutputDir);
                }
            }
            else if (node is List<object> list)
            {
                var lines = list.Select(item => item.ToString()).ToList();
                var filePath = Path.Combine(baseOutputDir, currentPath.Replace('/', Path.DirectorySeparatorChar) + ".txt");
                
                var fileDir = Path.GetDirectoryName(filePath);
                
                if (!string.IsNullOrEmpty(fileDir))
                {
                    Directory.CreateDirectory(fileDir);
                }

                File.WriteAllLines(filePath, lines);
            }
        }
    }
}