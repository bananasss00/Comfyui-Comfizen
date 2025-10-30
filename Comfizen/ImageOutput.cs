using PropertyChanged;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace Comfizen
{
    public enum FileType { Image, Video }

    // Добавляем интерфейс для уведомлений об изменении свойств
    [AddINotifyPropertyChangedInterface]
    public class ImageOutput : INotifyPropertyChanged
    {
        private static readonly string[] VideoExtensions = { ".mp4", ".mov", ".avi", ".mkv", ".webm", ".gif" };

        public ImageOutput()
        {
            PropertyChanged += (sender, args) => { }; 
        }
        
        public byte[] ImageBytes { get; set; }
        public string FileName { get; set; }
        public string Prompt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string VisualHash { get; set; }
        public string FilePath { get; set; }
        public string NodeId { get; set; }
        public bool IsSaved { get; set; }
        
        /// <summary>
        /// A 64-bit perceptual hash of the image, used for similarity comparison.
        /// </summary>
        public ulong PerceptualHash { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        
        private string _resolution;
        public string Resolution
        {
            get
            {
                if (_resolution == null)
                {
                    if (Type == FileType.Image && ImageBytes != null)
                    {
                        try
                        {
                            using var ms = new MemoryStream(ImageBytes);
                            var frame = BitmapFrame.Create(ms, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                            _resolution = $"{frame.PixelWidth}x{frame.PixelHeight}";
                        }
                        catch { _resolution = string.Empty; }
                    }
                    else
                    {
                        _resolution = string.Empty;
                    }
                }
                return _resolution;
            }
            set
            {
                _resolution = value;
                // PropertyChanged.Fody automatically calls notification
            }
        }
        
        public FileType Type => VideoExtensions.Any(ext => FileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            ? FileType.Video
            : FileType.Image;
        
        private BitmapImage _image;
        private bool _isImageLoading = false;
        
        [JsonIgnore]
        public BitmapImage Image
        {
            get
            {
                if (_image != null)
                {
                    return _image;
                }

                if (!_isImageLoading && Type == FileType.Image && ImageBytes != null)
                {
                    _isImageLoading = true;
                    Task.Run(() =>
                    {
                        try
                        {
                            var image = ByteArrayToImage(ImageBytes);
                            image.Freeze(); // Make it thread-safe before passing to UI thread
                        
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                _image = image;
                                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
                                _isImageLoading = false;
                            });
                        }
                        catch (Exception ex)
                        {
                            Logger.Log(ex, $"Failed to decode image thumbnail: {FileName}");
                            Application.Current.Dispatcher.Invoke(() => _isImageLoading = false);
                        }
                    });
                }

                return null; // Return null initially, UI will update via PropertyChanged when ready
            }
        }
        
        /// <summary>
        /// Registers the video with the in-memory server and returns a unique local URL for playback.
        /// </summary>
        public Uri GetHttpUri()
        {
            if (Type != FileType.Video || ImageBytes == null)
                return null;

            try
            {
                return InMemoryHttpServer.Instance.RegisterMedia(ImageBytes, FileName);
            }
            catch (Exception ex)
            {
                Logger.Log(ex, $"Failed to register video with InMemoryHttpServer: {FileName}");
                return null;
            }
        }

        private BitmapImage ByteArrayToImage(byte[] byteArrayIn)
        {
            var ms = new MemoryStream(byteArrayIn);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            return image;
        }
    }
}