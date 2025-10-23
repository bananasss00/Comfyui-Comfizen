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
    
    public GlobalSettingsViewModel GlobalSettings { get; private set; }
    public GlobalPresetsViewModel GlobalPresets { get; private set; }
    
    private readonly List<SeedFieldViewModel> _seedViewModels = new();

    private readonly List<string> _wildcardPropertyPaths = new();
    
    public ICommand ExecuteActionCommand { get; }
    private bool _isUpdatingFromGlobalPreset = false; // Flag to prevent recursion

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
        
        GlobalSettings = new GlobalSettingsViewModel();
        GlobalPresets = new GlobalPresetsViewModel(ApplyGlobalPreset);
    }
    
    public ObservableCollection<WorkflowUITabLayoutViewModel> TabLayoouts { get; set; } = new();

    public SeedControl SelectedSeedControl { get; set; }
    
    /// <summary>
    /// Bubbles up the PresetsModified event from any of the child WorkflowGroupViewModels.
    /// </summary>
    public event Action PresetsModifiedInGroup;

    public event PropertyChangedEventHandler? PropertyChanged;
    
    public string CreatePromptTask()
    {
        var prompt = _workflow.JsonClone();
        ProcessSpecialFields(prompt);
        return prompt.ToString();
    }
    
    public void ProcessSpecialFields(JToken prompt)
    {
        ApplyWildcards(prompt);
        ApplyInpaintData(prompt);
        ApplySeedControl(prompt);
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
                prop.Value = new JValue(Utils.ReplaceWildcards(text, GlobalSettings.WildcardSeed));
            }
        }
    }

    private void ApplyInpaintData(JToken prompt)
    {
        foreach (var vm in _inpaintViewModels)
        {
            // Если есть поле для изображения, обрабатываем его
            if (vm.ImageField != null)
            {
                var prop = Utils.GetJsonPropertyByPath((JObject)prompt, vm.ImageField.Path);
                if (prop != null)
                {
                    var base64Image = vm.Editor.GetImageAsBase64();
                    if (base64Image != null) prop.Value = new JValue(base64Image);
                }
            }

            // Если есть поле для маски, обрабатываем его
            if (vm.MaskField != null)
            {
                var prop = Utils.GetJsonPropertyByPath((JObject)prompt, vm.MaskField.Path);
                if (prop != null)
                {
                    var base64Mask = vm.Editor.GetMaskAsBase64();
                    if (base64Mask != null) prop.Value = new JValue(base64Mask);
                }
            }
        }
    }

    private void ApplySeedControl(JToken prompt)
    {
        if (SelectedSeedControl == SeedControl.Fixed) return;

        foreach (var seedVm in _seedViewModels)
        {
            if (seedVm.IsLocked) continue;

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

        if (_hasWildcardFields && !GlobalSettings.IsSeedLocked)
        {
            var newSeed = GlobalSettings.WildcardSeed;
            switch (SelectedSeedControl)
            {
                case SeedControl.Increment: newSeed++; break;
                case SeedControl.Decrement: newSeed--; break;
                case SeedControl.Randomize: newSeed = Utils.GenerateSeed(); break;
            }
            // Обновляем UI через свойство ViewModel
            GlobalSettings.WildcardSeed = newSeed;
        }
    }

    public async Task LoadInputs()
    {
        CleanupInputs();

        _hasWildcardFields = _workflow.Groups.SelectMany(g => g.Fields)
            .Any(f => f.Type == FieldType.WildcardSupportPrompt);
        GlobalSettings.IsVisible = _hasWildcardFields;

        // Create a lookup for all groups for quick access
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
                }

                if (fieldVm != null)
                {
                    groupVm.Fields.Add(fieldVm);

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

        await Task.WhenAll(comboBoxLoadTasks);
        
        DiscoverGlobalPresets(); 
        
        // After loading all ViewModels, try to find a matching preset for each group.
        foreach (var groupVm in groupVmLookup.Values)
        {
            TryAutoSelectPreset(groupVm);
        }
            
        SyncGlobalPresetFromGroups();
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
        GlobalPresets.GlobalPresetNames.Clear();

        if (_workflow.Presets == null) return;

        var sharedPresetNames = _workflow.Presets
            .SelectMany(kvp => kvp.Value) // Flatten all presets from all groups into a single list
            .GroupBy(preset => preset.Name)      // Group them by name
            .Where(group => group.Count() > 1)  // Find names that appear more than once
            .Select(group => group.Key)         // Select the name
            .OrderBy(name => name);             // Order alphabetically

        foreach (var name in sharedPresetNames)
        {
            GlobalPresets.GlobalPresetNames.Add(name);
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
            .Where(g => g.Presets.Any(p => GlobalPresets.GlobalPresetNames.Contains(p.Name)))
            .ToList();

        if (!participatingGroups.Any())
        {
            GlobalPresets.SetSelectedPresetSilently(null);
            return;
        }

        // Check the selected preset of the first participating group.
        var firstPresetName = participatingGroups.First().SelectedPreset?.Name;

        // If the first group has no preset selected, there's no common selection.
        if (firstPresetName == null || !GlobalPresets.GlobalPresetNames.Contains(firstPresetName))
        {
            GlobalPresets.SetSelectedPresetSilently(null);
            return;
        }

        // Check if all other participating groups have the same preset selected.
        bool allMatch = participatingGroups.Skip(1)
            .All(g => g.SelectedPreset?.Name == firstPresetName);

        // If all match, update the global selection. Otherwise, clear it.
        GlobalPresets.SetSelectedPresetSilently(allMatch ? firstPresetName : null);
    }
    
    /// <summary>
    /// Applies a preset of a given name to all groups that contain it.
    /// </summary>
    private void ApplyGlobalPreset(string presetName)
    {
        if (string.IsNullOrEmpty(presetName)) return;
        
        _isUpdatingFromGlobalPreset = true; // Set flag to prevent feedback loop

        try
        {
            // --- START OF CHANGE: Iterate through groups in all tabs ---
            foreach (var groupVm in TabLayoouts.SelectMany(t => t.Groups))
                // --- END OF CHANGE ---
            {
                var presetToApply = groupVm.Presets.FirstOrDefault(p => p.Name == presetName);
                if (presetToApply != null)
                {
                    // This will apply the values and set the group's SelectedPreset
                    groupVm.SelectedPreset = presetToApply; 
                }
            }
        }
        finally
        {
            _isUpdatingFromGlobalPreset = false; // Unset the flag
        }
    }
    
    /// <summary>
    /// Checks if the current state of a group's savable fields exactly matches one of its saved presets.
    /// If a match is found, updates the SelectedPreset property to reflect it in the UI.
    /// Fields like Seed and Script Buttons are ignored in this comparison.
    /// </summary>
    private void TryAutoSelectPreset(WorkflowGroupViewModel groupVm)
    {
        if (!groupVm.Presets.Any())
        {
            return; // No presets to check against.
        }

        // START OF FIX: Rebuild how the current state dictionary is created
        
        // 1. Create a dictionary to hold the current values of all savable fields.
        var currentSavableValues = new Dictionary<string, JToken>();

        foreach (var fieldVm in groupVm.Fields)
        {
            // Skip fields that are never saved in presets.
            if (fieldVm.Type == FieldType.ScriptButton || fieldVm.Type == FieldType.Seed)
            {
                continue;
            }

            // Handle virtual Markdown fields by getting the value from the ViewModel.
            if (fieldVm is MarkdownFieldViewModel markdownVm)
            {
                currentSavableValues[markdownVm.Path] = new JValue(markdownVm.Value);
            }
            // Handle standard fields by getting the value from the JProperty.
            else if (fieldVm.Property != null)
            {
                currentSavableValues[fieldVm.Path] = fieldVm.Property.Value;
            }
        }
        
        // END OF FIX

        // 2. Find the first preset that exactly matches this current state.
        var matchingPresetVm = groupVm.Presets.FirstOrDefault(presetVm =>
        {
            var presetValues = presetVm.Model.Values;

            // 3. The number of fields must be identical for an exact match.
            if (presetValues.Count != currentSavableValues.Count)
            {
                return false;
            }

            // 4. Every key/value pair in the preset must exist and be equal in the current state.
            return presetValues.All(presetPair =>
                currentSavableValues.TryGetValue(presetPair.Key, out var currentValue) &&
                JToken.DeepEquals(presetPair.Value, currentValue)
            );
        });

        if (matchingPresetVm != null)
        {
            // A match was found. Set it as the selected preset.
            groupVm.SelectedPreset = matchingPresetVm;
        }
    }

    private InputFieldViewModel CreateDefaultFieldViewModel(WorkflowField field, JProperty? prop)
    {
        switch (field.Type)
        {
            case FieldType.Markdown:
                return new MarkdownFieldViewModel(field);
                
            case FieldType.Seed:
                var seedVm = new SeedFieldViewModel(field, prop);
                _seedViewModels.Add(seedVm);
                return seedVm;
                
            case FieldType.Model:
            case FieldType.Sampler:
            case FieldType.Scheduler:
            case FieldType.ComboBox:
                return new ComboBoxFieldViewModel(field, prop);
                 
            case FieldType.SliderInt:
            case FieldType.SliderFloat:
                return new SliderFieldViewModel(field, prop);

            case FieldType.WildcardSupportPrompt:
                 _wildcardPropertyPaths.Add(prop.Path);
                 return new TextFieldViewModel(field, prop);
            
            case FieldType.Any: 
                 if (prop.Value.Type == JTokenType.Boolean)
                 {
                     return new CheckBoxFieldViewModel(field, prop);
                 }
                 return new TextFieldViewModel(field, prop);
            
            case FieldType.ScriptButton:
                // We pass the ExecuteActionCommand from the controller itself
                return new ScriptButtonFieldViewModel(field, this.ExecuteActionCommand);
            
            default:
                return new TextFieldViewModel(field, prop);
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
        
        // --- START OF CHANGE: Correctly unsubscribe from events ---
        // Unsubscribe from events to prevent memory leaks when a tab is reloaded.
        var allGroups = TabLayoouts?.SelectMany(t => t.Groups) ?? Enumerable.Empty<WorkflowGroupViewModel>();
        foreach (var groupVm in allGroups)
        {
            groupVm.PropertyChanged -= OnGroupPresetChanged;
        }
        
        TabLayoouts.Clear();
        // --- END OF CHANGE ---

        _hasWildcardFields = false;
        if (GlobalSettings != null)
        {
            GlobalSettings.IsVisible = false;
        }
    }
}