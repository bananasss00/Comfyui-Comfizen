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

        /// <summary>
        /// Конвертирует PNG в WebP, встраивая предоставленный workflow в EXIF-метаданные.
        /// </summary>
        /// <param name="pngBytes">Байты исходного PNG-изображения.</param>
        /// <param name="workflowJson">Строка JSON с рабочим процессом (workflow).</param>
        /// <param name="quality">Качество WebP от 1 до 100.</param>
        /// <returns>Массив байт с итоговым WebP-изображением.</returns>
        public static byte[] ConvertPngToWebpWithWorkflow(byte[] pngBytes, string workflowJson, int quality = 83)
        {
            // Загружаем изображение с помощью ImageSharp
            using var image = Image.Load(pngBytes);

            if (!string.IsNullOrEmpty(workflowJson))
            {
                image.Metadata.ExifProfile ??= new SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifProfile();
                string workflowData = $"prompt:{workflowJson}";
                image.Metadata.ExifProfile.SetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Make, workflowData);
            }

            // --- НАЧАЛО ИСПРАВЛЕНИЯ ---
            // Создаем энкодер для WebP. 
            // EXIF-профиль уже прикреплен к объекту 'image', и метод .Save()
            // автоматически использует его. Передавать его в энкодер не нужно.
            var encoder = new WebpEncoder
            {
                Quality = quality,
                FileFormat = WebpFileFormatType.Lossy,
            };
            // --- КОНЕЦ ИСПРАВЛЕНИЯ ---

            // Сохраняем изображение в поток в памяти
            using var outputStream = new MemoryStream();
            image.Save(outputStream, encoder);

            return outputStream.ToArray();
        }
        
        /// <summary>
        /// Встраивает workflow в PNG, используя библиотеку Pngcs для гарантированного создания НЕ СЖАТОГО tEXt чанка.
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
                    // --- НАЧАЛО ИСПРАВЛЕНИЯ ---
                    // Передаем свойство .Scanline, которое имеет тип int[]
                    writer.WriteRow(line.Scanline);
                    // --- КОНЕЦ ИСПРАВЛЕНИЯ ---
                }

                writer.CopyChunksLast(reader, ChunkCopyBehaviour.COPY_ALL);

                reader.End();
                writer.End();

                return outputStream.ToArray();
            }
        }

        /// <summary>
        /// Конвертирует PNG в JPG с заданным качеством.
        /// </summary>
        /// <param name="pngBytes">Байты исходного PNG-изображения.</param>
        /// <param name="quality">Качество JPG от 1 до 100.</param>
        /// <returns>Массив байт с итоговым JPG-изображением.</returns>
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
            // TagLib-Sharp работает с файлами, поэтому нам нужен временный файл.
            string tempFilePath = Path.GetTempFileName();
            try
            {
                // 1. Записываем исходные байты во временный файл.
                File.WriteAllBytes(tempFilePath, videoBytes);

                // 2. Открываем файл с помощью TagLib-Sharp.
                using (var tfile = TagLib.File.Create(tempFilePath))
                {
                    // 3. Формируем строку и записываем ее в стандартное поле "Comment".
                    // Это наиболее универсальное поле для хранения произвольных данных.
                    tfile.Tag.Comment = $"prompt:{workflowJson}";

                    // 4. Сохраняем изменения в файл.
                    tfile.Save();
                }

                // 5. Считываем измененные байты обратно в память.
                byte[] updatedVideoBytes = File.ReadAllBytes(tempFilePath);
        
                return updatedVideoBytes;
            }
            catch (Exception ex)
            {
                // В случае ошибки (например, неподдерживаемый формат видео)
                // просто возвращаем исходные байты, чтобы не прерывать сохранение.
                Debug.WriteLine($"Error embedding workflow in video: {ex.Message}");
                return videoBytes;
            }
            finally
            {
                // 6. Обязательно удаляем временный файл.
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
            return Math.Abs(BitConverter.ToInt64(buffer, 0)); // seed 0-9223372036854775807
        }
        
        /// <summary>
        /// Рекурсивно объединяет два JObject. Обновляет значения в 'target' из 'source'.
        /// </summary>
        /// <param name="target">Основной JObject (из файла workflow), который будет изменен.</param>
        /// <param name="source">JObject с данными (из файла сессии), которые нужно применить.</param>
        public static void MergeJsonObjects(JObject target, JObject source)
        {
            foreach (var sourceProperty in source.Properties())
            {
                // Ищем свойство с таким же именем в целевом объекте
                var targetProperty = target.Property(sourceProperty.Name);

                // Если свойство не найдено в цели, пропускаем его (чтобы не добавлять устаревшие поля из сессии)
                if (targetProperty == null)
                {
                    continue;
                }

                // Если оба значения являются вложенными объектами, вызываем слияние рекурсивно
                if (sourceProperty.Value is JObject sourceNestedObj && targetProperty.Value is JObject targetNestedObj)
                {
                    MergeJsonObjects(targetNestedObj, sourceNestedObj);
                }
                // В противном случае просто заменяем значение
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
                    if (tag.DirectoryName == "PNG-tEXt" && tag.Name == "Textual Data") // Assuming 'prompt' corresponds to 'UserComment'
                    {
                        string workflow = tag.Description.Substring(7); // remove 'prompt: ' at string start
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
        /// Supports file-based wildcards like {__colors/bright__} and inline choices like {red|green|blue}.
        /// </summary>
        /// <param name="input">The string containing wildcards.</param>
        /// <param name="seed">The seed for the random number generator.</param>
        /// <returns>A new string with wildcards replaced.</returns>
        public static string ReplaceWildcards(string input, long seed)
        {
            Random random = new Random((int)(seed & 0xFFFFFFFF));

            // First pass: replace file-based wildcards {__name__}
            string pass1 = Regex.Replace(input, @"\{__([^{}]+)__\}", match =>
            {
                string wildcardName = match.Groups[1].Value.Trim();
                string[] lines = WildcardFileHandler.GetLines(wildcardName);
                if (lines == null || lines.Length == 0)
                {
                    return match.Value; // Keep original tag if file not found or empty
                }
                return lines[random.Next(lines.Length)];
            });

            // Second pass: replace inline choices {a|b|c}
            string pass2 = Regex.Replace(pass1, @"\{([^{}|]+(?:\|[^{}|]+)+)\}", match =>
            {
                string[] choices = match.Groups[1].Value.Split('|');
                return choices[random.Next(choices.Length)].Trim();
            });

            return pass2;
        }
        
        public static string ReplaceWildcards(string input)
        {
            // Call the seeded version with a time-based seed for non-reproducible randomness
            // The seed is cast to int to fit the Random constructor.
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

    }
}