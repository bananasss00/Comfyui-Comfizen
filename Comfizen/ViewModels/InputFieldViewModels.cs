using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.VisualBasic.Logging;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace Comfizen
{
    public enum PresetSortOption
    {
        Alphabetical,
        DateModified
    }
    
    public enum PresetFilterLogic
    {
        OR, // Any
        AND // All
    }
    
    [AddINotifyPropertyChangedInterface]
    public class PresetFieldSelectionViewModel : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsSelected { get; set; }
        public string TabName { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;
    }
    
    /// <summary>
    /// A helper class to store structured information for a preset's tooltip.
    /// </summary>
    public class PresetFieldInfo
    {
        public string FieldName { get; set; }
        public string FieldValue { get; set; }
        public string TabName { get; set; }
        public bool ShowTabName { get; set; }
        public string FieldPath { get; set; }
        public bool IsModified { get; set; }
    }
    
    /// <summary>
    /// A ViewModel for a single hook toggle switch in the UI.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class HookToggleViewModel
    {
        public string HookName { get; set; }
        public bool IsEnabled { get; set; } = true; // Hooks are enabled by default
    }
    
    /// <summary>
    /// ViewModel for managing a single group's state within the Global Preset Editor.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class GroupStateViewModel
    {
        public WorkflowGroupViewModel Group { get; }
        public string GroupName => Group.Name;
        public Guid GroupId => Group.Id;

        // A combined list of snippets and layouts for the dropdown
        public ObservableCollection<GroupPresetViewModel> AvailablePresets { get; }

        // The currently selected preset (layer) for this group
        public GroupPresetViewModel SelectedPreset { get; set; }

        public GroupStateViewModel(WorkflowGroupViewModel group)
        {
            Group = group;
            // Create a new collection that includes a "null" option for "no preset"
            AvailablePresets = new ObservableCollection<GroupPresetViewModel> { null };
            foreach (var preset in group.AllPresets.OrderBy(p => p.Name))
            {
                AvailablePresets.Add(preset);
            }
        }
    }


    /// <summary>
    /// ViewModel for the Global Preset Editor popup.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class GlobalPresetEditorViewModel
    {
        public ObservableCollection<GlobalPreset> GlobalPresets { get; }
        
        private GlobalPreset _selectedPreset;
        public GlobalPreset SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                _selectedPreset = value;
                LoadPresetForEditing(value);
            }
        }

        public string EditingPresetName { get; set; }

        // --- START OF CHANGES: New editing properties ---
        public string EditingDescription { get; set; }
        public string EditingCategory { get; set; }
        public ObservableCollection<string> EditingTags { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> AllTags { get; } = new ObservableCollection<string>();
        
        public ICollectionView FilteredPresetsView { get; private set; }
        public ObservableCollection<string> AllCategories { get; } = new ObservableCollection<string>();
        // --- END OF CHANGES ---

        public ObservableCollection<GroupStateViewModel> GroupStates { get; } = new ObservableCollection<GroupStateViewModel>();

        public ICommand AddNewCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }

        private readonly Action<GlobalPreset> _saveCallback;
        private readonly Action<GlobalPreset> _deleteCallback;
        private readonly Func<Dictionary<Guid, List<string>>> _getCurrentStateCallback;
        
        public GlobalPresetEditorViewModel(
            ObservableCollection<GlobalPreset> globalPresets,
            IEnumerable<WorkflowGroupViewModel> allGroups,
            Action<GlobalPreset> saveCallback,
            Action<GlobalPreset> deleteCallback,
            Func<Dictionary<Guid, List<string>>> getCurrentStateCallback)
        {
            GlobalPresets = globalPresets;
            _saveCallback = saveCallback;
            _deleteCallback = deleteCallback;
            _getCurrentStateCallback = getCurrentStateCallback;

            FilteredPresetsView = CollectionViewSource.GetDefaultView(GlobalPresets);
            
            FilteredPresetsView.GroupDescriptions.Clear(); 
            FilteredPresetsView.SortDescriptions.Clear();

            FilteredPresetsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(GlobalPreset.DisplayCategory)));
            FilteredPresetsView.SortDescriptions.Add(new SortDescription(nameof(GlobalPreset.DisplayCategory), ListSortDirection.Ascending));
            FilteredPresetsView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
            
            UpdateCategories();
            UpdateTags();

            foreach (var group in allGroups.OrderBy(g => g.Name))
            {
                GroupStates.Add(new GroupStateViewModel(group));
            }

            AddNewCommand = new RelayCommand(_ => CreateNewPreset());
            SaveCommand = new RelayCommand(_ => SaveChanges(), _ => !string.IsNullOrWhiteSpace(EditingPresetName));
            DeleteCommand = new RelayCommand(_ => DeletePreset(), _ => SelectedPreset != null);
        }
        
        private void UpdateCategories()
        {
            var categories = GlobalPresets
                .Select(p => p.Category)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .OrderBy(c => c)
                .ToList();
            
            AllCategories.Clear();
            foreach (var cat in categories) AllCategories.Add(cat);
        }
        
        private void UpdateTags()
        {
            var tags = GlobalPresets.SelectMany(p => p.Tags ?? new List<string>()).Distinct().OrderBy(t => t);
            AllTags.Clear();
            foreach (var t in tags) AllTags.Add(t);
        }

        public void LoadCurrentStateIntoEditor()
        {
            var currentState = _getCurrentStateCallback();
            
            // --- START OF CHANGES: Clear new fields ---
            EditingPresetName = "";
            EditingDescription = "";
            EditingCategory = "";
            EditingTags.Clear();
            // --- END OF CHANGES ---
            
            SelectedPreset = null; // Deselect from the list

            foreach (var groupState in GroupStates)
            {
                if (currentState.TryGetValue(groupState.GroupId, out var layerNames) && layerNames.Any())
                {
                    var layerName = layerNames.First();
                    groupState.SelectedPreset = groupState.AvailablePresets.FirstOrDefault(p => p?.Name == layerName);
                }
                else
                {
                    groupState.SelectedPreset = null;
                }
            }
        }

        private void LoadPresetForEditing(GlobalPreset preset)
        {
            if (preset == null)
            {
                // This can happen when deselecting. We don't want to clear the form here.
                return;
            }

            EditingPresetName = preset.Name;
            
            // --- START OF CHANGES: Load new fields ---
            EditingDescription = preset.Description;
            EditingCategory = preset.Category;
            EditingTags.Clear();
            if (preset.Tags != null)
            {
                foreach (var t in preset.Tags) EditingTags.Add(t);
            }
            // --- END OF CHANGES ---

            foreach (var groupState in GroupStates)
            {
                if (preset.GroupStates.TryGetValue(groupState.GroupId, out var layerNames) && layerNames.Any())
                {
                    // For now, we only support one layer per group in the editor
                    var layerName = layerNames.First();
                    groupState.SelectedPreset = groupState.AvailablePresets.FirstOrDefault(p => p?.Name == layerName);
                }
                else
                {
                    groupState.SelectedPreset = null;
                }
            }
        }

        public void CreateNewPreset()
        {
            // This now simply clears the form for a fresh start.
            SelectedPreset = null;
            EditingPresetName = "";
            // --- START OF CHANGES: Clear fields ---
            EditingDescription = "";
            EditingCategory = ""; // Optionally preserve last used category here
            EditingTags.Clear();
            // --- END OF CHANGES ---
            
            foreach (var groupState in GroupStates)
            {
                groupState.SelectedPreset = null;
            }
        }

        private void SaveChanges()
        {
            // --- START OF CHANGES: Save with new metadata ---
            var presetToSave = new GlobalPreset
            {
                Name = EditingPresetName,
                Description = EditingDescription,
                Category = EditingCategory,
                Tags = EditingTags.ToList() 
            };
            // --- END OF CHANGES ---

            presetToSave.GroupStates.Clear();

            foreach (var groupState in GroupStates)
            {
                if (groupState.SelectedPreset != null)
                {
                    presetToSave.GroupStates[groupState.GroupId] = new List<string> { groupState.SelectedPreset.Name };
                }
            }
            
            _saveCallback(presetToSave);
            
            // Refresh categories list
            UpdateCategories();
            UpdateTags();
            FilteredPresetsView.Refresh();
        }

        private void DeletePreset()
        {
            if (SelectedPreset != null)
            {
                _deleteCallback(SelectedPreset);
            }
        }
    }
    
    public class GlobalPresetTooltipGroup
    {
        public string GroupName { get; set; }
        public List<ActiveLayerDetail> Layers { get; set; }
    }
    
    public class ActiveLayerDetail
    {
        public string LayerName { get; set; }
        public bool IsLayout { get; set; }
        public List<PresetFieldInfo> Fields { get; set; }
    }
    
    [AddINotifyPropertyChangedInterface]
    public class GlobalPresetConfigurationItem
    {
        public string GroupName { get; set; }
        public Guid GroupId { get; set; }
        public bool IsSelected { get; set; }
        public bool IsEnabled { get; set; }

        public ObservableCollection<string> ActiveLayers { get; set; } = new ObservableCollection<string>();

        // --- LOGIC FOR ADVANCED PICKER ---
        
        public ObservableCollection<GroupPresetViewModel> AllPresetsSource { get; private set; }
        public ICollectionView FilteredPresetsView { get; private set; }

        public string SearchFilter { get; set; }
        public ObservableCollection<string> FilterCategories { get; } = new ObservableCollection<string>();
        public string SelectedCategory { get; set; } = "[ All ]";
        
        public ObservableCollection<string> FilterTags { get; } = new ObservableCollection<string>();
        public string SelectedTag { get; set; } = "[ All ]";

        public ICommand RemoveLayerCommand { get; }
        public ICommand AddLayerCommand { get; }
        public ICommand ClearFiltersCommand { get; }
        
        public ObservableCollection<ActiveLayerDetail> DetailedStateInfo { get; } = new ObservableCollection<ActiveLayerDetail>();

        public GlobalPresetConfigurationItem(IEnumerable<GroupPresetViewModel> availablePresets)
        {
            AllPresetsSource = new ObservableCollection<GroupPresetViewModel>(availablePresets);
            
            FilteredPresetsView = CollectionViewSource.GetDefaultView(AllPresetsSource);
            FilteredPresetsView.Filter = FilterPredicate;
            
            FilteredPresetsView.SortDescriptions.Add(new SortDescription("IsLayout", ListSortDirection.Ascending));
            FilteredPresetsView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));

            PopulateFilters();
            
            ActiveLayers.CollectionChanged += (s, e) => UpdateDetailedState();
            
            UpdateDetailedState();
            
            RemoveLayerCommand = new RelayCommand(param =>
            {
                if (param is string layerName && ActiveLayers.Contains(layerName))
                {
                    ActiveLayers.Remove(layerName);
                    FilteredPresetsView.Refresh(); 
                }
            });

            AddLayerCommand = new RelayCommand(param =>
            {
                string layerName = null;
                if (param is GroupPresetViewModel vm) layerName = vm.Name;
                else if (param is string s) layerName = s;

                if (!string.IsNullOrEmpty(layerName) && !ActiveLayers.Contains(layerName))
                {
                    ActiveLayers.Add(layerName);
                    IsSelected = true;
                    FilteredPresetsView.Refresh();
                }
            });
            
            ClearFiltersCommand = new RelayCommand(_ =>
            {
                SearchFilter = "";
                SelectedCategory = "[ All ]";
                SelectedTag = "[ All ]";
            });
        }

        private void UpdateDetailedState()
        {
            DetailedStateInfo.Clear();
            
            foreach (var layerName in ActiveLayers)
            {
                var preset = AllPresetsSource.FirstOrDefault(p => p.Name == layerName);
                if (preset != null)
                {
                    DetailedStateInfo.Add(new ActiveLayerDetail
                    {
                        LayerName = layerName,
                        IsLayout = preset.IsLayout,
                        Fields = preset.FieldDetails 
                    });
                }
            }
        }

        private void PopulateFilters()
        {
            var allStr = "[ All ]";
            
            FilterCategories.Add(allStr);
            var cats = AllPresetsSource.Select(p => p.Model.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c);
            foreach (var c in cats) FilterCategories.Add(c);

            FilterTags.Add(allStr);
            var tags = AllPresetsSource.SelectMany(p => p.Model.Tags ?? new List<string>()).Distinct().OrderBy(t => t);
            foreach (var t in tags) FilterTags.Add(t);
        }

        private bool FilterPredicate(object obj)
        {
            if (obj is not GroupPresetViewModel preset) return false;

            if (ActiveLayers.Contains(preset.Name)) return false; 

            if (SelectedCategory != "[ All ]" && preset.Model.Category != SelectedCategory) return false;
            if (SelectedTag != "[ All ]" && (preset.Model.Tags == null || !preset.Model.Tags.Contains(SelectedTag))) return false;

            if (!string.IsNullOrWhiteSpace(SearchFilter))
            {
                var terms = SearchFilter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    bool matchName = preset.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!matchName) return false;
                }
            }

            return true;
        }

        public void OnPropertyChanged(string propertyName, object before, object after)
        {
            if (propertyName == nameof(SearchFilter) || 
                propertyName == nameof(SelectedCategory) || 
                propertyName == nameof(SelectedTag))
            {
                FilteredPresetsView?.Refresh();
            }
        }
    }
    
    /// <summary>
    /// A ViewModel for the combined global controls section, managing both wildcard seed and global presets.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class GlobalControlsViewModel : INotifyPropertyChanged
    {
        private readonly Workflow _workflow;
        private bool _isInternalUpdate = false; 
        
        public string Header { get; set; } = LocalizationService.Instance["GlobalSettings_Header"];
        public bool IsExpanded { get; set; } = true;
        
        public bool IsHooksSectionVisible => ImplementedHooks.Any();
        public ObservableCollection<HookToggleViewModel> ImplementedHooks { get; } = new();
        
        public bool IsSeedSectionVisible { get; set; } = false;
        public bool IsPresetsSectionVisible => true;
        public bool IsVisible => IsSeedSectionVisible || IsPresetsSectionVisible || IsHooksSectionVisible;

        public long WildcardSeed { get; set; } = Utils.GenerateSeed(0, 4294967295L);
        public bool IsSeedLocked { get; set; } = false;
        
        public ObservableCollection<GlobalPreset> GlobalPresets { get; } = new();
        
        private GlobalPreset _selectedGlobalPreset;
        public GlobalPreset SelectedGlobalPreset
        {
            get => _selectedGlobalPreset;
            set
            {
                if (_selectedGlobalPreset == value) return;
                _selectedGlobalPreset = value;
                OnPropertyChanged(nameof(SelectedGlobalPreset));
                OnPropertyChanged(nameof(CurrentStateStatus));
                OnPropertyChanged(nameof(CurrentPresetTooltipData)); 
            }
        }
        
        public IEnumerable<GlobalPresetTooltipGroup> CurrentPresetTooltipData => GetTooltipDataForPreset(SelectedGlobalPreset);

        public IEnumerable<GlobalPresetTooltipGroup> GetTooltipDataForPreset(GlobalPreset preset)
        {
            if (preset == null) return null;

            var result = new List<GlobalPresetTooltipGroup>();

            foreach (var groupState in preset.GroupStates)
            {
                var groupVm = _allAvailableGroups.FirstOrDefault(g => g.Id == groupState.Key);
                if (groupVm == null) continue;

                var layerDetails = new List<ActiveLayerDetail>();

                foreach (var layerName in groupState.Value)
                {
                    var presetVm = groupVm.AllPresets.FirstOrDefault(p => p.Name == layerName);
                    if (presetVm != null)
                    {
                        layerDetails.Add(new ActiveLayerDetail
                        {
                            LayerName = layerName,
                            IsLayout = presetVm.IsLayout,
                            Fields = presetVm.FieldDetails
                        });
                    }
                }

                if (layerDetails.Any())
                {
                    result.Add(new GlobalPresetTooltipGroup
                    {
                        GroupName = groupVm.Name,
                        Layers = layerDetails
                    });
                }
            }

            return result.OrderBy(x => x.GroupName).ToList();
        }
        
        public ICommand OpenGlobalPresetEditorCommand { get; set; }

        public string CurrentStateStatus 
        {
            get
            {
                if (SelectedGlobalPreset == null) return LocalizationService.Instance["Presets_State_Unsaved"];
                return SelectedGlobalPreset.Name;
            }
        }

        private bool _isPresetPanelOpen;
        public bool IsPresetPanelOpen
        {
            get => _isPresetPanelOpen;
            set
            {
                if (_isPresetPanelOpen == value) return;
                _isPresetPanelOpen = value;
                OnPropertyChanged(nameof(IsPresetPanelOpen));
                if (value) OnPropertyChanged(nameof(CurrentPresetSortOption));
            }
        }

        public string PresetSearchFilter { get; set; }
        public PresetSortOption CurrentPresetSortOption { get; set; } = PresetSortOption.Alphabetical;
        
        public ObservableCollection<string> AllCategories { get; } = new();
        public string SelectedCategoryFilter { get; set; }
        
        public ObservableCollection<string> AllTags { get; } = new();
        public string SelectedTagFilter { get; set; }
        
        public ICollectionView FilteredGlobalPresetsView { get; private set; }
        
        public bool CreateSnapshotInAllGroups { get; set; } = false;

        private bool _isSavePresetPopupOpen;
        public bool IsSavePresetPopupOpen
        {
            get => _isSavePresetPopupOpen;
            set
            {
                if (_isSavePresetPopupOpen == value) return;
            
                if (value)
                {
                    PopulateConfigurationItems();
                    
                    // Reset snapshot checkbox on open
                    CreateSnapshotInAllGroups = false;

                    if (!IsUpdateMode)
                    {
                        NewPresetName = _lastUsedName;
                        NewPresetDescription = _lastUsedDescription;
                        NewPresetCategory = _lastUsedCategory;
                        NewPresetTags.Clear();
                        if (_lastUsedTags != null)
                        {
                            foreach (var t in _lastUsedTags) NewPresetTags.Add(t);
                        }
                    }
                }
                else
                {
                    _presetToUpdateName = null;
                    OnPropertyChanged(nameof(IsUpdateMode));
                }

                _isSavePresetPopupOpen = value;
                OnPropertyChanged(nameof(IsSavePresetPopupOpen));
            }
        }
        
        public bool IsUpdateMode => !string.IsNullOrEmpty(_presetToUpdateName);
        private string _presetToUpdateName;

        public string NewPresetName { get; set; }
        public string NewPresetDescription { get; set; }
        public string NewPresetCategory { get; set; }
        public ObservableCollection<string> NewPresetTags { get; set; } = new ObservableCollection<string>();

        public ICommand ClearFiltersCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand ApplyPresetCommand { get; }
        public ICommand DeletePresetCommand { get; }
        public ICommand StartUpdatePresetCommand { get; }
        public ICommand SavePresetCommand { get; }
        public ICommand CancelSavePresetCommand { get; }
        public ICommand RandomizeAllSeedsCommand { get; }
        
        private readonly Action<GlobalPreset> _applyPresetAction;
        private readonly Func<Dictionary<Guid, List<string>>> _getCurrentStateCallback;
        private readonly Action<GlobalPreset> _saveCallback;
        private readonly Action<GlobalPreset> _deleteCallback;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            if (name == nameof(IsSeedSectionVisible) || name == nameof(IsPresetsSectionVisible) || name == nameof(IsHooksSectionVisible))
            {
                OnPropertyChanged(nameof(IsVisible));
            }
        }
        
        private string _lastUsedName = "";
        private string _lastUsedDescription = "";
        private string _lastUsedCategory = "";
        private List<string> _lastUsedTags = new List<string>();
        
        private List<WorkflowGroupViewModel> _allAvailableGroups = new();
        public ObservableCollection<GlobalPresetConfigurationItem> PresetConfigurationItems { get; } = new();
        
        private readonly Func<IEnumerable<SeedFieldViewModel>> _getAllSeedViewModels;

        public GlobalControlsViewModel(
            Workflow workflow,
            Action<GlobalPreset> applyPresetAction, 
            Func<Dictionary<Guid, List<string>>> getCurrentStateCallback,
            Action<GlobalPreset> saveCallback,
            Action<GlobalPreset> deleteCallback,
            Func<IEnumerable<SeedFieldViewModel>> getAllSeedViewModels)
        {
            _workflow = workflow;
            _applyPresetAction = applyPresetAction;
            _getCurrentStateCallback = getCurrentStateCallback;
            _saveCallback = saveCallback;
            _deleteCallback = deleteCallback;
            _getAllSeedViewModels = getAllSeedViewModels;
            
            CancelSavePresetCommand = new RelayCommand(_ => IsSavePresetPopupOpen = false);
            
            GlobalPresets.CollectionChanged += (s, e) => 
            {
                if (_isInternalUpdate) return;
                
                OnPropertyChanged(nameof(IsPresetsSectionVisible));
                // PopulateCategoriesAndTags();
                FilteredGlobalPresetsView?.Refresh();
            };
            
            ImplementedHooks.CollectionChanged += (s, e) => OnPropertyChanged(nameof(IsHooksSectionVisible));
            
            ClearFiltersCommand = new RelayCommand(_ =>
            {
                SelectedCategoryFilter = "[ All ]";
                SelectedTagFilter = "[ All ]";
                PresetSearchFilter = "";
            });
            
            ToggleFavoriteCommand = new RelayCommand(p =>
            {
                if (p is GlobalPreset preset)
                {
                    preset.IsFavorite = !preset.IsFavorite;
                    FilteredGlobalPresetsView?.Refresh();
                    _saveCallback(null);
                }
            });

            ApplyPresetCommand = new RelayCommand(p =>
            {
                if (p is GlobalPreset preset)
                {
                    SelectedGlobalPreset = preset;
                    _applyPresetAction?.Invoke(preset);
                    IsPresetPanelOpen = false;
                }
            });

            DeletePresetCommand = new RelayCommand(p =>
            {
                if (p is GlobalPreset preset)
                {
                     _deleteCallback(preset);
                }
            });
            
            StartUpdatePresetCommand = new RelayCommand(p =>
            {
                if (p is GlobalPreset preset)
                {
                    _presetToUpdateName = preset.Name;
                    OnPropertyChanged(nameof(IsUpdateMode));
                    
                    NewPresetName = preset.Name;
                    NewPresetDescription = preset.Description;
                    NewPresetCategory = preset.Category;
                    NewPresetTags.Clear();
                    if (preset.Tags != null) foreach(var t in preset.Tags) NewPresetTags.Add(t);
                    
                    IsSavePresetPopupOpen = true;
                    IsPresetPanelOpen = false;
                }
            });

            SavePresetCommand = new RelayCommand(_ => SaveChanges(), _ => !string.IsNullOrWhiteSpace(NewPresetName));
            RandomizeAllSeedsCommand = new RelayCommand(_ => RandomizeAllSeeds());
            
            // Initialize View
            FilteredGlobalPresetsView = CollectionViewSource.GetDefaultView(GlobalPresets);
            FilteredGlobalPresetsView.Filter = FilterPresets;
            FilteredGlobalPresetsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(GlobalPreset.DisplayCategory)));
            UpdateSortDescriptions();

            this.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SelectedCategoryFilter) || 
                    e.PropertyName == nameof(SelectedTagFilter) ||
                    e.PropertyName == nameof(PresetSearchFilter))
                {
                    FilteredGlobalPresetsView?.Refresh();
                }
                if (e.PropertyName == nameof(CurrentPresetSortOption))
                {
                    UpdateSortDescriptions();
                }
            };
            
            PopulateCategoriesAndTags();
        }
        
        private void RandomizeAllSeeds()
        {
            // Randomize the main wildcard seed if it's not locked
            if (!IsSeedLocked)
            {
                WildcardSeed = Utils.GenerateSeed(0, 4294967295L);
            }
        
            // Get all seed fields from the controller
            var allSeeds = _getAllSeedViewModels?.Invoke();
            if (allSeeds == null) return;
        
            // Randomize each unlocked seed field
            foreach (var seedVm in allSeeds)
            {
                if (!seedVm.IsLocked)
                {
                    var newValue = Utils.GenerateSeed(seedVm.MinValue, seedVm.MaxValue);
                    // The setter for Value in SeedFieldViewModel already handles string conversion.
                    seedVm.Value = newValue.ToString();
                }
            }
        }
        
        public void UpdateAvailableGroups(IEnumerable<WorkflowGroupViewModel> groups)
        {
            _allAvailableGroups = groups.ToList();
        }

        private void UpdateSortDescriptions()
        {
            if (FilteredGlobalPresetsView == null) return;
            FilteredGlobalPresetsView.SortDescriptions.Clear();
            FilteredGlobalPresetsView.SortDescriptions.Add(new SortDescription("IsFavorite", ListSortDirection.Descending));
            
            if (CurrentPresetSortOption == PresetSortOption.DateModified)
            {
                FilteredGlobalPresetsView.SortDescriptions.Add(new SortDescription(nameof(GlobalPreset.LastModified), ListSortDirection.Descending));
            }
            else
            {
                FilteredGlobalPresetsView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
            }
        }

        private bool FilterPresets(object item)
        {
            if (item is not GlobalPreset preset) return false;

            if (SelectedCategoryFilter != "[ All ]" && !string.IsNullOrEmpty(SelectedCategoryFilter) && preset.Category != SelectedCategoryFilter) return false;
            if (SelectedTagFilter != "[ All ]" && !string.IsNullOrEmpty(SelectedTagFilter) && (preset.Tags == null || !preset.Tags.Contains(SelectedTagFilter))) return false;

            if (!string.IsNullOrWhiteSpace(PresetSearchFilter))
            {
                var terms = PresetSearchFilter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var term in terms)
                {
                    bool matchName = preset.Name?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchDesc = preset.Description?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!matchName && !matchDesc) return false;
                }
            }
            return true;
        }
        
        private void PopulateConfigurationItems()
        {
            PresetConfigurationItems.Clear();

            var existingPreset = IsUpdateMode ? GlobalPresets.FirstOrDefault(p => p.Name == _presetToUpdateName) : null;
            
            // Get a list of all group IDs that are actually assigned to a UI tab.
            var assignedGroupIds = _workflow.Tabs.SelectMany(t => t.GroupIds).ToHashSet();

            // Iterate only over the groups that are assigned.
            foreach (var group in _allAvailableGroups.Where(g => assignedGroupIds.Contains(g.Id)).OrderBy(g => g.Name))
            {
                List<string> layersToLoad;
                bool shouldBeSelected;

                bool isGroupInSavedPreset = IsUpdateMode && existingPreset != null && existingPreset.GroupStates.ContainsKey(group.Id);

                if (isGroupInSavedPreset)
                {
                    layersToLoad = existingPreset.GroupStates[group.Id].ToList();
                    shouldBeSelected = true;
                }
                else
                {
                    layersToLoad = group.ActiveLayers.Select(l => l.Name).ToList();
                    
                    if (IsUpdateMode)
                    {
                        shouldBeSelected = false;
                    }
                    else
                    {
                        shouldBeSelected = layersToLoad.Any();
                    }
                }
                
                var allGroupPresets = group.AllPresets;

                var item = new GlobalPresetConfigurationItem(allGroupPresets)
                {
                    GroupName = group.Name,
                    GroupId = group.Id,
                    IsSelected = shouldBeSelected,
                    IsEnabled = group.AllPresets.Any() 
                };

                foreach (var layer in layersToLoad)
                {
                    item.ActiveLayers.Add(layer);
                }
                
                item.FilteredPresetsView.Refresh();
                
                PresetConfigurationItems.Add(item);
            }
        }

        private void PopulateCategoriesAndTags()
        {
            var currentCat = SelectedCategoryFilter;
            var currentTag = SelectedTagFilter;
            var allStr = "[ All ]";

            // --- START OF OPTIMIZATION ---
            // Use HashSet for efficient collection of unique items.
            // This is significantly faster than LINQ's Distinct() on large collections.
            var categories = new HashSet<string>();
            var tags = new HashSet<string>();

            foreach (var preset in GlobalPresets)
            {
                if (!string.IsNullOrEmpty(preset.Category))
                {
                    categories.Add(preset.Category);
                }
                if (preset.Tags != null)
                {
                    foreach (var tag in preset.Tags)
                    {
                        tags.Add(tag);
                    }
                }
            }

            // Sort the results once after collecting them.
            var sortedCategories = categories.OrderBy(c => c).ToList();
            var sortedTags = tags.OrderBy(t => t).ToList();

            // Bulk update the ObservableCollections to minimize UI notifications.
            // Instead of Clear() + many Add() calls, this approach is much cleaner and faster.
            AllCategories.Clear();
            AllCategories.Add(allStr);
            foreach (var c in sortedCategories) AllCategories.Add(c);

            AllTags.Clear();
            AllTags.Add(allStr);
            foreach (var t in sortedTags) AllTags.Add(t);
            // --- END OF OPTIMIZATION ---

            SelectedCategoryFilter = AllCategories.Contains(currentCat) ? currentCat : allStr;
            SelectedTagFilter = AllTags.Contains(currentTag) ? currentTag : allStr;
        }
        
        private void SaveChanges()
        {
            var safeName = NewPresetName;
            var safeDescription = NewPresetDescription;
            var safeCategory = NewPresetCategory;
            var safeTags = NewPresetTags.ToList();

            _isInternalUpdate = true;
            
            try
            {
                if (IsUpdateMode && !string.Equals(_presetToUpdateName, safeName, StringComparison.OrdinalIgnoreCase))
                {
                     var old = GlobalPresets.FirstOrDefault(p => p.Name == _presetToUpdateName);
                     if (old != null) GlobalPresets.Remove(old);
                }
                else 
                {
                    var existing = GlobalPresets.FirstOrDefault(p => p.Name == safeName);
                    if (existing != null) GlobalPresets.Remove(existing);
                }

                var newPreset = new GlobalPreset
                {
                    Name = safeName,
                    Description = safeDescription,
                    Category = safeCategory,
                    Tags = safeTags,
                    LastModified = DateTime.UtcNow
                };
                
                if (CreateSnapshotInAllGroups)
                {
                    // Get a list of all group IDs that are actually assigned to a UI tab.
                    var assignedGroupIds = _workflow.Tabs.SelectMany(t => t.GroupIds).ToHashSet();
                    
                    // Iterate only over the groups that are assigned.
                    foreach (var groupVm in _allAvailableGroups.Where(g => assignedGroupIds.Contains(g.Id)))
                    {
                        groupVm.CreateOrUpdateSnapshot(safeName, safeDescription, safeCategory, safeTags);
                        newPreset.GroupStates[groupVm.Id] = new List<string> { safeName };
                    }
                }
                else
                {
                    // Standard logic: use selected checkboxes
                    foreach (var item in PresetConfigurationItems)
                    {
                        if (item.IsSelected && item.ActiveLayers.Any())
                        {
                            newPreset.GroupStates[item.GroupId] = item.ActiveLayers.ToList();
                        }
                    }
                }
                
                _lastUsedName = safeName;
                _lastUsedDescription = safeDescription;
                _lastUsedCategory = safeCategory;
                _lastUsedTags = safeTags;
                
                GlobalPresets.Add(newPreset);
                
                _saveCallback(newPreset);

                SelectedGlobalPreset = newPreset;
            }
            finally
            {
                _isInternalUpdate = false;
                
                OnPropertyChanged(nameof(IsPresetsSectionVisible));
                PopulateCategoriesAndTags();
                FilteredGlobalPresetsView?.Refresh();
                
                IsSavePresetPopupOpen = false;
                NewPresetName = "";
                NewPresetDescription = "";
                NewPresetCategory = "";
            }
        }
        
        /// <summary>
        /// Populates the list of hook toggles based on which hooks are implemented in the workflow.
        /// </summary>
        public void PopulateHooks(ScriptCollection scripts)
        {
            ImplementedHooks.Clear();
            if (scripts?.Hooks != null)
            {
                // Add toggles only for hooks that have a script written.
                foreach (var hook in scripts.Hooks.Where(h => !string.IsNullOrWhiteSpace(h.Value)).OrderBy(h => h.Key))
                {
                    ImplementedHooks.Add(new HookToggleViewModel { HookName = hook.Key });
                }
            }
        }
        
        /// <summary>
        /// Gets the current enabled/disabled state of all hooks.
        /// </summary>
        public Dictionary<string, bool> GetHookStates()
        {
            return ImplementedHooks.ToDictionary(h => h.HookName, h => h.IsEnabled);
        }

        /// <summary>
        /// Applies saved hook states from a session file.
        /// </summary>
        public void ApplyHookStates(Dictionary<string, bool> hookStates)
        {
            if (hookStates == null) return;
            foreach (var hookVM in ImplementedHooks)
            {
                if (hookStates.TryGetValue(hookVM.HookName, out var isEnabled))
                {
                    hookVM.IsEnabled = isEnabled;
                }
            }
        }
        
        /// <summary>
        /// Sets the selected preset without triggering the application action.
        /// Used for syncing the UI from the model state.
        /// </summary>
        public void SetSelectedPresetSilently(GlobalPreset preset)
        {
             _selectedGlobalPreset = preset;
             OnPropertyChanged(nameof(SelectedGlobalPreset));
             OnPropertyChanged(nameof(CurrentStateStatus));
        }
    }

    [AddINotifyPropertyChangedInterface]
    public class GroupPresetViewModel
    {
        public GroupPreset Model { get; }
        public string Name => Model.Name;
        public bool IsLayout => Model.IsLayout;
        
        public string DisplayCategory => string.IsNullOrWhiteSpace(Model.Category) 
            ? LocalizationService.Instance["Presets_Category_General"] 
            : Model.Category;
        
        // --- START OF FIX 1: Expose IsFavorite via the ViewModel ---
        public bool IsFavorite
        {
            get => Model.IsFavorite;
            set
            {
                if (Model.IsFavorite == value) return;
                Model.IsFavorite = value;
                // This will notify the UI that the star icon needs to update
                // OnPropertyChanged(nameof(IsFavorite)); // This is handled by Fody
            }
        }
        // --- END OF FIX 1 ---
        
        public List<PresetFieldInfo> FieldDetails { get; private set; } = new List<PresetFieldInfo>();

        public void GenerateToolTip(WorkflowGroupViewModel parentGroup, Dictionary<string, InputFieldViewModel> fieldLookup)
        {
            var details = new List<PresetFieldInfo>();
            // 'fieldLookup' allows finding a field by its Path instantly.
            
            var showTabNames = parentGroup.Tabs.Count > 1;

            // Helper to process a single field-value pair
            void AddFieldDetail(string fieldPath, JToken val)
            {
                // Fast lookup instead of LINQ FirstOrDefault
                if (fieldLookup.TryGetValue(fieldPath, out var field))
                {
                    // Find tab name (this loop is still needed but tabs are few)
                    // Optimization: Pass a second dictionary for Field->TabName if needed, but this is usually fast enough.
                    var tab = parentGroup.Tabs.FirstOrDefault(t => t.Fields.Contains(field));
                    
                    string displayValue;

                    if (field is ComboBoxFieldViewModel comboVm)
                    {
                        displayValue = comboVm.GetDisplayLabelForValue(val);
                    }
                    else
                    {
                        displayValue = FormatJTokenForDisplay(val);
                    }

                    details.Add(new PresetFieldInfo
                    {
                        FieldName = field.Name,
                        FieldValue = displayValue,
                        TabName = tab?.Name,
                        ShowTabName = showTabNames,
                        FieldPath = fieldPath
                    });
                }
            }

            if (IsLayout)
            {
                var combinedFields = new Dictionary<string, JToken>();
                var allSnippetsInGroup = parentGroup.AllPresets.Where(p => !p.IsLayout).ToList();

                if (Model.SnippetNames != null)
                {
                    foreach (var snippetName in Model.SnippetNames)
                    {
                        var snippet = allSnippetsInGroup.FirstOrDefault(s => s.Name == snippetName);
                        if (snippet != null)
                        {
                            foreach (var valuePair in snippet.Model.Values)
                            {
                                combinedFields[valuePair.Key] = valuePair.Value;
                            }
                        }
                    }
                }
                
                foreach (var combinedPair in combinedFields.OrderBy(kv => kv.Key))
                {
                     AddFieldDetail(combinedPair.Key, combinedPair.Value);
                }
            }
            else // Is Snippet
            {
                foreach (var valuePair in Model.Values)
                {
                    AddFieldDetail(valuePair.Key, valuePair.Value);
                }
            }
            
            FieldDetails = details;
        }
        
        private string FormatJTokenForDisplay(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "null";

            switch (token.Type)
            {
                case JTokenType.String:
                    var str = token.ToString();
                    if (str.Length > 1000 && (str.StartsWith("iVBOR") || str.StartsWith("/9j/") || str.StartsWith("UklG")))
                    {
                        return LocalizationService.Instance["Presets_Base64Placeholder"];
                    }
                    return $"\"{str}\"";
                case JTokenType.Boolean:
                    return token.ToString().ToLower();
                default:
                    return token.ToString();
            }
        }
        
        public GroupPresetViewModel(GroupPreset model)
        {
            Model = model;
        }
        
        public override string ToString() => Name;
    }
    
    [AddINotifyPropertyChangedInterface]
    public class ActiveLayerViewModel
    {
        public enum LayerState { Normal, Modified }
        
        public string Name { get; }
        public GroupPresetViewModel SourcePreset { get; }
        public LayerState State { get; set; } = LayerState.Normal;
        
        public List<PresetFieldInfo> FieldDetails { get; private set; }

        public ActiveLayerViewModel(GroupPresetViewModel source)
        {
            Name = source.Name;
            SourcePreset = source;
            State = LayerState.Normal;
            
            FieldDetails = source.FieldDetails.Select(fd => new PresetFieldInfo {
                FieldName = fd.FieldName,
                FieldValue = fd.FieldValue,
                TabName = fd.TabName,
                ShowTabName = fd.ShowTabName,
                FieldPath = fd.FieldPath,
                IsModified = false 
            }).ToList();
        }
    }
    
    public class InpaintFieldViewModel : InputFieldViewModel
    {
        public InpaintEditor Editor { get; }
        
        // Поля для хранения ссылок на оригинальные WorkflowField
        public WorkflowField ImageField { get; }
        public WorkflowField MaskField { get; }

        /// <summary>
        /// Конструктор для объединенного редактора или одиночного поля.
        /// </summary>
        /// <param name="primaryField">Основное поле (ImageInput или MaskInput).</param>
        /// <param name="pairedMaskField">Парное поле MaskInput (если есть).</param>
        /// <param name="property">JProperty от основного поля.</param>
        public InpaintFieldViewModel(WorkflowField primaryField, WorkflowField pairedMaskField, JProperty property) : base(primaryField, property)
        {
            // Определяем, какое поле за что отвечает
            if (primaryField.Type == FieldType.ImageInput)
            {
                ImageField = primaryField;
                MaskField = pairedMaskField; // Может быть null
            }
            else // primaryField.Type == FieldType.MaskInput
            {
                ImageField = null;
                MaskField = primaryField;
            }
            
            // Настраиваем InpaintEditor на основе наличия полей
            Editor = new InpaintEditor(
                imageEditingEnabled: ImageField != null,
                maskEditingEnabled: MaskField != null
            );
        }
        
        /// <summary>
        /// Loads image and mask data from Base64 strings (typically from a session file)
        /// into the InpaintEditor control.
        /// </summary>
        public void LoadSessionData(string imageBase64, string maskBase64)
        {
            // Load the source image if available
            if (!string.IsNullOrEmpty(imageBase64))
            {
                try
                {
                    var imageBytes = Convert.FromBase64String(imageBase64);
                    Editor.SetSourceImage(imageBytes);
                }
                catch (FormatException)
                {
                    // Ignore invalid base64 data, the field will just remain empty.
                    Logger.Log($"Could not load session image for '{Name}' due to invalid Base64 format.", LogLevel.Warning);
                }
            }

            // --- START OF CHANGE: Call the new method to convert mask image to strokes ---
            if (!string.IsNullOrEmpty(maskBase64))
            {
                try
                {
                    var maskBytes = Convert.FromBase64String(maskBase64);
                    // This is an async void method that will update the UI when done.
                    Editor.LoadStrokesFromMaskImageAsync(maskBytes);
                }
                catch (FormatException)
                {
                    // Ignore invalid base64 data for the mask.
                    Logger.Log($"Could not load session mask for '{Name}' due to invalid Base64 format.", LogLevel.Warning);
                }
            }
            // --- END OF CHANGE ---
        }
    }
    
    [AddINotifyPropertyChangedInterface]
    public class WorkflowGroupTabViewModel : INotifyPropertyChanged
    {
        public WorkflowGroupTab Model { get; }
        public string Name
        {
            get => Model.Name;
            set
            {
                if (Model.Name != value)
                {
                    Model.Name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }
        public ObservableCollection<InputFieldViewModel> Fields { get; } = new();
    
        public bool BindingTrigger { get; private set; }
        
        public void RefreshBindings()
        {
            BindingTrigger = !BindingTrigger;
        }
        
        public bool IsRenaming
        {
            get => Model.IsRenaming;
            set
            {
                if (Model.IsRenaming != value)
                {
                    Model.IsRenaming = value;
                    OnPropertyChanged(nameof(IsRenaming));
                }
            }
        }
        
        public string HighlightColor
        {
            get => Model.HighlightColor;
            set
            {
                if (Model.HighlightColor != value)
                {
                    Model.HighlightColor = value;
                    OnPropertyChanged(nameof(HighlightColor));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        
        public WorkflowGroupTabViewModel(WorkflowGroupTab model)
        {
            Model = model;
        }
    }
    
    // --- ViewModel для группы ---
    [AddINotifyPropertyChangedInterface]
    public class WorkflowGroupViewModel : INotifyPropertyChanged
    {
        private readonly WorkflowGroup _model;
        private readonly Workflow _workflow; 
        
        private readonly AppSettings _settings;
        
        private static string _lastUsedPresetName = "";
        private static string _lastUsedDescription = "";
        private static string _lastUsedCategory = "";
        private static List<string> _lastUsedTags = new List<string>();
        private static SavePresetType _lastUsedType = SavePresetType.Snippet;
        private static HashSet<string> _lastSelectedFieldPaths = new HashSet<string>();
        public bool IsUpdateMode => !string.IsNullOrEmpty(_presetToUpdateName);
        
        public WorkflowGroup Model => _model;
       public string Name
       {
           get => _model.Name;
           set
           {
               if (_model.Name != value)
               {
                   _model.Name = value;
                   OnPropertyChanged(nameof(Name));
               }
           }
       }
        
        public bool IsRenaming
        {
            get => _model.IsRenaming;
            set
            {
                if (_model.IsRenaming != value)
                {
                    _model.IsRenaming = value;
                    OnPropertyChanged(nameof(IsRenaming));
                }
            }
        }
        
        /// <summary>
        /// Gets or sets a value indicating whether this group is currently displayed in a separate window.
        /// This is a transient UI state and is not saved to the workflow file.
        /// </summary>
        [JsonIgnore]
        public bool IsUndocked { get; set; }
        
        public bool IsExpanded
        {
            get => _model.IsExpanded;
            set
            {
                if (_model.IsExpanded != value)
                {
                    _model.IsExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }
        
        /// <summary>
        /// A collection of view models for the tabs within this group.
        /// </summary>
        public ObservableCollection<WorkflowGroupTabViewModel> Tabs { get; set; } = new();
        
        /// <summary>
        /// The currently selected tab in the group's UI.
        /// </summary>
        private WorkflowGroupTabViewModel _selectedTab;
        public WorkflowGroupTabViewModel SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (_selectedTab != value)
                {
                    _selectedTab = value;
                    OnPropertyChanged(nameof(SelectedTab));
                    if (value != null)
                    {
                        TriggerPendingHighlightsForTab(value);
                    }
                }
            }
        }

        /// <summary>
        /// Controls whether the group is expanded in the UI Constructor.
        /// This property is not saved to the workflow JSON file.
        /// </summary>
        public bool IsExpandedInDesigner
        {
            get => _model.IsExpandedInDesigner;
            set
            {
                if (_model.IsExpandedInDesigner != value)
                {
                    _model.IsExpandedInDesigner = value;
                    OnPropertyChanged(nameof(IsExpandedInDesigner));
                }
            }
        }
        
        //public ObservableCollection<InputFieldViewModel> Fields { get; set; } = new();

        /// <summary>
        /// The highlight color for the group in HEX format.
        /// </summary>
        /// <summary>
        /// The highlight color for the group in HEX format.
        /// </summary>
        public string HighlightColor
        {
            get => _model.HighlightColor;
            set
            {
                if (_model.HighlightColor != value)
                {
                    _model.HighlightColor = value;
                    OnPropertyChanged(nameof(HighlightColor));
                }
            }
        }
        
        public Guid Id => _model.Id;
        public bool HasPresets => AllPresets.Any();
        
        /// <summary>
        /// Contains all presets (both Snippets and Layouts) available for this group.
        /// </summary>
        public ObservableCollection<GroupPresetViewModel> AllPresets { get; } = new();
        
        /// <summary>
        /// Contains the layers currently applied to this group.
        /// </summary>
        public ObservableCollection<ActiveLayerViewModel> ActiveLayers { get; } = new();
        
        public string PresetSearchFilter { get; set; }

        private bool PresetFilter(object item)
        {
            if (item is not GroupPresetViewModel presetVm) return false;

            // 1. Filter by Category
            var allCategories = "[ All ]";
            if (SelectedCategoryFilter != allCategories && presetVm.Model.Category != SelectedCategoryFilter)
                {
                return false;
                }

            // 2. Filter by Tag
            var allTags = "[ All ]";
            if (SelectedTagFilter != allTags && (presetVm.Model.Tags == null || !presetVm.Model.Tags.Contains(SelectedTagFilter)))
                {
                return false;
                }
        
            // 3. Apply search text filter
                if (!string.IsNullOrWhiteSpace(PresetSearchFilter))
                {
                    var searchTerms = PresetSearchFilter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                bool nameMatch = searchTerms.All(term => presetVm.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
                bool descriptionMatch = !string.IsNullOrEmpty(presetVm.Model.Description) && 
                                        searchTerms.All(term => presetVm.Model.Description.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);

                if (!nameMatch && !descriptionMatch)
                {
                    return false;
                }
            }
        
            // 4. Apply "filter by fields" if active
                if (IsFilterByFieldsActive)
                {
                    var selectedFieldPaths = FieldsForPresetFilter
                        .Where(f => f.IsSelected)
                        .Select(f => f.Path)
                        .ToHashSet();
                    
                    if (selectedFieldPaths.Any())
                    {
                        if (CurrentPresetFilterLogic == PresetFilterLogic.AND)
                        {
                        if (!selectedFieldPaths.All(fieldPath => presetVm.Model.Values.ContainsKey(fieldPath)))
                            return false;
                        }
                        else // OR logic
                        {
                        if (!presetVm.Model.Values.Keys.Any(selectedFieldPaths.Contains))
                            return false;
                        }
                    }
                }

            return true;
                }
        
        public ICollectionView FilteredSnippetsView { get; private set; }
        public ICollectionView FilteredLayoutsView { get; private set; }

        
        public IEnumerable<GroupPresetViewModel> Snippets => AllPresets.Where(p => !p.IsLayout);
        public IEnumerable<GroupPresetViewModel> Layouts => AllPresets.Where(p => p.IsLayout);

        /// <summary>
        /// A textual representation of the current state based on active layers.
        /// </summary>
        public string CurrentStateStatus
        {
            get
            {
                if (!ActiveLayers.Any()) return LocalizationService.Instance["Presets_State_Unsaved"];
                
                var modifiedLayer = ActiveLayers.FirstOrDefault(l => l.State == ActiveLayerViewModel.LayerState.Modified);
                if (modifiedLayer != null)
                {
                    return string.Format(LocalizationService.Instance["Presets_State_Modified"], modifiedLayer.Name);
                }

                if (ActiveLayers.Count == 1)
        {
                    return string.Format(LocalizationService.Instance["Presets_State_Single"], ActiveLayers[0].Name);
                }

                return string.Format(LocalizationService.Instance["Presets_State_Multiple"], ActiveLayers.Count);
            }
        }

        private bool _isPresetPanelOpen;
        public bool IsPresetPanelOpen
        {
            get => _isPresetPanelOpen;
            set
            {
                if (_isPresetPanelOpen == value) return;
                _isPresetPanelOpen = value;
                OnPropertyChanged(nameof(IsPresetPanelOpen));

                // If the panel is being opened, force a notification
                // for the sort and filter properties to ensure the UI is in sync.
                if (value)
                {
                    OnPropertyChanged(nameof(CurrentPresetSortOption));
                    OnPropertyChanged(nameof(CurrentPresetFilterLogic));
                }
            }
        }

        // Свойство для InpaintEditor
        public InpaintEditor InpaintEditorControl { get; set; }
        public bool HasInpaintEditor => InpaintEditorControl != null;
        
        public ObservableCollection<PresetFieldSelectionViewModel> FieldsForPresetSelection { get; } = new();

        private bool _isSavePresetPopupOpen;
        public bool IsSavePresetPopupOpen
        {
            get => _isSavePresetPopupOpen;
            set
            {
                if (_isSavePresetPopupOpen == value) return;
                
                if (value)
                {
                    // Creation mode (New Preset)
                    if (string.IsNullOrEmpty(_presetToUpdateName))
                    {
                        // Restore metadata (Name, Desc, etc.) from static history
                        NewPresetName = _lastUsedPresetName;
                        NewPresetDescription = _lastUsedDescription;
                        NewPresetCategory = _lastUsedCategory;
                        
                        NewPresetTags.Clear();
                        if (_lastUsedTags != null)
                        {
                            foreach(var t in _lastUsedTags) NewPresetTags.Add(t);
                        }
                        
                        if (ActiveLayers.Any())
                        {
                             NewPresetType = SavePresetType.Layout;
                        }
                        else
                        {
                             NewPresetType = _lastUsedType;
                        }

                        // Restore field selection based on paths found in history
                        FieldsForPresetSelection.Clear();
                        
                        // Check if we have any history recorded
                        bool hasHistory = _lastSelectedFieldPaths != null && _lastSelectedFieldPaths.Count > 0;

                        foreach (var tabVm in Tabs)
                        {
                            var savableFieldsInTab = tabVm.Model.Fields
                                .Where(f => f.Type != FieldType.ScriptButton &&
                                            f.Type != FieldType.Label &&
                                            f.Type != FieldType.Separator &&
                                            f.Type != FieldType.Spoiler &&
                                            f.Type != FieldType.SpoilerEnd);
                    
                            foreach (var field in savableFieldsInTab)
                            {
                                // If history exists, strictly check if this path was selected last time.
                                // If history is empty (first run), select everything by default.
                                bool isSelected = !hasHistory || _lastSelectedFieldPaths.Contains(field.Path);

                                FieldsForPresetSelection.Add(new PresetFieldSelectionViewModel
                                {
                                    Name = field.Name,
                                    Path = field.Path,
                                    IsSelected = isSelected, 
                                    TabName = tabVm.Name
                                });
                            }
                        }
                    }
                    // Update mode (Editing existing preset)
                    else 
                    {
                         // Logic for update mode remains the same (loading from existing preset)
                         // ... (omitted for brevity, no changes needed here) ...
                    }
                }
                else
                {
                    _presetToUpdateName = null;
                    OnPropertyChanged(nameof(IsUpdateMode)); 
                }

                _isSavePresetPopupOpen = value;
                OnPropertyChanged(nameof(IsSavePresetPopupOpen));
            }
        }

        public string NewPresetName { get; set; }
        public string NewPresetDescription { get; set; }
        public string NewPresetCategory { get; set; }
        public ObservableCollection<string> NewPresetTags { get; set; } = new ObservableCollection<string>();

        
        public enum SavePresetType { Snippet, Layout }
        public SavePresetType NewPresetType { get; set; }
        
        public ICommand StartUpdatePresetCommand { get; }
        private string _presetToUpdateName;
        public ICommand SavePresetCommand { get; }
        public ICommand DeletePresetCommand { get; }
        
        
        public ICommand ApplyPresetCommand { get; }
        public ICommand DetachLayerCommand { get; }
        public ICommand ReapplyLayerCommand { get; }
        public ICommand UpdateLayerCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand ClearPresetFiltersCommand { get; }
        
        public PresetSortOption CurrentPresetSortOption { get; set; } = PresetSortOption.Alphabetical;
        public PresetFilterLogic CurrentPresetFilterLogic { get; set; } = PresetFilterLogic.OR;
        public bool IsFilterByFieldsActive { get; set; }
        public ObservableCollection<PresetFieldSelectionViewModel> FieldsForPresetFilter { get; } = new();
        
        public ObservableCollection<string> AllCategories { get; } = new();
        public string SelectedCategoryFilter { get; set; }
        
        public ObservableCollection<string> AllTags { get; } = new();
        public string SelectedTagFilter { get; set; }


        public ICommand SelectAllFieldsForPresetCommand { get; }
        public ICommand DeselectAllFieldsForPresetCommand { get; }
        public ICommand AddTabCommand { get; }
        public ICommand RemoveSelectedTabCommand { get; }
        public ICommand ExportPresetsCommand { get; }
        public ICommand ImportPresetsCommand { get; }
        public ICommand ClearGroupPresetsCommand { get; }

        
        /// <summary>
        /// This event is raised whenever presets are added or removed, signaling a need to save the workflow.
        /// </summary>
        public event Action PresetsModified;
        
        public void ReloadPresetsAndNotify()
        {
            LoadPresets();
            OnPropertyChanged(nameof(HasPresets)); // Notify the UI that the preset status might have changed
            PresetsModified?.Invoke();
        }
        
        
        /// <summary>
        /// A transient property to store the name of the parent UI tab for display purposes.
        /// This is not saved to the workflow file.
        /// </summary>
        [JsonIgnore]
        public string ParentTabName { get; set; }
        
        public event PropertyChangedEventHandler PropertyChanged;
        private readonly List<InputFieldViewModel> _fieldsPendingHighlight = new List<InputFieldViewModel>();
        
        private bool _isApplyingPreset = false; // Flag to prevent re-entrancy issues
        
        public event Action ActiveLayersChanged;
        
        public WorkflowGroupViewModel(WorkflowGroup model, Workflow workflow, AppSettings settings)
        {
            _model = model;
            _workflow = workflow; // Store the reference
            _settings = settings;
            
            foreach (var tabModel in _model.Tabs)
            {
                var tabVm = new WorkflowGroupTabViewModel(tabModel);
                Tabs.Add(tabVm);
            }
            SelectedTab = Tabs.FirstOrDefault();
            
            // --- NEW: Refresh status text when layers change ---
            ActiveLayers.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(CurrentStateStatus));
                ActiveLayersChanged?.Invoke();
            };
            
            // --- NEW: Refresh filtered lists when search text changes ---
            this.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PresetSearchFilter) || 
                    e.PropertyName == nameof(IsFilterByFieldsActive) || 
                    e.PropertyName == nameof(CurrentPresetFilterLogic) ||
                    e.PropertyName == nameof(SelectedCategoryFilter) ||
                    e.PropertyName == nameof(SelectedTagFilter))
                {
                    FilteredSnippetsView?.Refresh();
                    FilteredLayoutsView?.Refresh();
                }
                
                // --- START OF FIX 2: Rebuild SortDescriptions when sort option changes ---
                if (e.PropertyName == nameof(CurrentPresetSortOption))
                {
                    RebuildPresetViews(); // Rebuilds views with new sorting rules
                }
                // --- END OF FIX 2 ---
            };
            
            LoadPresets();
            PopulateFieldsForPresetFilter();
            
            ApplyPresetCommand = new RelayCommand(p => ApplyPreset(p as GroupPresetViewModel));
            DetachLayerCommand = new RelayCommand(p => DetachLayer(p as ActiveLayerViewModel));
            ReapplyLayerCommand = new RelayCommand(p => ReapplyLayer(p as ActiveLayerViewModel));
            UpdateLayerCommand = new RelayCommand(p =>
            {
                if (p is ActiveLayerViewModel layer)
                {
                    // The "Update" command on a layer is just a shortcut for starting the update process.
                    StartUpdatePresetCommand.Execute(layer.SourcePreset);
                }
            });

            ToggleFavoriteCommand = new RelayCommand(p =>
            {
                if (p is GroupPresetViewModel presetVm)
                {
                    presetVm.IsFavorite = !presetVm.IsFavorite; // Use the ViewModel's property
                    // Trigger a resort of the list and notify that a save is needed
                    FilteredSnippetsView?.Refresh();
                    FilteredLayoutsView?.Refresh();
                    PresetsModified?.Invoke();
                }
            });

            ClearPresetFiltersCommand = new RelayCommand(_ =>
            {
                SelectedCategoryFilter = AllCategories.FirstOrDefault();
                SelectedTagFilter = AllTags.FirstOrDefault();
                // PropertyChanged events will trigger the refresh automatically
            });
            
            StartUpdatePresetCommand = new RelayCommand(param =>
            {
                if (param is GroupPresetViewModel existingPresetVm)
                {
                    _presetToUpdateName = existingPresetVm.Name;
                    OnPropertyChanged(nameof(IsUpdateMode)); // Notify UI that we are in update mode
                    NewPresetName = existingPresetVm.Name;
                    NewPresetDescription = existingPresetVm.Model.Description;
                    NewPresetCategory = existingPresetVm.Model.Category;
                    NewPresetTags.Clear();
                    if (existingPresetVm.Model.Tags != null)
                    {
                        foreach(var t in existingPresetVm.Model.Tags) NewPresetTags.Add(t);
                    }

                    NewPresetType = existingPresetVm.IsLayout ? SavePresetType.Layout : SavePresetType.Snippet;

                    FieldsForPresetSelection.Clear();
                    
                    HashSet<string> selectedPaths;
                    if (existingPresetVm.IsLayout)
                    {
                        // For layouts, select fields from all contained snippets
                        selectedPaths = new HashSet<string>();
                        var snippetsInLayout = AllPresets.Where(p => !p.IsLayout && existingPresetVm.Model.SnippetNames.Contains(p.Name));
                        foreach (var path in snippetsInLayout.SelectMany(s => s.Model.Values.Keys))
                        {
                            selectedPaths.Add(path);
                        }
                    }
                    else
                    {
                        selectedPaths = new HashSet<string>(existingPresetVm.Model.Values.Keys);
                    }

                    // Iterate through tabs and their fields in their defined order.
                    foreach (var tabVm in Tabs)
                    {
                        var savableFieldsInTab = tabVm.Model.Fields
                            .Where(f => f.Type != FieldType.ScriptButton &&
                                        f.Type != FieldType.Label &&
                                        f.Type != FieldType.Separator &&
                                        f.Type != FieldType.Spoiler &&
                                        f.Type != FieldType.SpoilerEnd);

                        foreach (var fieldModel in savableFieldsInTab)
                        {
                            FieldsForPresetSelection.Add(new PresetFieldSelectionViewModel
                            {
                                Name = fieldModel.Name,
                                Path = fieldModel.Path,
                                IsSelected = selectedPaths.Contains(fieldModel.Path),
                                TabName = tabVm.Name // Add tab name for UI context
                            });
                        }
                    }
                    
                    IsSavePresetPopupOpen = true;
                }
            });
            
            SavePresetCommand = new RelayCommand(_ =>
            {
                if (string.IsNullOrWhiteSpace(NewPresetName)) return;

                if (!string.IsNullOrEmpty(_presetToUpdateName) && _presetToUpdateName.Equals(NewPresetName, StringComparison.OrdinalIgnoreCase))
                {
                    if (_workflow.Presets.TryGetValue(Id, out var presets))
                    {
                        presets.RemoveAll(p => p.Name.Equals(_presetToUpdateName, StringComparison.OrdinalIgnoreCase));
                    }
                }
    
                var selectedFields = FieldsForPresetSelection.Where(f => f.IsSelected).ToList();
                SaveCurrentStateAsPreset(NewPresetName, selectedFields, NewPresetType == SavePresetType.Layout);
    
                // Update static metadata history
                _lastUsedPresetName = NewPresetName;
                _lastUsedDescription = NewPresetDescription;
                _lastUsedCategory = NewPresetCategory;
                _lastUsedTags = NewPresetTags.ToList();
                _lastUsedType = NewPresetType;
    
                // Update static field path history
                // We replace the set completely with the currently selected paths
                _lastSelectedFieldPaths = new HashSet<string>(selectedFields.Select(f => f.Path));

                _presetToUpdateName = null;
                OnPropertyChanged(nameof(IsUpdateMode));
                IsSavePresetPopupOpen = false;
                NewPresetName = string.Empty;

                LoadPresets();
                PresetsModified?.Invoke();

            }, _ => !string.IsNullOrWhiteSpace(NewPresetName));

            DeletePresetCommand = new RelayCommand(param =>
            {
                if (param is string presetName)
                {
                    // --- START OF CHANGE ---
                    bool proceed = !_settings.ShowPresetDeleteConfirmation ||
                                   (MessageBox.Show(
                                       string.Format(LocalizationService.Instance["Presets_DeleteConfirmMessage"], presetName),
                                       LocalizationService.Instance["Presets_DeleteConfirmTitle"],
                                       MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);

                    if (proceed)
                    {
                        DeletePreset(presetName);
                        }
                    // --- END OF CHANGE ---
                    }
            });

            SelectAllFieldsForPresetCommand = new RelayCommand(_ => ToggleAllFieldsForPreset(true));
            DeselectAllFieldsForPresetCommand = new RelayCommand(_ => ToggleAllFieldsForPreset(false));

            AddTabCommand = new RelayCommand(_ =>
            {
                var baseName = "New Tab";
                string newTabName = baseName;
                int counter = 1;
                while (Tabs.Any(t => t.Name == newTabName))
                {
                    newTabName = $"{baseName} {++counter}";
                }
                var newTabModel = new WorkflowGroupTab { Name = newTabName };
                _model.Tabs.Add(newTabModel);
                var newTabVm = new WorkflowGroupTabViewModel(newTabModel);
                Tabs.Add(newTabVm);
                SelectedTab = newTabVm;
            });

            RemoveSelectedTabCommand = new RelayCommand(_ =>
            {
                if (SelectedTab != null)
                {
                    RemoveTab(SelectedTab);
                }
            }, _ => SelectedTab != null && Tabs.Count > 1);
            ExportPresetsCommand = new RelayCommand(ExportPresets);
            ImportPresetsCommand = new RelayCommand(ImportPresets);
            ClearGroupPresetsCommand = new RelayCommand(ClearGroupPresets, _ => AllPresets.Any());
        }
        
        /// <summary>
        /// Removes a sub-tab from the group, with an optional confirmation check.
        /// </summary>
        /// <param name="tabToRemove">The view model of the tab to be removed.</param>
        public void RemoveTab(WorkflowGroupTabViewModel tabToRemove)
        {
            if (tabToRemove == null || Tabs.Count <= 1) return;

            bool proceed = !_settings.ShowTabDeleteConfirmation ||
                           (MessageBox.Show(
                               string.Format("Are you sure you want to delete the tab '{0}' and all its fields?", tabToRemove.Name),
                               "Confirm Deletion",
                               MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);

            if (proceed)
            {
                _model.Tabs.Remove(tabToRemove.Model);
                Tabs.Remove(tabToRemove);
                SelectedTab = Tabs.FirstOrDefault();
            }
        }
        
        private void ClearGroupPresets(object obj)
        {
            var message = string.Format(LocalizationService.Instance["UIConstructor_ConfirmClearPresetsMessage"], Name);
            var title = LocalizationService.Instance["UIConstructor_ConfirmClearPresetsTitle"];

            if (MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                // 1. Remove from Workflow Data (Persistence)
                if (_workflow.Presets.ContainsKey(Id))
                {
                    _workflow.Presets[Id].Clear();
                }

                // 2. Update ViewModel (UI)
                ReloadPresetsAndNotify();
                
                Logger.LogToConsole(string.Format(LocalizationService.Instance["UIConstructor_PresetsClearedMessage"], Name));
            }
        }
        
        
        public void ClearActiveLayers()
        {
            ActiveLayers.Clear();
        }
        
        private void PopulateFieldsForPresetFilter()
        {
            FieldsForPresetFilter.Clear();
            // Iterate through tabs and their fields in their defined order
            foreach (var tabVm in Tabs)
            {
                var savableFieldsInTab = tabVm.Model.Fields
                    .Where(f => f.Type != FieldType.ScriptButton &&
                                f.Type != FieldType.Label &&
                                f.Type != FieldType.Separator &&
                                f.Type != FieldType.Spoiler &&
                                f.Type != FieldType.SpoilerEnd);
        
                foreach (var field in savableFieldsInTab)
                {
                    var fieldVm = new PresetFieldSelectionViewModel
                    {
                        Name = field.Name,
                        Path = field.Path,
                        IsSelected = false, // Start unselected
                        TabName = tabVm.Name
                    };
                    // When a field is selected/deselected for filtering, refresh the lists
                    fieldVm.PropertyChanged += (s, e) => {
                        if (e.PropertyName == nameof(PresetFieldSelectionViewModel.IsSelected) && IsFilterByFieldsActive)
                        {
                            FilteredSnippetsView?.Refresh();
                            FilteredLayoutsView?.Refresh();
                        }
                    };
                    FieldsForPresetFilter.Add(fieldVm);
                }
            }
        }
        
        private void ExportPresets(object obj)
        {
            if (!_workflow.Presets.TryGetValue(Id, out var presets) || !presets.Any())
            {
                MessageBox.Show(LocalizationService.Instance["Presets_ExportError_NoPresets"], "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // --- START OF REWORK: Create a more detailed, human-readable export structure ---
            var allFieldsInGroup = this.Tabs.SelectMany(t => t.Fields).ToList();
            
            var exportableSnippets = presets.Where(p => !p.IsLayout).Select(snippet => new
            {
                name = snippet.Name,
                fields = snippet.Values.Select(valuePair => new
                {
                    name = allFieldsInGroup.FirstOrDefault(f => f.Path == valuePair.Key)?.Name ?? "Unknown Field",
                    path = valuePair.Key,
                    value = valuePair.Value
                }).ToList()
            }).ToList();
            
            var exportableLayouts = presets.Where(p => p.IsLayout).ToList();

            var exportData = new
            {
                snippets = exportableSnippets,
                layouts = exportableLayouts
            };
            // --- END OF REWORK ---

            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                FileName = $"{Name}_presets.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(exportData, Formatting.Indented);
                    File.WriteAllText(dialog.FileName, json);
                    Logger.LogToConsole(
                        string.Format(LocalizationService.Instance["Presets_ExportSuccessMessage"], Name));
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, $"Failed to export presets for group '{Name}'.");
                }
            }
        }

        private void ImportPresets(object obj)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(dialog.FileName);
                    
                    // --- START OF REWORK: Deserialize new detailed structure and handle legacy format ---
                    var importedSnippets = new List<GroupPreset>();
                    var importedLayouts = new List<GroupPreset>();

                    var tempJson = JObject.Parse(json);

                    if (tempJson["snippets"] != null || tempJson["layouts"] != null)
                    {
                        // New detailed format
                        if (tempJson["snippets"] is JArray snippetsArray)
                        {
                            foreach (var token in snippetsArray)
                            {
                                var snippetName = token["name"]?.ToString();
                                var fields = token["fields"] as JArray;

                                if (string.IsNullOrEmpty(snippetName) || fields == null) continue;

                                var newSnippet = new GroupPreset { Name = snippetName };
                                foreach (var fieldToken in fields)
                                {
                                    var path = fieldToken["path"]?.ToString();
                                    var value = fieldToken["value"];
                                    // The 'name' field is ignored on import, only 'path' is used.
                                    if (!string.IsNullOrEmpty(path) && value != null)
                                    {
                                        newSnippet.Values[path] = value;
                                    }
                                }
                                importedSnippets.Add(newSnippet);
                            }
                        }
                        importedLayouts = tempJson["layouts"]?.ToObject<List<GroupPreset>>() ?? new List<GroupPreset>();
                    }
                    else // Legacy format (simple array)
                    {
                        var legacyPresets = JArray.Parse(json).ToObject<List<GroupPreset>>();
                        if (legacyPresets != null)
                        {
                            importedSnippets.AddRange(legacyPresets.Where(p => !p.IsLayout));
                        }
                    }
                    
                    var allImportedPresets = importedSnippets.Concat(importedLayouts).ToList();

                    if (!allImportedPresets.Any())
                    {
                        throw new Exception("File is empty or has an invalid format.");
                    }
                    // --- END OF REWORK ---

                    var message = string.Format(LocalizationService.Instance["Presets_ImportConfirmMessage"], allImportedPresets.Count, Name);
                    var caption = LocalizationService.Instance["Presets_ImportConfirmTitle"];
                    
                    var messageBoxResult = MessageBox.Show(message, caption, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                    
                    if (messageBoxResult == MessageBoxResult.Cancel) return;

                    var existingPresets = _workflow.Presets.ContainsKey(Id) ? _workflow.Presets[Id] : new List<GroupPreset>();

                    if (messageBoxResult == MessageBoxResult.Yes) // Yes = Replace
                    {
                        _workflow.Presets[Id] = allImportedPresets;
                    }
                    else // No = Merge
                    {
                        foreach (var importedPreset in allImportedPresets)
                        {
                            var existing = existingPresets.FirstOrDefault(p => p.Name.Equals(importedPreset.Name, StringComparison.OrdinalIgnoreCase));
                            if (existing != null)
                            {
                                // Overwrite existing preset with imported one
                                existingPresets.Remove(existing);
                            }
                            existingPresets.Add(importedPreset);
                        }
                        _workflow.Presets[Id] = existingPresets;
                    }
                    // --- END OF REWORK ---
                    
                    ReloadPresetsAndNotify();

                    Logger.LogToConsole(string.Format(LocalizationService.Instance["Presets_ImportSuccessMessage"], Name));
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, $"Failed to import presets for group '{Name}'.");
                    MessageBox.Show(
                        LocalizationService.Instance["Presets_ImportErrorMessage"],
                        LocalizationService.Instance["Presets_ImportErrorTitle"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }
        
        private void ToggleAllFieldsForPreset(bool isSelected)
        {
            foreach (var field in FieldsForPresetSelection)
            {
                field.IsSelected = isSelected;
            }
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        
        // --- START OF CHANGES: New methods for preset logic ---
        private void PopulateCategoriesAndTags()
        {
            var allCategories = "[ All ]";
            var allTags = "[ All ]";
            
            var categories = AllPresets.Select(p => p.Model.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
            var tags = AllPresets.SelectMany(p => p.Model.Tags ?? new List<string>()).Distinct().OrderBy(t => t).ToList();
            
            AllCategories.Clear();
            AllCategories.Add(allCategories);
            categories.ForEach(c => AllCategories.Add(c));
            
            AllTags.Clear();
            AllTags.Add(allTags);
            tags.ForEach(t => AllTags.Add(t));

            SelectedCategoryFilter = allCategories;
            SelectedTagFilter = allTags;
        }

        private void RebuildPresetViews()
        {
            // For Snippets
            FilteredSnippetsView = CollectionViewSource.GetDefaultView(Snippets);
            FilteredSnippetsView.Filter = PresetFilter;
            if (FilteredSnippetsView.CanGroup)
            {
                FilteredSnippetsView.GroupDescriptions.Clear();
                FilteredSnippetsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(GroupPresetViewModel.DisplayCategory)));
            }
            
            // For Layouts
            FilteredLayoutsView = CollectionViewSource.GetDefaultView(Layouts);
            FilteredLayoutsView.Filter = PresetFilter;
            if (FilteredLayoutsView.CanGroup)
            {
                FilteredLayoutsView.GroupDescriptions.Clear();
                FilteredLayoutsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(GroupPresetViewModel.DisplayCategory)));
            }

            // Manually refresh sorting
             ICollectionView[] views = { FilteredSnippetsView, FilteredLayoutsView };
             foreach (var view in views)
             {
                 view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription("IsFavorite", ListSortDirection.Descending));
                 if (CurrentPresetSortOption == PresetSortOption.DateModified)
                 {
                     view.SortDescriptions.Add(new SortDescription("Model.LastModified", ListSortDirection.Descending));
                 }
                 else
                 {
                     view.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
                 }
             }
            
            OnPropertyChanged(nameof(FilteredSnippetsView));
            OnPropertyChanged(nameof(FilteredLayoutsView));
        }
        
        public void LoadPresets()
        {
            var hadPresetsBefore = AllPresets.Any();

            AllPresets.Clear();
            
            var fieldLookup = new Dictionary<string, InputFieldViewModel>();
            
            // 1. Сценарий главного окна: Ищем поля во ViewModel
            foreach (var tab in Tabs)
            {
                foreach (var field in tab.Fields)
                {
                    if (!string.IsNullOrEmpty(field.Path) && !fieldLookup.ContainsKey(field.Path))
                    {
                        fieldLookup[field.Path] = field;
                    }
                }
            }

            // 2. Сценарий Дизайнера (UIConstructor): ViewModel полей пуст, берем из Модели
            if (fieldLookup.Count == 0)
            {
                foreach (var tabModel in _model.Tabs)
                {
                    foreach (var fieldModel in tabModel.Fields)
                    {
                        if (!string.IsNullOrEmpty(fieldModel.Path) && !fieldLookup.ContainsKey(fieldModel.Path))
                        {
                            // Создаем временную ViewModel-обертку для генератора тултипов.
                            // JProperty передаем null, так как в дизайнере нам нужны только метаданные (Имя, Путь).
                            // Тип поля не важен для отображения текста, используем TextFieldViewModel как универсальный контейнер.
                            fieldLookup[fieldModel.Path] = new TextFieldViewModel(fieldModel, null);
                        }
                    }
                }
            }
            
            if (_workflow.Presets.TryGetValue(Id, out var presets))
            {
                foreach (var preset in presets)
                {
                    var presetVM = new GroupPresetViewModel(preset);
                    presetVM.GenerateToolTip(this, fieldLookup);
                    AllPresets.Add(presetVM);
                }
            }
            
            var hasPresetsAfter = AllPresets.Any(); // Check state after modification
            
            // If the "has presets" status has changed, notify the UI
            if (hadPresetsBefore != hasPresetsAfter)
            {
                OnPropertyChanged(nameof(HasPresets));
            }
            
            PopulateCategoriesAndTags();
            RebuildPresetViews();
            
            RebuildActiveLayersFromState();
        }

        public void ApplyPreset(GroupPresetViewModel presetVM)
        {
            if (presetVM == null) return;

            // If it's a Layout, apply its snippets recursively
            if (presetVM.IsLayout && presetVM.Model.SnippetNames != null)
            {
                foreach (var snippetName in presetVM.Model.SnippetNames)
                {
                    var snippetVM = AllPresets.FirstOrDefault(p => !p.IsLayout && p.Name == snippetName);
                    if (snippetVM != null)
                    {
                        ApplySnippet(snippetVM, isPartOfLayout: true);
                }
                }
            }
            // If it's a Snippet, apply it directly
            else if (!presetVM.IsLayout)
            {
                ApplySnippet(presetVM);
            }
        }
                
        private void ApplySnippet(GroupPresetViewModel snippetVM, bool isPartOfLayout = false)
                {
            if (snippetVM == null || snippetVM.IsLayout) return;

            var changedFields = new List<InputFieldViewModel>();
            var conflictingLayers = new List<ActiveLayerViewModel>();

            // Check for conflicts: find any active layers that modify the same fields
            var fieldsInNewSnippet = snippetVM.Model.Values.Keys.ToHashSet();
            foreach (var activeLayer in ActiveLayers)
                    {
                var fieldsInActiveLayer = activeLayer.SourcePreset.Model.Values.Keys;
                if (fieldsInActiveLayer.Any(fieldsInNewSnippet.Contains))
                        {
                    conflictingLayers.Add(activeLayer);
                        }
                    }

            // Detach conflicting layers
            foreach (var layerToRemove in conflictingLayers)
                    {
                ActiveLayers.Remove(layerToRemove);
                Logger.LogToConsole($"Layer '{layerToRemove.Name}' was detached because '{snippetVM.Name}' modifies the same fields.");
            }
            
            // Apply the new values
            foreach (var valuePair in snippetVM.Model.Values)
                        {
                var fieldVM = Tabs.SelectMany(t => t.Fields).FirstOrDefault(f => f.Path == valuePair.Key);
                if (fieldVM == null) continue;

                if (ApplyFieldValue(fieldVM, valuePair.Value))
                    {
                        changedFields.Add(fieldVM);
                    }
                }

            // Add the new layer to the active list
            var existingLayer = ActiveLayers.FirstOrDefault(l => l.Name == snippetVM.Name);
            if (existingLayer == null)
            {
                ActiveLayers.Add(new ActiveLayerViewModel(snippetVM));
            }

            OnPropertyChanged(nameof(CurrentStateStatus));
            
                if (changedFields.Any())
                {
                // 1. Add ALL changed fields (from all tabs) to the pending list.
                _fieldsPendingHighlight.AddRange(changedFields);

                // 2. Immediately trigger highlighting ONLY for pending fields
                //    that are on the currently visible tab.
                if (SelectedTab != null)
                {
                    TriggerPendingHighlightsForTab(SelectedTab);
                }
            }
        }
        
        /// <summary>
        /// A new method to handle field value changes from presets.
        /// Returns true if the value was changed.
        /// </summary>
        private bool ApplyFieldValue(InputFieldViewModel fieldVM, JToken presetValue)
        {
            bool valueChanged = false;

            if (fieldVM is MarkdownFieldViewModel markdownVm)
            {
                if (markdownVm.Value != presetValue.ToString())
                {
                    markdownVm.Value = presetValue.ToString();
                    valueChanged = true;
                }
            }
            else if (fieldVM is NodeBypassFieldViewModel bypassVm)
            {
                var presetBool = presetValue.ToObject<bool>();
                if (bypassVm.IsEnabled != presetBool)
                {
                    bypassVm.IsEnabled = presetBool;
                    valueChanged = true;
                }
            }
            // --- START OF CHANGE: Explicit handling for ComboBox to be safe ---
            else if (fieldVM is ComboBoxFieldViewModel comboVm)
            {
                 var prop = _workflow.GetPropertyByPath(fieldVM.Path);
                 // Compare raw tokens (String vs String)
                 if (prop != null && !Utils.AreJTokensEquivalent(prop.Value, presetValue))
                 {
                     prop.Value = presetValue.DeepClone(); // Update the API value (String)
                     
                     // Force the UI to refresh.
                     // The 'Value' getter in ComboBoxFieldViewModel will trigger, 
                     // read the new prop.Value, find the matching Wrapper, and update the View.
                     comboVm.RefreshValue(); 
                     
                     valueChanged = true;
                 }
            }
            // --- END OF CHANGE ---
            else
            {
                var prop = _workflow.GetPropertyByPath(fieldVM.Path);
                if (prop != null && !Utils.AreJTokensEquivalent(prop.Value, presetValue))
                {
                    prop.Value = presetValue.DeepClone();
                    (fieldVM as dynamic)?.RefreshValue();
                    valueChanged = true;
                }
            }
            return valueChanged;
        }
        
        /// <summary>
        /// Detaches a layer, effectively marking its fields as manually set.
        /// </summary>
        private void DetachLayer(ActiveLayerViewModel layerVM)
        {
            if (layerVM != null)
            {
                ActiveLayers.Remove(layerVM);
            }
        }

        /// <summary>
        /// Re-applies the original values from a layer, reverting any manual changes.
        /// </summary>
        private void ReapplyLayer(ActiveLayerViewModel layerVM)
            {
            if (layerVM == null) return;
            
            ApplyPreset(layerVM.SourcePreset);
            layerVM.State = ActiveLayerViewModel.LayerState.Normal;
            OnPropertyChanged(nameof(CurrentStateStatus));
            }
        
        /// <summary>
        /// Creates a new preset (Snippet) that includes all savable fields in the group with their current values.
        /// Overwrites any existing preset with the same name.
        /// </summary>
        public void CreateOrUpdateSnapshot(string name, string description, string category, List<string> tags)
        {
            // 1. Gather all valid fields
            var allSavableFields = Tabs.SelectMany(t => t.Fields)
                .Where(f => f.Type != FieldType.ScriptButton &&
                            f.Type != FieldType.Label &&
                            f.Type != FieldType.Separator &&
                            f.Type != FieldType.Spoiler &&
                            f.Type != FieldType.SpoilerEnd)
                .Select(f => new PresetFieldSelectionViewModel { Path = f.Path }) // Create minimal view model for reusability
                .ToList();

            if (!allSavableFields.Any()) return;

            // 2. Use existing save logic, but inject parameters
            // Note: SaveCurrentStateAsPreset reads from NewPresetDescription/Category/Tags properties of the ViewModel.
            // We need to temporarily set them or refactor SaveCurrentStateAsPreset to accept them.
            // Refactoring SaveCurrentStateAsPreset to be pure is cleaner.
            
            // Let's do it manually here to avoid side effects on the UI state.
            
            var newPreset = new GroupPreset
            {
                Name = name,
                IsLayout = false, // Snapshots are always snippets
                LastModified = DateTime.UtcNow,
                Description = description,
                Category = category,
                Tags = tags != null ? new List<string>(tags) : new List<string>(),
                Values = new Dictionary<string, JToken>()
            };

            foreach (var field in Tabs.SelectMany(t => t.Fields))
            {
                // Should match logic in SaveCurrentStateAsPreset
                if (field.Type == FieldType.ScriptButton || field.Type == FieldType.Label || field.Type == FieldType.Separator ||
                    field.Type == FieldType.Spoiler ||
                    field.Type == FieldType.SpoilerEnd) continue;

                if (field is MarkdownFieldViewModel markdownVm)
                {
                    newPreset.Values[field.Path] = new JValue(markdownVm.Value);
                }
                else if (field is NodeBypassFieldViewModel bypassVm)
                {
                    newPreset.Values[field.Path] = new JValue(bypassVm.IsEnabled);
                }
                else if (field.Property != null)
                {
                    var prop = _workflow.GetPropertyByPath(field.Path);
                    if (prop != null)
                    {
                        newPreset.Values[field.Path] = prop.Value.DeepClone();
                    }
                }
            }

            // 3. Save to workflow
            if (!_workflow.Presets.ContainsKey(Id))
            {
                _workflow.Presets[Id] = new List<GroupPreset>();
            }

            // Overwrite
            _workflow.Presets[Id].RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            _workflow.Presets[Id].Add(newPreset);

            // 4. Refresh UI
            LoadPresets();
            PresetsModified?.Invoke();
            
            // 5. Update active layers to reflect this new snapshot
            // Since a snapshot covers everything, it will likely become the only active layer.
            // We need to "apply" it logically (without changing values, since they match) so it shows up as active.
            var newPresetVm = AllPresets.FirstOrDefault(p => p.Name == name);
            if (newPresetVm != null)
            {
                // This adds it to ActiveLayers list
                ApplyPreset(newPresetVm); 
            }
        }

        
        private void SaveCurrentStateAsPreset(string name, IEnumerable<PresetFieldSelectionViewModel> selectedFields, bool isLayout)
        {
            var existingPreset = _workflow.Presets.GetValueOrDefault(Id)?.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            
            var newPreset = existingPreset?.Clone() ?? new GroupPreset();
            newPreset.Name = name;
            newPreset.IsLayout = isLayout;
            newPreset.LastModified = DateTime.UtcNow;
            
            newPreset.Description = NewPresetDescription;
            newPreset.Category = NewPresetCategory;
            newPreset.Tags = NewPresetTags.ToList();

            if (isLayout)
            {
                newPreset.SnippetNames = ActiveLayers.Select(l => l.Name).ToList();
                newPreset.Values.Clear();
            }
            else
            {
                newPreset.Values.Clear();
                var selectedFieldPaths = new HashSet<string>(selectedFields.Select(f => f.Path));
                foreach (var field in Tabs.SelectMany(t => t.Fields))
                {
                    if (!selectedFieldPaths.Contains(field.Path)) continue;
                
                    if (field is MarkdownFieldViewModel markdownVm)
                    {
                        newPreset.Values[field.Path] = new JValue(markdownVm.Value);
                    }
                    else if (field is NodeBypassFieldViewModel bypassVm)
                    {
                        newPreset.Values[field.Path] = new JValue(bypassVm.IsEnabled);
                    }
                    else if (field.Property != null)
                    {
                        var prop = _workflow.GetPropertyByPath(field.Path);
                        if (prop != null)
                        {
                            newPreset.Values[field.Path] = prop.Value.DeepClone();
                        }
                    }
                }
            }

            if (!_workflow.Presets.ContainsKey(Id))
            {
                _workflow.Presets[Id] = new List<GroupPreset>();
            }
            
            _workflow.Presets[Id].RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            _workflow.Presets[Id].Add(newPreset);
            
            LoadPresets();
            PresetsModified?.Invoke();
        }

        private void DeletePreset(string presetName)
        {
            if (_workflow.Presets.TryGetValue(Id, out var presets))
            {
                presets.RemoveAll(p => p.Name == presetName);
            
                // Also remove this preset if it's part of any Layouts
                foreach (var layout in presets.Where(p => p.IsLayout && p.SnippetNames != null))
                {
                    layout.SnippetNames.RemoveAll(sn => sn == presetName);
                }

                LoadPresets(); // This reloads AllPresets and rebuilds ActiveLayers
            PresetsModified?.Invoke();
            }
        }

        /// <summary>
        /// Analyzes the current state of fields and reconstructs the ActiveLayers collection.
        /// This is crucial for session loading and backward compatibility.
        /// It's public so the WorkflowInputsController can call it after session data is loaded.
        /// </summary>
        public void RebuildActiveLayersFromState()
            {
            ActiveLayers.Clear();
            if (!AllPresets.Any())
            {
                OnPropertyChanged(nameof(CurrentStateStatus));
                return;
            }

            var currentState = Tabs.SelectMany(t => t.Fields)
                .Where(f => f.Property != null || f is MarkdownFieldViewModel || f is NodeBypassFieldViewModel)
                .ToDictionary(f => f.Path, f => GetFieldValueAsJToken(f));

            // --- START OF REWORKED LOGIC ---
            // 1. Find all snippets where every single one of its fields perfectly matches the current UI state.
            var perfectlyMatchedSnippets = Snippets
                .Where(s => s.Model.Values.Any() && s.Model.Values.All(p => 
                    currentState.TryGetValue(p.Key, out var val) && Utils.AreJTokensEquivalent(val, p.Value)))
                .ToList();

            // 2. Filter out snippets that are subsets of other perfectly matched snippets.
            //    This ensures only the most "complete" or "maximal" presets are shown as active.
            var finalActiveSnippets = new List<GroupPresetViewModel>();
            foreach (var candidate in perfectlyMatchedSnippets)
            {
                bool isSubsetOfAnother = perfectlyMatchedSnippets.Any(other => 
                    candidate != other && 
                    candidate.Model.Values.Keys.All(key => other.Model.Values.ContainsKey(key)));

                if (!isSubsetOfAnother)
                {
                    finalActiveSnippets.Add(candidate);
                        }
            }

            // 3. Add the final list to the active layers.
            //    On session load, layers are always considered "Normal", not "Modified".
            foreach (var snippet in finalActiveSnippets)
            {
                ActiveLayers.Add(new ActiveLayerViewModel(snippet));
            }
            // --- END OF REWORKED LOGIC ---
            
            OnPropertyChanged(nameof(CurrentStateStatus));
        }

        /// <summary>
        /// Checks if the current state of the UI matches a specific snippet.
        /// </summary>
        private bool IsStateMatchingSnippet(GroupPresetViewModel snippetVM, Dictionary<string, JToken> currentState)
        {
            if (snippetVM.IsLayout || !snippetVM.Model.Values.Any())
            {
                return false;
            }
            
            foreach (var presetPair in snippetVM.Model.Values)
            {
                if (!currentState.TryGetValue(presetPair.Key, out var currentValue) || !Utils.AreJTokensEquivalent(currentValue, presetPair.Value))
                {
                    return false; // A field doesn't match
        }
            }
            return true; // All fields in the snippet match
        }

        private JToken GetFieldValueAsJToken(InputFieldViewModel fieldVM)
        {
            if (fieldVM is MarkdownFieldViewModel markdownVM)
            {
                return new JValue(markdownVM.Value ?? "");
            }
            if (fieldVM is NodeBypassFieldViewModel bypassVM)
            {
                return new JValue(bypassVM.IsEnabled);
            }
            return _workflow.GetPropertyByPath(fieldVM.Path)?.Value;
        }

        /// <summary>
        /// Called when a field's value is changed by the user.
        /// Updates the state of any Active Layer that controls this field and checks for new matching snippets.
        /// </summary>
        public void NotifyFieldValueChanged(InputFieldViewModel fieldVM)
        {
            bool stateChanged = false;
            
            var currentState = Tabs.SelectMany(t => t.Fields)
                .Where(f => (f.Property != null || f is MarkdownFieldViewModel || f is NodeBypassFieldViewModel) 
                            && !string.IsNullOrEmpty(f.Path)) // Filter out fields without a path
                .ToDictionary(f => f.Path, f => GetFieldValueAsJToken(f));

            // Check for newly matching snippets
            var activeLayerNames = ActiveLayers.Select(l => l.Name).ToHashSet();
            var nonActiveSnippets = Snippets.Where(s => !activeLayerNames.Contains(s.Name));

            foreach (var snippetToCheck in nonActiveSnippets)
            {
                if (IsStateMatchingSnippet(snippetToCheck, currentState))
                {
                    ApplySnippet(snippetToCheck);
                    stateChanged = true;
                }
            }
            
            // --- START OF REWORKED LOGIC ---
            // Re-evaluate the state of all currently active layers.
            foreach (var layer in ActiveLayers)
            {
                var isNowMatching = IsStateMatchingSnippet(layer.SourcePreset, currentState);
                var newState = isNowMatching ? ActiveLayerViewModel.LayerState.Normal : ActiveLayerViewModel.LayerState.Modified;

                if (layer.State != newState)
                {
                    layer.State = newState;
                    stateChanged = true;
                }

                // Update modification status for individual fields in the tooltip.
                if (layer.State == ActiveLayerViewModel.LayerState.Modified)
                {
                    var originalValues = layer.SourcePreset.Model.Values;
                    foreach (var fieldInfo in layer.FieldDetails)
                    {
                        // Check if the current state has a value for this field and if the preset also defined a value for it.
                        if (currentState.TryGetValue(fieldInfo.FieldPath, out var currentValue) && 
                            originalValues.TryGetValue(fieldInfo.FieldPath, out var originalValue))
                        {
                            // If they are not equivalent, mark as modified.
                            fieldInfo.IsModified = !Utils.AreJTokensEquivalent(currentValue, originalValue);
                        }
                        else
                        {
                            // If either is missing, it's not a direct modification of a preset value.
                            fieldInfo.IsModified = false;
                        }
                    }
                }
                else // State is Normal, so no fields are modified relative to this layer.
                {
                    foreach (var fieldInfo in layer.FieldDetails)
                    {
                        fieldInfo.IsModified = false;
                    }
                }
            }
            // --- END OF REWORKED LOGIC ---

            if (stateChanged)
            {
                OnPropertyChanged(nameof(CurrentStateStatus));
            }
        }
        
        private void TriggerPendingHighlightsForTab(WorkflowGroupTabViewModel tabVm)
        {
            if (tabVm == null || !_fieldsPendingHighlight.Any()) return;

            // Find fields that are pending highlight AND are on the newly activated tab
            var fieldsOnThisTab = _fieldsPendingHighlight.Intersect(tabVm.Fields).ToList();

            if (fieldsOnThisTab.Any())
            {
                TriggerHighlightEffect(fieldsOnThisTab);
                
                // Remove the now-highlighted fields from the pending list
                _fieldsPendingHighlight.RemoveAll(f => fieldsOnThisTab.Contains(f));
            }
        }
        
        /// <summary>
        /// Asynchronously applies a temporary highlight effect to a list of fields on the UI thread.
        /// </summary>
        /// <param name="fields">The list of InputFieldViewModel to highlight.</param>
        private async void TriggerHighlightEffect(List<InputFieldViewModel> fields)
        {
            // english: Set IsHighlighted to true on the UI thread to trigger the animation.
            foreach (var field in fields)
            {
                field.IsHighlighted = true;
            }

            // english: Wait for the animation to complete. This does not block the UI thread.
            await Task.Delay(1500);

            // english: Set IsHighlighted back to false to reset the trigger state.
            foreach (var field in fields)
            {
                field.IsHighlighted = false;
            }
        }

        
        /// <summary>
        /// Compares the current state of the group's fields against a preset to check for a match.
        /// A preset is considered "matching" if all the values defined *within the preset*
        /// are identical to the current values of the corresponding fields in the UI.
        /// UI fields that are not part of the preset are ignored.
        /// </summary>
        /// <param name="presetVM">The preset view model to compare against.</param>
        /// <returns>True if the current state matches the preset's defined values, otherwise false.</returns>
        public bool IsStateMatchingPreset(GroupPresetViewModel presetVM)
        {
            if (presetVM == null)
            {
                return false;
            }
            
            var presetValues = presetVM.Model.Values;

            // If a preset has no values, it can't be matched against.
            if (!presetValues.Any())
            {
                return false;
            }

            // Iterate through only the values defined in the preset.
            foreach (var valuePair in presetValues)
            {
                var fieldPath = valuePair.Key;
                var presetValue = valuePair.Value;

                var fieldVm = Tabs.SelectMany(t => t.Fields).FirstOrDefault(f => f.Path == fieldPath);

                // If the field from the preset doesn't exist in the UI anymore, it's a mismatch.
                if (fieldVm == null)
                {
                    return false;
                }

                // Get the current value from the UI field.
                JToken currentValue;
                if (fieldVm is MarkdownFieldViewModel markdownVm)
                {
                    currentValue = new JValue(markdownVm.Value);
                }
                else if (fieldVm.Property != null)
                {
                    currentValue = _workflow.GetPropertyByPath(fieldPath)?.Value;
                }
                else
                {
                    continue; // Should not happen for persistable fields.
                }

                // If the current value doesn't match the preset's value, it's not a match.
                if (currentValue == null || !Utils.AreJTokensEquivalent(currentValue, presetValue))
                {
                    return false; 
                }
            }

            // If all values in the preset have been checked and matched, then the state matches.
            return true;
        }
        // --- END OF CHANGES ---
    }

    // --- Базовый класс для всех полей ввода ---
    public abstract class InputFieldViewModel : INotifyPropertyChanged
    {
        public string Name { get; }
        public string Path { get; } // START OF CHANGES: Add Path property
        public string NodeTitle { get; }
        public string NodeType { get; }
        public FieldType Type { get; protected set; }
        public JProperty? Property { get; } 
        
        public WorkflowField FieldModel { get; }
        
        // This is a transient UI property and is not saved.
        // It is used to trigger a highlight animation when a preset changes the field's value.
        [JsonIgnore]
        public bool IsHighlighted { get; set; }
        
        /// <summary>
        /// The highlight color for the field in HEX format.
        /// </summary>
        public string HighlightColor { get; set; }

        protected InputFieldViewModel(WorkflowField field, JProperty? property, string nodeTitle = null, string nodeType = null)
        {
            Name = field.Name;
            Path = field.Path; // END OF CHANGES: Initialize Path property
            NodeTitle = nodeTitle;
            NodeType = nodeType;
            Property = property;
            HighlightColor = field.HighlightColor;
            FieldModel = field;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        
        /// <summary>
        /// Forces the ViewModel to re-read its value from the underlying JProperty and notify the UI.
        /// </summary>
        public virtual void RefreshValue()
        {
            // Default implementation does nothing, needs to be overridden in derived classes.
            // For example, in TextFieldViewModel:
            // OnPropertyChanged(nameof(Value));
        }
    }

    // --- Конкретные реализации ---

    public class TextFieldViewModel : InputFieldViewModel
    {
        public string Value
        {
            get
            {
                var propValue = Property.Value;
                string text;

                switch (propValue.Type)
                {
                    case JTokenType.Float:
                        text = propValue.ToObject<double>().ToString("G", CultureInfo.InvariantCulture);
                        break;
                    case JTokenType.Integer:
                        text = propValue.ToObject<long>().ToString(CultureInfo.InvariantCulture);
                        break;
                    default:
                        text = propValue.ToString();
                        break;
                }

                if (text.Length > 1000 && (text.StartsWith("iVBOR") || text.StartsWith("/9j/") || text.StartsWith("UklG")))
                {
                    return string.Format(LocalizationService.Instance["TextField_Base64Placeholder"], text.Length / 1024);
                }
                return text;
            }
            set
            {
                if (value != null && (value.StartsWith("[Base64 Image Data:") || value.StartsWith("[Данные изображения Base64:")))
                    return;

                if (value == null)
                {
                    Property.Value = "";
                    return;
                }

                // Attempt to parse the string as a double.
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double numericValue))
                {
                    // Check for non-canonical forms that indicate the user is still typing or wants to preserve formatting.
                    // Case 1: Ends with a decimal separator (e.g., "123.").
                    bool endsWithSeparator = value.EndsWith(CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator);
                    // Case 2: Contains a separator and ends with zero (e.g., "1.0", "1.20").
                    bool hasTrailingZeros = value.Contains(CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator) && value.EndsWith("0");

                    if (endsWithSeparator || hasTrailingZeros)
                    {
                        // Preserve the exact string value to allow the user to continue typing.
                        Property.Value = new JValue(value);
                    }
                    else
                    {
                        // The input is in a canonical form (e.g., "123", "1.25"). Store it as the appropriate numeric type.
                        if (numericValue == Math.Truncate(numericValue))
                        {
                            Property.Value = new JValue(Convert.ToInt64(numericValue));
                        }
                        else
                        {
                            Property.Value = new JValue(numericValue);
                        }
                    }
                }
                else
                {
                    // If it's not a valid number at all, store it as a string.
                    Property.Value = new JValue(value);
                }
            }
        }
        
        /// <summary>
        /// Public method to update the value from image bytes (for Drag&Drop and Paste).
        /// </summary>
        public void UpdateWithImageData(byte[] imageBytes)
        {
            if (imageBytes == null) return;
            
            var settings = SettingsService.Instance.Settings;
            if (settings.CompressAnyFieldImagesToJpg)
            {
                try
                {
                    using var image = Image.Load(imageBytes);
                    using var ms = new MemoryStream();
                    var encoder = new JpegEncoder { Quality = settings.AnyFieldJpgCompressionQuality };
                    image.SaveAsJpeg(ms, encoder);
                    imageBytes = ms.ToArray();
                }
                catch (Exception ex)
                {
                    // If conversion fails, log it and proceed with the original bytes
                    Logger.Log(ex, "Failed to compress image to JPG for 'Any' field. Using original format.");
                }
            }
            
            var base64String = Convert.ToBase64String(imageBytes);
            Property.Value = new JValue(base64String);
            OnPropertyChanged(nameof(Value));
        }

        public TextFieldViewModel(WorkflowField field, JProperty property, string nodeTitle = null, string nodeType = null) : base(field, property, nodeTitle, nodeType)
        {
            Type = field.Type;
        }
        
        public override void RefreshValue()
        {
            OnPropertyChanged(nameof(Value));
        }
    }

    public class MarkdownFieldViewModel : InputFieldViewModel
    {
        private readonly WorkflowField _field; // Поле для хранения ссылки на модель

        public string Value
        {
            get => _field.DefaultValue; // Читаем и пишем значение в модель
            set
            {
                if (_field.DefaultValue != value)
                {
                    _field.DefaultValue = value;
                    OnPropertyChanged(nameof(Value));
                }
            }
        }
        
        public int? MaxDisplayLines
        {
            get => _field.MaxDisplayLines;
            set
            {
                if (_field.MaxDisplayLines != value)
                {
                    _field.MaxDisplayLines = value;
                    OnPropertyChanged(nameof(MaxDisplayLines));
                }
            }
        }

        // Новый конструктор, не требующий JProperty
        public MarkdownFieldViewModel(WorkflowField field) : base(field, null)
        {
            _field = field;
            Type = FieldType.Markdown;
        }
    }

    public class SeedFieldViewModel : InputFieldViewModel
    {
        private readonly WorkflowField _field;
        public ICommand RandomizeCommand { get; }
        public string Value
        {
            get
            {
                // Safety check: Try to parse the value as a long.
                var propValue = Property.Value;
                if (long.TryParse(propValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var longValue))
                {
                    return longValue.ToString(CultureInfo.InvariantCulture);
                }
                
                // If parsing fails, log a warning and return a default value.
                Logger.Log($"[UI Validation] Seed field '{Name}' has a non-integer value: '{propValue}'. Defaulting to 0.", LogLevel.Warning);
                return "0";
            }
            set
            {
                if (long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var longValue))
                {
                    // Safely check if the value has actually changed before updating.
                    bool hasChanged = true;
                    if (long.TryParse(Property.Value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var currentLongValue))
                    {
                        if (currentLongValue == longValue)
                        {
                            hasChanged = false;
                        }
                    }

                    if (hasChanged)
                    {
                        Property.Value = new JValue(longValue);
                        OnPropertyChanged(nameof(Value));
                    }
                }
            }
        }
        public bool IsLocked
        {
            get => _field.IsSeedLocked;
            set
            {
                if (_field.IsSeedLocked != value)
                {
                    _field.IsSeedLocked = value;
                    OnPropertyChanged(nameof(IsLocked));
                }
            }
        }
        
        public long MinValue { get; }
        public long MaxValue { get; }
        
        public SeedFieldViewModel(WorkflowField field, JProperty property, string nodeTitle = null, string nodeType = null) : base(field, property, nodeTitle, nodeType)
        {
            Type = FieldType.Seed;
            _field = field;
            long max = field.MaxSeedValue ?? long.MaxValue;
            MinValue = field.AllowNegativeSeed ? -max : 0;
            MaxValue = max;
            RandomizeCommand = new RelayCommand(_ => Randomize());
        }
        
        private void Randomize()
        {
            var newValue = Utils.GenerateSeed(MinValue, MaxValue);
            Value = newValue.ToString();
        }
        
        public override void RefreshValue()
        {
            OnPropertyChanged(nameof(Value));
        }
    }

    public class SliderFieldViewModel : InputFieldViewModel
    {
        public double Value
        {
            get
            {
                try
                {
                    // This is the "ideal" getter, now wrapped in a safety block.
                    // It correctly handles converting both integers and floats from JSON to a double for the UI slider.
                    return Property.Value.ToObject<double>();
                }
                catch (Exception ex) when (ex is FormatException || ex is ArgumentException)
                {
                    // If the underlying value is not a number (e.g., "abc"), we catch the error.
                    Logger.Log($"[UI Validation] Slider field '{Name}' has a non-numeric value: '{Property.Value}'. Defaulting to 0.0 to prevent a crash.", LogLevel.Warning);
                    // Return a safe default value.
                    return 0.0;
                }
            }
            set
            {
                // --- START OF CHANGE: Remove snapping to step logic ---
                // We allow any value entered by the user. The Slider UI control 
                // will handle snapping visually, but the text input remains precise.
                
                JToken newValueToken;
                if (Type == FieldType.SliderInt)
                {
                    // For integer sliders, we must ensure it's a whole number.
                    var intValue = Convert.ToInt64(Math.Round(value));
                    newValueToken = new JValue(intValue);
                }
                else // SliderFloat
                {
                    // For float sliders, use the exact value provided (e.g. from manual text input).
                    // We bypass the Precision setting here to allow custom fine-tuning.
                    newValueToken = new JValue(value);
                }
        
                if (!JToken.DeepEquals(Property.Value, newValueToken))
                {
                    Property.Value = newValueToken;
            
                    OnPropertyChanged(nameof(Value));
                    OnPropertyChanged(nameof(FormattedValue));
                }
                // --- END OF CHANGE ---
            }
        }

        /// <summary>
        /// A safe property that returns a formatted string for display.
        /// </summary>
        public string FormattedValue
        {
            get
            {
                try
                {
                    return Property.Value.ToObject<double>().ToString(StringFormat);
                }
                catch (Exception ex) when (ex is FormatException || ex is ArgumentException)
                {
                    // If the underlying value is not a number (e.g., "abc"), we catch the error.
                    Logger.Log($"[UI Validation] Slider field '{Name}' has a non-numeric value: '{Property.Value}'. Defaulting to 0.0 to prevent a crash.", LogLevel.Warning);
                    // Return a safe default value.
                    return 0.0.ToString(StringFormat);
                }
            }
        }
        
        public double MinValue { get; }
        public double MaxValue { get; }
        public double StepValue { get; }
        public string StringFormat { get; }
        private readonly WorkflowField _field;

        public SliderFieldViewModel(WorkflowField field, JProperty property, string nodeTitle = null, string nodeType = null) : base(field, property, nodeTitle, nodeType)
        {
            Type = field.Type;
            _field = field;
            MinValue = field.MinValue ?? 0;
            MaxValue = field.MaxValue ?? 100;
            StepValue = field.StepValue ?? (field.Type == FieldType.SliderInt ? 1 : 0.01);
            StringFormat = field.Type == FieldType.SliderInt ? "F0" : "F" + (field.Precision ?? 2);
        }
        
        public override void RefreshValue()
        {
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(FormattedValue));
        }
    }
    
    /// <summary>
    /// Helper class to wrap ComboBox items allowing separate Display and API values.
    /// </summary>
    public class ComboBoxItemWrapper
    {
        public string Label { get; }
        public string Value { get; }

        public ComboBoxItemWrapper(string label, string value)
        {
            Label = label;
            Value = value;
        }

        // Override ToString so the FilterableComboBox search and display work correctly by default
        public override string ToString() => Label;

        public override bool Equals(object obj)
        {
            if (obj is ComboBoxItemWrapper other) return Value == other.Value;
            if (obj is string str) return Value == str; // Allow comparison with raw string
            return false;
        }

        public override int GetHashCode() => Value.GetHashCode();
    }
    
    public class ComboBoxFieldViewModel : InputFieldViewModel
    {
        private readonly WorkflowField _field;
        
        // We store the actual API value in the Property (JToken),
        // but the UI binds to this property which returns the corresponding Wrapper object.
        public object Value
        {
            get
            {
                var rawValue = Property.Value.ToString();
                
                // Try to find the matching wrapper in our list
                var matchingItem = ItemsSource.OfType<ComboBoxItemWrapper>()
                    .FirstOrDefault(item => item.Value == rawValue);

                // If found, return the wrapper (so the UI shows the Label).
                // If not found (e.g. custom value typed by user), return the raw string.
                return matchingItem ?? (object)rawValue;
            }
            set
            {
                string newValueString;

                if (value is ComboBoxItemWrapper wrapper)
                {
                    newValueString = wrapper.Value;
                }
                else
                {
                    newValueString = value?.ToString() ?? "";
                }

                JToken newToken;
                if (long.TryParse(newValueString, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longVal))
                {
                    newToken = new JValue(longVal);
                }
                else if (double.TryParse(newValueString, NumberStyles.Any, CultureInfo.InvariantCulture, out double doubleVal))
                {
                    newToken = new JValue(doubleVal);
                }
                else if (bool.TryParse(newValueString, out bool boolVal))
                {
                    newToken = new JValue(boolVal);
                }
                else
                {
                    newToken = new JValue(newValueString);
                }

                if (!Utils.AreJTokensEquivalent(Property.Value, newToken))
                {
                    Property.Value = newToken;
                    OnPropertyChanged(nameof(Value));
                }
            }
        }

        // Changed from List<string> to List<object> to support both Wrappers and Strings
        public List<object> ItemsSource { get; set; }

        public ComboBoxFieldViewModel(WorkflowField field, JProperty property, string nodeTitle = null, string nodeType = null) : base(field, property, nodeTitle, nodeType)
        {
            Type = field.Type;
            _field = field;
            ItemsSource = new List<object>();
        }

        public async Task LoadItemsAsync(ModelService modelService, AppSettings settings)
        {
            try
            {
                var rawItems = new List<string>();

                if (Type == FieldType.Model)
                {
                    var types = await modelService.GetModelTypesAsync();
                    var modelTypeInfo = types.FirstOrDefault(t => t.Name == _field.ModelType);
                    if (modelTypeInfo != null)
                    {
                        var models = await modelService.GetModelFilesAsync(modelTypeInfo);
                        rawItems.AddRange(settings.SpecialModelValues);
                        rawItems.AddRange(models);
                    }
                }
                else
                {
                    rawItems = _field.Type switch
                    {
                        FieldType.Sampler => settings.Samplers,
                        FieldType.Scheduler => settings.Schedulers,
                        FieldType.ComboBox => _field.ComboBoxItems,
                        _ => new List<string>()
                    };
                }

                // --- PARSING LOGIC ---
                var processedItems = new List<object>();
                foreach (var item in rawItems.Distinct())
                {
                    // Check for separator " :: "
                    // Example: "High Quality :: high_res_model"
                    // Label: High Quality, Value: high_res_model
                    if (item.Contains(" :: "))
                    {
                        var parts = item.Split(new[] { " :: " }, StringSplitOptions.None);
                        if (parts.Length >= 2)
                        {
                            processedItems.Add(new ComboBoxItemWrapper(parts[0].Trim(), parts[1].Trim()));
                        }
                        else
                        {
                            processedItems.Add(new ComboBoxItemWrapper(item, item));
                        }
                    }
                    else
                    {
                        // Old style: Label is Value
                        processedItems.Add(new ComboBoxItemWrapper(item, item));
                    }
                }

                ItemsSource = processedItems;
                OnPropertyChanged(nameof(ItemsSource));
                
                // Refresh Value to ensure the correct initial Label is shown if it matches a loaded item
                OnPropertyChanged(nameof(Value));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load items for combobox '{Name}': {ex.Message}");
            }
        }
        
        /// <summary>
        /// Helper method to get the Display Label corresponding to a raw API value.
        /// Used by Presets to show friendly names in tooltips.
        /// </summary>
        public string GetDisplayLabelForValue(JToken token)
        {
            if (token == null) return "null";
            var rawVal = token.ToString();

            // Search in the items list for a wrapper with a matching value
            var wrapper = ItemsSource.OfType<ComboBoxItemWrapper>()
                .FirstOrDefault(w => w.Value == rawVal);

            // Return the label if found, otherwise the raw value
            return wrapper != null ? wrapper.Label : rawVal;
        }

        public override void RefreshValue()
        {
            // When the underlying JProperty changes externally (e.g. by a preset),
            // we must notify the UI to re-read the 'Value' property.
            // The 'Value' getter will then look up the correct Wrapper based on the new JProperty value.
            OnPropertyChanged(nameof(Value));
        }
    }
    
    public class CheckBoxFieldViewModel : InputFieldViewModel
    {
        public bool IsChecked
        {
            get
            {
                var propValue = Property.Value;
                try
                {
                    // JToken's explicit conversion is quite robust for booleans
                    return propValue.ToObject<bool>();
                }
                catch (Exception)
                {
                    // If conversion fails (e.g., value is "abc"), log it and default to false.
                    Logger.Log($"[UI Validation] CheckBox field '{Name}' has a non-boolean value: '{propValue}'. Defaulting to false.", LogLevel.Warning);
                    return false;
                }
            }
            set
            {
                // Safely check if the value has changed.
                bool hasChanged = true;
                try
                {
                    if (Property.Value.ToObject<bool>() == value)
                    {
                        hasChanged = false;
                    }
                }
                catch
                {
                    // If the current value isn't a valid boolean, it has definitely changed.
                    hasChanged = true;
                }

                if (hasChanged)
                {
                    Property.Value = new JValue(value);
                    OnPropertyChanged(nameof(IsChecked));
                }
            }
        }
        public CheckBoxFieldViewModel(WorkflowField field, JProperty property, string nodeTitle = null, string nodeType = null) : base(field, property, nodeTitle, nodeType)
        {
            Type = FieldType.Any; 
        }
        
        public override void RefreshValue()
        {
            OnPropertyChanged(nameof(IsChecked));
        }
    }
    
    public class ScriptButtonFieldViewModel : InputFieldViewModel
    {
        public ICommand ExecuteScriptCommand { get; }
        public string ActionName { get; }

        // Новый конструктор, не требующий JProperty
        public ScriptButtonFieldViewModel(WorkflowField field, ICommand command) : base(field, null)
        {
            Type = FieldType.ScriptButton;
            ActionName = field.ActionName;
            ExecuteScriptCommand = command;
        }
    }
    
    public class NodeBypassFieldViewModel : InputFieldViewModel
    {
        /// <summary>
        /// Состояние чекбокса в UI. True = ноды работают, False = ноды обходятся.
        /// </summary>
        public bool IsEnabled
        {
            // --- START OF FIX: Use robust boolean parsing instead of string comparison ---
            // This correctly handles "True", "true", "False", "false", and returns false for any other value.
            // It ensures the getter always reports the correct state of the underlying model value.
            get => bool.TryParse(_field.DefaultValue, out var result) && result;
            // --- END OF FIX ---
            set
            {
                // The setter logic was already correct.
                if ((_field.DefaultValue?.ToLower() == "true") != value)
                {
                    _field.DefaultValue = value.ToString();
                    OnPropertyChanged(nameof(IsEnabled));
                }
            }
        }

        /// <summary>
        /// Список ID нод, которыми управляет это поле.
        /// </summary>
        public ObservableCollection<string> BypassNodeIds { get; }

        private readonly WorkflowField _field;

        public NodeBypassFieldViewModel(WorkflowField field, JProperty property) : base(field, property)
        {
            _field = field;
            Type = FieldType.NodeBypass;
            BypassNodeIds = field.BypassNodeIds ?? new ObservableCollection<string>();

            // Устанавливаем значение по умолчанию, если оно не задано
            if (string.IsNullOrEmpty(_field.DefaultValue))
            {
                _field.DefaultValue = "true";
            }
        }
    
        public override void RefreshValue()
        {
            OnPropertyChanged(nameof(IsEnabled));
        }
    }
    
    public class LabelFieldViewModel : InputFieldViewModel
    {
        // The label text is just the Name property from the base class.
        public LabelFieldViewModel(WorkflowField field) : base(field, null)
        {
            Type = FieldType.Label;
        }
    }
    
    public class SeparatorFieldViewModel : InputFieldViewModel
    {
        public SeparatorFieldViewModel(WorkflowField field) : base(field, null)
        {
            Type = FieldType.Separator;
        }
    }
    public class SpoilerFieldViewModel : InputFieldViewModel
    {
        // This event will be fired whenever the spoiler is expanded or collapsed.
        public event Action SpoilerStateChanged;
    
        public bool IsExpanded
        {
            get => FieldModel.IsSpoilerExpanded;
            set
            {
                if (FieldModel.IsSpoilerExpanded != value)
                {
                    FieldModel.IsSpoilerExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                    // Fire the event to notify listeners.
                    SpoilerStateChanged?.Invoke();
                }
            }
        }
    
        public SpoilerFieldViewModel(WorkflowField field) : base(field, null)
        {
            Type = FieldType.Spoiler;
        
            field.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(WorkflowField.IsSpoilerExpanded))
                {
                    OnPropertyChanged(nameof(IsExpanded));
                }
            };
        }
    }
    
    public class SpoilerEndViewModel : InputFieldViewModel
    {
        public SpoilerEndViewModel(WorkflowField field) : base(field, null)
        {
            Type = FieldType.SpoilerEnd;
        }
    }
}