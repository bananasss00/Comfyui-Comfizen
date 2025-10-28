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
        private readonly ComfyuiModel _comfyuiModel;
        private readonly AppSettings _settings;
        
        private readonly ObservableCollection<ImageOutput> _currentGalleryItems;

        public ICommand OpenFullScreenCommand { get; set; }
        public ICommand CloseFullScreenCommand { get; set; }
        
        public bool IsFullScreenOpen { get; set; }
        
        public ImageOutput CurrentFullScreenImage { get; set; }
        public ICommand MoveNextCommand { get; set; }
        public ICommand MovePreviousCommand { get; set; }
        public ICommand SaveCurrentImageCommand { get; set; }
        public bool ShowSaveConfirmation { get; set; }
        public string SaveConfirmationText { get; set; }
        
        public event PropertyChangedEventHandler? PropertyChanged;

        public FullScreenViewModel(MainViewModel mainViewModel, ComfyuiModel comfyuiModel, AppSettings settings, ObservableCollection<ImageOutput> galleryItems)
        {
            _mainViewModel = mainViewModel;
            _comfyuiModel = comfyuiModel;
            _settings = settings;
            
            _currentGalleryItems = galleryItems;

            OpenFullScreenCommand = new RelayCommand(x => {
                if (x is ImageOutput selectedImage)
                {
                    IsFullScreenOpen = true;
                    CurrentFullScreenImage = selectedImage;
                }
            });

            CloseFullScreenCommand = new RelayCommand(x => IsFullScreenOpen = false);

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

                if (CurrentFullScreenImage.Type == FileType.Video)
                {
                    await _comfyuiModel.SaveVideoFileAsync(
                        _settings.SavedImagesDirectory, 
                        CurrentFullScreenImage.FilePath,
                        CurrentFullScreenImage.ImageBytes,
                        promptToSave
                    );
                }
                else
                {
                    await _comfyuiModel.SaveImageFileAsync(
                        _settings.SavedImagesDirectory,
                        CurrentFullScreenImage.FilePath,
                        CurrentFullScreenImage.ImageBytes,
                        promptToSave,
                        _settings
                    );
                }
                
                CurrentFullScreenImage.IsSaved = true;
                SaveConfirmationText = LocalizationService.Instance["Fullscreen_Saved"]; 
                await Task.Delay(1500);
                ShowSaveConfirmation = false;
            }, x => CurrentFullScreenImage != null && !CurrentFullScreenImage.IsSaved);

            MoveNextCommand = new RelayCommand(x =>
            {
                if (CurrentFullScreenImage != null && _currentGalleryItems.Contains(CurrentFullScreenImage))
                {
                    int currentIndex = _currentGalleryItems.IndexOf(CurrentFullScreenImage);
                    if (currentIndex + 1 < _currentGalleryItems.Count)
                    {
                        CurrentFullScreenImage = _currentGalleryItems[currentIndex + 1];
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
                    }
                }
            }, x => CurrentFullScreenImage != null && _currentGalleryItems.IndexOf(CurrentFullScreenImage) > 0);
        }
    }
}