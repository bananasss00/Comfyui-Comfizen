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
using System.Globalization;
using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;

namespace Comfizen
{
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
                Logger.Log("ffmpeg command not found in PATH. Metadata stripping for videos will be disabled.", LogLevel.Warning);
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
            
            var fileBasedFormats = new[] { "mkv", "mp4" };

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

        public static long GenerateSeed(long min, long max)
        {
            if (min >= max) return min;
            
            Random random = new Random();
            byte[] buf = new byte[8];
            random.NextBytes(buf);
            long longRand = BitConverter.ToInt64(buf, 0);

            return (Math.Abs(longRand % (max - min)) + min);
        }
        
        public static void MergeJsonObjects(JObject target, JObject source)
        {
            if (target == null || source == null) return;
            
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
            if (obj == null)
            {
                Logger.Log($"GetJsonPropertyByPath was called with a null JObject for path: '{path ?? "null"}'.", LogLevel.Warning);
                return null;
            }

            if (string.IsNullOrEmpty(path))
            {
                Logger.Log("GetJsonPropertyByPath was called with a null or empty path.", LogLevel.Warning);
                return null;
            }
            
            if (path.StartsWith("virtual_"))
            {
                return null;
            }

            // ========================================================== //
            //     START OF FIX: Resiliency against incorrect paths       //
            // ========================================================== //
            // This handles legacy workflow files where paths might be incorrectly stored with a "prompt." prefix.
            // We safely remove it before proceeding, as the 'obj' parameter is already the content of "prompt".
            string correctedPath = path;
            if (correctedPath.StartsWith("prompt."))
            {
                correctedPath = correctedPath.Substring("prompt.".Length);
            }
            // ========================================================== //
            //     END OF FIX                                             //
            // ========================================================== //

            var parts = correctedPath.Split('.');
            JToken? currentToken = obj;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                string part = parts[i];
                
                if (currentToken is not JObject currentObj)
                {
                    Logger.Log($"Path traversal failed for '{path}'. The segment '{string.Join(".", parts.Take(i))}' did not resolve to a JObject.", LogLevel.Warning);
                    return null;
                }

                currentToken = currentObj[part];

                if (currentToken == null)
                {
                    Logger.Log($"Path traversal failed for '{path}'. Segment '{part}' not found at path '{string.Join(".", parts.Take(i + 1))}'.", LogLevel.Warning);
                    return null;
                }
            }

            if (currentToken is not JObject finalObj)
            {
                Logger.Log($"Path traversal failed for '{path}'. The final container '{string.Join(".", parts.Take(parts.Length - 1))}' was not a JObject.", LogLevel.Warning);
                return null;
            }

            string finalPart = parts.Last();
            var property = finalObj.Property(finalPart);

            if (property == null)
            {
                Logger.Log($"Property '{finalPart}' not found at the end of path '{path}'.", LogLevel.Warning);
                return null;
            }

            return property;
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
            if (string.IsNullOrEmpty(input)) return input;

            var processor = new WildcardProcessor(seed);
            string result = processor.Process(input);

            // Log the processed prompt if it has changed
            if (result != input)
            {
                Logger.LogToConsole($"[Wildcard] Processed prompt: {result}");
            }

            return result;
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
        
        /// <summary>
        /// Recursively removes all properties named "_meta" from a JObject and its children.
        /// </summary>
        /// <param name="token">The JToken to clean.</param>
        public static void StripAllMetaProperties(JToken token)
        {
            if (token is JObject obj)
            {
                // Remove "_meta" property from the current object
                obj.Remove("_meta");

                // Recursively call for all child properties
                foreach (var prop in obj.Properties().ToList()) // ToList() to avoid modification issues
                {
                    StripAllMetaProperties(prop.Value);
                }
            }
            else if (token is JArray arr)
            {
                // Recursively call for all items in the array
                foreach (var item in arr)
                {
                    StripAllMetaProperties(item);
                }
            }
        }
        
        /// <summary>
        /// Compares two JTokens for equivalence, with special handling for floating-point precision.
        /// </summary>
        /// <param name="t1">The first JToken.</param>
        /// <param name="t2">The second JToken.</param>
        /// <returns>True if the tokens are equivalent, otherwise false.</returns>
        public static bool AreJTokensEquivalent(JToken t1, JToken t2)
        {
            // Define a small tolerance for float comparisons.
            const double epsilon = 1e-6;

            if (t1.Type == JTokenType.Float || t2.Type == JTokenType.Float)
            {
                // If either token is a float, convert both to double for comparison.
                // This correctly handles comparing an integer (e.g., 5) with a float (e.g., 5.0).
                if (double.TryParse(t1.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d1) &&
                    double.TryParse(t2.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d2))
                {
                    // Compare the absolute difference against the tolerance.
                    return Math.Abs(d1 - d2) < epsilon;
                }
            }

            // For all other types (string, boolean, integer vs integer, etc.), use the strict DeepEquals.
            return JToken.DeepEquals(t1, t2);
        }
        
        /// <summary>
        // A helper class to pass grid generation results to the drawing utility.
        /// </summary>
        public class GridCellResult
        {
            public List<ImageOutput> ImageOutputs { get; set; } = new List<ImageOutput>();
            public string XValue { get; set; }
            public string YValue { get; set; }
        }
        

        /// <summary>
        /// A private helper method that contains all the common logic for drawing a grid's background, labels, and borders.
        /// The actual content of each cell is drawn by the 'drawCellAction' delegate.
        /// </summary>
        private static Image<Rgba32> CreateGrid(
            List<GridCellResult> results,
            string xAxisField, IReadOnlyList<string> xValues,
            string yAxisField, IReadOnlyList<string> yValues,
            int cellWidth, int cellHeight,
            Action<IImageProcessingContext, Rectangle, GridCellResult> drawCellAction)
        {
            // --- START OF FIX ---
            // The previous implementation created new GridCellResult objects here,
            // which broke the key-based lookup for pre-extracted frames.
            // This now creates a shallow copy of the list, preserving the original object references
            // to be used as valid keys for the dictionary.
            var resultsPool = new List<GridCellResult>(results);
            // --- END OF FIX ---

            // --- Configuration ---
            var backgroundColor = Color.ParseHex("#3F3F46");
            var textColor = Color.ParseHex("#E0E0E0");
            var lineColor = Color.ParseHex("#2D2D30");
            const int padding = 10;
            const int labelPadding = 8;
            const float fontSize = 14f;
            const float axisFontSize = 16f;

            // --- Font Loading ---
            FontFamily fontFamily;
            try { fontFamily = SystemFonts.Get("Segoe UI"); }
            catch { fontFamily = SystemFonts.Families.FirstOrDefault(); }
            if (fontFamily == null) return null;

            var font = fontFamily.CreateFont(fontSize, FontStyle.Regular);
            var axisFont = fontFamily.CreateFont(axisFontSize, FontStyle.Bold);

            // --- Layout Calculation ---
            bool hasYAxis = yValues.Count > 1 || (yValues.Count == 1 && !string.IsNullOrEmpty(yValues[0]));
            var textMeasureOptions = new TextOptions(font);
            var xLabelMaxHeight = xValues.Select(v => TextMeasurer.MeasureBounds(v, textMeasureOptions).Height).DefaultIfEmpty(0).Max();
            var yLabelMaxWidth = yValues.Select(v => TextMeasurer.MeasureBounds(v, textMeasureOptions).Width).DefaultIfEmpty(0).Max();
            int topLabelAreaHeight = (int)xLabelMaxHeight + labelPadding * 2;
            int leftLabelAreaWidth = hasYAxis ? (int)yLabelMaxWidth + labelPadding * 2 : 0;
            if (hasYAxis)
            {
                var yAxisLabelBounds = TextMeasurer.MeasureBounds("Y: " + yAxisField, new TextOptions(axisFont));
                leftLabelAreaWidth += (int)yAxisLabelBounds.Height + padding;
            }
            int totalWidth = leftLabelAreaWidth + (cellWidth * xValues.Count) + (padding * (xValues.Count + 1));
            int totalHeight = topLabelAreaHeight + (cellHeight * yValues.Count) + (padding * (yValues.Count + 1));

            var canvas = new Image<Rgba32>(totalWidth, totalHeight);
            
            canvas.Mutate(ctx =>
            {
                ctx.Fill(backgroundColor);

                // Draw Axis Labels
                ctx.DrawText(new RichTextOptions(axisFont) { Origin = new PointF(leftLabelAreaWidth + padding, padding) }, "X: " + xAxisField, textColor);
                if (hasYAxis)
                {
                    var yAxisTextOptions = new RichTextOptions(axisFont) { Origin = new PointF(padding + axisFontSize / 2, topLabelAreaHeight + (totalHeight - topLabelAreaHeight) / 2f), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    var rotationMatrix = Matrix3x2.CreateRotation((float)(-90 * (Math.PI / 180.0)), yAxisTextOptions.Origin);
                    ctx.SetDrawingTransform(rotationMatrix);
                    ctx.DrawText(yAxisTextOptions, "Y: " + yAxisField, textColor);
                    ctx.SetDrawingTransform(Matrix3x2.Identity);
                }

                // Draw Value Labels
                var valueLabelOptions = new RichTextOptions(font) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                for (int i = 0; i < xValues.Count; i++) { valueLabelOptions.Origin = new PointF(leftLabelAreaWidth + padding + (i * (cellWidth + padding)) + cellWidth / 2, topLabelAreaHeight / 2); ctx.DrawText(valueLabelOptions, xValues[i], textColor); }
                if (hasYAxis) { for (int i = 0; i < yValues.Count; i++) { valueLabelOptions.Origin = new PointF(leftLabelAreaWidth / 2, topLabelAreaHeight + padding + (i * (cellHeight + padding)) + cellHeight / 2); ctx.DrawText(valueLabelOptions, yValues[i], textColor); } }

                // --- Draw Cell Content using the provided action ---
                for (int yIndex = 0; yIndex < yValues.Count; yIndex++)
                {
                    for (int xIndex = 0; xIndex < xValues.Count; xIndex++)
                    {
                        var currentXValue = xValues[xIndex];
                        var currentYValue = yValues[yIndex];

                        var result = resultsPool.FirstOrDefault(r => r.XValue == currentXValue && r.YValue == currentYValue);

                        if (result != null && result.ImageOutputs.Any())
                        {
                            resultsPool.Remove(result);

                            var xPos = leftLabelAreaWidth + padding + (xIndex * (cellWidth + padding));
                            var yPos = topLabelAreaHeight + padding + (yIndex * (cellHeight + padding));
                            var cellRect = new Rectangle(xPos, yPos, cellWidth, cellHeight);
                            
                            drawCellAction(ctx, cellRect, result);
                            
                            var pen = Pens.Solid(lineColor, 1);
                            var rectangleF = new RectangleF(xPos - 0.5f, yPos - 0.5f, cellWidth + 1, cellHeight + 1);
                            ctx.Draw(pen, rectangleF);
                        }
                    }
                }
            });

            return canvas;
        }
        
        public static byte[] CreateImageGrid(
            List<GridCellResult> results,
            string xAxisField, IReadOnlyList<string> xValues,
            string yAxisField, IReadOnlyList<string> yValues,
            bool limitCellSize, double maxMegapixels)
        {
            if (results == null || !results.Any()) return null;
            
            // --- Image Grid Specifics: Determine cell size from the first image ---
            using var firstImage = Image.Load(results.First().ImageOutputs.First().ImageBytes);
            int cellWidth = firstImage.Width;
            int cellHeight = firstImage.Height;

            // --- START OF ADDED LOGIC: Limit cell size by megapixels ---
            if (limitCellSize && maxMegapixels > 0)
            {
                long maxPixels = (long)(maxMegapixels * 1_000_000);
                long currentPixels = (long)cellWidth * cellHeight;

                if (currentPixels > maxPixels)
                {
                    double scaleRatio = Math.Sqrt((double)maxPixels / currentPixels);
                    cellWidth = (int)(cellWidth * scaleRatio);
                    cellHeight = (int)(cellHeight * scaleRatio);
                }
            }
            // --- END OF ADDED LOGIC ---
            
            // --- Define how to draw a cell for an image grid ---
            Action<IImageProcessingContext, Rectangle, GridCellResult> drawCellAction = (ctx, cellRect, result) =>
            {
                int count = result.ImageOutputs.Count;
                if (count == 0) return;
                
                // If there's only one image, draw it, resized to fit the cell.
                if (count == 1)
                {
                    using var img = Image.Load(result.ImageOutputs[0].ImageBytes);
                    // --- START OF CHANGE: Resize image to fit the calculated cell size ---
                    img.Mutate(x => x.Resize(new ResizeOptions 
                    { 
                        Size = new Size(cellRect.Width, cellRect.Height), 
                        Mode = ResizeMode.Pad, 
                        PadColor = Color.Black 
                    }));
                    // --- END OF CHANGE ---
                    ctx.DrawImage(img, cellRect.Location, 1f);
                    return;
                }
                
                // --- Compact Tiling Logic for multiple images in a cell ---
                int cols = (int)Math.Ceiling(Math.Sqrt(count));
                int rows = (int)Math.Ceiling((double)count / cols);
                int tileWidth = cellRect.Width / cols;
                int tileHeight = cellRect.Height / rows;
                
                for (int i = 0; i < count; i++)
                {
                    using var img = Image.Load(result.ImageOutputs[i].ImageBytes);
                    img.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(tileWidth, tileHeight), Mode = ResizeMode.Pad, PadColor = Color.Black }));
                    
                    int x = cellRect.X + (i % cols) * tileWidth;
                    int y = cellRect.Y + (i / cols) * tileHeight;
                    ctx.DrawImage(img, new Point(x, y), 1f);
                }
            };
            
            // --- Use the common grid creation method ---
            using var canvas = CreateGrid(results, xAxisField, xValues, yAxisField, yValues, cellWidth, cellHeight, drawCellAction);
            if (canvas == null) return null;

            using var ms = new MemoryStream();
            canvas.SaveAsPng(ms);
            return ms.ToArray();
        }
        
        public static async Task<byte[]> CreateVideoGridAsync(
            List<GridCellResult> results,
            string xAxisField, IReadOnlyList<string> xValues,
            string yAxisField, IReadOnlyList<string> yValues,
            int frameCount,
            bool limitCellSize, double maxMegapixels)
        {
            if (results == null || !results.Any() || frameCount <= 0) return null;

            var extractionTasks = new Dictionary<GridCellResult, Task<List<Image<Rgba32>>>>();
            foreach (var result in results)
            {
                var videoToProcess = result.ImageOutputs.FirstOrDefault(io => io.Type == FileType.Video);
                if (videoToProcess != null)
                {
                    extractionTasks[result] = ExtractFramesAsync(videoToProcess.ImageBytes, frameCount);
                }
            }

            await Task.WhenAll(extractionTasks.Values);

            var preExtractedFrames = extractionTasks.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Result 
            );
            
            try
            {
                var firstFrameList = preExtractedFrames.Values.FirstOrDefault(fl => fl != null && fl.Any());
                if (firstFrameList == null) return null;
                
                int frameWidth = firstFrameList[0].Width;
                int frameHeight = firstFrameList[0].Height;
                
                // --- START OF ADDED LOGIC: Limit frame size by megapixels ---
                if (limitCellSize && maxMegapixels > 0)
                {
                    long maxPixels = (long)(maxMegapixels * 1_000_000);
                    long currentPixels = (long)frameWidth * frameHeight;
    
                    if (currentPixels > maxPixels)
                    {
                        double scaleRatio = Math.Sqrt((double)maxPixels / currentPixels);
                        frameWidth = (int)(frameWidth * scaleRatio);
                        frameHeight = (int)(frameHeight * scaleRatio);
                    }
                }
                // --- END OF ADDED LOGIC ---
                
                int cols = (int)Math.Ceiling(Math.Sqrt(frameCount));
                int rows = (int)Math.Ceiling((double)frameCount / cols);
                int cellWidth = frameWidth * cols;
                int cellHeight = frameHeight * rows;
                
                Action<IImageProcessingContext, Rectangle, GridCellResult> drawCellAction = (ctx, cellRect, result) =>
                {
                    if (preExtractedFrames.TryGetValue(result, out var frames) && frames != null)
                    {
                        for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
                        {
                            // --- START OF FIX ---
                            // Create a temporary clone of the frame for modification and drawing.
                            // This prevents modifying the original image object, which caused rendering issues.
                            // The 'using' statement ensures the clone is disposed of immediately after it's drawn.
                            using var frameToDraw = frames[frameIndex].Clone();
                            // --- END OF FIX ---

                            if (frameToDraw.Width != frameWidth || frameToDraw.Height != frameHeight)
                            {
                                frameToDraw.Mutate(i => i.Resize(new ResizeOptions { Size = new Size(frameWidth, frameHeight), Mode = ResizeMode.Pad, PadColor = Color.Black }));
                            }

                            int xOffset = (frameIndex % cols) * frameWidth;
                            int yOffset = (frameIndex / cols) * frameHeight;
                            var drawPoint = new Point(cellRect.X + xOffset, cellRect.Y + yOffset);
                            
                            // Draw the clone, not the original frame.
                            ctx.DrawImage(frameToDraw, drawPoint, 1f);
                        }
                    }
                };
                
                using var canvas = CreateGrid(results, xAxisField, xValues, yAxisField, yValues, cellWidth, cellHeight, drawCellAction);
                if (canvas == null) return null;

                using var ms = new MemoryStream();
                await canvas.SaveAsPngAsync(ms);
                return ms.ToArray();
            }
            finally
            {
                // This final cleanup of the original pre-loaded images remains correct.
                foreach (var frameList in preExtractedFrames.Values)
                {
                    if (frameList != null)
                    {
                        foreach (var frame in frameList)
                        {
                            frame.Dispose();
                        }
                    }
                }
            }
        }

        public static async Task<List<Image<Rgba32>>> ExtractFramesAsync(byte[] videoBytes, int frameCount)
        {
            if (!IsFfmpegAvailable() || videoBytes == null || videoBytes.Length == 0 || frameCount <= 0)
            {
                return null;
            }

            var tempInputFile = Path.GetTempFileName();
            var frames = new List<Image<Rgba32>>();

            try
            {
                await File.WriteAllBytesAsync(tempInputFile, videoBytes);

                double duration = await GetVideoDurationAsync(tempInputFile);
                if (duration <= 0) return null;

                // --- START OF ROBUST IMPLEMENTATION ---
                double effectiveDuration = Math.Max(duration, 0.1);
                double frameRate = frameCount / effectiveDuration;
                string frameRateString = frameRate.ToString(CultureInfo.InvariantCulture);

                string ffmpegArgs = $"-i \"{tempInputFile}\" -vf \"fps={frameRateString}\" -vframes {frameCount} -f image2pipe -vcodec png -";
                
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = ffmpegArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = new Process { StartInfo = processStartInfo })
                using (var outputStream = new MemoryStream())
                {
                    process.Start();

                    // Step 1: Asynchronously copy the entire standard output to a memory stream.
                    // Do not try to parse it live.
                    var outputReaderTask = process.StandardOutput.BaseStream.CopyToAsync(outputStream);
                    var errorReaderTask = process.StandardError.ReadToEndAsync();

                    // Wait for the process to exit AND for the stream copying to complete.
                    await Task.WhenAll(outputReaderTask, errorReaderTask);
                    process.WaitForExit();

                    var errorOutput = await errorReaderTask;
                    if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(errorOutput))
                    {
                        // Log errors, but don't immediately fail, as ffmpeg might still have produced frames.
                        Logger.Log($"ffmpeg process exited with code {process.ExitCode}. Error: {errorOutput}", LogLevel.Warning);
                    }

                    // Step 2: Now that we have all the data buffered, parse it safely.
                    if (outputStream.Length > 0)
                    {
                        outputStream.Position = 0; // Rewind the stream to the beginning.
                        
                        // Loop through the buffered data, loading one image at a time.
                        while (outputStream.Position < outputStream.Length)
                        {
                            try
                            {
                                // Image.Load will read one complete image and advance the stream's position automatically.
                                var frame = Image.Load<Rgba32>(outputStream);
                                frames.Add(frame);
                            }
                            catch (Exception ex)
                            {
                                // If parsing fails here, it's likely due to trailing non-image data from ffmpeg.
                                // We can break the loop as we've probably read all valid frames.
                                Logger.Log(ex, "Failed to parse a frame from the buffered output. This can happen if ffmpeg writes text at the end of the stream. Stopping frame read.");
                                break; 
                            }
                        }
                    }
                }
                
                // --- END OF ROBUST IMPLEMENTATION ---
                
                return frames.Take(frameCount).ToList();
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Exception during ffmpeg frame extraction.");
                return null;
            }
            finally
            {
                if (File.Exists(tempInputFile))
                {
                    try { File.Delete(tempInputFile); } catch {}
                }
            }
        }
        
        private static async Task<double> GetVideoDurationAsync(string videoPath)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = new Process { StartInfo = processStartInfo })
            {
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                process.WaitForExit();

                if (process.ExitCode == 0 && double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double duration))
                {
                    return duration;
                }
                
                Logger.Log($"ffprobe failed to get duration for '{videoPath}'. Error: {error}", LogLevel.Error);
                return -1;
            }
        }
        
        /// <summary>
        /// Computes a 64-bit average hash (aHash) for an image.
        /// This hash represents the basic structure of the image and can be used for similarity comparison.
        /// </summary>
        /// <param name="imageBytes">The byte array of the image.</param>
        /// <returns>A 64-bit perceptual hash, or 0 if an error occurs.</returns>
        public static ulong ComputeAverageHash(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0) return 0;

            try
            {
                using (var image = Image.Load<L8>(imageBytes))
                {
                    // 1. Resize to a small, fixed size (8x8)
                    image.Mutate(x => x.Resize(8, 8));

                    // 2. Calculate the average pixel value
                    long totalValue = 0;
                    image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var row = accessor.GetRowSpan(y);
                            for (int x = 0; x < row.Length; x++)
                            {
                                totalValue += row[x].PackedValue;
                            }
                        }
                    });
                    byte avgValue = (byte)(totalValue / 64);

                    // 3. Create the hash: 1 if pixel > average, 0 otherwise
                    ulong hash = 0;
                    image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var row = accessor.GetRowSpan(y);
                            for (int x = 0; x < row.Length; x++)
                            {
                                if (row[x].PackedValue >= avgValue)
                                {
                                    hash |= 1UL << (y * 8 + x);
                                }
                            }
                        }
                    });

                    return hash;
                }
            }
            catch
            {
                // If ImageSharp fails to load the image, return a zero hash.
                return 0;
            }
        }

        /// <summary>
        /// Computes a 64-bit average hash for a video by creating a tiled thumbnail image with ffmpeg.
        /// </summary>
        /// <param name="videoBytes">The byte array of the video file.</param>
        /// <returns>A 64-bit perceptual hash, or 0 if ffmpeg is unavailable or an error occurs.</returns>
        public static async Task<ulong> ComputeVideoPerceptualHashAsync(byte[] videoBytes)
        {
            if (!IsFfmpegAvailable() || videoBytes == null || videoBytes.Length == 0)
            {
                return 0;
            }
            
            var tempInputFile = Path.GetTempFileName();
            
            // This ffmpeg command samples the video at 1 frame per second,
            // scales each frame, and creates up to a 3x3 tiled image from the first 9 resulting frames.
            // This provides a good visual summary for perceptual hashing.
            string ffmpegArgs = $"-i \"{tempInputFile}\" -vf \"fps=1,scale=96:-1,tile=3x3\" -vframes 1 -f image2pipe -vcodec png -";
        
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = ffmpegArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = false, // We no longer write to stdin
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
        
            try
            {
                await File.WriteAllBytesAsync(tempInputFile, videoBytes);

                using (var process = new Process { StartInfo = processStartInfo })
                using (var outputStream = new MemoryStream())
                {
                    process.Start();
        
                    // We no longer need the inputTask as we are using a file
                    var outputTask = process.StandardOutput.BaseStream.CopyToAsync(outputStream);
                    var errorTask = process.StandardError.ReadToEndAsync();
        
                    await Task.WhenAll(outputTask, errorTask);
                    
                    process.WaitForExit();
                    var errorOutput = await errorTask;
        
                    if (process.ExitCode != 0)
                    {
                        Logger.Log($"ffmpeg for video hash failed. Error: {errorOutput}", LogLevel.Error);
                        return 0; // Return 0 on failure
                    }
        
                    if (outputStream.Length > 0)
                    {
                        // Hash the generated PNG tile image
                        return ComputeAverageHash(outputStream.ToArray());
                    }
        
                    return 0; // No output from ffmpeg
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Exception during video perceptual hash computation.");
                return 0;
            }
            finally
            {
                // Ensure the temporary file is always deleted
                if (File.Exists(tempInputFile))
                {
                    try { File.Delete(tempInputFile); } catch {}
                }
            }
        }

        public static int CalculateHammingDistance(ulong hash1, ulong hash2)
        {
            ulong xor = hash1 ^ hash2;
            int distance = 0;
            while (xor > 0)
            {
                // This clears the least significant set bit
                xor &= (xor - 1);
                distance++;
            }
            return distance;
        }
    }
}