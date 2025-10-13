// ImageOutput.cs

using PropertyChanged;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using FFmpeg.AutoGen;
using Unosquare.FFME.Common;

// https://github.com/unosquare/ffmediaelement/issues/679
/*
 * work build:
 * FFME.Windows NuGet package version 4.4.350
  *   FFMPEG.AutoGen NuGet package version 4.4.1.1
  *   FFMPEG build 4.4.4-94-win64-gpl-shared
 */

namespace Comfizen
{
    public enum FileType { Image, Video }
    
    /// <summary>
    /// Реализация интерфейса IMediaInputStream для чтения медиаданных
    /// напрямую из потока в памяти (MemoryStream).
    /// </summary>
    public sealed unsafe class MemoryInputStream : IMediaInputStream
    {
        private readonly MemoryStream _backingStream;
        private readonly object _readLock = new object();
        private readonly byte[] _readBuffer;

        /// <summary>
        /// Уникальный префикс схемы для идентификации потоков из памяти.
        /// </summary>
        public static string Scheme => "memorystream://";

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="MemoryInputStream"/>.
        /// </summary>
        /// <param name="data">Массив байт с видеоданными.</param>
        public MemoryInputStream(byte[] data)
        {
            _backingStream = new MemoryStream(data);
            StreamUri = new Uri($"{Scheme}{Guid.NewGuid()}");
            CanSeek = true; // MemoryStream всегда поддерживает поиск
            _readBuffer = new byte[ReadBufferLength];
        }

        public Uri StreamUri { get; }
        public bool CanSeek { get; }
        public int ReadBufferLength => 1024 * 32; // Увеличим буфер для потоковой передачи
        public InputStreamInitializing OnInitializing { get; } = null;
        public InputStreamInitialized OnInitialized { get; } = null;

        public void Dispose()
        {
            _backingStream?.Dispose();
        }

        /// <summary>
        /// Метод чтения данных, вызываемый FFmpeg.
        /// </summary>
        public int Read(void* opaque, byte* targetBuffer, int targetBufferLength)
        {
            lock (_readLock)
            {
                try
                {
                    // Читаем из MemoryStream в наш промежуточный буфер
                    var readCount = _backingStream.Read(_readBuffer, 0, _readBuffer.Length);

                    if (readCount > 0)
                    {
                        // Копируем данные из управляемого буфера в неуправляемый буфер FFmpeg
                        Marshal.Copy(_readBuffer, 0, (IntPtr)targetBuffer, readCount);
                        return readCount;
                    }
                    
                    // Если прочтено 0 байт, значит, поток закончился
                    return ffmpeg.AVERROR_EOF;
                }
                catch
                {
                    // В случае любой ошибки также сигнализируем о конце файла
                    return ffmpeg.AVERROR_EOF;
                }
            }
        }

        /// <summary>
        /// Метод для поиска (перемотки) в потоке, вызываемый FFmpeg.
        /// </summary>
        public long Seek(void* opaque, long offset, int whence)
        {
            lock (_readLock)
            {
                try
                {
                    // FFmpeg может запросить размер потока
                    if (whence == ffmpeg.AVSEEK_SIZE)
                    {
                        return _backingStream.Length;
                    }

                    // Для остальных случаев используем стандартный Seek
                    // FFmpeg в основном использует SeekOrigin.Begin
                    return _backingStream.Seek(offset, SeekOrigin.Begin);
                }
                catch
                {
                    return ffmpeg.AVERROR_EOF;
                }
            }
        }
    }

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
                // PropertyChanged.Fody автоматически вызовет уведомление об изменении
            }
        }
        
        public FileType Type => VideoExtensions.Any(ext => FileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            ? FileType.Video
            : FileType.Image;

        public BitmapImage Image
        {
            get
            {
                if (Type != FileType.Image || ImageBytes == null) return null;
                return ByteArrayToImage(ImageBytes);
            }
        }
        
        /// <summary>
        /// Создает и возвращает кастомный поток для FFME, который читает из памяти.
        /// </summary>
        public IMediaInputStream GetMediaStream()
        {
            if (Type != FileType.Video || ImageBytes == null)
                return null;

            try
            {
                return new MemoryInputStream(ImageBytes);
            }
            catch (Exception ex)
            {
                Logger.Log(ex, $"Failed create MemoryInputStream: {FileName}");
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