using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Windows;

namespace Comfizen
{
    /// <summary>
    /// ViewModel for the full-screen image/video viewer.
    /// </summary>
    public class FullScreenViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _mainViewModel;
        private readonly ImageProcessingViewModel _imageProcessing;
        private readonly ComfyuiModel _comfyuiModel;
        private readonly AppSettings _settings;
        
        private readonly ObservableCollection<ImageOutput> _currentGalleryItems;

        public ICommand OpenFullScreenCommand { get; set; }
        public ICommand CloseFullScreenCommand { get; set; }
        
        public bool IsFullScreenOpen { get; set; }
        
        private ImageOutput _currentFullScreenImage;
        public ImageOutput CurrentFullScreenImage
        {
            get => _currentFullScreenImage;
            set
            {
                if (_currentFullScreenImage == value) return;
                _currentFullScreenImage = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentFullScreenImage)));

                // Sync selection back to the main gallery
                if (_imageProcessing != null)
                {
                    _imageProcessing.SelectedGalleryImage = value;
                }
            }
        }
        public int CurrentImageIndex { get; set; }
        public int TotalImages { get; set; }
        public ICommand MoveNextCommand { get; set; }
        public ICommand MovePreviousCommand { get; set; }
        public ICommand SaveCurrentImageCommand { get; set; }
        public ICommand CopyImageCommand { get; }
        public ICommand DeleteCurrentImageCommand { get; }
        public bool ShowSaveConfirmation { get; set; }
        public string SaveConfirmationText { get; set; }
        
        private bool _isPlaying;
        // Gets or sets a value indicating whether the video is currently playing.
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
                }
            }
        }
        
        public ICommand PlayPauseCommand { get; }
        
        public event PropertyChangedEventHandler? PropertyChanged;

        public FullScreenViewModel(MainViewModel mainViewModel, ImageProcessingViewModel imageProcessing, ComfyuiModel comfyuiModel, AppSettings settings, ObservableCollection<ImageOutput> galleryItems)
        {
            _mainViewModel = mainViewModel;
            _imageProcessing = imageProcessing;
            _comfyuiModel = comfyuiModel;
            _settings = settings;
            
            _currentGalleryItems = galleryItems;

            OpenFullScreenCommand = new RelayCommand(x => {
                if (x is ImageOutput selectedImage)
                {
                    IsFullScreenOpen = true;
                    CurrentFullScreenImage = selectedImage;
                    IsPlaying = selectedImage.Type == FileType.Video; // Auto-play videos on open
                    UpdateIndexAndCount();
                }
            });

            CloseFullScreenCommand = new RelayCommand(x =>
            {
                IsPlaying = false; // Stop playback
                IsFullScreenOpen = false;
            });

            SaveCurrentImageCommand = new AsyncRelayCommand(async x =>
            {
                if (CurrentFullScreenImage == null) return;
    
                ShowSaveConfirmation = true;
                SaveConfirmationText = LocalizationService.Instance["Fullscreen_Saving"];
                await Task.Delay(10); 
    
                string promptToSave = null;

                if (_settings.SavePromptWithFile)
                {
                    promptToSave = CurrentFullScreenImage.Prompt;
                }
                
                string fileIdentifier = CurrentFullScreenImage.FilePath ?? CurrentFullScreenImage.FileName;
                bool success = false;

                if (CurrentFullScreenImage.Type == FileType.Video)
                {
                    success = await _comfyuiModel.SaveVideoFileAsync(
                        _settings.SavedImagesDirectory, 
                        fileIdentifier,
                        CurrentFullScreenImage.ImageBytes,
                        promptToSave
                    );
                }
                else
                {
                    success = await _comfyuiModel.SaveImageFileAsync(
                        _settings.SavedImagesDirectory,
                        fileIdentifier,
                        CurrentFullScreenImage.ImageBytes,
                        promptToSave,
                        _settings
                    );
                }
                
                if (success)
                {
                    CurrentFullScreenImage.IsSaved = true;
                    SaveConfirmationText = LocalizationService.Instance["Fullscreen_Saved"]; 
                    await Task.Delay(1500);
                }
                else
                {
                    SaveConfirmationText = "Save Failed!"; // Or a localized string
                    await Task.Delay(2000);
                }
                ShowSaveConfirmation = false;
            }, x => CurrentFullScreenImage != null && (!CurrentFullScreenImage.IsSaved || Keyboard.Modifiers == ModifierKeys.Control));

            MoveNextCommand = new RelayCommand(x =>
            {
                if (CurrentFullScreenImage != null && _currentGalleryItems.Contains(CurrentFullScreenImage))
                {
                    int currentIndex = _currentGalleryItems.IndexOf(CurrentFullScreenImage);
                    if (currentIndex + 1 < _currentGalleryItems.Count)
                    {
                        CurrentFullScreenImage = _currentGalleryItems[currentIndex + 1];
                        IsPlaying = CurrentFullScreenImage.Type == FileType.Video; // Auto-play on navigate
                        UpdateIndexAndCount();
                    }
                }
            }, x => CurrentFullScreenImage != null && _currentGalleryItems.IndexOf(CurrentFullScreenImage) < _currentGalleryItems.Count - 1);

            MovePreviousCommand = new RelayCommand(x =>
            {
                if (CurrentFullScreenImage != null && _currentGalleryItems.Contains(CurrentFullScreenImage))
                {
                    int currentIndex = _currentGalleryItems.IndexOf(CurrentFullScreenImage);
                    if (currentIndex - 1 >= 0)
                    {
                        CurrentFullScreenImage = _currentGalleryItems[currentIndex - 1];
                        IsPlaying = CurrentFullScreenImage.Type == FileType.Video; // Auto-play on navigate
                        UpdateIndexAndCount();
                    }
                }
            }, x => CurrentFullScreenImage != null && _currentGalleryItems.IndexOf(CurrentFullScreenImage) > 0);
            
            PlayPauseCommand = new RelayCommand(TogglePlayPause, x => CurrentFullScreenImage?.Type == FileType.Video);
            CopyImageCommand = new RelayCommand(CopyCurrentImageToClipboard, _ => CurrentFullScreenImage?.Type == FileType.Image);
            DeleteCurrentImageCommand = new RelayCommand(DeleteCurrentImage, _ => CurrentFullScreenImage != null);
        }
        private void CopyCurrentImageToClipboard(object obj)
        {
            if (CurrentFullScreenImage?.ImageBytes == null) return;

            try
            {
                using (var ms = new System.IO.MemoryStream(CurrentFullScreenImage.ImageBytes))
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = ms;
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    System.Windows.Clipboard.SetImage(bitmap);
                    
                    SaveConfirmationText = LocalizationService.Instance["Fullscreen_Copied"];
                    ShowSaveConfirmation = true;
                    Task.Delay(1500).ContinueWith(_ => ShowSaveConfirmation = false, TaskScheduler.FromCurrentSynchronizationContext());
                }
            }
            catch(Exception ex)
            {
                Logger.Log(ex, "Failed to copy image to clipboard.");
            }
        }
        
        private void DeleteCurrentImage(object obj)
        {
            if (CurrentFullScreenImage == null) return;

            // Use the same confirmation logic as the gallery
            if (_settings.ShowDeleteConfirmation)
            {
                var result = MessageBox.Show(
                    string.Format(LocalizationService.Instance["ImageProcessing_DeleteConfirmMessageSingle"], CurrentFullScreenImage.FileName),
                    LocalizationService.Instance["ImageProcessing_DeleteConfirmTitle"],
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }
            
            var imageToDelete = CurrentFullScreenImage;
            int currentIndex = _currentGalleryItems.IndexOf(imageToDelete);

            // Determine the next image to show *before* deleting
            ImageOutput nextImage = null;
            if (_currentGalleryItems.Count > 1)
            {
                if (currentIndex + 1 < _currentGalleryItems.Count)
                {
                    // If there's a next image, select it
                    nextImage = _currentGalleryItems[currentIndex + 1];
                }
                else
                {
                    // If it's the last image, select the previous one
                    nextImage = _currentGalleryItems[currentIndex - 1];
                }
            }

            // Remove the image from the source collection, which will update the filtered list
            _imageProcessing.ImageOutputs.Remove(imageToDelete);
            
            // Update the view
            if (nextImage != null && _currentGalleryItems.Contains(nextImage))
            {
                CurrentFullScreenImage = nextImage;
                IsPlaying = CurrentFullScreenImage.Type == FileType.Video;
                UpdateIndexAndCount();
            }
            else
            {
                // No more images left, or next image is no longer in the list, close fullscreen
                CloseFullScreenCommand.Execute(null);
            }
        }
        
        private void TogglePlayPause(object o)
        {
            IsPlaying = !IsPlaying;
        }
        
        private void UpdateIndexAndCount()
        {
            if (CurrentFullScreenImage != null && _currentGalleryItems.Count > 0)
            {
                TotalImages = _currentGalleryItems.Count;
                CurrentImageIndex = _currentGalleryItems.IndexOf(CurrentFullScreenImage) + 1;
            }
            else
            {
                TotalImages = 0;
                CurrentImageIndex = 0;
            }
        }
    }
}