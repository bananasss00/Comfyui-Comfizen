using MetadataExtractor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
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
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using Directory = System.IO.Directory;
using System.ComponentModel;

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
        
        private static bool? _isFfmpegAvailable;

        public static bool IsFfmpegAvailable()
        {
            if (_isFfmpegAvailable.HasValue)
            {
                return _isFfmpegAvailable.Value;
            }

            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "ffmpeg";
                    process.StartInfo.Arguments = "-version";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();
                    process.WaitForExit();
                    _isFfmpegAvailable = process.ExitCode == 0;
                }
            }
            catch (Win32Exception)
            {
                _isFfmpegAvailable = false;
                Logger.Log("ffmpeg command not found in PATH. Metadata stripping for videos will be disabled.");
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "An unexpected error occurred while checking for ffmpeg");
                _isFfmpegAvailable = false;
            }

            return _isFfmpegAvailable.Value;
        }
        
        public static async Task<byte[]> StripVideoMetadataAsync(byte[] videoBytes, string originalFileName)
        {
            if (!IsFfmpegAvailable())
            {
                return videoBytes;
            }

            var extension = Path.GetExtension(originalFileName)?.TrimStart('.');
            if (string.IsNullOrEmpty(extension))
            {
                Logger.Log($"Could not determine file extension for metadata stripping of file '{originalFileName}'. Skipping.");
                return videoBytes;
            }
            
            var fileBasedFormats = new[] { "mkv" };

            if (fileBasedFormats.Contains(extension.ToLowerInvariant()))
            {
                var tempInputFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + "." + extension);
                var tempOutputFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + "." + extension);

                try
                {
                    await File.WriteAllBytesAsync(tempInputFile, videoBytes);

                    var processStartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-y -i \"{tempInputFile}\" -map_metadata -1 -c copy \"{tempOutputFile}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true
                    };
                    
                    using (var process = new Process { StartInfo = processStartInfo })
                    {
                        process.Start();
                        string error = await process.StandardError.ReadToEndAsync();
                        await process.WaitForExitAsync();

                        if (process.ExitCode != 0)
                        {
                            Logger.Log($"ffmpeg (file-based) failed with exit code {process.ExitCode} for '{originalFileName}'. Error: {error}");
                            return videoBytes;
                        }

                        if (File.Exists(tempOutputFile) && new FileInfo(tempOutputFile).Length > 0)
                        {
                            return await File.ReadAllBytesAsync(tempOutputFile);
                        }
                        
                        Logger.Log($"ffmpeg (file-based) stripping did not create an output file for '{originalFileName}'. Error: {error}");
                        return videoBytes;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, $"Exception during file-based ffmpeg metadata stripping for '{originalFileName}'");
                    return videoBytes;
                }
                finally
                {
                    try { if (File.Exists(tempInputFile)) File.Delete(tempInputFile); } catch {}
                    try { if (File.Exists(tempOutputFile)) File.Delete(tempOutputFile); } catch {}
                }
            }
            else
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-f {extension} -i - -map_metadata -1 -c copy -movflags isml+frag_keyframe -f {extension} -",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                try
                {
                    using (var process = new Process { StartInfo = processStartInfo })
                    using (var outputStream = new MemoryStream())
                    {
                        process.Start();

                        var inputTask = process.StandardInput.BaseStream.WriteAsync(videoBytes, 0, videoBytes.Length).ContinueWith(_ => process.StandardInput.Close());
                        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(outputStream);
                        var errorTask = process.StandardError.ReadToEndAsync();

                        await Task.WhenAll(inputTask, outputTask, errorTask);
                        
                        process.WaitForExit();
                        var errorOutput = await errorTask;

                        if (process.ExitCode != 0)
                        {
                            Logger.Log($"ffmpeg (stream-based) failed for '{originalFileName}'. Error: {errorOutput}");
                            return videoBytes;
                        }
                        
                        if (outputStream.Length > 0)
                        {
                            return outputStream.ToArray();
                        }
                        
                        Logger.Log($"ffmpeg (stream-based) produced no output for '{originalFileName}'. Error: {errorOutput}");
                        return videoBytes;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, $"Exception during stream-based ffmpeg metadata stripping for '{originalFileName}'");
                    return videoBytes;
                }
            }
        }
        
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
        
        public static byte[] ProcessImageAndAppendWorkflow(byte[] sourceImageBytes, string workflowJson, IImageEncoder encoder)
        {
            byte[] processedImageBytes;

            using (var image = Image.Load(sourceImageBytes))
            {
                using (var ms = new MemoryStream())
                {
                    if (encoder is JpegEncoder)
                    {
                        using (var cleanImage = new Image<Rgb24>(image.Width, image.Height))
                        {
                            cleanImage.Mutate(ctx => ctx.BackgroundColor(Color.White).DrawImage(image, 1f));
                            cleanImage.Save(ms, encoder);
                        }
                    }
                    else
                    {
                        using (var cleanImage = new Image<Rgba32>(image.Width, image.Height))
                        {
                            cleanImage.Mutate(ctx => ctx.DrawImage(image, 1f));
                            cleanImage.Save(ms, encoder);
                        }
                    }
                    processedImageBytes = ms.ToArray();
                }
            }

            return AppendWorkflowToBytes(processedImageBytes, workflowJson);
        }

        public static byte[] EmbedWorkflowInVideo(byte[] videoBytes, string workflowJson)
        {
            return AppendWorkflowToBytes(videoBytes, workflowJson);
        }

        private static byte[] AppendWorkflowToBytes(byte[] originalBytes, string workflowJson)
        {
            if (string.IsNullOrEmpty(workflowJson)) return originalBytes;

            var compressedWorkflowBytes = Compress(workflowJson);
            
            using (var ms = new MemoryStream(originalBytes.Length + MagicMarkerBytes.Length + compressedWorkflowBytes.Length))
            {
                ms.Write(originalBytes, 0, originalBytes.Length);
                ms.Write(MagicMarkerBytes, 0, MagicMarkerBytes.Length);
                ms.Write(compressedWorkflowBytes, 0, compressedWorkflowBytes.Length);
                return ms.ToArray();
            }
        }

        public static string? ReadStateFromImage(byte[] fileBytes)
        {
            var json = ReadAppendedWorkflow(fileBytes);
            return string.IsNullOrEmpty(json) ? null : TryFormatJson(json);
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
                
                try
                {
                    return Decompress(compressedBytes);
                }
                catch
                {
                    return Encoding.UTF8.GetString(compressedBytes);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Error reading appended workflow");
                return null;
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
                return cleanedJObject?.ToString(Formatting.None); // Save compressed
            }
            catch (JsonReaderException) { return jsonString; }
        }
    }
}