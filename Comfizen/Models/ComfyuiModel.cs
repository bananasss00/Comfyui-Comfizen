using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Comfizen
{

    public class ComfyuiModel
    {
        // --- НАЧАЛО ИЗМЕНЕНИЯ ---
        private readonly AppSettings _settings;

        public ComfyuiModel(AppSettings settings)
        {
            _settings = settings;
        }
        // --- КОНЕЦ ИЗМЕНЕНИЯ ---

        public async Task Interrupt()
        {
            // --- НАЧАЛО ИЗМЕНЕНИЯ ---
            var api = new ComfyUI_API(_settings.ServerAddress);
            // --- КОНЕЦ ИЗМЕНЕНИЯ ---
            await api.InterruptAsync();
        }
        
        public async Task SaveImageFileAsync(string saveDirectory, byte[] sourcePngBytes, string prompt, AppSettings settings)
        {
            Directory.CreateDirectory(saveDirectory);
        
            byte[] finalImageBytes;
            string extension;
            bool saveJsonSeparately = false;

            string promptToEmbed = settings.SavePromptWithFile ? prompt : null;

            switch (settings.SaveFormat)
            {
                case ImageSaveFormat.Png:
                    finalImageBytes = Utils.ConvertPngToPngWithWorkflow(sourcePngBytes, promptToEmbed, settings.PngCompressionLevel);
                    extension = ".png";
                    break;
                
                case ImageSaveFormat.Jpg:
                    finalImageBytes = Utils.ConvertPngToJpg(sourcePngBytes, settings.JpgQuality);
                    extension = ".jpg";
                    if (!string.IsNullOrEmpty(promptToEmbed))
                    {
                        saveJsonSeparately = true;
                    }
                    break;
                
                case ImageSaveFormat.Webp:
                default:
                    finalImageBytes = Utils.ConvertPngToWebpWithWorkflow(sourcePngBytes, promptToEmbed, settings.WebpQuality);
                    extension = ".webp";
                    break;
            }

            var md5 = Utils.ComputeMd5Hash(finalImageBytes);
            var savePath = Path.Combine(saveDirectory, md5 + extension);

            if (File.Exists(savePath))
                return;

            await File.WriteAllBytesAsync(savePath, finalImageBytes);

            if (saveJsonSeparately)
            {
                var jsonSavePath = Path.Combine(saveDirectory, md5 + ".json");
                await File.WriteAllTextAsync(jsonSavePath, prompt);
            }
        }
        
        public async Task SaveVideoFile(string saveDirectory, byte[] videoBytes, string originalFileName, string prompt)
        {
            Directory.CreateDirectory(saveDirectory);
    
            var videoBytesWithWorkflow = string.IsNullOrEmpty(prompt) 
                ? videoBytes 
                : Utils.EmbedWorkflowInVideo(videoBytes, prompt);
            
            var md5 = Utils.ComputeMd5Hash(videoBytesWithWorkflow);
    
            var extension = Path.GetExtension(originalFileName);
            var savePath = Path.Combine(saveDirectory, md5 + extension);
            var jsonSavePath = Path.Combine(saveDirectory, md5 + ".json");

            if (File.Exists(savePath))
                return;
            
            await File.WriteAllBytesAsync(savePath, videoBytesWithWorkflow);
            
            if (!string.IsNullOrEmpty(prompt))
            {
                await File.WriteAllTextAsync(jsonSavePath, prompt);
            }
        }

        public async IAsyncEnumerable<ImageOutput> QueuePrompt(string json)
        {
            // --- НАЧАЛО ИЗМЕНЕНИЯ ---
            var api = new ComfyUI_API(_settings.ServerAddress);
            // --- КОНЕЦ ИЗМЕНЕНИЯ ---
            await api.Connect();

            var result = await api.GetImagesAsync(json);

            foreach (var kv in result)
            {
                foreach (var fileOutput in kv.Value)
                {
                    string prompt = null;
                    if (fileOutput.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        prompt = Utils.ReadPngPrompt(fileOutput.Data);
                    }
                    else
                    {
                        prompt = json;
                    }
                    
                    if (string.IsNullOrEmpty(prompt))
                    {
                        prompt = json;
                    }
                    
                    var isVideo = new[] { ".mp4", ".mov", ".avi", ".mkv", ".webm", ".gif" }
                        .Any(ext => fileOutput.FileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
                    yield return new ImageOutput
                    {
                        ImageBytes = fileOutput.Data,
                        FileName = fileOutput.FileName,
                        Prompt = prompt,
                        VisualHash = isVideo ? Utils.ComputeMd5Hash(fileOutput.Data) : Utils.ComputePixelHash(fileOutput.Data),
                        FilePath = fileOutput.FilePath
                    };
                }
            }
        }
    }
}