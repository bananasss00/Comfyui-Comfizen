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
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hjg.Pngcs;
using Hjg.Pngcs.Chunks;
using MetadataExtractor.Formats.Exif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using Directory = System.IO.Directory;

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
                _cache.TryAdd(wildcardName, Array.Empty<string>());
                return Array.Empty<string>();
            }
    
            var lines = File.ReadAllLines(fullPath)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .ToArray();
            _cache.TryAdd(wildcardName, lines);
            return lines;
        }
    }
    
    public static class Utils
    {
        private const string MagicMarker = "COMFIZEN_WORKFLOW_EMBED_V1";
        private static readonly byte[] MagicMarkerBytes = Encoding.UTF8.GetBytes(MagicMarker);
        
        // ========================================================== //
        //     НАЧАЛО ИЗМЕНЕНИЯ: Методы для сжатия и распаковки      //
        // ========================================================== //
        private static byte[] Compress(string data)
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            using var msi = new MemoryStream(bytes);
            using var mso = new MemoryStream();
            using (var gs = new GZipStream(mso, CompressionMode.Compress))
            {
                msi.CopyTo(gs);
            }
            return mso.ToArray();
        }

        private static string Decompress(byte[] bytes)
        {
            using var msi = new MemoryStream(bytes);
            using var mso = new MemoryStream();
            using (var gs = new GZipStream(msi, CompressionMode.Decompress))
            {
                gs.CopyTo(mso);
            }
            return Encoding.UTF8.GetString(mso.ToArray());
        }

        private static string CompressAndBase64Encode(string data)
        {
            if (string.IsNullOrEmpty(data)) return string.Empty;
            var compressedBytes = Compress(data);
            return Convert.ToBase64String(compressedBytes);
        }

        private static string Base64DecodeAndDecompress(string compressedBase64)
        {
            if (string.IsNullOrEmpty(compressedBase64)) return string.Empty;
            try
            {
                var bytes = Convert.FromBase64String(compressedBase64);
                return Decompress(bytes);
            }
            catch
            {
                // Fallback for old, uncompressed data
                return compressedBase64;
            }
        }
        
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
                string compressedData = CompressAndBase64Encode(workflowJson);
                image.Metadata.ExifProfile.SetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Make, $"prompt:{compressedData}");
            }
            var encoder = new WebpEncoder { Quality = quality, FileFormat = WebpFileFormatType.Lossy };
            using var outputStream = new MemoryStream();
            image.Save(outputStream, encoder);
            return outputStream.ToArray();
        }
        
        public static byte[] ConvertPngToPngWithWorkflow(byte[] pngBytes, string workflowJson, int compressionLevel)
        {
            if (string.IsNullOrEmpty(workflowJson)) return pngBytes;
            using var inputStream = new MemoryStream(pngBytes);
            using var outputStream = new MemoryStream();
            var reader = new PngReader(inputStream);
            var writer = new PngWriter(outputStream, reader.ImgInfo);
            writer.CopyChunksFirst(reader, ChunkCopyBehaviour.COPY_ALL);
            var textChunk = new PngChunkTEXT(reader.ImgInfo);
            string compressedData = CompressAndBase64Encode(workflowJson);
            textChunk.SetKeyVal("prompt", compressedData);
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

        public static byte[] ConvertPngToJpgWithWorkflow(byte[] pngBytes, string workflowJson, int quality)
        {
            using var image = Image.Load(pngBytes);
            using var outputImage = new Image<Rgba32>(image.Width, image.Height, Color.White);
            outputImage.Mutate(x => x.DrawImage(image, 1f));
            var encoder = new JpegEncoder { Quality = quality };
            using var outputStream = new MemoryStream();
            outputImage.Save(outputStream, encoder);
            var jpgBytes = outputStream.ToArray();
            return string.IsNullOrEmpty(workflowJson) ? jpgBytes : AppendWorkflowToBytes(jpgBytes, workflowJson);
        }

        private static byte[] AppendWorkflowToBytes(byte[] originalBytes, string workflowJson)
        {
            if (string.IsNullOrEmpty(workflowJson)) return originalBytes;
            var compressedWorkflowBytes = Compress(workflowJson);
            using var ms = new MemoryStream(originalBytes.Length + MagicMarkerBytes.Length + compressedWorkflowBytes.Length);
            ms.Write(originalBytes, 0, originalBytes.Length);
            ms.Write(MagicMarkerBytes, 0, MagicMarkerBytes.Length);
            ms.Write(compressedWorkflowBytes, 0, compressedWorkflowBytes.Length);
            return ms.ToArray();
        }
        
        public static byte[] EmbedWorkflowInVideo(byte[] videoBytes, string workflowJson)
        {
            if (string.IsNullOrEmpty(workflowJson)) return videoBytes;
            var tempFilePath = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tempFilePath, videoBytes);
                using (var tfile = TagLib.File.Create(tempFilePath))
                {
                    string compressedData = CompressAndBase64Encode(workflowJson);
                    tfile.Tag.Comment = $"prompt:{compressedData}";
                    tfile.Save();
                }
                return File.ReadAllBytes(tempFilePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error embedding workflow in video metadata: {ex.Message}. Appending to file as a fallback.");
                return AppendWorkflowToBytes(videoBytes, workflowJson);
            }
            finally
            {
                if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
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
            foreach (var sp in source.Properties())
            {
                var tp = target.Property(sp.Name);
                if (tp == null) continue;
                if (sp.Value is JObject sourceNested && tp.Value is JObject targetNested) MergeJsonObjects(targetNested, sourceNested);
                else tp.Value = sp.Value;
            }
        }

        public static JProperty? GetJsonPropertyByPath(JObject obj, string path)
        {
            var parts = path.Split('.');
            var current = obj;
            JProperty? prop = null;
            for (int i = 0; i < parts.Length; i++)
            {
                if (i == parts.Length - 1) prop = current.Property(parts[i]);
                else current = (JObject)current[parts[i]];
            }
            return prop;
        }

        public static string? ReadStateFromImage(byte[] imageBytes)
        {
            string? stateJson = ReadPngPrompt(imageBytes);
            if (!string.IsNullOrEmpty(stateJson)) return TryFormatJson(stateJson);

            stateJson = ReadWebpPrompt(imageBytes);
            if (!string.IsNullOrEmpty(stateJson)) return TryFormatJson(stateJson);

            stateJson = ReadAppendedWorkflow(imageBytes);
            if (!string.IsNullOrEmpty(stateJson)) return TryFormatJson(stateJson);
            
            stateJson = ReadVideoWorkflow(imageBytes);
            if (!string.IsNullOrEmpty(stateJson)) return TryFormatJson(stateJson);

            return null;
        }
        
        private static string? ReadAppendedWorkflow(byte[] fileBytes)
        {
            var markerIndex = FindLast(fileBytes, MagicMarkerBytes);
            if (markerIndex == -1) return null;
            try
            {
                var startIndex = markerIndex + MagicMarkerBytes.Length;
                var compressedBytes = new byte[fileBytes.Length - startIndex];
                Array.Copy(fileBytes, startIndex, compressedBytes, 0, compressedBytes.Length);
                return Decompress(compressedBytes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading appended workflow: {ex.Message}");
                return null;
            }
        }
        
        private static string? ReadVideoWorkflow(byte[] videoBytes)
        {
            var tempFilePath = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tempFilePath, videoBytes);
                using var tfile = TagLib.File.Create(tempFilePath);
                var comment = tfile.Tag.Comment;
                if (comment != null && comment.StartsWith("prompt:"))
                {
                    return Base64DecodeAndDecompress(comment.Substring(7));
                }
                return null;
            }
            catch (TagLib.UnsupportedFormatException) { return null; }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading video metadata: {ex.Message}");
                return null;
            }
            finally
            {
                if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
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
                        return Base64DecodeAndDecompress(makeTag.Substring(7));
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"Error reading WebP metadata: {ex.Message}"); }
            return null;
        }

        public static string? ReadPngPrompt(byte[] bytes)
        {
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(new MemoryStream(bytes));
                foreach (var tag in directories.SelectMany(d => d.Tags))
                {
                    if (tag.DirectoryName == "PNG-tEXt" && tag.Name == "Textual Data" && tag.Description.StartsWith("prompt\0"))
                    {
                        var rawData = tag.Description.Substring("prompt\0".Length);
                        return Base64DecodeAndDecompress(rawData);
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"Error reading PNG metadata: {ex.Message}"); }
            return null;
        }

        private static string TryFormatJson(string json)
        {
            try
            {
                dynamic parsedJson = JsonConvert.DeserializeObject(json);
                return JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
            }
            catch { return json; }
        }
        
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
            var random = new Random((int)(seed & 0xFFFFFFFF));
            string pass1 = Regex.Replace(input, @"\{__([^{}]+)__\}", m =>
            {
                var lines = WildcardFileHandler.GetLines(m.Groups[1].Value.Trim());
                return lines.Length == 0 ? m.Value : lines[random.Next(lines.Length)];
            });
            return Regex.Replace(pass1, @"\{([^{}|]+(?:\|[^{}|]+)+)\}", m =>
            {
                var choices = m.Groups[1].Value.Split('|');
                return choices[random.Next(choices.Length)].Trim();
            });
        }
        
        public static string ReplaceWildcards(string input) => ReplaceWildcards(input, DateTime.Now.Ticks);
        
        public static JObject? CleanBase64FromJObject(JObject? originalJson)
        {
            if (originalJson == null) return null;
            var cleanedJson = originalJson.DeepClone() as JObject;
            if (cleanedJson == null) return null;
            var props = cleanedJson.Descendants().OfType<JProperty>().Where(p => p.Value.Type == JTokenType.String).ToList();
            foreach (var p in props)
            {
                var v = p.Value.Value<string>();
                if (!string.IsNullOrEmpty(v) && v.Length > 1000 && (v.StartsWith("iVBOR") || v.StartsWith("/9j/") || v.StartsWith("UklG"))) p.Value = "";
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
            catch (JsonReaderException) { return jsonString; }
        }
    }
}