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
    /// A ViewModel for the combined global controls section, managing both wildcard seed and global presets.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class GlobalControlsViewModel : INotifyPropertyChanged
    {
        public string Header { get; set; } = LocalizationService.Instance["GlobalSettings_Header"];
        public bool IsExpanded { get; set; } = true;
        
        public bool IsHooksSectionVisible => ImplementedHooks.Any();
        public ObservableCollection<HookToggleViewModel> ImplementedHooks { get; } = new();
        
        // english: Combined visibility logic
        public bool IsSeedSectionVisible { get; set; } = false;
        public bool IsPresetsSectionVisible => GlobalPresetNames.Any();
        public bool IsVisible => IsSeedSectionVisible || IsPresetsSectionVisible || IsHooksSectionVisible;

        // english: From former GlobalSettingsViewModel
        public long WildcardSeed { get; set; } = Utils.GenerateSeed(0, 4294967295L);
        public bool IsSeedLocked { get; set; } = false;
        
        // english: From former GlobalPresetsViewModel
        public ObservableCollection<string> GlobalPresetNames { get; } = new();
        private readonly Action<string> _applyPresetAction;
        private string _selectedGlobalPreset;
        private bool _isInternalSet = false;

        public string SelectedGlobalPreset
        {
            get => _selectedGlobalPreset;
            set
            {
                if (_selectedGlobalPreset == value) return;
                
                _selectedGlobalPreset = value;
                OnPropertyChanged(nameof(SelectedGlobalPreset));

                if (!_isInternalSet && value != null)
                {
                    _applyPresetAction?.Invoke(value);
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            // Add cascading notifications for the main visibility property
            if (name == nameof(IsSeedSectionVisible) || name == nameof(IsPresetsSectionVisible) || name == nameof(IsHooksSectionVisible))
            {
                OnPropertyChanged(nameof(IsVisible));
            }
        }
        
        public GlobalControlsViewModel(Action<string> applyPresetAction)
        {
            _applyPresetAction = applyPresetAction;
            GlobalPresetNames.CollectionChanged += (s, e) => OnPropertyChanged(nameof(IsPresetsSectionVisible));
            ImplementedHooks.CollectionChanged += (s, e) => OnPropertyChanged(nameof(IsHooksSectionVisible));
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
        public void SetSelectedPresetSilently(string presetName)
        {
            _isInternalSet = true;
            SelectedGlobalPreset = presetName;
            _isInternalSet = false;
        }
    }

    [AddINotifyPropertyChangedInterface]
    public class GroupPresetViewModel
    {
        public GroupPreset Model { get; }
        public string Name => Model.Name;
        public bool IsLayout => Model.IsLayout;
        
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

        public void GenerateToolTip(WorkflowGroupViewModel parentGroup)
        {
            var details = new List<PresetFieldInfo>();
            var allUiFields = parentGroup.Tabs.SelectMany(t => t.Fields).ToList();
            var showTabNames = parentGroup.Tabs.Count > 1;

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
                        var field = allUiFields.FirstOrDefault(f => f.Path == combinedPair.Key);
                        if (field != null)
                        {
                            var tab = parentGroup.Tabs.FirstOrDefault(t => t.Fields.Contains(field));
                        details.Add(new PresetFieldInfo
                            {
                            FieldName = field.Name,
                            FieldValue = FormatJTokenForDisplay(combinedPair.Value),
                            TabName = tab?.Name,
                            ShowTabName = showTabNames
                        });
                            }
                            }
                        }
            else // Is Snippet
            {
                foreach (var valuePair in Model.Values)
                {
                    var field = allUiFields.FirstOrDefault(f => f.Path == valuePair.Key);
                    if (field != null)
                    {
                            var tab = parentGroup.Tabs.FirstOrDefault(t => t.Fields.Contains(field));
                        details.Add(new PresetFieldInfo
                        {
                            FieldName = field.Name,
                            FieldValue = FormatJTokenForDisplay(valuePair.Value),
                            TabName = tab?.Name,
                            ShowTabName = showTabNames
                        });
                        }
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
    }
    
    [AddINotifyPropertyChangedInterface]
    public class ActiveLayerViewModel
    {
        public enum LayerState { Normal, Modified }
        
        public string Name { get; }
        public GroupPresetViewModel SourcePreset { get; }
        public LayerState State { get; set; } = LayerState.Normal;

        public ActiveLayerViewModel(GroupPresetViewModel source)
        {
            Name = source.Name;
            SourcePreset = source;
            State = LayerState.Normal;
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
        
        private string _lastUsedCategory = "";
        private string _lastUsedTags = "";
        private HashSet<string> _lastSelectedFieldPaths = null;
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
                
                // If we are opening the popup...
                if (value)
                {
                    // ...and we are NOT in update mode (i.e., we are creating a NEW preset)
                    if (string.IsNullOrEmpty(_presetToUpdateName))
                    {
                        // Then prepare a blank form.
                        NewPresetName = "";
                        NewPresetDescription = "";
                        NewPresetCategory = _lastUsedCategory; // Pre-fill category
                        NewPresetTags = _lastUsedTags;         // Pre-fill tags
                        NewPresetType = ActiveLayers.Any() ? SavePresetType.Layout : SavePresetType.Snippet;

                        FieldsForPresetSelection.Clear();
                        foreach (var tabVm in Tabs)
                        {
                            var savableFieldsInTab = tabVm.Model.Fields
                                .Where(f => f.Type != FieldType.ScriptButton &&
                                            f.Type != FieldType.Label &&
                                            f.Type != FieldType.Separator);
                    
                            foreach (var field in savableFieldsInTab)
                            {
                                FieldsForPresetSelection.Add(new PresetFieldSelectionViewModel
                                {
                                    Name = field.Name,
                                    Path = field.Path,
                                    IsSelected = true, 
                                    TabName = tabVm.Name
                                });
                            }
                        }
                        
                        if (_lastSelectedFieldPaths != null)
                        {
                            // If we have a saved selection, apply it
                            foreach (var fieldVM in FieldsForPresetSelection)
                            {
                                fieldVM.IsSelected = _lastSelectedFieldPaths.Contains(fieldVM.Path);
                            }
                        }
                    }
                }
                // If we are closing the popup (for any reason)
                else
                {
                    // Reset the update mode flag.
                    _presetToUpdateName = null;
                    OnPropertyChanged(nameof(IsUpdateMode)); // Notify UI
                }

                _isSavePresetPopupOpen = value;
                OnPropertyChanged(nameof(IsSavePresetPopupOpen));
            }
        }
        public string NewPresetName { get; set; }
        public string NewPresetDescription { get; set; }
        public string NewPresetCategory { get; set; }
        public string NewPresetTags { get; set; }

        
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
            ActiveLayers.CollectionChanged += (s, e) => OnPropertyChanged(nameof(CurrentStateStatus));
            
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
                    NewPresetTags = existingPresetVm.Model.Tags != null ? string.Join(", ", existingPresetVm.Model.Tags) : "";

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

                    // START OF CHANGE: Correctly iterate through tabs and their fields to populate the selection list
                    foreach (var tabVm in Tabs)
                    {
                        var savableFieldsInTab = tabVm.Model.Fields
                            .Where(f => f.Type != FieldType.ScriptButton &&
                                        f.Type != FieldType.Label &&
                                        f.Type != FieldType.Separator)
                            .OrderBy(f => f.Name);

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
                    // END OF CHANGE
                    
                    IsSavePresetPopupOpen = true;
                }
            });
            
            SavePresetCommand = new RelayCommand(_ =>
            {
                if (string.IsNullOrWhiteSpace(NewPresetName)) return;

                // If we were in update mode AND the name has changed, it's a "Save As" operation.
                // We should NOT remove the old preset.
                // If the name is the same, or we weren't in update mode, we remove the old one to perform a replace.
                if (!string.IsNullOrEmpty(_presetToUpdateName) && _presetToUpdateName.Equals(NewPresetName, StringComparison.OrdinalIgnoreCase))
                {
                    if (_workflow.Presets.TryGetValue(Id, out var presets))
                    {
                        presets.RemoveAll(p => p.Name.Equals(_presetToUpdateName, StringComparison.OrdinalIgnoreCase));
                    }
                }
                
                var selectedFields = FieldsForPresetSelection.Where(f => f.IsSelected).ToList();
                SaveCurrentStateAsPreset(NewPresetName, selectedFields, NewPresetType == SavePresetType.Layout);
                
                _lastUsedCategory = NewPresetCategory;
                _lastUsedTags = NewPresetTags;
                _lastSelectedFieldPaths = new HashSet<string>(selectedFields.Select(f => f.Path));

                _presetToUpdateName = null;
                OnPropertyChanged(nameof(IsUpdateMode)); // Notify UI that we are leaving update mode
                IsSavePresetPopupOpen = false;
                NewPresetName = string.Empty;

                LoadPresets(); // This will also rebuild active layers
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
                if (SelectedTab == null || Tabs.Count <= 1) return;

                var message = string.Format("Are you sure you want to delete the tab '{0}' and all its fields?", SelectedTab.Name);
                var caption = "Confirm Deletion";

                if (MessageBox.Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    _model.Tabs.Remove(SelectedTab.Model);
                    Tabs.Remove(SelectedTab);
                    SelectedTab = Tabs.FirstOrDefault();
                }
            }, _ => SelectedTab != null && Tabs.Count > 1);
            ExportPresetsCommand = new RelayCommand(ExportPresets);
            ImportPresetsCommand = new RelayCommand(ImportPresets);
        }
        
        private void PopulateFieldsForPresetFilter()
        {
            FieldsForPresetFilter.Clear();
            // Use the same logic as for saving presets (order, tab names)
            foreach (var tabVm in Tabs)
            {
                // We can include all savable fields here
                var savableFieldsInTab = tabVm.Model.Fields
                    .Where(f => f.Type != FieldType.ScriptButton &&
                                f.Type != FieldType.Label &&
                                f.Type != FieldType.Separator);
        
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
                FilteredSnippetsView.GroupDescriptions.Add(new PropertyGroupDescription("Model.Category"));
            }
            
            // For Layouts
            FilteredLayoutsView = CollectionViewSource.GetDefaultView(Layouts);
            FilteredLayoutsView.Filter = PresetFilter;
            if (FilteredLayoutsView.CanGroup)
            {
                FilteredLayoutsView.GroupDescriptions.Clear();
                FilteredLayoutsView.GroupDescriptions.Add(new PropertyGroupDescription("Model.Category"));
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
            AllPresets.Clear();
            if (_workflow.Presets.TryGetValue(Id, out var presets))
            {
                foreach (var preset in presets)
                {
                    var presetVM = new GroupPresetViewModel(preset);
                    AllPresets.Add(presetVM);
                }
            }
            
            // Generate tooltips for all presets after they have been loaded.
            foreach (var presetVM in AllPresets)
            {
                presetVM.GenerateToolTip(this);
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

        
        private void SaveCurrentStateAsPreset(string name, IEnumerable<PresetFieldSelectionViewModel> selectedFields, bool isLayout)
        {
            var existingPreset = _workflow.Presets.GetValueOrDefault(Id)?.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            
            var newPreset = existingPreset?.Clone() ?? new GroupPreset();
            newPreset.Name = name;
            newPreset.IsLayout = isLayout;
            newPreset.LastModified = DateTime.UtcNow;
            
            newPreset.Description = NewPresetDescription;
            newPreset.Category = NewPresetCategory;
            newPreset.Tags = NewPresetTags?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(t => t.Trim())
                                     .Where(t => !string.IsNullOrWhiteSpace(t))
                                     .ToList() ?? new List<string>();

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
                .Where(f => f.Property != null || f is MarkdownFieldViewModel || f is NodeBypassFieldViewModel)
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
                if (value.StartsWith("[Base64 Image Data:") || value.StartsWith("[Данные изображения Base64:"))
                    return;

                // --- START OF FIX: Allow typing of decimal separators ---
                // Get the invariant decimal separator, which is '.'
                string decimalSeparator = CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator;
                
                // If the user is typing a number and just typed a decimal point
                // (e.g., "1."), we need to keep it as a string temporarily.
                // Otherwise, TryParse would convert "1." to the integer 1, and the dot would disappear.
                if (value.EndsWith(decimalSeparator))
                {
                    // Check if the part before the dot is a valid number.
                    string valueWithoutSeparator = value.Substring(0, value.Length - 1);
                    if (double.TryParse(valueWithoutSeparator, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                    {
                        // It's a valid partial number. Store as string and wait for more input.
                        Property.Value = new JValue(value);
                        return; // Exit early
                    }
                }
                // --- END OF FIX ---

                // When setting the value, try to parse it as a number using invariant culture.
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double numericValue))
                {
                    // If the parsed value is a whole number, store it as an integer to keep the JSON clean.
                    if (numericValue == Math.Truncate(numericValue))
                    {
                        Property.Value = new JValue(Convert.ToInt64(numericValue));
                    }
                    else
                    {
                        Property.Value = new JValue(numericValue);
                    }
                }
                else
                {
                    // If it cannot be parsed as a number, treat it as a string.
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
        public string Value
        {
            get => Property.Value.ToObject<long>().ToString(CultureInfo.InvariantCulture);
            set
            {
                if (long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var longValue) && Property.Value.ToObject<long>() != longValue)
                {
                    Property.Value = new JValue(longValue);
                    OnPropertyChanged(nameof(Value));
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
            get => Property.Value.ToObject<double>();
            set
            {
                // 1. Calculate the value snapped to the step
                var step = StepValue;
                if (step <= 0) step = 1e-9; // Protect against division by zero
                var snappedValue = System.Math.Round((value - MinValue) / step) * step + MinValue;

                // 2. Create a JToken of the correct type (Integer or Float)
                JToken newValueToken;
                if (Type == FieldType.SliderInt)
                {
                    // For integer sliders, round to the nearest whole number and clamp.
                    // This ensures the value is stored as an integer (e.g., 4) instead of a float (4.0).
                    var intValue = Convert.ToInt64(System.Math.Round(snappedValue));
                    intValue = Math.Max((long)MinValue, Math.Min((long)MaxValue, intValue));
                    newValueToken = new JValue(intValue);
                }
                else // SliderFloat
                {
                    // For float sliders, round to the specified precision and clamp.
                    var precision = _field.Precision ?? 2;
                    var roundedFloatValue = System.Math.Round(snappedValue, precision);
                    roundedFloatValue = Math.Max(MinValue, Math.Min(MaxValue, roundedFloatValue));
                    newValueToken = new JValue(roundedFloatValue);
                }
                
                // 3. Update the underlying JObject and notify the UI only if the value has actually changed.
                // This prevents infinite update loops.
                if (!JToken.DeepEquals(Property.Value, newValueToken))
                {
                    Property.Value = newValueToken;
                    
                    // Notify the UI that both the raw value and the formatted text have changed.
                    // WPF will automatically update the slider's position to the final value.
                    OnPropertyChanged(nameof(Value));
                    OnPropertyChanged(nameof(FormattedValue));
                }
            }
        }

        /// <summary>
        /// Новое свойство, которое возвращает уже отформатированную строку для отображения.
        /// </summary>
        public string FormattedValue => Property.Value.ToObject<double>().ToString(StringFormat);
        
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

    public class ComboBoxFieldViewModel : InputFieldViewModel
    {
        public string Value
        {
            get => Property.Value.ToString();
            set
            {
                if (Property.Value.ToString() != value)
                {
                    Property.Value = new JValue(value);
                    OnPropertyChanged(nameof(Value));
                }
            }
        }
        public List<string> ItemsSource { get; set; }

        public ComboBoxFieldViewModel(WorkflowField field, JProperty property, string nodeTitle = null, string nodeType = null) : base(field, property, nodeTitle, nodeType)
        {
            Type = field.Type;
            // ИСПРАВЛЕНИЕ: Сохраняем field в член класса
            _field = field; 
            ItemsSource = new List<string>();
        }

        public async Task LoadItemsAsync(ModelService modelService, AppSettings settings)
        {
            try
            {
                var finalItemsSource = new List<string>();

                if (Type == FieldType.Model)
                {
                    var types = await modelService.GetModelTypesAsync(); // This might throw an exception
                    var modelTypeInfo = types.FirstOrDefault(t => t.Name == _field.ModelType);
                    if (modelTypeInfo != null)
                    {
                        var models = await modelService.GetModelFilesAsync(modelTypeInfo);
                        finalItemsSource.AddRange(settings.SpecialModelValues);
                        finalItemsSource.AddRange(models);
                    }
                }
                else
                {
                    finalItemsSource = _field.Type switch
                    {
                        FieldType.Sampler => settings.Samplers,
                        FieldType.Scheduler => settings.Schedulers,
                        FieldType.ComboBox => _field.ComboBoxItems,
                        _ => new List<string>()
                    };
                }
                ItemsSource = finalItemsSource.Distinct().ToList();
                OnPropertyChanged(nameof(ItemsSource));
            }
            catch (Exception ex)
            {
                // Silently log the error. The user has already been notified by the ModelService.
                System.Diagnostics.Debug.WriteLine($"Failed to load items for combobox '{Name}': {ex.Message}");
            }
        }
        
        public override void RefreshValue()
        {
            OnPropertyChanged(nameof(Value));
        }
        
        private readonly WorkflowField _field;
    }
    
    public class CheckBoxFieldViewModel : InputFieldViewModel
    {
        public bool IsChecked
        {
            get => Property.Value.ToObject<bool>();
            set
            {
                if (Property.Value.ToObject<bool>() != value)
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
}