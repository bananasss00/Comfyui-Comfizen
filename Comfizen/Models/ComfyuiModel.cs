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
        private readonly AppSettings _settings;

        public ComfyuiModel(AppSettings settings)
        {
            _settings = settings;
        }

        public async Task Interrupt()
        {
            var api = new ComfyUI_API(_settings.ServerAddress);
            await api.InterruptAsync();
        }
        

        public async Task SaveImageFileAsync(string saveDirectory, string relativeFilePath, byte[] sourcePngBytes, string prompt, AppSettings settings)
        {
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

            // Construct the desired full path including subdirectories
            var desiredPath = Path.Combine(saveDirectory, relativeFilePath);
            
            // Ensure the target directory exists
            var targetDirectory = Path.GetDirectoryName(desiredPath);
            Directory.CreateDirectory(targetDirectory);

            // Get the final, unique path for the image
            desiredPath = Path.ChangeExtension(desiredPath, extension);
            var finalSavePath = await Utils.GetUniqueFilePathAsync(desiredPath, finalImageBytes);

            if (finalSavePath == null)
            {
                // File with same content already exists, so we do nothing.
                return;
            }

            await File.WriteAllBytesAsync(finalSavePath, finalImageBytes);

            if (saveJsonSeparately && !string.IsNullOrEmpty(prompt))
            {
                var jsonSavePath = Path.ChangeExtension(finalSavePath, ".json");
                await File.WriteAllTextAsync(jsonSavePath, prompt);
            }
        }
        

        public async Task SaveVideoFileAsync(string saveDirectory, string relativeFilePath, byte[] videoBytes, string prompt)
        {
            // Construct the desired full path including subdirectories
            var desiredPath = Path.Combine(saveDirectory, relativeFilePath);
            
            // Ensure the target directory exists
            var targetDirectory = Path.GetDirectoryName(desiredPath);
            Directory.CreateDirectory(targetDirectory);
    
            var videoBytesWithWorkflow = string.IsNullOrEmpty(prompt) 
                ? videoBytes 
                : Utils.EmbedWorkflowInVideo(videoBytes, prompt);

            // Get the final, unique path for the video
            var finalSavePath = await Utils.GetUniqueFilePathAsync(desiredPath, videoBytesWithWorkflow);
            
            if (finalSavePath == null)
            {
                // File with same content already exists, do nothing.
                return;
            }
            
            await File.WriteAllBytesAsync(finalSavePath, videoBytesWithWorkflow);
            
            if (!string.IsNullOrEmpty(prompt))
            {
                var jsonSavePath = Path.ChangeExtension(finalSavePath, ".json");
                await File.WriteAllTextAsync(jsonSavePath, prompt);
            }
        }


        public async IAsyncEnumerable<ImageOutput> QueuePrompt(string json)
        {
            var api = new ComfyUI_API(_settings.ServerAddress);
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