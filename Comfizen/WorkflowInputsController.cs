using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Windows.Controls.Primitives;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using System.Drawing;
using System.Drawing.Imaging;

namespace Comfizen;

public class GridAxisSource : ISearchableContent
{
    public virtual string DisplayName { get; set; }
    public object Source { get; set; } // Can be InputFieldViewModel or WorkflowGroupViewModel
    public string GroupName { get; set; }
    
    // For easy binding in templates
    public bool IsField => Source is InputFieldViewModel;
    public bool IsGroup => Source is WorkflowGroupViewModel;

    public override string ToString() => DisplayName;
    
    public string GetSearchString()
    {
        if (string.IsNullOrEmpty(GroupName)) return DisplayName;
        return $"{DisplayName} {GroupName}";
    }
}

public class NullGridAxisSource : GridAxisSource
{
    public static NullGridAxisSource Instance { get; } = new NullGridAxisSource();
    private NullGridAxisSource()
    {
        DisplayName = "---";
        Source = null;
        GroupName = "Grid";
    }
}

[AddINotifyPropertyChangedInterface]
public class WorkflowUITabLayoutViewModel
{
    public string Header { get; set; }
    public ObservableCollection<WorkflowGroupViewModel> Groups { get; } = new ObservableCollection<WorkflowGroupViewModel>();
}


public enum XYGridMode
{
    Image,
    Video
}

// This avoids data binding issues with selecting a null item.
public class NullFieldViewModelPlaceholder : InputFieldViewModel
{
    // Singleton pattern ensures we use the exact same instance everywhere.
    public static NullFieldViewModelPlaceholder Instance { get; } = new NullFieldViewModelPlaceholder();

    // Private constructor for singleton pattern. Sets the display name.
    private NullFieldViewModelPlaceholder() : base(new WorkflowField { Name = "---" }, null, null, null) { }
}

[AddINotifyPropertyChangedInterface]
public class WorkflowInputsController : INotifyPropertyChanged
{
    private readonly WorkflowTabViewModel _parentTab; // Link to the parent
    private readonly AppSettings _settings;
    private readonly Workflow _workflow;
    private readonly ModelService _modelService;
    private bool _hasWildcardFields;
    private readonly List<InpaintFieldViewModel> _inpaintViewModels = new();
    private JObject _objectInfo;
    public JObject ObjectInfo => _objectInfo;
    
    public GlobalControlsViewModel GlobalControls { get; private set; }
    
    private readonly List<SeedFieldViewModel> _seedViewModels = new();

    private readonly List<string> _wildcardPropertyPaths = new();
    public IReadOnlyList<string> WildcardPropertyPaths => _wildcardPropertyPaths;
    
    public ICommand ExecuteActionCommand { get; }
    private bool _isUpdatingFromGlobalPreset = false; // Flag to prevent recursion
    private bool _isApplyingPreset = false; // NEW FLAG to prevent TryAutoSelectPreset during preset application
    
    // XYGrid
    public bool IsXyGridEnabled { get; set; }
    public bool XyGridCreateGridImage { get; set; } = true;
    public bool XyGridShowIndividualImages { get; set; } = false;
    public bool IsXyGridPopupOpen { get; set; }
    
    public bool XyGridLimitCellSize { get; set; } = false;
    public double XyGridMaxMegapixels { get; set; } = 4.0;
    
    public XYGridMode GridMode { get; set; } = XYGridMode.Image;
    public int VideoGridFrames { get; set; } = 4;
    
    public ObservableCollection<GridAxisSource> GridableSources { get; } = new ObservableCollection<GridAxisSource>();

    private GridAxisSource _selectedXSource;
    public GridAxisSource SelectedXSource
    {
        get => _selectedXSource ?? NullGridAxisSource.Instance;
        set
        {
            var newValue = (value is NullGridAxisSource) ? null : value;
            if (_selectedXSource == newValue) return;
            _selectedXSource = newValue;
            OnPropertyChanged(nameof(SelectedXSource));
            OnPropertyChanged(nameof(IsXSourceComboBox));
            OnPropertyChanged(nameof(XSourceItemsSource));
            OnPropertyChanged(nameof(IsXSourcePresetGroup));
            if (IsXSourcePresetGroup && SelectedXSource.Source is WorkflowGroupViewModel groupVm)
            {
                XSourcePresetsView = CollectionViewSource.GetDefaultView(groupVm.AllPresets);
                XSourcePresetsView.GroupDescriptions.Add(new PropertyGroupDescription("Model.IsLayout"));
                XSourcePresetsView.SortDescriptions.Add(new SortDescription("IsLayout", ListSortDirection.Ascending));
                XSourcePresetsView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
            }
            else
            {
                XSourcePresetsView = null;
            }
            OnPropertyChanged(nameof(XSourcePresetsView));
        }
    }
    public string XValues { get; set; }

    private GridAxisSource _selectedYSource;
    public GridAxisSource SelectedYSource
    {
        get => _selectedYSource ?? NullGridAxisSource.Instance;
        set
        {
            var newValue = (value is NullGridAxisSource) ? null : value;
            if (_selectedYSource == newValue) return;
            _selectedYSource = newValue;
            OnPropertyChanged(nameof(SelectedYSource));
            OnPropertyChanged(nameof(IsYSourceComboBox));
            OnPropertyChanged(nameof(YSourceItemsSource));
            OnPropertyChanged(nameof(IsYSourcePresetGroup));
            if (IsYSourcePresetGroup && SelectedYSource.Source is WorkflowGroupViewModel groupVm)
            {
                YSourcePresetsView = CollectionViewSource.GetDefaultView(groupVm.AllPresets);
                YSourcePresetsView.GroupDescriptions.Add(new PropertyGroupDescription("Model.IsLayout"));
                YSourcePresetsView.SortDescriptions.Add(new SortDescription("IsLayout", ListSortDirection.Ascending));
                YSourcePresetsView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
            }
            else
            {
                YSourcePresetsView = null;
            }
            OnPropertyChanged(nameof(YSourcePresetsView));
        }
    }
    public string YValues { get; set; }

    // New properties to support combo box helpers
    public bool IsXSourceComboBox => SelectedXSource?.Source is ComboBoxFieldViewModel;
    public List<object> XSourceItemsSource => (SelectedXSource?.Source as ComboBoxFieldViewModel)?.ItemsSource;
    public bool IsYSourceComboBox => SelectedYSource?.Source is ComboBoxFieldViewModel;
    public List<object> YSourceItemsSource => (SelectedYSource?.Source as ComboBoxFieldViewModel)?.ItemsSource;
    
    public bool IsXSourcePresetGroup => SelectedXSource?.Source is WorkflowGroupViewModel;
    public ICollectionView XSourcePresetsView { get; private set; }
    public bool IsYSourcePresetGroup => SelectedYSource?.Source is WorkflowGroupViewModel;
    public ICollectionView YSourcePresetsView { get; private set; }
    
    public ICommand AddValueToGridCommand { get; }
    
    public GlobalPresetEditorViewModel GlobalPresetEditor { get; set; }
    public bool IsGlobalPresetEditorOpen { get; set; }

    public ICommand AddPresetValueToGridCommand { get; }
    
    public WorkflowInputsController(Workflow workflow, AppSettings settings, ModelService modelService, WorkflowTabViewModel parentTab)
    {
        _workflow = workflow;
        _settings = settings;
        _modelService = modelService;
        _parentTab = parentTab;
        
        ExecuteActionCommand = new RelayCommand(actionName =>
        {
            if (actionName is string name)
            {
                _parentTab.ExecuteAction(name);
            }
        });
        

        GlobalControls = new GlobalControlsViewModel(
            ApplyGlobalPreset,       // Argument 1: Apply
            GetCurrentGlobalState,   // Argument 2: Get State (Callback defined below)
            SaveGlobalPreset,        // Argument 3: Save
            DeleteGlobalPreset       // Argument 4: Delete
        );
        

        GlobalControls.OpenGlobalPresetEditorCommand = new RelayCommand(_ => OpenGlobalPresetEditor());
        
        this.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(SelectedXSource))
            {
                OnPropertyChanged(nameof(IsXSourceComboBox));
                OnPropertyChanged(nameof(XSourceItemsSource));
            }
            if (e.PropertyName == nameof(SelectedYSource))
            {
                OnPropertyChanged(nameof(IsYSourceComboBox));
                OnPropertyChanged(nameof(YSourceItemsSource));
            }
        };

        AddValueToGridCommand = new RelayCommand(param =>
        {
            if (param is not Tuple<object, object> tuple)
            {
                return;
            }

            // CHANGE: Handle both strings and ComboBoxItemWrapper
            string selectedValueString = null;

            if (tuple.Item1 is ComboBoxItemWrapper wrapper)
            {
                // Use the underlying API value, not the display label
                selectedValueString = wrapper.Value;
            }
            else
            {
                selectedValueString = tuple.Item1?.ToString();
            }

            if (string.IsNullOrEmpty(selectedValueString) || tuple.Item2 is not string axis)
            {
                return;
            }

            if (axis == "X")
            {
                XValues = string.IsNullOrEmpty(XValues) ? selectedValueString : XValues + Environment.NewLine + selectedValueString;
            }
            else if (axis == "Y")
            {
                YValues = string.IsNullOrEmpty(YValues) ? selectedValueString : YValues + Environment.NewLine + selectedValueString;
            }
        });
        
        AddPresetValueToGridCommand = new RelayCommand(param =>
        {
            if (param is not Tuple<object, object> tuple || tuple.Item1 is not GroupPresetViewModel preset || tuple.Item2 is not string axis)
            {
                return;
            }

            string selectedValue = preset.Name;

            if (axis == "X")
            {
                XValues = string.IsNullOrEmpty(XValues) ? selectedValue : XValues + Environment.NewLine + selectedValue;
            }
            else if (axis == "Y")
            {
                YValues = string.IsNullOrEmpty(YValues) ? selectedValue : YValues + Environment.NewLine + selectedValue;
            }
        });
    }
    
    // Helper for constructor
    private Dictionary<Guid, List<string>> GetCurrentGlobalState()
    {
        var allGroups = TabLayoouts.SelectMany(t => t.Groups).ToList();
        return allGroups
            .Where(g => g.ActiveLayers.Any())
            .ToDictionary(g => g.Id, g => g.ActiveLayers.Select(l => l.Name).ToList());
    }
    
    private void OpenGlobalPresetEditor()
    {
        var allGroups = TabLayoouts.SelectMany(t => t.Groups).ToList();
    
        GlobalPresetEditor = new GlobalPresetEditorViewModel(
            _workflow.GlobalPresets,
            allGroups,
            SaveGlobalPreset,
            DeleteGlobalPreset,
            GetCurrentGlobalState // Use the same helper
        );

        GlobalPresetEditor.LoadCurrentStateIntoEditor();
        IsGlobalPresetEditorOpen = true;
    }

    private void SaveGlobalPreset(GlobalPreset presetToSave)
    {
        if (presetToSave != null)
        {
            var existing = _workflow.GlobalPresets.FirstOrDefault(p => p.Name == presetToSave.Name);
            if (existing != null) _workflow.GlobalPresets.Remove(existing);
            
            _workflow.GlobalPresets.Add(presetToSave);
            
            GlobalControls.SelectedGlobalPreset = presetToSave;
        }
        PresetsModifiedInGroup?.Invoke();
    }

    private void DeleteGlobalPreset(GlobalPreset presetToDelete)
    {
        bool proceed = !_settings.ShowPresetDeleteConfirmation ||
                       (MessageBox.Show(
                           string.Format(LocalizationService.Instance["Presets_DeleteConfirmMessage"], presetToDelete.Name),
                           LocalizationService.Instance["Presets_DeleteConfirmTitle"],
                           MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);

        if (!proceed) return;

        var existing = _workflow.GlobalPresets.FirstOrDefault(p => p.Name == presetToDelete.Name);
        if (existing != null)
        {
            _workflow.GlobalPresets.Remove(existing);
            
            if (GlobalControls.GlobalPresets.Contains(presetToDelete))
            {
                GlobalControls.GlobalPresets.Remove(presetToDelete);
            }
            
            PresetsModifiedInGroup?.Invoke();
        }
    }
    
    /// <summary>
    /// Re-creates the ICollectionView for the X and Y axes if they are bound to a preset group.
    /// This is necessary to reflect newly created or deleted presets in the XY Grid popup.
    /// </summary>
    private void RefreshPresetViewsForXyGrid()
    {
        if (IsXSourcePresetGroup && SelectedXSource.Source is WorkflowGroupViewModel xGroupVm)
        {
            XSourcePresetsView = CollectionViewSource.GetDefaultView(xGroupVm.AllPresets);
            XSourcePresetsView.GroupDescriptions.Add(new PropertyGroupDescription("Model.IsLayout"));
            XSourcePresetsView.SortDescriptions.Add(new SortDescription("IsLayout", ListSortDirection.Ascending));
            XSourcePresetsView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
            OnPropertyChanged(nameof(XSourcePresetsView));
        }
            
        if (IsYSourcePresetGroup && SelectedYSource.Source is WorkflowGroupViewModel yGroupVm)
        {
            YSourcePresetsView = CollectionViewSource.GetDefaultView(yGroupVm.AllPresets);
            YSourcePresetsView.GroupDescriptions.Add(new PropertyGroupDescription("Model.IsLayout"));
            YSourcePresetsView.SortDescriptions.Add(new SortDescription("IsLayout", ListSortDirection.Ascending));
            YSourcePresetsView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
            OnPropertyChanged(nameof(YSourcePresetsView));
        }
    }
    
    /// <summary>
    /// Populates the hook toggles in the GlobalControlsViewModel based on the workflow's scripts.
    /// </summary>
    public void PopulateHooks(ScriptCollection scripts)
    {
        GlobalControls.PopulateHooks(scripts);
    }

    public ObservableCollection<WorkflowUITabLayoutViewModel> TabLayoouts { get; set; } = new();

    public SeedControl SelectedSeedControl { get; set; }
    
    public WorkflowUITabLayoutViewModel SelectedTabLayout { get; set; }
    
    /// <summary>
    /// Bubbles up the PresetsModified event from any of the child WorkflowGroupViewModels.
    /// </summary>
    public event Action PresetsModifiedInGroup;
    public event Action InputsLoaded;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    
    public async Task<string> CreatePromptTaskAsync()
    {
        var prompt = _workflow.JsonClone();
        await ProcessSpecialFieldsAsync(prompt);
        return prompt.ToString();
    }
    
    public async Task ProcessSpecialFieldsAsync(JToken prompt, HashSet<string> pathsToIgnore = null)
    {
        ApplyPromptTokenFiltering(prompt);
        ApplyNodeBypass((JObject)prompt);
        ApplyWildcards(prompt);
        await ApplyInpaintDataAsync(prompt);
        ApplySeedControl(prompt, pathsToIgnore);
    }
    
    private void ApplyPromptTokenFiltering(JToken prompt)
    {
        // Reuse the existing flag that checks if any WildcardSupportPrompt fields exist.
        if (!_hasWildcardFields) return;

        foreach (var path in _wildcardPropertyPaths)
        {
            var prop = Utils.GetJsonPropertyByPath((JObject)prompt, path);
            if (prop == null || prop.Value.Type != JTokenType.String) continue;

            var originalText = prop.Value.ToObject<string>();
            if (string.IsNullOrWhiteSpace(originalText)) continue;

            // Tokenize, filter out disabled tokens, and join the remaining ones back into a string.
            var allTokens = PromptUtils.Tokenize(originalText);
            var enabledTokens = allTokens.Where(t => !t.StartsWith(PromptUtils.DISABLED_TOKEN_PREFIX));
            var filteredText = string.Join(", ", enabledTokens);

            prop.Value = new JValue(filteredText);
        }
    }

    /// <summary>
    /// Modifies a JObject prompt in-place to apply node bypass logic for a single generation run.
    /// This method orchestrates the bypass process by creating a dedicated NodeBypassService
    /// and delegating the complex graph manipulation logic to it.
    /// </summary>
    /// <param name="prompt">The JObject representing the workflow API, which will be modified directly.</param>
    public void ApplyNodeBypass(JObject prompt)
    {
        if (_objectInfo == null)
        {
            // This can happen if the initial API call to /object_info failed.
            // In this case, we cannot safely perform a bypass, so we do nothing.
            return;
        }

        // Delegate all bypass logic to the specialized service.
        var bypassService = new NodeBypassService(_objectInfo, _workflow.NodeConnectionSnapshots, TabLayoouts);
        bypassService.ApplyBypass(prompt);
    }

    private void ApplyWildcards(JToken prompt)
    {
        if (!_hasWildcardFields) return;

        foreach (var wildcardProperty in _wildcardPropertyPaths)
        {
            var prop = Utils.GetJsonPropertyByPath((JObject)prompt, wildcardProperty);
            if (prop != null && prop.Value.Type == JTokenType.String)
            {
                var text = prop.Value.ToObject<string>();
                // Используем значение из ViewModel
                prop.Value = new JValue(Utils.ReplaceWildcards(text, GlobalControls.WildcardSeed));
            }
        }
    }

    private async Task ApplyInpaintDataAsync(JToken prompt)
    {
        foreach (var vm in _inpaintViewModels)
        {
            // english: If an image field exists, process it
            if (vm.ImageField != null)
            {
                var prop = Utils.GetJsonPropertyByPath((JObject)prompt, vm.ImageField.Path);
                if (prop != null)
                {
                    // english: Await the asynchronous method to get the Base64 image
                    var base64Image = await vm.Editor.GetImageAsBase64Async();
                    if (base64Image != null) prop.Value = new JValue(base64Image);
                }
            }

            // english: If a mask field exists, process it
            if (vm.MaskField != null)
            {
                var prop = Utils.GetJsonPropertyByPath((JObject)prompt, vm.MaskField.Path);
                if (prop != null)
                {
                    // english: Await the asynchronous method to get the Base64 mask
                    var base64Mask = await vm.Editor.GetMaskAsBase64Async();
                    if (base64Mask != null) prop.Value = new JValue(base64Mask);
                }
            }
        }
    }

    private void ApplySeedControl(JToken prompt, HashSet<string> pathsToIgnore = null)
    {
        if (SelectedSeedControl == SeedControl.Fixed) return;

        foreach (var seedVm in _seedViewModels)
        {
            if (seedVm.IsLocked || (pathsToIgnore != null && pathsToIgnore.Contains(seedVm.Path)))
            {
                continue;
            }

            // ИСПРАВЛЕНИЕ: Используем публичное свойство Property
            var prop = Utils.GetJsonPropertyByPath((JObject)prompt, seedVm.Property.Path);
            if (prop != null && long.TryParse(prop.Value.ToString(), out var currentValue))
            {
                var newValue = currentValue;
                switch (SelectedSeedControl)
                {
                    case SeedControl.Increment: newValue++; break;
                    case SeedControl.Decrement: newValue--; break;
                    case SeedControl.Randomize: newValue = Utils.GenerateSeed(seedVm.MinValue, seedVm.MaxValue); break;
                }
                
                prop.Value = new JValue(newValue);
                
                seedVm.Value = newValue.ToString();
            }
        }

        if (_hasWildcardFields && !GlobalControls.IsSeedLocked)
        {
            var newSeed = GlobalControls.WildcardSeed;
            switch (SelectedSeedControl)
            {
                case SeedControl.Increment: newSeed++; break;
                case SeedControl.Decrement: newSeed--; break;
                case SeedControl.Randomize: newSeed = Utils.GenerateSeed(0, 4294967295L); break;
            }
            // Обновляем UI через свойство ViewModel
            GlobalControls.WildcardSeed = newSeed;
        }
    }
    
    /// <summary>
    /// Updates the underlying workflow's JObject with the current state from complex controls
    /// like the InpaintEditor before the session is saved.
    /// </summary>
    public async Task PrepareForSessionSaveAsync()
    {
        foreach (var vm in _inpaintViewModels)
        {
            // Save the source image Base64 string
            if (vm.ImageField != null)
            {
                var prop = _workflow.GetPropertyByPath(vm.ImageField.Path);
                if (prop != null)
                {
                    var base64Image = await vm.Editor.GetImageAsBase64Async();
                    prop.Value = new JValue(base64Image ?? string.Empty);
                }
            }

            // Save the mask Base64 string
            if (vm.MaskField != null)
            {
                var prop = _workflow.GetPropertyByPath(vm.MaskField.Path);
                if (prop != null)
                {
                    var base64Mask = await vm.Editor.GetMaskAsBase64Async();
                    prop.Value = new JValue(base64Mask ?? string.Empty);
                }
            }
        }
    }

    public async Task LoadInputs(string lastActiveTabName = null)
    {
        CleanupInputs();
        
        try
        {
            _objectInfo = await _modelService.GetObjectInfoAsync();
        }
        catch
        {
            // The error is already logged by ModelService. We'll proceed without object_info,
            // which will gracefully disable the bypass functionality.
            _objectInfo = null;
        }

        _hasWildcardFields = _workflow.Groups.SelectMany(g => g.Tabs).SelectMany(t => t.Fields)
            .Any(f => f.Type == FieldType.WildcardSupportPrompt);
        GlobalControls.IsSeedSectionVisible = _hasWildcardFields;

        var groupVmLookup = new Dictionary<Guid, WorkflowGroupViewModel>();
        var comboBoxLoadTasks = new List<Task>();

        // First pass: Create all GroupViewModels and populate their TabViewModels
        foreach (var group in _workflow.Groups)
        {
            var groupVm = new WorkflowGroupViewModel(group, _workflow, _settings);
            groupVm.PresetsModified += () =>
            {
                PresetsModifiedInGroup?.Invoke();
                LoadGlobalPresets();
                PopulateGridableSources();
                RefreshPresetViewsForXyGrid();
            };
            groupVm.ActiveLayersChanged += SyncGlobalPresetFromGroups;
            groupVmLookup[group.Id] = groupVm;

            foreach (var tabVm in groupVm.Tabs)
            {
                var processedFields = new HashSet<WorkflowField>();

                for (int i = 0; i < tabVm.Model.Fields.Count; i++)
                {
                    var field = tabVm.Model.Fields[i];
                    if (processedFields.Contains(field))
                    {
                        continue;
                    }

                    InputFieldViewModel fieldVm = null;
                    var property = _workflow.GetPropertyByPath(field.Path);

                    if (property != null)
                    {
                        if (field.Type == FieldType.ImageInput)
                        {
                            WorkflowField pairedMaskField = null;
                            if (i + 1 < tabVm.Model.Fields.Count && tabVm.Model.Fields[i + 1].Type == FieldType.MaskInput)
                            {
                                pairedMaskField = tabVm.Model.Fields[i + 1];
                            }

                            fieldVm = new InpaintFieldViewModel(field, pairedMaskField, property);
                            _inpaintViewModels.Add((InpaintFieldViewModel)fieldVm);
                            processedFields.Add(field);
                            if (pairedMaskField != null) processedFields.Add(pairedMaskField);
                        }
                        else if (field.Type == FieldType.MaskInput)
                        {
                            fieldVm = new InpaintFieldViewModel(field, null, property);
                            _inpaintViewModels.Add((InpaintFieldViewModel)fieldVm);
                            processedFields.Add(field);
                        }
                        else
                        {
                            fieldVm = CreateDefaultFieldViewModel(field, property);
                            processedFields.Add(field);
                        }
                    }
                    else
                    {
                        if (field.Type == FieldType.Markdown)
                        {
                            fieldVm = new MarkdownFieldViewModel(field);
                            processedFields.Add(field);
                        }
                        else if (field.Type == FieldType.Label)
                        {
                            fieldVm = new LabelFieldViewModel(field);
                            processedFields.Add(field);
                        }
                        else if (field.Type == FieldType.Separator)
                        {
                            fieldVm = new SeparatorFieldViewModel(field);
                            processedFields.Add(field);
                        }
                        else if (field.Type == FieldType.ScriptButton)
                        {
                            fieldVm = new ScriptButtonFieldViewModel(field, this.ExecuteActionCommand);
                            processedFields.Add(field);
                        }
                        else if (field.Type == FieldType.NodeBypass)
                        {
                            fieldVm = new NodeBypassFieldViewModel(field, null);
                            processedFields.Add(field);

                            var bypassVm = (NodeBypassFieldViewModel)fieldVm;
                            var controlledNodeIds = bypassVm.BypassNodeIds.ToHashSet();
                            if (!controlledNodeIds.Any()) continue;

                            var nodesToSnapshot = new HashSet<string>(controlledNodeIds);
                            if (_workflow.LoadedApi != null)
                            {
                                foreach (var nodeProperty in _workflow.LoadedApi.Properties())
                                {
                                    if (nodeProperty.Value is not JObject node || node["inputs"] is not JObject inputs) continue;

                                    foreach (var inputProperty in inputs.Properties())
                                    {
                                        if (inputProperty.Value is JArray link && link.Count > 0 &&
                                            link[0].Type == JTokenType.String && controlledNodeIds.Contains(link[0].ToString()))
                                        {
                                            nodesToSnapshot.Add(nodeProperty.Name);
                                            break;
                                        }
                                    }
                                }
                            }

                            foreach (var nodeId in nodesToSnapshot)
                            {
                                if (_workflow.LoadedApi?[nodeId] is not JObject node) continue;

                                if (node["inputs"] is JObject inputsToSave) // This check is now the only condition
                                {
                                    var originalConnections = new JObject();
                                    foreach (var prop in inputsToSave.Properties().Where(p => p.Value is JArray))
                                    {
                                        originalConnections.Add(prop.Name, prop.Value.DeepClone());
                                    }

                                    // Always overwrite or add the snapshot for this node.
                                    // This ensures that after an API replacement, the snapshots are updated.
                                    _workflow.NodeConnectionSnapshots[nodeId] = originalConnections;
                                }
                            }
                        }
                        else
                        {
                            Logger.Log($"[UI Validation] UI field '{field.Name}' references a non-existent API path: '{field.Path}'. This field will be ignored.", LogLevel.Error);
                            processedFields.Add(field);
                        }
                    }

                    if (fieldVm != null)
                    {
                        tabVm.Fields.Add(fieldVm);
                        
                        if (fieldVm is InpaintFieldViewModel inpaintVm)
                        {
                            string imageBase64 = null;
                            if (inpaintVm.ImageField != null)
                            {
                                var imageProp = _workflow.GetPropertyByPath(inpaintVm.ImageField.Path);
                                // Check if it's a non-empty string, which is likely our base64 data
                                if (imageProp?.Value.Type == JTokenType.String && !string.IsNullOrEmpty(imageProp.Value.ToString()))
                                {
                                    imageBase64 = imageProp.Value.ToString();
                                }
                            }

                            string maskBase64 = null;
                            if (inpaintVm.MaskField != null)
                            {
                                var maskProp = _workflow.GetPropertyByPath(inpaintVm.MaskField.Path);
                                if (maskProp?.Value.Type == JTokenType.String && !string.IsNullOrEmpty(maskProp.Value.ToString()))
                                {
                                    maskBase64 = maskProp.Value.ToString();
                                }
                            }
                            
                            // Load the data into the editor if anything was found
                            if (!string.IsNullOrEmpty(imageBase64) || !string.IsNullOrEmpty(maskBase64))
                            {
                                inpaintVm.LoadSessionData(imageBase64, maskBase64);
                            }
                        }
                        
                        fieldVm.PropertyChanged += OnFieldViewModelPropertyChanged;

                        if (fieldVm is ComboBoxFieldViewModel comboBoxVm)
                        {
                            comboBoxLoadTasks.Add(comboBoxVm.LoadItemsAsync(_modelService, _settings));
                        }
                    }
                }
            }
        }

        // Second pass: Arrange GroupViewModels into tabs
        if (_workflow.Tabs.Any())
        {
            foreach (var tabDef in _workflow.Tabs)
            {
                var tabLayout = new WorkflowUITabLayoutViewModel { Header = tabDef.Name };
                foreach (var groupId in tabDef.GroupIds)
                {
                    if (groupVmLookup.TryGetValue(groupId, out var groupVm))
                    {
                        tabLayout.Groups.Add(groupVm);
                    }
                }
                if (tabLayout.Groups.Any())
                {
                    TabLayoouts.Add(tabLayout);
                }
            }
        }
        else
        {
            // Fallback for old workflows without tabs: create one default tab
            var defaultTabLayout = new WorkflowUITabLayoutViewModel { Header = "Controls" }; // Or localize this
            // --- START OF CHANGE: Use .Model property ---
            foreach (var groupVm in groupVmLookup.Values.OrderBy(g => _workflow.Groups.IndexOf(g.Model)))
                // --- END OF CHANGE ---
            {
                defaultTabLayout.Groups.Add(groupVm);
            }
            if (defaultTabLayout.Groups.Any())
            {
                TabLayoouts.Add(defaultTabLayout);
            }
        }
        
        PopulateGridableSources();
        
        await Task.WhenAll(comboBoxLoadTasks);
        
        LoadGlobalPresets(); 
        
        SyncGlobalPresetFromGroups();
        
        // This is necessary because the initial call in the constructor happens before session values are applied.
        foreach (var groupVm in groupVmLookup.Values)
        {
            groupVm.RebuildActiveLayersFromState();
        }

        GlobalControls.UpdateAvailableGroups(groupVmLookup.Values);
            
        // --- START OF NEW LOGIC ---
        // After all tab layouts are created, select the active one.
        WorkflowUITabLayoutViewModel tabToSelect = null;
        if (!string.IsNullOrEmpty(lastActiveTabName))
        {
            // Try to find the tab with the saved name.
            tabToSelect = TabLayoouts.FirstOrDefault(t => t.Header == lastActiveTabName);
        }

        // If no saved tab was found (e.g., first load, or tab was renamed/deleted), default to the first one.
        SelectedTabLayout = tabToSelect ?? TabLayoouts.FirstOrDefault();
        InputsLoaded?.Invoke();
    }

    private void PopulateGridableSources()
    {
        var selectedX = SelectedXSource?.Source;
        var selectedY = SelectedYSource?.Source;

        GridableSources.Clear();
        GridableSources.Add(NullGridAxisSource.Instance);

        // 1. Add fields preserving the UI order (Layout -> Group -> Tab -> Field)
        foreach (var tabLayout in TabLayoouts)
        {
            foreach (var groupVm in tabLayout.Groups)
            {
                foreach (var tabVm in groupVm.Tabs)
                {
                    foreach (var fieldVm in tabVm.Fields)
                    {
                        if (fieldVm.FieldModel != null && (
                            fieldVm.FieldModel.Type == FieldType.Any ||
                            fieldVm.FieldModel.Type == FieldType.Seed ||
                            fieldVm.FieldModel.Type == FieldType.WildcardSupportPrompt ||
                            fieldVm.FieldModel.Type == FieldType.Sampler ||
                            fieldVm.FieldModel.Type == FieldType.Scheduler ||
                            fieldVm.FieldModel.Type == FieldType.SliderInt ||
                            fieldVm.FieldModel.Type == FieldType.SliderFloat ||
                            fieldVm.FieldModel.Type == FieldType.ComboBox ||
                            fieldVm.FieldModel.Type == FieldType.NodeBypass ||
                            fieldVm.FieldModel.Type == FieldType.Model))
                        {
                            GridableSources.Add(new GridAxisSource
                            {
                                DisplayName = fieldVm.Name,
                                Source = fieldVm,
                                GroupName = groupVm.Name
                            });
                        }
                    }
                }
            }
        }
        
        // 2. Add all groups that have presets (at the bottom)
        // We also iterate through TabLayouts to keep the group order consistent with the UI
        var processedGroups = new HashSet<WorkflowGroupViewModel>();
        
        foreach (var tabLayout in TabLayoouts)
        {
            foreach (var groupVm in tabLayout.Groups)
            {
                if (groupVm.HasPresets && !processedGroups.Contains(groupVm))
                {
                    GridableSources.Add(new GridAxisSource
                    {
                        DisplayName = $"[Presets] {groupVm.Name}",
                        Source = groupVm,
                        GroupName = groupVm.Name
                    });
                    processedGroups.Add(groupVm);
                }
            }
        }
        
        if (selectedX != null)
        {
            SelectedXSource = GridableSources.FirstOrDefault(s => s.Source == selectedX);
        }
        if (selectedY != null)
        {
            SelectedYSource = GridableSources.FirstOrDefault(s => s.Source == selectedY);
        }
    }

    /// <summary>
    /// When any field's value changes, find its parent group and notify it.
    /// The group VM will then handle the logic of updating its active layer states.
    /// </summary>
    private void OnFieldViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "Value" && e.PropertyName != "IsChecked" && e.PropertyName != "IsEnabled")
            return;
    
        if (_isUpdatingFromGlobalPreset) return;

        if (sender is InputFieldViewModel fieldVm)
        {
            if (fieldVm is MarkdownFieldViewModel)
            {
                PresetsModifiedInGroup?.Invoke(); // trigger save wf on disk with new value markdown
                return; 
            }

            var groupVm = FindGroupForField(fieldVm);
            if (groupVm != null)
            {
                groupVm.NotifyFieldValueChanged(fieldVm);
            }
        }
    }
    
    /// <summary>
    /// Finds the WorkflowGroupViewModel that contains the specified InputFieldViewModel.
    /// </summary>
    private WorkflowGroupViewModel FindGroupForField(InputFieldViewModel fieldVm)
    {
        return TabLayoouts.SelectMany(tab => tab.Groups)
            .FirstOrDefault(group => group.Tabs.SelectMany(t => t.Fields).Contains(fieldVm));
    }

    /// <summary>
    /// Handles the PropertyChanged event for a WorkflowGroupViewModel to sync the global preset selection.
    /// </summary>
    private void OnGroupPresetChanged(object sender, PropertyChangedEventArgs e)
    {
        // Check if the 'SelectedPreset' property of a group has changed
        // and ensure we're not in the middle of a global update to prevent recursion.
        // if (e.PropertyName == nameof(WorkflowGroupViewModel.SelectedPreset) && !_isUpdatingFromGlobalPreset)
        // {
        //     SyncGlobalPresetFromGroups();
        // }
    }
    
    /// <summary>
    /// Populates the GlobalControls ViewModel with defined global presets from the workflow.
    /// </summary>
    public void LoadGlobalPresets()
    {
        GlobalControls.GlobalPresets.Clear();
        if (_workflow.GlobalPresets == null) return;

        foreach (var preset in _workflow.GlobalPresets.OrderBy(p => p.Name))
        {
            GlobalControls.GlobalPresets.Add(preset);
        }
    }
    
    /// <summary>
    /// Checks if the current state of all active layers across all groups matches
    /// any of the defined global presets and updates the UI accordingly.
    /// </summary>
    private void SyncGlobalPresetFromGroups()
    {
        if (_isApplyingPreset) return;

        var allGroups = TabLayoouts.SelectMany(t => t.Groups).ToList();
        var groupLookup = allGroups.ToDictionary(g => g.Id);

        // 1. Get the current state, resolving layouts into their constituent snippets.
        var currentState = allGroups.ToDictionary(g => g.Id, g => {
            var activeLayers = new HashSet<string>();
            foreach (var layer in g.ActiveLayers)
            {
                if (layer.SourcePreset.IsLayout)
                {
                    // If it's a layout, add all its snippets.
                    foreach (var snippetName in layer.SourcePreset.Model.SnippetNames ?? new List<string>())
                    {
                        activeLayers.Add(snippetName);
                    }
                }
                else
                {
                    // If it's a snippet, add it directly.
                    activeLayers.Add(layer.Name);
                }
            }
            return activeLayers;
        });

        GlobalPreset matchedPreset = null;

        // 2. Iterate through all defined global presets to find a match.
        foreach (var globalPreset in _workflow.GlobalPresets)
        {
            bool isMatch = true;
            // 3. Check if the set of groups in the preset matches the set of groups with active layers.
            // NOTE: We only check groups DEFINED in the global preset. Other groups are ignored.
            
            foreach (var requiredState in globalPreset.GroupStates)
            {
                var groupId = requiredState.Key;
                
                // Resolve the global preset's layers for this group into a flat list of snippets.
                var requiredSnippets = new HashSet<string>();
                if (groupLookup.TryGetValue(groupId, out var groupVm))
                {
                    foreach (var layerName in requiredState.Value)
                    {
                        var preset = groupVm.AllPresets.FirstOrDefault(p => p.Name == layerName);
                        if (preset != null)
                        {
                            if (preset.IsLayout)
                            {
                                foreach (var snippetName in preset.Model.SnippetNames ?? new List<string>())
                                {
                                    requiredSnippets.Add(snippetName);
                                }
                            }
                            else
                            {
                                requiredSnippets.Add(layerName);
                            }
                        }
                    }
                }

                // Compare the resolved snippets with the current state's active snippets for this group.
                // If the group is missing from current state (empty) but required by preset, it's a mismatch.
                if (!currentState.TryGetValue(groupId, out var currentSnippets) || !requiredSnippets.SetEquals(currentSnippets))
                {
                    isMatch = false;
                    break;
                }
            }

            if (isMatch)
            {
                matchedPreset = globalPreset;
                break; // Found a match.
            }
        }

        GlobalControls.SetSelectedPresetSilently(matchedPreset);
    }
    
    /// <summary>
    /// Applies a global preset, setting the active layers for all relevant groups.
    /// </summary>
    private void ApplyGlobalPreset(GlobalPreset preset)
    {
        if (preset == null) return;
        
        _isUpdatingFromGlobalPreset = true;
        _isApplyingPreset = true;
        try
        {
            var allGroups = TabLayoouts.SelectMany(t => t.Groups).ToList();

            // Apply presets ONLY to groups defined in the global preset.
            foreach (var groupState in preset.GroupStates)
            {
                var groupId = groupState.Key;
                var presetNames = groupState.Value;

                var groupVmToApply = allGroups.FirstOrDefault(g => g.Id == groupId);
                if (groupVmToApply == null) continue;
                
                // Clear existing layers for this group to ensure clean application
                groupVmToApply.ClearActiveLayers();

                // Apply all presets listed for this group.
                foreach (var presetName in presetNames)
                {
                    var presetToApply = groupVmToApply.AllPresets.FirstOrDefault(p => p.Name == presetName);
                    if (presetToApply != null)
                    {
                        groupVmToApply.ApplyPreset(presetToApply);
                    }
                }
            }
        }
        finally
        {
            _isUpdatingFromGlobalPreset = false;
            _isApplyingPreset = false;
        }
    }
    
    private InputFieldViewModel CreateDefaultFieldViewModel(WorkflowField field, JProperty? prop)
    {
        string nodeTitle = null;
        string nodeType = null;
        if (!string.IsNullOrEmpty(field.Path) && !field.Path.StartsWith("virtual_"))
        {
            var pathParts = field.Path.Split('.');
            if (pathParts.Length > 0)
            {
                string nodeId = pathParts[0];
                var nodeData = _workflow.LoadedApi?[nodeId];
                if (nodeData != null)
                {
                    nodeTitle = nodeData["_meta"]?["title"]?.ToString();
                    nodeType = nodeData["class_type"]?.ToString();
                }
            }
        }
        
        if (string.IsNullOrEmpty(nodeTitle)) nodeTitle = field.NodeTitle;
        if (string.IsNullOrEmpty(nodeType)) nodeType = field.NodeType;
        
        switch (field.Type)
        {
            case FieldType.Markdown:
                return new MarkdownFieldViewModel(field);
                
            case FieldType.Seed:
                var seedVm = new SeedFieldViewModel(field, prop, nodeTitle, nodeType);
                _seedViewModels.Add(seedVm);
                return seedVm;
            // case FieldType.NodeBypass:
            //     return new NodeBypassFieldViewModel(field, null);
            case FieldType.Model:
            case FieldType.Sampler:
            case FieldType.Scheduler:
            case FieldType.ComboBox:
                return new ComboBoxFieldViewModel(field, prop, nodeTitle, nodeType);
                 
            case FieldType.SliderInt:
            case FieldType.SliderFloat:
                var sliderVm = new SliderFieldViewModel(field, prop, nodeTitle, nodeType);
                if (field.Type == FieldType.SliderInt && sliderVm.Property.Value.Type == JTokenType.Float)
                {
                    var longValue = sliderVm.Property.Value.ToObject<long>();
                    sliderVm.Property.Value = new JValue(longValue);
                }
                return sliderVm;

            case FieldType.WildcardSupportPrompt:
                 _wildcardPropertyPaths.Add(prop.Path);
                 return new TextFieldViewModel(field, prop, nodeTitle, nodeType);
            
            case FieldType.Any: 
                 if (prop.Value.Type == JTokenType.Boolean)
                 {
                     return new CheckBoxFieldViewModel(field, prop, nodeTitle, nodeType);
                 }
                 return new TextFieldViewModel(field, prop, nodeTitle, nodeType);
            
            case FieldType.ScriptButton:
                // We pass the ExecuteActionCommand from the controller itself
                return new ScriptButtonFieldViewModel(field, this.ExecuteActionCommand);
            
            default:
                return new TextFieldViewModel(field, prop, nodeTitle, nodeType);
        }
    }

    public void HandlePasteOperation()
    {
        // Ищем редактор под мышкой в новом списке
        var targetEditor = _inpaintViewModels.Select(vm => vm.Editor)
            .FirstOrDefault(editor => editor.IsMouseOver && editor.CanAcceptImage);
        
        if (targetEditor == null)
        {
            // Если не нашли, берем первый доступный
            targetEditor = _inpaintViewModels.Select(vm => vm.Editor)
                .FirstOrDefault(e => e.CanAcceptImage);
        }
        
        if (targetEditor == null) return;
        
        var imageBytes = GetImageBytesFromClipboard();
        if (imageBytes != null)
        {
            targetEditor.SetSourceImage(imageBytes);
        }
    }

    /// <summary>
    /// Универсальный метод для извлечения изображения из буфера обмена.
    /// Обрабатывает форматы Image, DIB и FileDrop.
    /// </summary>
    /// <returns>Массив байт в формате PNG или null, если изображение не найдено.</returns>
    private byte[] GetImageBytesFromClipboard()
    {
        // 1. Сначала пытаемся получить изображение через System.Drawing (наиболее совместимый способ)
        try
        {
            if (System.Windows.Forms.Clipboard.ContainsImage())
            {
                // Используем System.Drawing.Image, т.к. он отлично парсит DIB и другие форматы
                using (var drawingImage = System.Windows.Forms.Clipboard.GetImage())
                {
                    if (drawingImage != null)
                    {
                        using (var ms = new MemoryStream())
                        {
                            // Сохраняем в MemoryStream в формате PNG (он сохраняет прозрачность)
                            drawingImage.Save(ms, ImageFormat.Png);
                            return ms.ToArray();
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // Игнорируем ошибки от System.Drawing и переходим к запасному варианту
        }

        // 2. Запасной вариант: проверяем на наличие скопированных файлов (стандартный WPF-способ)
        if (Clipboard.ContainsFileDropList())
        {
            var filePaths = Clipboard.GetFileDropList();
            if (filePaths != null && filePaths.Count > 0)
            {
                var filePath = filePaths[0];
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                if (new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }.Contains(extension))
                {
                    try { return File.ReadAllBytes(filePath); } catch { /* ignore */ }
                }
            }
        }

        // Если ничего не сработало, возвращаем null
        return null;
    }

    private void CleanupInputs()
    {
        _seedViewModels.Clear();
        _wildcardPropertyPaths.Clear();
        _inpaintViewModels.Clear();
        
        GridableSources.Clear();
        IsXyGridEnabled = false;
        IsXyGridPopupOpen = false; // Reset popup state
        SelectedXSource = null;
        SelectedYSource = null;
        XValues = null;
        YValues = null;
        
        // Unsubscribe from events to prevent memory leaks when a tab is reloaded.
        var allGroups = TabLayoouts?.SelectMany(t => t.Groups) ?? Enumerable.Empty<WorkflowGroupViewModel>();
        foreach (var groupVm in allGroups)
        {
            groupVm.PropertyChanged -= OnGroupPresetChanged;

            foreach (var tabVm in groupVm.Tabs)
            {
                foreach (var fieldVm in tabVm.Fields)
                {
                    fieldVm.PropertyChanged -= OnFieldViewModelPropertyChanged;
                }
            }
        }

        TabLayoouts.Clear();

        _hasWildcardFields = false;
        if (GlobalControls != null)
        {
            // english: Update the visibility on the new combined ViewModel.
            GlobalControls.IsSeedSectionVisible = false;
        }
    }
}