using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PropertyChanged;

namespace Comfizen
{
    public enum SavedStatusFilter { All, Saved, Unsaved }
    
    /// <summary>
    /// ViewModel for managing the gallery of generated outputs.
    /// Handles filtering, sorting, and deletion of images and videos.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class ImageProcessingViewModel : INotifyPropertyChanged
    {
        private readonly ComfyuiModel _comfyuiModel;
        public AppSettings Settings { get; set; }
            
        public ObservableCollection<ImageOutput> ImageOutputs { get; set; } = new();
        public ObservableCollection<ImageOutput> FilteredImageOutputs { get; set; } = new();
            
        public string SearchFilterText { get; set; }
        public FileTypeFilter SelectedFileTypeFilter { get; set; } = FileTypeFilter.All;
        public SavedStatusFilter SelectedSavedStatusFilter { get; set; } = SavedStatusFilter.All;
        public SortOption SelectedSortOption { get; set; } = SortOption.NewestFirst;
        
        public double GalleryThumbnailSize { get; set; } = 128.0;
            
        public int SelectedItemsCount { get; set; }

        public ICommand ClearOutputsCommand { get; }
        public ICommand DeleteImageCommand { get; }
        public ICommand DeleteSelectedImagesCommand { get; }
        public ICommand SaveSelectedImagesCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ImageProcessingViewModel(ComfyuiModel comfyuiModel, AppSettings settings)
        {
            _comfyuiModel = comfyuiModel;
            Settings = settings;
            ImageOutputs.CollectionChanged += (s, e) => UpdateFilteredOutputs();
                
            this.PropertyChanged += OnFilterChanged;

            ClearOutputsCommand = new RelayCommand(
                x =>
                {
                    // Create a copy of the filtered list to avoid modification during enumeration
                    var itemsToClear = FilteredImageOutputs.ToList();
                    foreach (var item in itemsToClear)
                    {
                        ImageOutputs.Remove(item);
                    }
                },
                // The command can only be executed if there are items visible in the gallery
                x => FilteredImageOutputs.Any()
            );

            DeleteImageCommand = new RelayCommand(param =>
            {
                if (param is not ImageOutput image) return;
                    
                bool proceed = !Settings.ShowDeleteConfirmation || 
                               (MessageBox.Show(string.Format(LocalizationService.Instance["ImageProcessing_DeleteConfirmMessageSingle"], image.FileName), 
                                   LocalizationService.Instance["ImageProcessing_DeleteConfirmTitle"], MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
                    
                if (!proceed) return;

                ImageOutputs.Remove(image);
            });

            DeleteSelectedImagesCommand = new RelayCommand(param =>
            {
                if (param is not IList selectedItems || selectedItems.Count == 0) return;
                    
                var itemsToDelete = selectedItems.Cast<ImageOutput>().ToList();

                bool proceed = !Settings.ShowDeleteConfirmation ||
                               (MessageBox.Show(string.Format(LocalizationService.Instance["ImageProcessing_DeleteConfirmMessageMultiple"], itemsToDelete.Count),
                                   LocalizationService.Instance["ImageProcessing_DeleteConfirmTitle"], MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);

                if (!proceed) return;

                foreach (var image in itemsToDelete)
                {
                    ImageOutputs.Remove(image);
                }
            });
            
            SaveSelectedImagesCommand = new AsyncRelayCommand(async param =>
            {
                if (param is not IList selectedItems || selectedItems.Count == 0) return;

                var itemsToSave = selectedItems.Cast<ImageOutput>().ToList();

                foreach (var image in itemsToSave)
                {
                    // Skip if already saved
                    if (image.IsSaved) continue;

                    string promptToSave = Settings.SavePromptWithFile ? image.Prompt : null;

                    if (image.Type == FileType.Video)
                    {
                        await _comfyuiModel.SaveVideoFileAsync(
                            Settings.SavedImagesDirectory,
                            image.FilePath,
                            image.ImageBytes,
                            promptToSave
                        );
                    }
                    else
                    {
                        await _comfyuiModel.SaveImageFileAsync(
                            Settings.SavedImagesDirectory,
                            image.FilePath,
                            image.ImageBytes,
                            promptToSave,
                            Settings
                        );
                    }
                    // Mark as saved to update the UI
                    image.IsSaved = true;
                }
            }, param => param is IList selectedItems && selectedItems.Count > 0);
        }
            
        private void OnFilterChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SearchFilterText)
                    or nameof(SelectedFileTypeFilter)
                    or nameof(SelectedSortOption)
                    or nameof(SelectedSavedStatusFilter))
            {
                UpdateFilteredOutputs();
            }
        }

        private void UpdateFilteredOutputs()
        {
            var filteredQuery = ImageOutputs.AsEnumerable();

            // Filter by media type
            switch (SelectedFileTypeFilter)
            {
                case FileTypeFilter.Images: filteredQuery = filteredQuery.Where(io => io.Type == FileType.Image); break;
                case FileTypeFilter.Video: filteredQuery = filteredQuery.Where(io => io.Type == FileType.Video); break;
            }

            // Add filtering by saved status
            switch (SelectedSavedStatusFilter)
            {
                case SavedStatusFilter.Saved: filteredQuery = filteredQuery.Where(io => io.IsSaved); break;
                case SavedStatusFilter.Unsaved: filteredQuery = filteredQuery.Where(io => !io.IsSaved); break;
            }

            if (!string.IsNullOrWhiteSpace(SearchFilterText))
            {
                filteredQuery = filteredQuery.Where(io => io.FileName.Contains(SearchFilterText, StringComparison.OrdinalIgnoreCase));
            }

            var newFilteredList = (SelectedSortOption == SortOption.NewestFirst
                ? filteredQuery.OrderByDescending(io => io.CreatedAt)
                : filteredQuery.OrderBy(io => io.CreatedAt)).ToList();
            
            var itemsToRemove = FilteredImageOutputs.Except(newFilteredList).ToList();
            foreach (var item in itemsToRemove)
            {
                FilteredImageOutputs.Remove(item);
            }
            
            for (int i = 0; i < newFilteredList.Count; i++)
            {
                var item = newFilteredList[i];
                var currentIndex = FilteredImageOutputs.IndexOf(item);

                if (currentIndex == -1)
                {
                    FilteredImageOutputs.Insert(i, item);
                }
                else if (currentIndex != i)
                {
                    FilteredImageOutputs.Move(currentIndex, i);
                }
            }
        }
    }
}