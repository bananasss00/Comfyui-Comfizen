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
using MetadataExtractor.Formats.Exif;
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
        // ========================================================== //
        //     НАЧАЛО ИЗМЕНЕНИЯ: Маркер для встроенных данных          //
        // ========================================================== //
        /// <summary>
        /// A unique marker to identify the start of our appended workflow data in a file.
        /// </summary>
        private const string MagicMarker = "COMFIZEN_WORKFLOW_EMBED_V1";
        private static readonly byte[] MagicMarkerBytes = Encoding.UTF8.GetBytes(MagicMarker);
        
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

        public static async Task<string> GetUniqueFilePathAsync(string desiredPath, byte[] newFileBytes)
        {
            if (!File.Exists(desiredPath))
            {
                return desiredPath;
            }

            var existingFileBytes = await File.ReadAllBytesAsync(desiredPath);
            var existingHash = ComputeMd5Hash(existingFileBytes);
            var newHash = ComputeMd5Hash(newFileBytes);

            if (string.Equals(existingHash, newHash, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

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

        // ========================================================== //
        //     НАЧАЛО ИЗМЕНЕНИЯ: Метод для JPG и других форматов      //
        // ========================================================== //
        /// <summary>
        /// Converts PNG to JPG and appends the workflow to the end of the file.
        /// </summary>
        public static byte[] ConvertPngToJpgWithWorkflow(byte[] pngBytes, string workflowJson, int quality)
        {
            using var image = Image.Load(pngBytes);
            using var outputImage = new Image<Rgba32>(image.Width, image.Height, Color.White);
            outputImage.Mutate(x => x.DrawImage(image, 1f));

            var encoder = new JpegEncoder { Quality = quality };

            using var outputStream = new MemoryStream();
            outputImage.Save(outputStream, encoder);
            var jpgBytes = outputStream.ToArray();

            if (string.IsNullOrEmpty(workflowJson))
            {
                return jpgBytes;
            }

            return AppendWorkflowToBytes(jpgBytes, workflowJson);
        }

        /// <summary>
        /// Appends a magic marker and workflow data to a byte array.
        /// </summary>
        private static byte[] AppendWorkflowToBytes(byte[] originalBytes, string workflowJson)
        {
            if (string.IsNullOrEmpty(workflowJson))
            {
                return originalBytes;
            }

            var workflowBytes = Encoding.UTF8.GetBytes(workflowJson);
            
            using (var ms = new MemoryStream(originalBytes.Length + MagicMarkerBytes.Length + workflowBytes.Length))
            {
                ms.Write(originalBytes, 0, originalBytes.Length);
                ms.Write(MagicMarkerBytes, 0, MagicMarkerBytes.Length);
                ms.Write(workflowBytes, 0, workflowBytes.Length);
                return ms.ToArray();
            }
        }
        
        public static byte[] EmbedWorkflowInVideo(byte[] videoBytes, string workflowJson)
        {
            if (string.IsNullOrEmpty(workflowJson))
            {
                return videoBytes;
            }
            
            string tempFilePath = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tempFilePath, videoBytes);

                using (var tfile = TagLib.File.Create(tempFilePath))
                {
                    tfile.Tag.Comment = $"prompt:{workflowJson}";
                    tfile.Save();
                }

                return File.ReadAllBytes(tempFilePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error embedding workflow in video metadata: {ex.Message}. Appending to file as a fallback.");
                // Если метаданные записать не удалось (неподдерживаемый формат), прикрепляем в конец
                return AppendWorkflowToBytes(videoBytes, workflowJson);
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

        // ========================================================== //
        //     НАЧАЛО ИЗМЕНЕНИЯ: Универсальный метод чтения данных    //
        // ========================================================== //
        public static string? ReadStateFromImage(byte[] imageBytes)
        {
            // Priority 1: PNG tEXt chunk
            string? stateJson = ReadPngPrompt(imageBytes);
            if (!string.IsNullOrEmpty(stateJson)) return TryFormatJson(stateJson);

            // Priority 2: WebP EXIF data
            stateJson = ReadWebpPrompt(imageBytes);
            if (!string.IsNullOrEmpty(stateJson)) return TryFormatJson(stateJson);

            // Priority 3: Appended data (for JPG and other formats)
            stateJson = ReadAppendedWorkflow(imageBytes);
            if (!string.IsNullOrEmpty(stateJson)) return TryFormatJson(stateJson);
            
            // Priority 4: Video metadata
            stateJson = ReadVideoWorkflow(imageBytes);
            if (!string.IsNullOrEmpty(stateJson)) return TryFormatJson(stateJson);

            return null;
        }
        
        /// <summary>
        /// Reads workflow data appended to the end of a file.
        /// </summary>
        private static string? ReadAppendedWorkflow(byte[] fileBytes)
        {
            var markerIndex = FindLast(fileBytes, MagicMarkerBytes);
            if (markerIndex == -1)
            {
                return null;
            }

            try
            {
                var startIndex = markerIndex + MagicMarkerBytes.Length;
                return Encoding.UTF8.GetString(fileBytes, startIndex, fileBytes.Length - startIndex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading appended workflow: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Reads workflow from video file metadata.
        /// </summary>
        private static string? ReadVideoWorkflow(byte[] videoBytes)
        {
            string tempFilePath = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tempFilePath, videoBytes);
                using (var tfile = TagLib.File.Create(tempFilePath))
                {
                    var comment = tfile.Tag.Comment;
                    if (comment != null && comment.StartsWith("prompt:"))
                    {
                        return comment.Substring(7);
                    }
                }
                return null;
            }
            catch (TagLib.UnsupportedFormatException)
            {
                // Это не видео или формат не поддерживается, это нормально.
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading video metadata: {ex.Message}");
                return null;
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }
        
        public static string? ReadWebpPrompt(byte[] bytes)
        {
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(new MemoryStream(bytes));
                var exifDir = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
                if (exifDir != null)
                {
                    var makeTag = exifDir.GetDescription(271);
                    if (makeTag != null && makeTag.StartsWith("prompt:"))
                    {
                        return makeTag.Substring(7);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading WebP metadata: {ex.Message}");
            }
            return null;
        }

        public static string? ReadPngPrompt(byte[] bytes)
        {
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(new MemoryStream(bytes));
                foreach (var directory in directories)
                {
                    foreach (var tag in directory.Tags)
                    {
                        if (tag.DirectoryName == "PNG-tEXt" && tag.Name == "Textual Data" && tag.Description.StartsWith("prompt\0"))
                        {
                            return tag.Description.Substring("prompt\0".Length);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading PNG metadata: {ex.Message}");
            }
            return null;
        }

        private static string TryFormatJson(string json)
        {
            try
            {
                dynamic parsedJson = JsonConvert.DeserializeObject(json);
                return JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
            }
            catch
            {
                return json;
            }
        }
        
        /// <summary>
        /// Finds the last occurrence of a byte sequence in another byte array.
        /// </summary>
        private static int FindLast(byte[] haystack, byte[] needle)
        {
            if (needle.Length > haystack.Length) return -1;
            for (int i = haystack.Length - needle.Length; i >= 0; i--)
            {
                bool found = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }
        
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
                return jsonString;
            }
        }
    }
}