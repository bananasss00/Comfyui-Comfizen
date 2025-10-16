using MetadataExtractor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hjg.Pngcs;
using Hjg.Pngcs.Chunks;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.Processing;
using Directory = System.IO.Directory;
using Image = SixLabors.ImageSharp.Image;

namespace Comfizen
{
    public static class WildcardFileHandler
    {
        private static readonly string WildcardsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wildcards");
        private static readonly ConcurrentDictionary<string, string[]> _cache = new ConcurrentDictionary<string, string[]>();
    
        static WildcardFileHandler()
        {
            Directory.CreateDirectory(WildcardsDirectory);
        }
    
        public static string[] GetLines(string wildcardName)
        {
            if (_cache.TryGetValue(wildcardName, out var cachedLines))
            {
                return cachedLines;
            }
    
            var relativePath = wildcardName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar) + ".txt";
            var fullPath = Path.Combine(WildcardsDirectory, relativePath);
    
            if (!File.Exists(fullPath))
            {
                // Cache the fact that the file doesn't exist to avoid repeated disk access
                _cache.TryAdd(wildcardName, Array.Empty<string>());
                return Array.Empty<string>();
            }
    
            var lines = File.ReadAllLines(fullPath)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#")) // Ignore empty lines and comments
                .ToArray();
            _cache.TryAdd(wildcardName, lines);
            return lines;
        }
    }
    
    public static class Utils
    {
        public static string ComputeMd5Hash(byte[] inputData)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(inputData);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("X2"));
                }
                return sb.ToString();
            }
        }
        
        public static string ComputePixelHash(byte[] imageBytes)
        {
            try
            {
                using var image = Image.Load(imageBytes);

                using var memoryStream = new MemoryStream();
                image.SaveAsBmp(memoryStream);

                return ComputeMd5Hash(memoryStream.ToArray());
            }
            catch (Exception)
            {
                return ComputeMd5Hash(imageBytes);
            }
        }

        /// <summary>
        /// Gets a unique file path to prevent overwriting existing files.
        /// If a file at the desired path exists, it compares their MD5 hashes.
        /// If hashes match, it returns null (signaling not to save).
        /// If hashes differ, it adds a numeric suffix (e.g., "filename_(1).ext") until a free name is found.
        /// </summary>
        /// <param name="desiredPath">The full, preferred path for the file.</param>
        /// <param name="newFileBytes">The byte content of the new file to be saved.</param>
        /// <returns>A unique file path, or null if an identical file already exists.</returns>
        public static async Task<string> GetUniqueFilePathAsync(string desiredPath, byte[] newFileBytes)
        {
            if (!File.Exists(desiredPath))
            {
                return desiredPath;
            }

            // File exists, so we must compare content
            var existingFileBytes = await File.ReadAllBytesAsync(desiredPath);
            var existingHash = ComputeMd5Hash(existingFileBytes);
            var newHash = ComputeMd5Hash(newFileBytes);

            if (string.Equals(existingHash, newHash, StringComparison.OrdinalIgnoreCase))
            {
                // Files are identical, no need to save.
                return null;
            }

            // Files are different, find a new name with a counter.
            var directory = Path.GetDirectoryName(desiredPath);
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(desiredPath);
            var extension = Path.GetExtension(desiredPath);
            int counter = 1;

            while (true)
            {
                var newFileName = $"{fileNameWithoutExtension}_({counter}){extension}";
                var newPath = Path.Combine(directory, newFileName);
                if (!File.Exists(newPath))
                {
                    return newPath;
                }
                counter++;
            }
        }

        /// <summary>
        /// Converts PNG to WebP, embedding the provided workflow into the EXIF metadata.
        /// </summary>
        public static byte[] ConvertPngToWebpWithWorkflow(byte[] pngBytes, string workflowJson, int quality = 83)
        {
            using var image = Image.Load(pngBytes);

            if (!string.IsNullOrEmpty(workflowJson))
            {
                image.Metadata.ExifProfile ??= new SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifProfile();
                string workflowData = $"prompt:{workflowJson}";
                image.Metadata.ExifProfile.SetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Make, workflowData);
            }
            
            var encoder = new WebpEncoder
            {
                Quality = quality,
                FileFormat = WebpFileFormatType.Lossy,
            };
            
            using var outputStream = new MemoryStream();
            image.Save(outputStream, encoder);

            return outputStream.ToArray();
        }
        
        /// <summary>
        /// Embeds a workflow into a PNG using Pngcs library to ensure an uncompressed tEXt chunk.
        /// </summary>
        public static byte[] ConvertPngToPngWithWorkflow(byte[] pngBytes, string workflowJson, int compressionLevel)
        {
            if (string.IsNullOrEmpty(workflowJson))
            {
                return pngBytes;
            }

            using (var inputStream = new MemoryStream(pngBytes))
            using (var outputStream = new MemoryStream())
            {
                PngReader reader = new PngReader(inputStream);
                PngWriter writer = new PngWriter(outputStream, reader.ImgInfo);

                writer.CopyChunksFirst(reader, ChunkCopyBehaviour.COPY_ALL);

                PngChunkTEXT textChunk = new PngChunkTEXT(reader.ImgInfo);
                textChunk.SetKeyVal("prompt", workflowJson);
                writer.GetChunksList().Queue(textChunk);

                for (int i = 0; i < reader.ImgInfo.Rows; i++)
                {
                    ImageLine line = reader.ReadRow(i);
                    writer.WriteRow(line.Scanline);
                }

                writer.CopyChunksLast(reader, ChunkCopyBehaviour.COPY_ALL);

                reader.End();
                writer.End();

                return outputStream.ToArray();
            }
        }

        /// <summary>
        /// Converts PNG to JPG with the specified quality.
        /// </summary>
        public static byte[] ConvertPngToJpg(byte[] pngBytes, int quality)
        {
            using var image = Image.Load(pngBytes);

            using var outputImage = new Image<Rgba32>(image.Width, image.Height, Color.White);
            outputImage.Mutate(x => x.DrawImage(image, 1f));

            var encoder = new JpegEncoder
            {
                Quality = quality
            };

            using var outputStream = new MemoryStream();
            outputImage.Save(outputStream, encoder);
            return outputStream.ToArray();
        }
        
        public static byte[] EmbedWorkflowInVideo(byte[] videoBytes, string workflowJson)
        {
            string tempFilePath = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tempFilePath, videoBytes);

                using (var tfile = TagLib.File.Create(tempFilePath))
                {
                    tfile.Tag.Comment = $"prompt:{workflowJson}";
                    tfile.Save();
                }

                byte[] updatedVideoBytes = File.ReadAllBytes(tempFilePath);
        
                return updatedVideoBytes;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error embedding workflow in video: {ex.Message}");
                return videoBytes;
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }

        public static long GenerateSeed()
        {
            var random = new Random();
            byte[] buffer = new byte[sizeof(ulong)];
            random.NextBytes(buffer);
            return Math.Abs(BitConverter.ToInt64(buffer, 0)); 
        }
        
        /// <summary>
        /// Recursively merges two JObjects. Updates values in 'target' from 'source'.
        /// </summary>
        public static void MergeJsonObjects(JObject target, JObject source)
        {
            foreach (var sourceProperty in source.Properties())
            {
                var targetProperty = target.Property(sourceProperty.Name);

                if (targetProperty == null)
                {
                    continue;
                }

                if (sourceProperty.Value is JObject sourceNestedObj && targetProperty.Value is JObject targetNestedObj)
                {
                    MergeJsonObjects(targetNestedObj, sourceNestedObj);
                }
                else
                {
                    targetProperty.Value = sourceProperty.Value;
                }
            }
        }

        public static JProperty? GetJsonPropertyByPath(JObject obj, string propertyPath)
        {
            var parts = propertyPath.Split('.');
            var currentPart = obj;
            JProperty? jProperty = null;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (i == parts.Length - 1)
                    jProperty = currentPart.Property(part);
                else
                    currentPart = (JObject)currentPart[part];
            }
            return jProperty;
        }

        public static string? ReadPngPrompt(byte[] bytes)
        {
            MemoryStream stream = new MemoryStream(bytes);
            var directories = ImageMetadataReader.ReadMetadata(stream);
            foreach (var directory in directories)
            {
                foreach (var tag in directory.Tags)
                {
                    if (tag.DirectoryName == "PNG-tEXt" && tag.Name == "Textual Data") 
                    {
                        string workflow = tag.Description.Substring(7); 
                        dynamic parsedWorkflow = JsonConvert.DeserializeObject(workflow);
                        string formattedJson = JsonConvert.SerializeObject(parsedWorkflow, Formatting.Indented);
                        return formattedJson;
                    }
                }
            }

            return null;
        }
        
        /// <summary>
        /// Replaces wildcards in the input string using a specific seed for reproducibility.
        /// </summary>
        public static string ReplaceWildcards(string input, long seed)
        {
            Random random = new Random((int)(seed & 0xFFFFFFFF));
            
            string pass1 = Regex.Replace(input, @"\{__([^{}]+)__\}", match =>
            {
                string wildcardName = match.Groups[1].Value.Trim();
                string[] lines = WildcardFileHandler.GetLines(wildcardName);
                if (lines == null || lines.Length == 0)
                {
                    return match.Value; 
                }
                return lines[random.Next(lines.Length)];
            });
            
            string pass2 = Regex.Replace(pass1, @"\{([^{}|]+(?:\|[^{}|]+)+)\}", match =>
            {
                string[] choices = match.Groups[1].Value.Split('|');
                return choices[random.Next(choices.Length)].Trim();
            });

            return pass2;
        }
        
        public static string ReplaceWildcards(string input)
        {
            return ReplaceWildcards(input, (long)DateTime.Now.Ticks);
        }

        public static void SaveJsonToFile(JToken token, string filePath)
        {
            using (StreamWriter file = File.CreateText(filePath))
            using (JsonTextWriter writer = new JsonTextWriter(file) { Formatting = Formatting.Indented })
            {
                token.WriteTo(writer);
            }
        }
        
        public static JObject? CleanBase64FromJObject(JObject? originalJson)
        {
            if (originalJson == null) return null;

            var cleanedJson = originalJson.DeepClone() as JObject;
            if (cleanedJson == null) return null;

            var properties = cleanedJson.Descendants()
                .OfType<JProperty>()
                .Where(p => p.Value.Type == JTokenType.String)
                .ToList(); 

            foreach (var prop in properties)
            {
                var value = prop.Value.Value<string>();
                if (!string.IsNullOrEmpty(value) && value.Length > 1000 && (value.StartsWith("iVBOR") || value.StartsWith("/9j/") || value.StartsWith("UklG")))
                {
                    prop.Value = ""; 
                }
            }
            return cleanedJson;
        }

        public static string? CleanBase64FromString(string? jsonString)
        {
            if (string.IsNullOrEmpty(jsonString)) return jsonString;

            try
            {
                var jObject = JObject.Parse(jsonString);
                var cleanedJObject = CleanBase64FromJObject(jObject);
                return cleanedJObject?.ToString(Formatting.Indented);
            }
            catch (JsonReaderException)
            {
                // If parsing fails, return the original string
                return jsonString;
            }
        }
    }
}