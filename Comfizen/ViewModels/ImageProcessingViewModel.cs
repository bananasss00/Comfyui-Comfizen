using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        
        public double SimilarityThreshold { get; set; } = 95.0;
        public bool IsSimilaritySortActive => SelectedSortOption == SortOption.Similarity;
        
        public ImageOutput SelectedGalleryImage { get; set; }
        public double GalleryThumbnailSize { get; set; } = 128.0;
            
        public int SelectedItemsCount { get; set; }
        public bool IsAnyVideoSelected { get; private set; }
        
        public void UpdateSelectionState(IList selectedItems)
        {
            if (selectedItems == null)
            {
                IsAnyVideoSelected = false;
                return;
            }
            IsAnyVideoSelected = selectedItems.OfType<ImageOutput>().Any(item => item.Type == FileType.Video);
        }

        public ICommand ClearOutputsCommand { get; }
        public ICommand DeleteImageCommand { get; }
        public ICommand DeleteSelectedImagesCommand { get; }
        public ICommand SaveSelectedImagesCommand { get; }
        public ICommand SaveSelectedImagesAsCommand { get; }
        public ICommand SaveSelectedImagesWithFormatCommand { get; }
        public ICommand SaveSelectedImagesAsWithFormatCommand { get; }
        public ICommand SaveGridElementsCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

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
                await SaveItemsAsync(selectedItems.Cast<ImageOutput>().ToList(), Settings.SavedImagesDirectory);

            }, param => param is IList selectedItems && selectedItems.Count > 0);
            
            SaveSelectedImagesAsCommand = new AsyncRelayCommand(async param =>
            {
                if (param is not IList selectedItems || selectedItems.Count == 0) return;

                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.Description = "Select a folder to save the selected items";
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    {
                        return;
                    }
                    await SaveItemsAsync(selectedItems.Cast<ImageOutput>().ToList(), dialog.SelectedPath);
                }
            }, param => param is IList selectedItems && selectedItems.Count > 0);

            SaveSelectedImagesWithFormatCommand = new AsyncRelayCommand(async param =>
            {
                if (param is not object[] args || args.Length < 2 || args[0] is not IList selectedItems || args[1] is not ImageSaveFormat format) return;
                await SaveItemsAsync(selectedItems.Cast<ImageOutput>().ToList(), Settings.SavedImagesDirectory, format);

            }, param => param is object[] args && args.Length >= 2 && args[0] is IList selectedItems && selectedItems.Count > 0);

            SaveSelectedImagesAsWithFormatCommand = new AsyncRelayCommand(async param =>
            {
                if (param is not object[] args || args.Length < 2 || args[0] is not IList selectedItems || args[1] is not ImageSaveFormat format) return;
                
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.Description = "Select a folder to save the selected items";
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    {
                        return;
                    }
                    await SaveItemsAsync(selectedItems.Cast<ImageOutput>().ToList(), dialog.SelectedPath, format);
                }
            }, param => param is object[] args && args.Length >= 2 && args[0] is IList selectedItems && selectedItems.Count > 0);
            
            SaveGridElementsCommand = new AsyncRelayCommand(SaveGridElementsAsync, param => param is ImageOutput);
        }
        
        /// <summary>
        /// Re-generates and saves each individual element from a composite XY Grid image.
        /// </summary>
        private async Task SaveGridElementsAsync(object parameter)
        {
            if (parameter is not ImageOutput gridImage || !gridImage.IsGridResult) return;
        
            try
            {
                var compositePrompt = JObject.Parse(gridImage.Prompt);
                
                // NEW: Read the embedded grid elements directly from the prompt metadata
                var gridElementsToken = compositePrompt["grid_elements"];
                var baseWorkflowJson = compositePrompt["workflow"] as JObject;
                var gridConfig = compositePrompt["grid_config"] as JObject;

                if (gridElementsToken == null || baseWorkflowJson == null || gridConfig == null)
                {
                    MessageBox.Show("This grid image is missing embedded data and cannot be saved element-wise.", "Missing Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var gridElements = gridElementsToken.ToObject<List<MainViewModel.SerializableGridCellResult>>();
                
                Logger.Log($"Starting to save {gridElements.Count} grid elements from cached data...");

                foreach (var cell in gridElements)
                {
                    // --- START OF FIX: Reconstruct the specific workflow for this cell ---
                    JObject cellWorkflow = baseWorkflowJson.DeepClone() as JObject;
                    
                    // Apply X Value
                    ApplyGridValueToWorkflow(cellWorkflow, gridConfig, "X", cell.XValue);
                    
                    // Apply Y Value
                    ApplyGridValueToWorkflow(cellWorkflow, gridConfig, "Y", cell.YValue);

                    // Re-embed the grid config into the cell's workflow so the new image is also aware it was part of a grid (optional but useful)
                    // Or keep it clean. Let's keep it clean as a standard workflow.
                    
                    // Serialize the modified workflow to be embedded in the file
                    string cellPromptJson = cellWorkflow.ToString(Formatting.None);
                    // --- END OF FIX ---

                    foreach (var element in cell.ImageOutputs)
                    {
                        var safeFileName = Utils.CreateSafeFilenameForGrid(element.FileName, cell.XValue, cell.YValue);
                        
                        var fileType = ImageOutput.GetFileTypeFromExtension(element.FileName);

                        if (fileType == FileType.Video)
                        {
                            await _comfyuiModel.SaveVideoFileAsync(Settings.SavedImagesDirectory, safeFileName, element.ImageBytes, cellPromptJson);
                        }
                        else
                        {
                            await _comfyuiModel.SaveImageFileAsync(Settings.SavedImagesDirectory, safeFileName, element.ImageBytes, cellPromptJson, Settings);
                        }
                    }
                }
                
                Logger.Log("Finished saving all grid elements.", LogLevel.Info);
                MessageBox.Show("All grid elements have been successfully saved with their specific metadata.", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Failed to save grid elements.");
                MessageBox.Show($"An error occurred while saving grid elements: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// Applies a specific grid axis value (X or Y) to the workflow JSON object.
        /// Handles Fields, Presets, and Global Presets.
        /// </summary>
        private void ApplyGridValueToWorkflow(JObject workflowContainer, JObject gridConfig, string axis, string value)
        {
            if (string.IsNullOrEmpty(value)) return;

            string identifier = gridConfig[$"{axis}AxisIdentifier"]?.ToString();
            string sourceType = gridConfig[$"{axis}AxisSourceType"]?.ToString(); // "Field", "PresetGroup", "GlobalPreset"

            if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(sourceType)) return;

            var promptJson = workflowContainer["prompt"] as JObject;
            if (promptJson == null) return;

            if (sourceType == "Field")
            {
                // Identifier is the path (e.g., "3.inputs.steps")
                var prop = Utils.GetJsonPropertyByPath(promptJson, identifier);
                if (prop != null)
                {
                    prop.Value = ConvertValueToJToken(value);
                }
            }
            else if (sourceType == "PresetGroup")
            {
                // Identifier is the Group ID (Guid)
                var presetsDict = workflowContainer["presets"]?.ToObject<Dictionary<Guid, List<GroupPreset>>>();
                if (presetsDict != null && Guid.TryParse(identifier, out var groupId) && presetsDict.TryGetValue(groupId, out var groupPresets))
                {
                    var preset = groupPresets.FirstOrDefault(p => p.Name == value);
                    if (preset != null)
                    {
                        ApplyPresetToJObject(promptJson, preset, groupPresets);
                    }
                }
            }
            else if (sourceType == "GlobalPreset")
            {
                // Identifier is likely "global"
                var globalPresets = workflowContainer["globalPresets"]?.ToObject<List<GlobalPreset>>();
                if (globalPresets != null)
                {
                    var preset = globalPresets.FirstOrDefault(p => p.Name == value);
                    if (preset != null)
                    {
                        // To apply a global preset, we need access to ALL group presets
                        var allPresetsDict = workflowContainer["presets"]?.ToObject<Dictionary<Guid, List<GroupPreset>>>() ?? new Dictionary<Guid, List<GroupPreset>>();
                        
                        foreach (var groupState in preset.GroupStates)
                        {
                            if (allPresetsDict.TryGetValue(groupState.Key, out var groupPresets))
                            {
                                foreach (var presetName in groupState.Value)
                                {
                                    var gp = groupPresets.FirstOrDefault(p => p.Name == presetName);
                                    if (gp != null)
                                    {
                                        ApplyPresetToJObject(promptJson, gp, groupPresets);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ApplyPresetToJObject(JObject prompt, GroupPreset preset, List<GroupPreset> allGroupPresets)
        {
            if (preset.IsLayout)
            {
                foreach (var snippetName in preset.SnippetNames ?? new List<string>())
                {
                    var snippet = allGroupPresets.FirstOrDefault(p => p.Name == snippetName);
                    if (snippet != null)
                    {
                        foreach (var kvp in snippet.Values)
                        {
                            var prop = Utils.GetJsonPropertyByPath(prompt, kvp.Key);
                            if (prop != null) prop.Value = kvp.Value.DeepClone();
                        }
                    }
                }
            }
            else
            {
                foreach (var kvp in preset.Values)
                {
                    var prop = Utils.GetJsonPropertyByPath(prompt, kvp.Key);
                    if (prop != null) prop.Value = kvp.Value.DeepClone();
                }
            }
        }

        private JToken ConvertValueToJToken(string stringValue)
        {
            // Simple robust conversion
            if (long.TryParse(stringValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long lVal)) return new JValue(lVal);
            if (double.TryParse(stringValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double dVal)) return new JValue(dVal);
            
            var lower = stringValue.Trim().ToLowerInvariant();
            if (lower == "true" || lower == "yes" || lower == "on") return new JValue(true);
            if (lower == "false" || lower == "no" || lower == "off") return new JValue(false);

            return new JValue(stringValue);
        }
        
        private async Task SaveItemsAsync(List<ImageOutput> itemsToSave, string targetDirectory, ImageSaveFormat? formatOverride = null)
        {
            foreach (var image in itemsToSave)
            {
                string promptToSave = Settings.SavePromptWithFile ? image.Prompt : null;
                bool success = false;

                if (image.Type == FileType.Video)
                {
                    success = await _comfyuiModel.SaveVideoFileAsync(
                        targetDirectory,
                        image.FilePath ?? image.FileName,
                        image.ImageBytes,
                        promptToSave
                    );
                }
                else
                {
                    success = await _comfyuiModel.SaveImageFileAsync(
                        targetDirectory,
                        image.FilePath ?? image.FileName,
                        image.ImageBytes,
                        promptToSave,
                        Settings,
                        formatOverride
                    );
                }
                        
                if (success)
                {
                    image.IsSaved = true;
                }
            }
        }
            
        private void OnFilterChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SearchFilterText)
                or nameof(SelectedFileTypeFilter)
                or nameof(SelectedSortOption)
                or nameof(SelectedSavedStatusFilter)
                or nameof(SimilarityThreshold))
            {
                UpdateFilteredOutputs();
            }
            
            if (e.PropertyName == nameof(SelectedSortOption))
            {
                OnPropertyChanged(nameof(IsSimilaritySortActive));
            }
        }

        private async void UpdateFilteredOutputs()
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

            List<ImageOutput> newFilteredList;

            if (IsSimilaritySortActive)
            {
                var itemsToHash = filteredQuery.Where(io => io.PerceptualHash == 0).ToList();
                if (itemsToHash.Any())
                {
                    var hashTasks = itemsToHash.Select(item => item.CalculatePerceptualHashAsync());
                    await Task.WhenAll(hashTasks);
                }

                var allItemsWithHash = filteredQuery
                    .Where(io => io.PerceptualHash != 0) // Filter out items where hash calculation failed
                    .OrderByDescending(io => io.CreatedAt) // Initial sort for stable group creation
                    .ToList();

                var processedImages = new HashSet<ImageOutput>();
                var similarityGroups = new List<List<ImageOutput>>();
                
                // 1. Find and create groups of similar items
                foreach (var item in allItemsWithHash)
                {
                    if (processedImages.Contains(item)) continue;

                    var group = allItemsWithHash
                        .Where(other => !processedImages.Contains(other))
                        .Select(other => new {
                            Image = other,
                            Similarity = (64 - Utils.CalculateHammingDistance(item.PerceptualHash, other.PerceptualHash)) / 64.0 * 100.0
                        })
                        .Where(i => i.Similarity >= SimilarityThreshold)
                        .OrderByDescending(i => i.Image.CreatedAt) // Sort items within a group by date
                        .Select(i => i.Image)
                        .ToList();

                    if (group.Count > 1)
                    {
                        similarityGroups.Add(group);
                        foreach (var groupedItem in group)
                        {
                            processedImages.Add(groupedItem);
                        }
                    }
                }

                var loners = allItemsWithHash.Except(processedImages).ToList();
                
                // 2. Assemble the final sorted list
                newFilteredList = new List<ImageOutput>();
                
                // Add all groups, sorted by the date of their newest item
                newFilteredList.AddRange(similarityGroups
                    .OrderByDescending(g => g.First().CreatedAt)
                    .SelectMany(g => g));

                // Add all the "lonely" items at the end, also sorted by date
                newFilteredList.AddRange(loners);
            }
            else
            {
                // Default sorting by date if similarity is not active
                newFilteredList = (SelectedSortOption == SortOption.NewestFirst
                    ? filteredQuery.OrderByDescending(io => io.CreatedAt)
                    : filteredQuery.OrderBy(io => io.CreatedAt)).ToList();
            }
            
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