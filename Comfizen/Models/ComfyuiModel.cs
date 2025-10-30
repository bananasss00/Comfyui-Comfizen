using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

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
            string promptToEmbed = settings.SavePromptWithFile ? prompt : null;

            if (settings.RemoveBase64OnSave && !string.IsNullOrEmpty(promptToEmbed))
            {
                promptToEmbed = Utils.CleanBase64FromString(promptToEmbed);
            }

            var (finalImageBytes, extension) = await Task.Run(() =>
            {
                byte[] bytes;
                string ext;
                switch (settings.SaveFormat)
                {
                    case ImageSaveFormat.Png:
                        var pngEncoder = new PngEncoder { CompressionLevel = (PngCompressionLevel)settings.PngCompressionLevel };
                        bytes = Utils.ProcessImageAndAppendWorkflow(sourcePngBytes, promptToEmbed, pngEncoder);
                        ext = ".png";
                        break;
                
                    case ImageSaveFormat.Jpg:
                        var jpgEncoder = new JpegEncoder { Quality = settings.JpgQuality };
                        bytes = Utils.ProcessImageAndAppendWorkflow(sourcePngBytes, promptToEmbed, jpgEncoder);
                        ext = ".jpg";
                        break;
                
                    case ImageSaveFormat.Webp:
                    default:
                        var webpEncoder = new WebpEncoder { Quality = settings.WebpQuality };
                        bytes = Utils.ProcessImageAndAppendWorkflow(sourcePngBytes, promptToEmbed, webpEncoder);
                        ext = ".webp";
                        break;
                }
                return (bytes, ext);
            });

            var desiredPath = Path.Combine(saveDirectory, relativeFilePath);
            var targetDirectory = Path.GetDirectoryName(desiredPath);
            Directory.CreateDirectory(targetDirectory);

            desiredPath = Path.ChangeExtension(desiredPath, extension);
            var finalSavePath = await Utils.GetUniqueFilePathAsync(desiredPath, finalImageBytes);

            if (finalSavePath == null)
            {
                return;
            }

            await File.WriteAllBytesAsync(finalSavePath, finalImageBytes);
        }
        
        public async Task SaveVideoFileAsync(string saveDirectory, string relativeFilePath, byte[] videoBytes, string prompt)
        {
            var desiredPath = Path.Combine(saveDirectory, relativeFilePath);
            var targetDirectory = Path.GetDirectoryName(desiredPath);
            Directory.CreateDirectory(targetDirectory);
    
            var processedVideoBytes = await Utils.StripVideoMetadataAsync(videoBytes, relativeFilePath);
            
            string promptToProcess = prompt;
            if (_settings.RemoveBase64OnSave && !string.IsNullOrEmpty(promptToProcess))
            {
                promptToProcess = Utils.CleanBase64FromString(promptToProcess);
            }

            var videoBytesWithWorkflow = Utils.EmbedWorkflowInVideo(processedVideoBytes, promptToProcess);

            var finalSavePath = await Utils.GetUniqueFilePathAsync(desiredPath, videoBytesWithWorkflow);
            
            if (finalSavePath == null)
            {
                return;
            }
            
            await File.WriteAllBytesAsync(finalSavePath, videoBytesWithWorkflow);
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
                    string prompt = Utils.ReadStateFromImage(fileOutput.Data) ?? json;
                    
                    var isVideo = new[] { ".mp4", ".mov", ".avi", ".mkv", ".webm", ".gif" }
                        .Any(ext => fileOutput.FileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
                        
                    yield return new ImageOutput
                    {
                        ImageBytes = fileOutput.Data,
                        FileName = fileOutput.FileName,
                        Prompt = prompt,
                        VisualHash = isVideo ? Utils.ComputeMd5Hash(fileOutput.Data) : Utils.ComputePixelHash(fileOutput.Data),
                        FilePath = fileOutput.FilePath,
                        NodeId = kv.Key
                    };
                }
            }
        }
    }
}