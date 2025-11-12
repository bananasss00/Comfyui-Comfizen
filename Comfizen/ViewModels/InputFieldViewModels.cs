using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
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
    [AddINotifyPropertyChangedInterface]
    public class PresetFieldSelectionViewModel
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsSelected { get; set; }
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
        public long WildcardSeed { get; set; } = Utils.GenerateSeed();
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

        public GroupPresetViewModel(GroupPreset model)
        {
            Model = model;
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

        // Свойство для InpaintEditor
        public InpaintEditor InpaintEditorControl { get; set; }
        public bool HasInpaintEditor => InpaintEditorControl != null;
        
        public Guid Id => _model.Id;
        public bool HasPresets => Presets.Any();
        public ObservableCollection<GroupPresetViewModel> Presets { get; } = new();
        public ObservableCollection<PresetFieldSelectionViewModel> FieldsForPresetSelection { get; } = new();

        private GroupPresetViewModel _selectedPreset;
        public GroupPresetViewModel SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (_selectedPreset != value)
                {
                    _selectedPreset = value;
                    OnPropertyChanged(nameof(SelectedPreset));
                    // Notify that the name has also changed for the FilterableComboBox
                    OnPropertyChanged(nameof(SelectedPresetName));
                }
            }
        }
        
        public IEnumerable<string> PresetNames => Presets.Select(p => p.Name).ToList();

        // NEW: Property to bind to the FilterableComboBox's string-based SelectedItem
        public string SelectedPresetName
        {
            get => SelectedPreset?.Name;
            set
            {
                if (SelectedPreset?.Name != value)
                {
                    // Find the full preset object by name and update the main property
                    SelectedPreset = Presets.FirstOrDefault(p => p.Name == value);
                }
            }
        }
        
        public bool IsSavePresetPopupOpen { get; set; }
        public string NewPresetName { get; set; }
        public ICommand OpenSavePresetPopupCommand { get; }
        public ICommand StartUpdatePresetCommand { get; }
        private string _presetToUpdateName;
        public ICommand SavePresetCommand { get; }
        public ICommand DeletePresetCommand { get; }
        public ICommand ApplyPresetCommand { get; }
        public ICommand ApplySelectedPresetCommand { get; }
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
        
        public WorkflowGroupViewModel(WorkflowGroup model, Workflow workflow)
        {
            _model = model;
            _workflow = workflow; // Store the reference
            
            foreach (var tabModel in _model.Tabs)
            {
                var tabVm = new WorkflowGroupTabViewModel(tabModel);
                Tabs.Add(tabVm);
            }
            SelectedTab = Tabs.FirstOrDefault();
            
            this.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(SelectedPreset) && SelectedPreset != null && !_isApplyingPreset)
                {
                    ApplyPreset(SelectedPreset);
                }
            };
            LoadPresets();
            
            ApplySelectedPresetCommand = new RelayCommand(
                _ => ApplyPreset(SelectedPreset), 
                _ => SelectedPreset != null 
            );
            
            OpenSavePresetPopupCommand = new RelayCommand(_ => 
            {
                NewPresetName = SelectedPresetName;
                
                FieldsForPresetSelection.Clear();
                var savableFields = Tabs.SelectMany(t => t.Fields)
                    .Where(f => f.Type != FieldType.ScriptButton)
                    .OrderBy(f => f.Name);

                foreach (var field in savableFields)
                {
                    FieldsForPresetSelection.Add(new PresetFieldSelectionViewModel
                    {
                        Name = field.Name,
                        Path = field.Path,
                        IsSelected = true 
                    });
                }
                IsSavePresetPopupOpen = true; 
            });
            
            StartUpdatePresetCommand = new RelayCommand(param =>
            {
                if (param is string presetName)
                {
                    _presetToUpdateName = presetName;
                    NewPresetName = presetName;
                    
                    FieldsForPresetSelection.Clear();
                    var existingPreset = Presets.FirstOrDefault(p => p.Name == presetName)?.Model;
                    var existingPresetPaths = new HashSet<string>(existingPreset?.Values.Keys ?? Enumerable.Empty<string>());

                    var savableFields = Tabs.SelectMany(t => t.Fields)
                        .Where(f => f.Type != FieldType.ScriptButton)
                        .OrderBy(f => f.Name);

                    foreach (var field in savableFields)
                    {
                        FieldsForPresetSelection.Add(new PresetFieldSelectionViewModel
                        {
                            Name = field.Name,
                            Path = field.Path,
                            IsSelected = existingPreset == null || existingPresetPaths.Contains(field.Path)
                        });
                    }
                    IsSavePresetPopupOpen = true;
                }
            });
            
            SavePresetCommand = new RelayCommand(_ =>
            {
                if (string.IsNullOrWhiteSpace(NewPresetName)) return;

                if (!string.IsNullOrEmpty(_presetToUpdateName) && !_presetToUpdateName.Equals(NewPresetName, StringComparison.OrdinalIgnoreCase))
                {
                    if (_workflow.Presets.TryGetValue(Id, out var presets))
                    {
                        presets.RemoveAll(p => p.Name.Equals(_presetToUpdateName, StringComparison.OrdinalIgnoreCase));
                    }
                }
                
                SaveCurrentStateAsPreset(NewPresetName, FieldsForPresetSelection.Where(f => f.IsSelected));

                _presetToUpdateName = null;
                IsSavePresetPopupOpen = false;
                NewPresetName = string.Empty;

                LoadPresets();
                PresetsModified?.Invoke();

            }, _ => !string.IsNullOrWhiteSpace(NewPresetName));

            DeletePresetCommand = new RelayCommand(param =>
            {
                if (param is string presetName)
                {
                    var message = string.Format(LocalizationService.Instance["Presets_DeleteConfirmMessage"], presetName);
                    var caption = LocalizationService.Instance["Presets_DeleteConfirmTitle"];
                    
                    if (MessageBox.Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    {
                        var presetVM = Presets.FirstOrDefault(p => p.Name == presetName);
                        if (presetVM != null)
                        {
                            DeletePreset(presetVM);
                        }
                    }
                }
            });
            
            ApplyPresetCommand = new RelayCommand(param =>
            {
                if (param is GroupPresetViewModel presetVM)
                {
                    ApplyPreset(presetVM);
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
        
        private void ExportPresets(object obj)
        {
            if (!_workflow.Presets.TryGetValue(Id, out var presets) || !presets.Any())
            {
                MessageBox.Show(LocalizationService.Instance["Presets_ExportError_NoPresets"], "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // --- START OF CHANGE: Create a more detailed, human-readable export structure ---
            var exportablePresets = new List<object>();
            var allFieldsInGroup = this.Tabs.SelectMany(t => t.Fields).ToList();

            foreach (var preset in presets)
            {
                var exportableFields = new List<object>();
                foreach (var valuePair in preset.Values)
                {
                    var fieldVm = allFieldsInGroup.FirstOrDefault(f => f.Path == valuePair.Key);
                    exportableFields.Add(new
                    {
                        name = fieldVm?.Name ?? "Unknown Field", // Include the field name
                        path = valuePair.Key,
                        value = valuePair.Value
                    });
                }
                
                exportablePresets.Add(new
                {
                    name = preset.Name,
                    fields = exportableFields
                });
            }
            // --- END OF CHANGE ---

            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                FileName = $"{Name}_presets.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Serialize the new structure
                    var json = JsonConvert.SerializeObject(exportablePresets, Formatting.Indented);
                    File.WriteAllText(dialog.FileName, json);
                    MessageBox.Show(
                        string.Format(LocalizationService.Instance["Presets_ExportSuccessMessage"], Name),
                        LocalizationService.Instance["Presets_ExportSuccessTitle"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
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
                    
                    var tempJArray = JArray.Parse(json);
                    var importedPresets = new List<GroupPreset>();

                    foreach (var token in tempJArray)
                    {
                        var presetName = token["name"]?.ToString();
                        var fields = token["fields"] as JArray;

                        if (string.IsNullOrEmpty(presetName) || fields == null) continue;

                        var newPreset = new GroupPreset { Name = presetName };
                        foreach (var fieldToken in fields)
                        {
                            var path = fieldToken["path"]?.ToString();
                            var value = fieldToken["value"];

                            if (!string.IsNullOrEmpty(path) && value != null)
                            {
                                newPreset.Values[path] = value;
                            }
                        }
                        importedPresets.Add(newPreset);
                    }


                    if (importedPresets == null || !importedPresets.Any())
                    {
                        throw new Exception("File is empty or has an invalid format.");
                    }

                    var confirmResult = MessageBox.Show(
                        string.Format(LocalizationService.Instance["Presets_ImportConfirmMessage"], Name),
                        LocalizationService.Instance["Presets_ImportConfirmTitle"],
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (confirmResult != MessageBoxResult.Yes) return;
                    
                    _workflow.Presets[Id] = importedPresets;
                    
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
        private void LoadPresets()
        {
            Presets.Clear();
            if (_workflow.Presets.TryGetValue(Id, out var presets))
            {
                foreach (var preset in presets.OrderBy(p => p.Name))
                {
                    Presets.Add(new GroupPresetViewModel(preset));
                }
            }
            // CHANGE: Notify that the names list has been updated
            OnPropertyChanged(nameof(PresetNames));
        }

        public void ApplyPreset(GroupPresetViewModel presetVM)
        {
            _isApplyingPreset = true; // Set flag at the beginning
            try
            {
                var changedFields = new List<InputFieldViewModel>();
                
                if (SelectedPreset != presetVM)
                {
                    SelectedPreset = presetVM;
                }
                
                foreach (var valuePair in presetVM.Model.Values)
                {
                    var fieldPath = valuePair.Key;
                    var presetValue = valuePair.Value;

                    var fieldVM = Tabs.SelectMany(t => t.Fields).FirstOrDefault(f => f.Path == fieldPath);
                    if (fieldVM == null) continue;

                    bool valueChanged = false;
                    if (fieldVM is MarkdownFieldViewModel markdownVm)
                    {
                        if (markdownVm.Value != presetValue.ToString())
                        {
                            markdownVm.Value = presetValue.ToString();
                            valueChanged = true;
                        }
                    }
                    else
                    {
                        var prop = _workflow.GetPropertyByPath(fieldPath);
                        if (prop != null && !Utils.AreJTokensEquivalent(prop.Value, presetValue))
                        {
                            prop.Value = presetValue.DeepClone();
                            (fieldVM as dynamic)?.RefreshValue();
                            valueChanged = true;
                        }
                    }

                    if (valueChanged)
                    {
                        changedFields.Add(fieldVM);
                    }
                }

                // english: Trigger highlight effect if any fields were changed. This is a fire-and-forget async void method,
                // which is acceptable here for a purely visual effect that doesn't need to be awaited.
                if (changedFields.Any())
                {
                    _fieldsPendingHighlight.AddRange(changedFields);
                    // Trigger for the currently active tab immediately
                    if (SelectedTab != null)
                    {
                        TriggerPendingHighlightsForTab(SelectedTab);
                    }
                }
            }
            finally
            {
                _isApplyingPreset = false; // Unset flag at the end
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

        private void SaveCurrentStateAsPreset(string name, IEnumerable<PresetFieldSelectionViewModel> selectedFields)
        {
            var newPreset = new GroupPreset { Name = name };
            var selectedFieldPaths = new HashSet<string>(selectedFields.Select(f => f.Path));
            
            foreach (var field in Tabs.SelectMany(t => t.Fields))
            {
                if (!selectedFieldPaths.Contains(field.Path))
                {
                    continue;
                }
                
                if (field is MarkdownFieldViewModel markdownVm)
                {
                    newPreset.Values[field.Path] = new JValue(markdownVm.Value);
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

            if (!_workflow.Presets.ContainsKey(Id))
            {
                _workflow.Presets[Id] = new List<GroupPreset>();
            }
            
            _workflow.Presets[Id].RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            _workflow.Presets[Id].Add(newPreset);
            
            LoadPresets();
            
            PresetsModified?.Invoke();

            var newlySavedPreset = Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (newlySavedPreset != null)
            {
                SelectedPreset = newlySavedPreset;
            }
        }

        private void DeletePreset(GroupPresetViewModel presetVM)
        {
            if (_workflow.Presets.TryGetValue(Id, out var presets))
            {
                presets.Remove(presetVM.Model);
                Presets.Remove(presetVM);
                PresetsModified?.Invoke();
                // CHANGE: Notify that the names list has been updated
                OnPropertyChanged(nameof(PresetNames));
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
        public SeedFieldViewModel(WorkflowField field, JProperty property, string nodeTitle = null, string nodeType = null) : base(field, property, nodeTitle, nodeType)
        {
            Type = FieldType.Seed;
            _field = field;
        }
        
        public override void RefreshValue()
        {
            OnPropertyChanged(nameof(Value));
        }
    }

    public class SliderFieldViewModel : InputFieldViewModel
    {
        // ========================================================== //
        //     НАЧАЛО ИЗМЕНЕНИЯ: Логика "привязки к шагу" в сеттере    //
        // ========================================================== //
        public double Value
        {
            get => Property.Value.ToObject<double>();
            set
            {
                // 1. Вычисляем "привязанное" к шагу значение
                var step = StepValue;
                if (step <= 0) step = 1e-9; // Защита от деления на ноль
                var snappedValue = System.Math.Round((value - MinValue) / step) * step + MinValue;

                // 2. Применяем округление в зависимости от типа (целое или с плавающей точкой)
                if (Type == FieldType.SliderInt)
                {
                    snappedValue = (long)System.Math.Round(snappedValue);
                }
                else // SliderFloat
                {
                    var precision = _field.Precision ?? 2;
                    snappedValue = System.Math.Round(snappedValue, precision);
                }
                
                // 3. Убедимся, что значение не вышло за пределы
                if (snappedValue < MinValue) snappedValue = MinValue;
                if (snappedValue > MaxValue) snappedValue = MaxValue;

                // 4. Обновляем JObject и уведомляем UI, только если значение действительно изменилось.
                // Это предотвращает бесконечные циклы обновлений.
                if (!JToken.DeepEquals(Property.Value, new JValue(snappedValue)))
                {
                    Property.Value = new JValue(snappedValue);
                    
                    // Уведомляем UI, что и сырое значение, и отформатированный текст изменились.
                    // WPF автоматически обновит положение слайдера до "привязанного" значения.
                    OnPropertyChanged(nameof(Value));
                    OnPropertyChanged(nameof(FormattedValue));
                }
            }
        }
        // ========================================================== //
        //     КОНЕЦ ИЗМЕНЕНИЯ                                        //
        // ========================================================== //

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
