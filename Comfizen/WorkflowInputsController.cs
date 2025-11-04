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

[AddINotifyPropertyChangedInterface]
public class WorkflowUITabLayoutViewModel
{
    public string Header { get; set; }
    public ObservableCollection<WorkflowGroupViewModel> Groups { get; } = new ObservableCollection<WorkflowGroupViewModel>();
}

[AddINotifyPropertyChangedInterface]
public class GlobalPresetsViewModel : INotifyPropertyChanged
{
    public string Header { get; set; } = LocalizationService.Instance["GlobalPresets_Header"];
    public bool IsExpanded { get; set; } = true;
    public bool IsVisible => GlobalPresetNames.Any();

    public ObservableCollection<string> GlobalPresetNames { get; } = new();
    
    private readonly Action<string> _applyPresetAction;
    private string _selectedGlobalPreset;
    private bool _isInternalSet = false; // Flag to prevent action trigger on internal updates

    public string SelectedGlobalPreset
    {
        get => _selectedGlobalPreset;
        set
        {
            if (_selectedGlobalPreset == value) return;
            
            _selectedGlobalPreset = value;
            OnPropertyChanged(nameof(SelectedGlobalPreset));

            // Only trigger the action if the change was made by the user (not internally)
            if (!_isInternalSet && value != null)
            {
                _applyPresetAction?.Invoke(value);
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    
    public GlobalPresetsViewModel(Action<string> applyPresetAction)
    {
        _applyPresetAction = applyPresetAction;
        GlobalPresetNames.CollectionChanged += (s, e) => OnPropertyChanged(nameof(IsVisible));
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
public class WorkflowInputsController : INotifyPropertyChanged
{
    private readonly WorkflowTabViewModel _parentTab; // Link to the parent
    private readonly AppSettings _settings;
    private readonly Workflow _workflow;
    private readonly ModelService _modelService;
    private bool _hasWildcardFields;
    private readonly List<InpaintFieldViewModel> _inpaintViewModels = new();
    
    public GlobalControlsViewModel GlobalControls { get; private set; }
    
    private readonly List<SeedFieldViewModel> _seedViewModels = new();

    private readonly List<string> _wildcardPropertyPaths = new();
    
    public ICommand ExecuteActionCommand { get; }
    private bool _isUpdatingFromGlobalPreset = false; // Flag to prevent recursion
    private bool _isApplyingPreset = false; // NEW FLAG to prevent TryAutoSelectPreset during preset application
    
    // XYGrid
    public bool IsXyGridEnabled { get; set; }
    public bool XyGridCreateGridImage { get; set; } = true;
    public bool XyGridShowIndividualImages { get; set; } = false;
    public bool IsXyGridPopupOpen { get; set; }
    public ObservableCollection<InputFieldViewModel> GridableFields { get; } = new ObservableCollection<InputFieldViewModel>();
    public InputFieldViewModel SelectedXField { get; set; }
    public string XValues { get; set; }
    public InputFieldViewModel SelectedYField { get; set; }
    public string YValues { get; set; }

    // New properties to support combo box helpers
    public bool IsXFieldComboBox => SelectedXField is ComboBoxFieldViewModel;
    public List<string> XFieldItemsSource => (SelectedXField as ComboBoxFieldViewModel)?.ItemsSource;
    public bool IsYFieldComboBox => SelectedYField is ComboBoxFieldViewModel;
    public List<string> YFieldItemsSource => (SelectedYField as ComboBoxFieldViewModel)?.ItemsSource;
    
    public ICommand AddValueToGridCommand { get; }
    
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
        
        GlobalControls = new GlobalControlsViewModel(ApplyGlobalPreset);
        
        this.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(SelectedXField))
            {
                OnPropertyChanged(nameof(IsXFieldComboBox));
                OnPropertyChanged(nameof(XFieldItemsSource));
            }
            if (e.PropertyName == nameof(SelectedYField))
            {
                OnPropertyChanged(nameof(IsYFieldComboBox));
                OnPropertyChanged(nameof(YFieldItemsSource));
            }
        };

        AddValueToGridCommand = new RelayCommand(param =>
        {
            if (param is not Tuple<object, object> tuple || tuple.Item1 is not string selectedValue || tuple.Item2 is not string axis)
            {
                return;
            }

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

    public ObservableCollection<WorkflowUITabLayoutViewModel> TabLayoouts { get; set; } = new();

    public SeedControl SelectedSeedControl { get; set; }
    
    public WorkflowUITabLayoutViewModel SelectedTabLayout { get; set; }
    
    /// <summary>
    /// Bubbles up the PresetsModified event from any of the child WorkflowGroupViewModels.
    /// </summary>
    public event Action PresetsModifiedInGroup;

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

            // --- START OF NEW LOGIC: Save original prompt to _meta ---
            
            // Extract node ID and input name from the path (e.g., "6.inputs.text" -> "6" and "text")
            // NOTE: A helper method to parse the path might be cleaner, but this works for the expected format.
            var pathParts = path.Split('.');
            if (pathParts.Length >= 3 && pathParts[1] == "inputs")
            {
                var nodeId = pathParts[0];
                var inputName = pathParts[2];

                if (prompt[nodeId] is JObject node)
                {
                    // Ensure _meta object exists
                    if (node["_meta"] is not JObject meta)
                    {
                        meta = new JObject();
                        node["_meta"] = meta;
                    }

                    // Ensure original_advanced_prompts object exists within _meta
                    if (meta["original_advanced_prompts"] is not JObject originalPrompts)
                    {
                        originalPrompts = new JObject();
                        meta["original_advanced_prompts"] = originalPrompts;
                    }

                    // Save the full, unfiltered prompt text.
                    // This will be stored in the generated image's metadata.
                    originalPrompts[inputName] = originalText;
                }
            }
            // --- END OF NEW LOGIC ---

            // Tokenize, filter out disabled tokens, and join the remaining ones back into a string.
            var allTokens = PromptUtils.Tokenize(originalText);
            var enabledTokens = allTokens.Where(t => !t.StartsWith(PromptUtils.DISABLED_TOKEN_PREFIX));
            var filteredText = string.Join(", ", enabledTokens);

            prop.Value = new JValue(filteredText);
        }
    }
    
    /// <summary>
    /// Modifies a JObject prompt in-place to apply node bypass logic for a single generation run.
    /// This method is idempotent; it restores the prompt to its original state before applying the current bypasses.
    /// It performs a "two-sided" bypass:
    /// 1. Disconnects all inputs TO the bypassed nodes to prevent them from executing.
    /// 2. Reroutes all connections FROM the bypassed nodes to their original sources, effectively "skipping" them in the graph.
    /// </summary>
    /// <param name="prompt">The JObject representing the workflow API, which will be modified directly.</param>
    public void ApplyNodeBypass(JObject prompt)
    {
        // --- SECTION 1: Find all bypass control ViewModels in the current UI layout ---
        var bypassViewModels = TabLayoouts
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Fields)
            .OfType<NodeBypassFieldViewModel>()
            .ToList();

        // --- SECTION 2: CRITICAL - Restore all nodes to their original state ---
        // This step is essential to make the bypass operation idempotent and stateless.
        // Before applying any new bypasses, we revert all nodes that have a backup
        // in their `_meta` property to their original connection state. This ensures
        // that disabling a bypass switch correctly restores the original workflow.
        foreach (var nodeProperty in prompt.Properties())
        {
            if (nodeProperty.Value is not JObject node ||
                node["_meta"]?["original_inputs"] is not JObject originalInputs ||
                node["inputs"] is not JObject currentInputs)
            {
                // Skip if the node doesn't have a backup.
                continue;
            }
            // Merge the backup back into the current inputs. This overwrites only the
            // connection properties (JArrays), preserving any changes to widget values.
            currentInputs.Merge(originalInputs.DeepClone(), new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
        }

        // If there are no bypass controls defined in the UI, there's nothing more to do.
        if (!bypassViewModels.Any())
        {
            return;
        }

        // --- SECTION 3: Identify all nodes that need to be bypassed for THIS run ---
        // We build a HashSet for efficient lookup (.Contains).
        var nodesToBypassThisRun = new HashSet<string>();
        foreach (var vm in bypassViewModels)
        {
            // The UI checkbox `IsEnabled` property is inverted from the bypass logic.
            // If the checkbox is UNCHECKED (`IsEnabled` is false), the bypass is ACTIVE.
            if (!vm.IsEnabled)
            {
                foreach (var nodeId in vm.BypassNodeIds)
                {
                    nodesToBypassThisRun.Add(nodeId);
                }
            }
        }
        
        // If no bypasses are active for this run, we can exit early.
        if (!nodesToBypassThisRun.Any())
        {
            return;
        }

        // --- SECTION 4: Perform the "two-sided" disconnection ---
        // PART A: Disconnect the INPUTS *OF* the bypassed nodes.
        // This prevents them from being queued for execution by the ComfyUI backend.
        foreach (var bypassedNodeId in nodesToBypassThisRun)
        {
            if (prompt[bypassedNodeId]?["inputs"] is not JObject bypassedNodeInputs) continue;

            // Find all input properties that are connections (represented as JArray) and remove them.
            var connectionProperties = bypassedNodeInputs.Properties()
                .Where(p => p.Value is JArray)
                .ToList();
            
            foreach (var prop in connectionProperties)
            {
                prop.Remove();
            }
        }

        // PART B: Build a redirection map to handle downstream connections.
        // This map will tell us where to get the input from if a node is connected to a bypassed node.
        var redirectionMap = new Dictionary<string, JArray>();
        foreach (var bypassedNodeId in nodesToBypassThisRun)
        {
            var bypassedNode = prompt[bypassedNodeId] as JObject;
            if (bypassedNode == null) continue;
            
            // IMPORTANT: We read the connections from the `_meta.original_inputs` snapshot,
            // because we just deleted the live connections in the step above.
            var originalInputs = bypassedNode["_meta"]?["original_inputs"] as JObject;
            if (originalInputs == null) continue;

            int outputIndex = 0;
            foreach (var inputProp in originalInputs.Properties())
            {
                if (inputProp.Value is JArray sourceLink)
                {
                    // The key is a unique identifier for the output slot of the bypassed node.
                    string outputKey = $"{bypassedNodeId}.{outputIndex}";
                    // The value is the original source this bypassed node was connected to.
                    redirectionMap[outputKey] = sourceLink;
                    outputIndex++;
                }
            }
        }

        // --- SECTION 5: Rewire all downstream node connections ---
        // Iterate through every node in the prompt to check and update its inputs.
        foreach (var nodeProperty in prompt.Properties())
        {
            if (nodeProperty.Value is not JObject node || node["inputs"] is not JObject nodeInputs) continue;

            // Check every input of the current node.
            foreach (var inputProperty in nodeInputs.Properties())
            {
                if (inputProperty.Value is not JArray originalLink || originalLink.Count != 2)
                {
                    continue; // Not a standard connection link.
                }

                JArray currentLink = originalLink;
                int depth = 0;
                const int maxDepth = 20; // Safety break to prevent infinite loops from cyclic dependencies.
                
                // Trace back the connection chain until we find a non-bypassed source.
                while (depth < maxDepth)
                {
                    string sourceNodeId = currentLink[0].ToString();

                    // If the source is NOT in our bypass list, we've found the final, valid source.
                    if (!nodesToBypassThisRun.Contains(sourceNodeId))
                    {
                        break;
                    }

                    // The source is bypassed, so we look it up in our redirection map.
                    string sourceOutputIndex = currentLink[1].ToString();
                    string sourceKey = $"{sourceNodeId}.{sourceOutputIndex}";
                    
                    if (redirectionMap.TryGetValue(sourceKey, out var newSourceLink))
                    {
                        // We found a new source. We will loop again in case this new source is also bypassed.
                        currentLink = newSourceLink; 
                    }
                    else
                    {
                        // The chain is broken (e.g., the input was a widget value, not a link). Stop tracing.
                        break;
                    }
                    depth++;
                }
                
                // If the final resolved link is different from the original one, update the prompt.
                if (currentLink != originalLink)
                {
                    // Use DeepClone to ensure the new value is a separate JArray instance.
                    inputProperty.Value = currentLink.DeepClone();
                }
            }
        }
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
                    case SeedControl.Randomize: newValue = Utils.GenerateSeed(); break;
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
                case SeedControl.Randomize: newSeed = Utils.GenerateSeed(); break;
            }
            // Обновляем UI через свойство ViewModel
            GlobalControls.WildcardSeed = newSeed;
        }
    }

    public async Task LoadInputs(string lastActiveTabName = null)
    {
        CleanupInputs();

        _hasWildcardFields = _workflow.Groups.SelectMany(g => g.Fields)
            .Any(f => f.Type == FieldType.WildcardSupportPrompt);
        GlobalControls.IsSeedSectionVisible = _hasWildcardFields;
        
        var groupVmLookup = new Dictionary<Guid, WorkflowGroupViewModel>();
        var comboBoxLoadTasks = new List<Task>();

        // First pass: Create all GroupViewModels and their fields
        foreach (var group in _workflow.Groups)
        {
            var groupVm = new WorkflowGroupViewModel(group, _workflow);
            groupVm.PresetsModified += () =>
            {
                PresetsModifiedInGroup?.Invoke();
                DiscoverGlobalPresets();
            };
            groupVm.PropertyChanged += OnGroupPresetChanged;
            groupVmLookup[group.Id] = groupVm;

            // (Этот код по обработке полей, Inpaint, ComboBox и т.д. остается без изменений)
            var processedFields = new HashSet<WorkflowField>();

            for (int i = 0; i < group.Fields.Count; i++)
            {
                var field = group.Fields[i];
                if (processedFields.Contains(field))
                {
                    continue; // Skip if this field was already handled as part of a pair
                }

                InputFieldViewModel fieldVm = null;
                var property = _workflow.GetPropertyByPath(field.Path);
                
                if (property != null)
                {
                    // Scenario 1: Found an ImageInput
                    if (field.Type == FieldType.ImageInput)
                    {
                        WorkflowField pairedMaskField = null;
                        if (i + 1 < group.Fields.Count && group.Fields[i + 1].Type == FieldType.MaskInput)
                        {
                            pairedMaskField = group.Fields[i + 1];
                        }

                        fieldVm = new InpaintFieldViewModel(field, pairedMaskField, property);
                        _inpaintViewModels.Add((InpaintFieldViewModel)fieldVm);
                        processedFields.Add(field);
                        if (pairedMaskField != null) processedFields.Add(pairedMaskField);
                    }
                    // Scenario 2: Found a MaskInput that was not part of a pair
                    else if (field.Type == FieldType.MaskInput)
                    {
                        // Create a standalone editor just for the mask
                        fieldVm = new InpaintFieldViewModel(field, null, property);
                        _inpaintViewModels.Add((InpaintFieldViewModel)fieldVm);
                        processedFields.Add(field);
                    }
                    // Scenario 3: Any other field
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
                    else if (field.Type == FieldType.ScriptButton)
                    {
                        fieldVm = new ScriptButtonFieldViewModel(field, this.ExecuteActionCommand);
                        processedFields.Add(field);
                    }
                    else if (field.Type == FieldType.NodeBypass)
                    {
                        fieldVm = new NodeBypassFieldViewModel(field, null);
                        processedFields.Add(field);
                        
                        // --- START OF REWORKED SNAPSHOT LOGIC ---
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
                            
                            if (node["_meta"] is not JObject meta)
                            {
                                meta = new JObject();
                                node["_meta"] = meta;
                            }
                            
                            if (meta["original_inputs"] == null && node["inputs"] is JObject inputsToSave)
                            {
                                // Create a snapshot of *only* the connections (JArray properties).
                                var originalConnections = new JObject();
                                foreach (var prop in inputsToSave.Properties().Where(p => p.Value is JArray))
                                {
                                    originalConnections.Add(prop.Name, prop.Value.DeepClone());
                                }
                                
                                if (originalConnections.HasValues)
                                {
                                    meta["original_inputs"] = originalConnections;
                                }
                            }
                        }
                        // --- END OF REWORKED SNAPSHOT LOGIC ---
                    }
                    else
                    {
                        // This field is not a known virtual type and its path was not found in the API JSON.
                        // It's a dangling reference, likely from a previous or different API structure.
                        Logger.Log($"[UI Validation] UI field '{field.Name}' references a non-existent API path: '{field.Path}'. This field will be ignored.", LogLevel.Error);
                        // fieldVm remains null, so it won't be added to the UI.
                        processedFields.Add(field);
                    }
                }

                if (fieldVm != null)
                {
                    groupVm.Fields.Add(fieldVm);

                    // --- START OF NEW LOGIC ---
                    // Subscribe to value changes in the field to trigger auto-preset selection.
                    fieldVm.PropertyChanged += OnFieldViewModelPropertyChanged;
                    // --- END OF NEW LOGIC ---

                    if (fieldVm is ComboBoxFieldViewModel comboBoxVm)
                    {
                        comboBoxLoadTasks.Add(comboBoxVm.LoadItemsAsync(_modelService, _settings));
                    }
                }
            }
            
            // if (groupVm.Fields.Any())
            // {
            //     InputGroups.Add(groupVm);
            // }
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
        
        PopulateGridableFields();
        
        await Task.WhenAll(comboBoxLoadTasks);
        
        DiscoverGlobalPresets(); 
        
        foreach (var groupVm in groupVmLookup.Values)
        {
            TryAutoSelectPreset(groupVm);
        }
            
        SyncGlobalPresetFromGroups();

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
    }
    
    private void PopulateGridableFields()
    {
        GridableFields.Clear();

        // Add a null option to allow disabling an axis
        GridableFields.Add(null);

        var fields = _workflow.Groups.SelectMany(g => g.Fields)
            .Where(f => f.Type == FieldType.Any ||
                        f.Type == FieldType.Seed ||
                        f.Type == FieldType.WildcardSupportPrompt ||
                        f.Type == FieldType.Sampler ||
                        f.Type == FieldType.Scheduler ||
                        f.Type == FieldType.SliderInt ||
                        f.Type == FieldType.SliderFloat ||
                        f.Type == FieldType.ComboBox ||
                        f.Type == FieldType.NodeBypass ||
                        f.Type == FieldType.Model)
            .OrderBy(f => f.Name);
            
        foreach(var field in fields)
        {
            // Find the ViewModel that was already created for this field model
            var fieldVm = TabLayoouts.SelectMany(t => t.Groups)
                .SelectMany(g => g.Fields)
                .FirstOrDefault(vm => vm.FieldModel == field);
            if (fieldVm != null)
            {
                GridableFields.Add(fieldVm);
            }
        }
    }
    
    /// <summary>
    /// When any field's value changes, find its parent group and check if the group's
    /// state now matches any of its saved presets.
    /// </summary>
    private void OnFieldViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        // We only care about properties that represent the field's actual value.
        if (e.PropertyName != "Value" && e.PropertyName != "IsChecked" && e.PropertyName != "IsEnabled")
            return;
    
        // If a preset is currently being applied, do nothing to avoid loops.
        if (_isApplyingPreset || _isUpdatingFromGlobalPreset)
            return;

        if (sender is InputFieldViewModel fieldVm)
        {
            var groupVm = FindGroupForField(fieldVm);
            if (groupVm != null)
            {
                // The state has changed, so we try to match it against existing presets.
                // This will either select a matching preset or clear the selection if no match is found.
                TryAutoSelectPreset(groupVm);
            }
        }
    }
    
    /// <summary>
    /// Finds the WorkflowGroupViewModel that contains the specified InputFieldViewModel.
    /// </summary>
    private WorkflowGroupViewModel FindGroupForField(InputFieldViewModel fieldVm)
    {
        return TabLayoouts.SelectMany(tab => tab.Groups)
            .FirstOrDefault(group => group.Fields.Contains(fieldVm));
    }
    
    /// <summary>
    /// Handles the PropertyChanged event for a WorkflowGroupViewModel to sync the global preset selection.
    /// </summary>
    private void OnGroupPresetChanged(object sender, PropertyChangedEventArgs e)
    {
        // Check if the 'SelectedPreset' property of a group has changed
        // and ensure we're not in the middle of a global update to prevent recursion.
        if (e.PropertyName == nameof(WorkflowGroupViewModel.SelectedPreset) && !_isUpdatingFromGlobalPreset)
        {
            SyncGlobalPresetFromGroups();
        }
    }
    
    /// <summary>
    /// Scans all presets in the workflow to find names that are shared across multiple groups.
    /// Populates the GlobalPresets ViewModel with these names.
    /// </summary>
    public void DiscoverGlobalPresets()
    {
        GlobalControls.GlobalPresetNames.Clear();

        if (_workflow.Presets == null) return;

        var sharedPresetNames = _workflow.Presets
            .SelectMany(kvp => kvp.Value) // Flatten all presets from all groups into a single list
            .GroupBy(preset => preset.Name)      // Group them by name
            .Where(group => group.Count() > 1)  // Find names that appear more than once
            .Select(group => group.Key)         // Select the name
            .OrderBy(name => name);             // Order alphabetically

        foreach (var name in sharedPresetNames)
        {
            GlobalControls.GlobalPresetNames.Add(name);
        }
    }

    /// <summary>
    /// Checks all groups to see if they share a common selected preset. If so, updates the global preset ComboBox.
    /// </summary>
    private void SyncGlobalPresetFromGroups()
    {
        var allGroups = TabLayoouts.SelectMany(t => t.Groups).ToList();

        // Get all groups that can participate in global presets.
        var participatingGroups = TabLayoouts.SelectMany(t => t.Groups)
            .Where(g => g.Presets.Any(p => GlobalControls.GlobalPresetNames.Contains(p.Name)))
            .ToList();

        if (!participatingGroups.Any())
        {
            GlobalControls.SetSelectedPresetSilently(null);
            return;
        }

        // Check the selected preset of the first participating group.
        var firstPresetName = participatingGroups.First().SelectedPreset?.Name;

        // If the first group has no preset selected, there's no common selection.
        if (firstPresetName == null || !GlobalControls.GlobalPresetNames.Contains(firstPresetName))
        {
            GlobalControls.SetSelectedPresetSilently(null);
            return;
        }

        // Check if all other participating groups have the same preset selected.
        bool allMatch = participatingGroups.Skip(1)
            .All(g => g.SelectedPreset?.Name == firstPresetName);

        // If all match, update the global selection. Otherwise, clear it.
        GlobalControls.SetSelectedPresetSilently(allMatch ? firstPresetName : null);
    }
    
    /// <summary>
    /// Applies a preset of a given name to all groups that contain it.
    /// </summary>
    private void ApplyGlobalPreset(string presetName)
    {
        if (string.IsNullOrEmpty(presetName)) return;
        
        _isUpdatingFromGlobalPreset = true; // Set flag to prevent feedback loop
        _isApplyingPreset = true; // Also set this flag to stop auto-selection during application

        try
        {
            // Iterate through groups in all tabs
            foreach (var groupVm in TabLayoouts.SelectMany(t => t.Groups))
            {
                var presetToApply = groupVm.Presets.FirstOrDefault(p => p.Name == presetName);
                if (presetToApply != null)
                {
                    groupVm.ApplyPreset(presetToApply); 
                }
            }
        }
        finally
        {
            _isUpdatingFromGlobalPreset = false; // Unset the flag
            _isApplyingPreset = false;
        }
    }
    
    /// <summary>
    /// Checks if the current state of a group's savable fields exactly matches one of its saved presets.
    /// If a match is found, updates the SelectedPreset property to reflect it in the UI.
    /// If no match is found, it clears the SelectedPreset.
    /// </summary>
    /// <summary>
    /// Checks if the current state of a group's savable fields exactly matches one of its saved presets.
    /// If a match is found, updates the SelectedPreset property to reflect it in the UI.
    /// If no match is found, it clears the SelectedPreset.
    /// </summary>
    private void TryAutoSelectPreset(WorkflowGroupViewModel groupVm)
    {
        if (!groupVm.Presets.Any())
        {
            groupVm.SelectedPreset = null; // Ensure selection is cleared if no presets exist
            return;
        }
        
        var matchingPresetVm = groupVm.Presets.FirstOrDefault(presetVm => groupVm.IsStateMatchingPreset(presetVm));

        // Only update the property if the found preset is different from the currently selected one.
        // This avoids triggering the PropertyChanged event unnecessarily, which could lead to loops.
        if (groupVm.SelectedPreset != matchingPresetVm)
        {
            // This is a "silent" update. We set the flag to prevent the preset from being re-applied.
            _isApplyingPreset = true;
            groupVm.SelectedPreset = matchingPresetVm;
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
                return new SliderFieldViewModel(field, prop, nodeTitle, nodeType);

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
        
        GridableFields.Clear();
        IsXyGridEnabled = false;
        IsXyGridPopupOpen = false; // Reset popup state
        SelectedXField = null;
        SelectedYField = null;
        XValues = null;
        YValues = null;
        
        // Unsubscribe from events to prevent memory leaks when a tab is reloaded.
        var allGroups = TabLayoouts?.SelectMany(t => t.Groups) ?? Enumerable.Empty<WorkflowGroupViewModel>();
        foreach (var groupVm in allGroups)
        {
            groupVm.PropertyChanged -= OnGroupPresetChanged;
            // --- START OF NEW LOGIC ---
            // Unsubscribe from all field events within the group.
            foreach (var fieldVm in groupVm.Fields)
            {
                fieldVm.PropertyChanged -= OnFieldViewModelPropertyChanged;
            }
            // --- END OF NEW LOGIC ---
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