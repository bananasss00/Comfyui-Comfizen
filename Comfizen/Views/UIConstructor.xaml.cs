using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using Formatting = Newtonsoft.Json.Formatting;

namespace Comfizen
{
    public enum AutoLayoutSortStrategy
    {
        ApiOrder,
        Alphabetical,
        ByType
    }
    
    public class BindingProxy : Freezable
    {
        protected override Freezable CreateInstanceCore()
        {
            return new BindingProxy();
        }

        public object Data
        {
            get { return GetValue(DataProperty); }
            set { SetValue(DataProperty, value); }
        }

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register("Data", typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));
    }
    
    /// <summary>
    /// Helper class to represent a color in the palette.
    /// </summary>
    public class ColorInfo
    {
        public string Name { get; set; }
        public string Hex { get; set; }
    }
    
    [AddINotifyPropertyChangedInterface]
    public class ActionNameViewModel : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public bool IsRenaming { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;

        public ActionNameViewModel(string name)
        {
            Name = name;
        }
    }
    
    // --- START OF CHANGES: New class to hold completion data ---
    /// <summary>
    /// Implements ICompletionData for the AvalonEdit completion window.
    /// It holds information about a single completion item.
    /// </summary>
    public class ScriptingCompletionData : ICompletionData
    {
        public ScriptingCompletionData(MemberInfo member)
        {
            Text = member.Name;
            
            if (member is MethodInfo method)
            {
                var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                Description = $"Method: {method.ReturnType.Name} {method.Name}({parameters})";
            }
            else if (member is PropertyInfo property)
            {
                Description = $"Property: {property.PropertyType.Name} {property.Name}";
            }
            else
            {
                Description = "Member: " + member.Name;
            }
        }

        public ImageSource Image => null;
        public string Text { get; }
        public object Content => Text;
        public object Description { get; }
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs e)
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }
    
    public class NodeInfo : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;
        
        public override string ToString()
        {
            return $"{Title} ({Id})";
        }
    }
    
    /// <summary>
    /// ViewModel for the UIConstructor window.
    /// Handles the logic for creating and modifying workflow UI layouts.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class UIConstructorView : INotifyPropertyChanged
    {
        private object _itemBeingRenamed;
        private readonly SessionManager _sessionManager;
        private readonly ModelService _modelService;
        public readonly AppSettings _settings;
        public bool UseNodeTitlePrefix { get; set; }
        private readonly SliderDefaultsService _sliderDefaultsService;
        public IHighlightingDefinition PythonSyntaxHighlighting { get; }
        public ObservableCollection<string> ModelSubTypes { get; } = new();
        private bool _apiWasReplaced = false;

        /// <summary>
        /// Master list of all group ViewModels for the current workflow.
        /// </summary>
        public ObservableCollection<WorkflowGroupViewModel> AllGroupViewModels { get; } = new();

        public ObservableCollection<WorkflowGroupViewModel> UnassignedGroups { get; } = new();
        public ObservableCollection<WorkflowGroupViewModel> SelectedTabGroups { get; } = new();

        // --- SCRIPTING PROPERTIES ---
        public ObservableCollection<string> AvailableHooks { get; }
        public string SelectedHookName { get; set; }
        public TextDocument SelectedHookScript { get; set; } = new TextDocument();

        public ObservableCollection<ActionNameViewModel> ActionNames { get; } = new ObservableCollection<ActionNameViewModel>();
        public ActionNameViewModel SelectedActionName { get; set; }
        public TextDocument SelectedActionScript { get; set; } = new TextDocument();
        
        public ICommand AddActionCommand { get; }
        public ICommand RemoveActionCommand { get; }
        
        private string _originalActionName; // For storing the name before renaming.
        
        public ICommand TestScriptCommand { get; }
        // --- END OF SCRIPTING PROPERTIES ---
        
        public ICommand AddMarkdownFieldCommand { get; }
        public ICommand AddScriptButtonFieldCommand { get; }
        public ICommand AddNodeBypassFieldCommand { get; }
        public ICommand AddLabelFieldCommand { get; }
        public ICommand AddSeparatorFieldCommand { get; }
        public ICommand AddSpoilerFieldCommand { get; }
        public ICommand AddSpoilerEndFieldCommand { get; }
        
        public ICommand AttachFullWorkflowCommand { get; }
        public ICommand ExportAttachedWorkflowCommand { get; }
        public ICommand RemoveAttachedWorkflowCommand { get; }
        
        // Constructor for a NEW workflow.
        public UIConstructorView() : this(new Workflow(), null) { }

        // Constructor for EDITING from a file path.
        public UIConstructorView(string? workflowRelativePath) 
            : this(LoadWorkflowFromFile(workflowRelativePath), workflowRelativePath) { }
        
        // --- START OF NEW PROPERTIES FOR TABS ---

        private WorkflowTabDefinition _selectedTab;
        public WorkflowTabDefinition SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (_selectedTab != value)
                {
                    _selectedTab = value;
                    OnPropertyChanged(nameof(SelectedTab));
                    UpdateGroupAssignments();
                }
            }
        }
        
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        
        public ICommand AddTabCommand { get; }
        public ICommand RemoveTabCommand { get; }
        public ICommand AutoLayoutCommand { get; }
        public bool IsAutoLayoutPopupOpen { get; set; }
        public AutoLayoutSortStrategy AutoLayoutSortStrategy { get; set; } = AutoLayoutSortStrategy.ApiOrder;
        public bool AutoLayoutIncludeNodeId { get; set; } = true;
        public bool AutoLayoutStripPrefix { get; set; } = true;
        
        private List<object> _clipboard = new List<object>();
        private List<GroupPreset> _presetClipboard = new List<GroupPreset>();
        private bool _isCutOperation = false;

        public ICommand CutCommand { get; }
        public ICommand PasteCommand { get; }
        public ICommand CopyPresetsCommand { get; }
        public ICommand PastePresetsCommand { get; }
        public ICommand PastePresetsMergeCommand { get; }
        
        public object PopupTarget { get; set; }
        
        public ObservableCollection<SliderDefaultRule> SliderPresets { get; private set; }
        public ICommand ApplySliderPresetCommand { get; }
        
        // Property for the search text inside the popup
        public string SliderPresetSearchText { get; set; }
        public bool ShowAllSliderPresets { get; set; } = false;
        
        
        public UIConstructorView(Workflow liveWorkflow, string? workflowRelativePath)
        {
            // Assign the live workflow object directly.
            Workflow = liveWorkflow;
            
            // All readonly and get-only properties are initialized here, inside the constructor.
            PythonSyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Python-Dark") ?? HighlightingManager.Instance.GetDefinition("Python");
            
            var settingsService = SettingsService.Instance; // Use singleton
            _settings = settingsService.Settings;
            UseNodeTitlePrefix = _settings.UseNodeTitlePrefixInDesigner;
            _sliderDefaultsService = new SliderDefaultsService(_settings.SliderDefaults);
            SliderPresets = new ObservableCollection<SliderDefaultRule>(_sliderDefaultsService.AllRules);
            _sessionManager = new SessionManager(_settings);
            _modelService = new ModelService(_settings);

            liveWorkflow.Groups.CollectionChanged += OnWorkflowGroupsModelChanged;
            InitializeGroupViewModels();

            if (!liveWorkflow.IsLoaded && !liveWorkflow.Tabs.Any() && !liveWorkflow.Groups.Any())
            {
                // Create a default tab
                var defaultTab = new WorkflowTabDefinition { Name = LocalizationService.Instance["UIConstructor_NewTabDefaultName"] };
                liveWorkflow.Tabs.Add(defaultTab);

                // Create a default group
                var defaultGroup = new WorkflowGroup { Name = string.Format(LocalizationService.Instance["UIConstructor_NewGroupDefaultName"], 1) };
                
                var defaultGroupTab = new WorkflowGroupTab { Name = "Controls" };
                defaultGroup.Tabs.Add(defaultGroupTab);
                
                liveWorkflow.Groups.Add(defaultGroup);

                // Link the group to the tab
                defaultTab.GroupIds.Add(defaultGroup.Id);
                
                // Activate the newly created tab
                SelectedTab = defaultTab;
            }
            
            LoadCommand = new RelayCommand(_ => LoadApiWorkflow());
            SaveWorkflowCommand = new RelayCommand(param => SaveWorkflow(param as Window), 
                _ => !string.IsNullOrWhiteSpace(NewWorkflowName) && Workflow.IsLoaded);
            ExportApiWorkflowCommand = new RelayCommand(_ => ExportApiWorkflow(), _ => Workflow.IsLoaded);
            AddGroupCommand = new RelayCommand(_ => AddGroup());
            RemoveGroupCommand = new RelayCommand(param => RemoveGroup(param as WorkflowGroupViewModel));
            DeleteCommand = new RelayCommand(HandleDelete, CanDelete);
            RemoveFieldFromGroupCommand = new RelayCommand(param => RemoveField(param as WorkflowField));
            ToggleRenameCommand = new RelayCommand(ToggleRename);

            // --- Scripting initialization ---
            AvailableHooks = new ObservableCollection<string> { 
                "on_workflow_load", 
                "on_queue_start", 
                "on_before_prompt_queue",
                "on_output_received",
                "on_queue_finish",
                "on_batch_finished"
            };
            AddActionCommand = new RelayCommand(_ => AddNewAction());
            RemoveActionCommand = new RelayCommand(_ => RemoveSelectedAction(), _ => SelectedActionName != null);
            TestScriptCommand = new RelayCommand(
                _ => TestSelectedScript(),
                _ => SelectedActionName != null && !string.IsNullOrWhiteSpace(SelectedActionScript.Text)
            );
            AutoLayoutCommand = new RelayCommand(AutoLayout, _ => AvailableFields.Any());

            // --- Other initializations ---
            AddMarkdownFieldCommand = new RelayCommand(param => AddVirtualField(param as WorkflowGroupViewModel, FieldType.Markdown));
            AddScriptButtonFieldCommand = new RelayCommand(param => AddVirtualField(param as WorkflowGroupViewModel, FieldType.ScriptButton));
            AddNodeBypassFieldCommand = new RelayCommand(param => AddVirtualField(param as WorkflowGroupViewModel, FieldType.NodeBypass));
            AddLabelFieldCommand = new RelayCommand(param => AddVirtualField(param as WorkflowGroupViewModel, FieldType.Label));
            AddSeparatorFieldCommand = new RelayCommand(param => AddVirtualField(param as WorkflowGroupViewModel, FieldType.Separator));
            AddSpoilerFieldCommand = new RelayCommand(param => AddVirtualField(param as WorkflowGroupViewModel, FieldType.Spoiler));
            AddSpoilerEndFieldCommand = new RelayCommand(param => AddVirtualField(param as WorkflowGroupViewModel, FieldType.SpoilerEnd));
            
            AttachFullWorkflowCommand = new RelayCommand(AttachFullWorkflow);
            ExportAttachedWorkflowCommand = new RelayCommand(ExportAttachedWorkflow, _ => Workflow.AttachedFullWorkflow != null);
            RemoveAttachedWorkflowCommand = new RelayCommand(RemoveAttachedWorkflow, _ => Workflow.AttachedFullWorkflow != null);
            
            ColorPalette = new ObservableCollection<ColorInfo>
            {
                // --- Warm Tones (Reds, Oranges, Browns) ---
                new ColorInfo { Name = LocalizationService.Instance["Color_Red"], Hex = "#825A5A" },
                new ColorInfo { Name = LocalizationService.Instance["Color_Terracotta"], Hex = "#A2604A" },
                new ColorInfo { Name = LocalizationService.Instance["Color_Brown"], Hex = "#8B5A2B" },
                new ColorInfo { Name = LocalizationService.Instance["Color_Amber"], Hex = "#A97D34" },

                // --- Green Tones ---
                new ColorInfo { Name = LocalizationService.Instance["Color_Olive"], Hex = "#82825A" },
                new ColorInfo { Name = LocalizationService.Instance["Color_Green"], Hex = "#5A825A" },
                new ColorInfo { Name = LocalizationService.Instance["Color_Teal"], Hex = "#4A8C82" },

                // --- Cool Tones (Blues, Purples) ---
                new ColorInfo { Name = LocalizationService.Instance["Color_Blue"], Hex = "#4A6A8C" },
                new ColorInfo { Name = LocalizationService.Instance["Color_Indigo"], Hex = "#4F5B93" },
                new ColorInfo { Name = LocalizationService.Instance["Color_Purple"], Hex = "#5A5A82" },
                new ColorInfo { Name = LocalizationService.Instance["Color_Plum"], Hex = "#825A7B" },
    
                // --- Neutral Tone ---
                new ColorInfo { Name = LocalizationService.Instance["Color_Slate"], Hex = "#6C757D" }
            };
            SetHighlightColorCommand = new RelayCommand(param =>
            {
                if (param is object[] args && args.Length == 2)
                {
                    var target = args[0];
                    var colorHex = args[1] as string;
                    
                    if (target is ListBox listBox)
                    {
                        foreach (var item in listBox.SelectedItems.OfType<WorkflowField>())
                        {
                            item.HighlightColor = colorHex;
                        }
                    }
                    else if (target is WorkflowGroupViewModel groupVm) groupVm.HighlightColor = colorHex;
                    else if (target is WorkflowGroupTabViewModel tabVm) tabVm.HighlightColor = colorHex;
                    else if (target is WorkflowField field) field.HighlightColor = colorHex;
                }
            });
            ClearHighlightColorCommand = new RelayCommand(param =>
            {
                if (param is ListBox listBox)
                {
                    foreach (var item in listBox.SelectedItems.OfType<WorkflowField>())
                    {
                        item.HighlightColor = null;
                    }
                }
                else if (param is WorkflowGroupViewModel groupVm) groupVm.HighlightColor = null;
                else if (param is WorkflowGroupTabViewModel tabVm) tabVm.HighlightColor = null;
                else if (param is WorkflowField field) field.HighlightColor = null;
            });

            AddBypassNodeIdCommand = new RelayCommand(param =>
            {
                if (param is Tuple<object, object> tuple &&
                    tuple.Item1 is NodeInfo selectedNode &&
                    tuple.Item2 is WorkflowField field)
                {
                    if (!field.BypassNodeIds.Contains(selectedNode.Id))
                    {
                        field.BypassNodeIds.Add(selectedNode.Id);
                        // --- NEW: Notify the UI about the change ---
                        field.NotifyBypassNodeIdsChanged();
                    }
                }
            });
            
            ToggleNodePrefixCommand = new RelayCommand(ToggleNodePrefix);

            RemoveBypassNodeIdCommand = new RelayCommand(param =>
            {
                if (param is object[] args && args.Length == 2 && 
                    args[0] is WorkflowField field && 
                    args[1] is NodeInfo nodeToRemove)
                {
                    field.BypassNodeIds.Remove(nodeToRemove.Id);
                    // --- NEW: Notify the UI about the change ---
                    field.NotifyBypassNodeIdsChanged();
                }
            });
            
            ApplySliderPresetCommand = new RelayCommand(param =>
            {
                if (param is object[] args && 
                    args.Length == 2 &&
                    args[0] is WorkflowField field && 
                    args[1] is SliderDefaultRule rule)
                {
                    field.MinValue = rule.Min;
                    field.MaxValue = rule.Max;
                    field.StepValue = rule.Step;
                    
                    // Apply precision only if the field supports float AND the rule specifies it
                    if (field.Type == FieldType.SliderFloat && rule.Precision.HasValue)
                    {
                        field.Precision = rule.Precision;
                    }
                }
            });
            
            // Attach event handlers
            // this.PropertyChanged += (s, e) => {
            //     if (e.PropertyName == nameof(SelectedHookName)) OnSelectedHookChanged();
            //     if (e.PropertyName == nameof(SelectedActionName)) OnSelectedActionChanged();
            //     if (e.PropertyName == nameof(SearchFilter) || e.PropertyName == nameof(Workflow)) UpdateAvailableFields();
            // };
            SelectedHookScript.TextChanged += (s, e) => SaveHookScript();
            SelectedActionScript.TextChanged += (s, e) => SaveActionScript();
            
            AddTabCommand = new RelayCommand(_ => AddNewTab());
            RemoveTabCommand = new RelayCommand(param => RemoveTab(param as WorkflowTabDefinition));
            
            CutCommand = new RelayCommand(param => HandleCut(param), param => CanCut(param));
            PasteCommand = new RelayCommand(HandlePaste, _ => _clipboard.Any());
            
            CopyPresetsCommand = new RelayCommand(CopyPresets, param => param is WorkflowGroupViewModel groupVm && groupVm.AllPresets.Any()); // CHANGED: Presets -> AllPresets
            PastePresetsCommand = new RelayCommand(param => PastePresets(param, merge: false), param => param is WorkflowGroupViewModel && _presetClipboard.Any());
            PastePresetsMergeCommand = new RelayCommand(param => PastePresets(param, merge: true), param => param is WorkflowGroupViewModel && _presetClipboard.Any());

            // Attach event handlers
            this.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(SelectedHookName)) OnSelectedHookChanged();
                if (e.PropertyName == nameof(SelectedActionName)) OnSelectedActionChanged();
                // --- MODIFIED: Update group assignments only when workflow changes ---
                if (e.PropertyName == nameof(Workflow))
                {
                    UpdateGroupAssignments(); 
                }
                // --- MODIFIED: Update available fields when search filter or workflow changes ---
                if (e.PropertyName == nameof(SearchFilter) || e.PropertyName == nameof(Workflow))
                {
                    UpdateAvailableFields();
                }
            };
            
            // Load initial data
            LoadModelSubTypesAsync();
            if (!string.IsNullOrEmpty(workflowRelativePath))
            {
                NewWorkflowName = Path.ChangeExtension(workflowRelativePath, null);
                UpdateAvailableFields();
                UpdateWorkflowNodesList();
                ValidateFieldPaths();
                UpdateExistingFieldMetadata();
                RefreshActionNames();
                // --- ADDED: Initialize tabs and groups ---
                UpdateGroupAssignments();
                SelectedTab = Workflow.Tabs.FirstOrDefault();
                // ---
            }
            
            foreach (var group in Workflow.Groups)
            {
                group.Tabs.CollectionChanged += OnGroupSubTabsChanged;
                foreach (var tab in group.Tabs)
                {
                    foreach (var field in tab.Fields)
                    {
                        field.PropertyChanged += OnFieldPropertyChanged;
                    }
                }
            }
        }
        
        // english: Constructor for importing a raw ComfyUI API JSON object.
        /// <param name="apiJson">The JObject of the API to import.</param>
        /// <param name="workflowName">The default name for the new workflow, typically from the filename.</param>
        public UIConstructorView(JObject apiJson, string workflowName) : this()
        {
            // english: Load the provided API and set the name.
            Workflow.LoadApiWorkflow(apiJson);
            NewWorkflowName = workflowName;
            _apiWasReplaced = true;
    
            // english: Update the UI with the new API data.
            UpdateAvailableFields();
            UpdateWorkflowNodesList();
            ValidateFieldPaths();
            RefreshActionNames();
            UpdateGroupAssignments();
            SelectedTab = Workflow.Tabs.FirstOrDefault();
            RefreshBypassNodeFields();
        }
        
        // The Workflow property is now set in the constructor.
        public Workflow Workflow { get; private set; }
        public ICommand LoadCommand { get; }
        public ICommand SaveWorkflowCommand { get; }
        public ICommand ExportApiWorkflowCommand { get; }
        public ICommand AddGroupCommand { get; }
        public ICommand RemoveGroupCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand RemoveFieldFromGroupCommand { get; }
        public ICommand ToggleRenameCommand { get; }
        public ICommand SetHighlightColorCommand { get; }
        public ICommand ClearHighlightColorCommand { get; }
        public ICommand RemoveBypassNodeIdCommand { get; }
        public ICommand AddBypassNodeIdCommand { get; }
        public ICommand ToggleNodePrefixCommand { get; }
        public ObservableCollection<ColorInfo> ColorPalette { get; }

        public string NewWorkflowName { get; set; }
        public string SearchFilter { get; set; }

        public ObservableCollection<WorkflowField> AvailableFields { get; } = new();
        public ObservableCollection<NodeInfo> WorkflowNodes { get; } = new();

        public ObservableCollection<FieldType> FieldTypes { get; } =
            new(Enum.GetValues(typeof(FieldType)).Cast<FieldType>()
                .Where(t => t != FieldType.Markdown && t != FieldType.ScriptButton && t != FieldType.NodeBypass
                            && t != FieldType.Label && t != FieldType.Separator && t != FieldType.Spoiler && t != FieldType.SpoilerEnd));

        public event PropertyChangedEventHandler? PropertyChanged;
        
        private void ExportAttachedWorkflow(object obj)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "ComfyUI Workflow (*.json)|*.json",
                FileName = Workflow.AttachedFullWorkflowName ?? "workflow.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var jsonContent = Workflow.AttachedFullWorkflow.ToString(Formatting.Indented);
                    File.WriteAllText(dialog.FileName, jsonContent);
                    Logger.LogToConsole($"Exported attached workflow to '{dialog.FileName}'.");
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Failed to export attached workflow.");
                }
            }
        }

        private void RemoveAttachedWorkflow(object obj)
        {
            if (MessageBox.Show(LocalizationService.Instance["UIConstructor_ConfirmAttachmentRemovalMessage"], 
                    LocalizationService.Instance["UIConstructor_ConfirmAttachmentRemovalTitle"], 
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                Workflow.AttachedFullWorkflow = null;
                Workflow.AttachedFullWorkflowName = null;
            }
        }

        private void AttachFullWorkflow(object obj)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "ComfyUI Workflow (*.json)|*.json|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var jsonContent = File.ReadAllText(dialog.FileName);
                    Workflow.AttachedFullWorkflow = JObject.Parse(jsonContent);
                    Workflow.AttachedFullWorkflowName = Path.GetFileName(dialog.FileName);
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Failed to attach workflow file.");
                    MessageBox.Show($"Error attaching file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private void InitializeGroupViewModels()
        {
            AllGroupViewModels.Clear();
            foreach (var groupModel in Workflow.Groups)
            {
                AllGroupViewModels.Add(new WorkflowGroupViewModel(groupModel, Workflow, _settings));
            }
            UpdateGroupAssignments();
        }
        
        private int GetFieldTypeSortRank(WorkflowField field)
        {
            // If it has a specific UI type, use that.
            switch (field.Type)
            {
                case FieldType.NodeBypass: return 0;
                case FieldType.Seed: return 1;
                case FieldType.SliderInt: return 10;
                case FieldType.SliderFloat: return 11;
                case FieldType.Model: return 20;
                case FieldType.Sampler: return 21;
                case FieldType.Scheduler: return 22;
                case FieldType.ComboBox: return 23;
                case FieldType.FilePath: return 30; // Path strings
                case FieldType.WildcardSupportPrompt: return 31; // Prompt strings
                // Any is special, we need to check the actual data type.
                case FieldType.Any:
                    var prop = Workflow.GetPropertyByPath(field.Path);
                    if (prop != null)
                    {
                        switch (prop.Value.Type)
                        {
                            case JTokenType.Boolean: return 0;
                            case JTokenType.Integer: return 10;
                            case JTokenType.Float: return 11;
                            default: return 32; // Other strings
                        }
                    }
                    return 100; // Fallback for 'Any' without property
                default:
                    return 100; // Other/virtual types at the end.
            }
        }

        private void AutoLayout(object obj)
        {
            if (!AvailableFields.Any()) return;

            var createdGroupNames = new HashSet<string>(AllGroupViewModels.Select(g => g.Name));

            var fieldsByNodeId = AvailableFields
                .Where(f => !string.IsNullOrEmpty(f.Path) && !f.Path.StartsWith("virtual_"))
                .GroupBy(f => f.NodeId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var group in fieldsByNodeId)
            {
                var nodeId = group.Key;
                var fields = group.Value;
                var firstField = fields.First();
                var nodeTitle = firstField.NodeTitle;

                string groupName;
                if (AutoLayoutIncludeNodeId)
                {
                    groupName = $"{nodeTitle} [{nodeId}]";
                }
                else
                {
                    groupName = nodeTitle;
                    int counter = 2;
                    while (createdGroupNames.Contains(groupName))
                    {
                        groupName = $"{nodeTitle} ({counter++})";
                    }
                }
                createdGroupNames.Add(groupName);

                var groupVm = AllGroupViewModels.FirstOrDefault(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
                if (groupVm == null)
                {
                    var newGroupModel = new WorkflowGroup { Name = groupName };
                    var defaultTab = new WorkflowGroupTab { Name = "Controls" };
                    newGroupModel.Tabs.Add(defaultTab);
                    Workflow.Groups.Add(newGroupModel);
                    groupVm = AllGroupViewModels.Last();
                    
                    if (SelectedTab != null && !SelectedTab.GroupIds.Contains(groupVm.Id))
                    {
                        SelectedTab.GroupIds.Add(groupVm.Id);
                    }
                }
                
                var targetTab = groupVm.Model.Tabs.FirstOrDefault() ?? groupVm.Model.Tabs.First();
                
                IEnumerable<WorkflowField> sortedFields = fields;
                switch (AutoLayoutSortStrategy)
                {
                    case AutoLayoutSortStrategy.Alphabetical:
                        sortedFields = fields.OrderBy(f => f.Name);
                        break;
                    case AutoLayoutSortStrategy.ByType:
                        sortedFields = fields.OrderBy(f => GetFieldTypeSortRank(f)).ThenBy(f => f.Name);
                        break;
                }

                foreach (var field in sortedFields)
                {
                    var fieldToAdd = field.Clone(); // Create a safe copy to modify
                    if (AutoLayoutStripPrefix && fieldToAdd.Name.Contains("::"))
                    {
                        fieldToAdd.Name = fieldToAdd.Name.Split(new[] { "::" }, 2, StringSplitOptions.None)[1];
                    }
                    AddFieldToSubTabAtIndex(fieldToAdd, targetTab);
                }
            }
            
            UpdateGroupAssignments();
            UpdateAvailableFields();
            IsAutoLayoutPopupOpen = false;
        }
        
        private void ToggleNodePrefix(object param)
        {
            if (param is not WorkflowField field) return;
            if (string.IsNullOrEmpty(field.NodeTitle)) return;

            string prefix = $"{field.NodeTitle}::";
            if (field.Name.StartsWith(prefix))
            {
                // Remove prefix
                field.Name = field.Name.Substring(prefix.Length);
            }
            else
            {
                // Add prefix
                field.Name = $"{prefix}{field.Name}";
            }
        }
        
        private bool CanDelete(object parameter)
        {
            return parameter is ListBox lb && lb.SelectedItems.Count > 0 ||
                   parameter is WorkflowField ||
                   parameter is WorkflowGroupViewModel ||
                   parameter is WorkflowGroupTabViewModel;
        }

        private void HandleDelete(object parameter)
        {
            if (parameter is ListBox listBox) // Handles single and multi-selection of fields
            {
                // ToList() is crucial to avoid modifying the collection while iterating
                var fieldsToDelete = listBox.SelectedItems.OfType<WorkflowField>().ToList();
                if (fieldsToDelete.Any() && MessageBox.Show(
                        string.Format(LocalizationService.Instance["UIConstructor_ConfirmDeleteFieldsMessage"], fieldsToDelete.Count),
                        LocalizationService.Instance["UIConstructor_ConfirmDeleteTitle"],
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    foreach (var field in fieldsToDelete)
                    {
                        RemoveField(field);
                    }
                }
            }
            else if (parameter is WorkflowField field) // Fallback for a single field
            {
                if (MessageBox.Show(
                        string.Format(LocalizationService.Instance["UIConstructor_ConfirmDeleteFieldMessage"], field.Name),
                        LocalizationService.Instance["UIConstructor_ConfirmDeleteTitle"],
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    RemoveField(field);
                }
            }
            else if (parameter is WorkflowGroupViewModel groupVm)
            {
                // The RemoveGroup method already contains its own confirmation dialog.
                RemoveGroup(groupVm);
            }
            else if (parameter is WorkflowGroupTabViewModel subTabVm)
            {
                RemoveSubTab(subTabVm);
            }
        }

        private void RemoveSubTab(WorkflowGroupTabViewModel subTabVm)
        {
            if (subTabVm == null) return;

            // Find the owner group of this sub-tab
            var ownerVm = AllGroupViewModels.FirstOrDefault(g => g.Tabs.Contains(subTabVm));
            if (ownerVm == null) return;
            
            ownerVm.RemoveTab(subTabVm);
        }
        
        private void CopyPresets(object parameter)
        {
            if (parameter is not WorkflowGroupViewModel groupVm) return;

            _presetClipboard.Clear();
            if (Workflow.Presets.TryGetValue(groupVm.Id, out var presetsToCopy))
            {
                // We clone the presets to ensure they are independent copies.
                _presetClipboard.AddRange(presetsToCopy.Select(p => p.Clone()));
            }
        }
        
        private void PastePresets(object parameter, bool merge)
        {
            if (parameter is not WorkflowGroupViewModel targetGroupVm || !_presetClipboard.Any()) return;

            if (!Workflow.Presets.ContainsKey(targetGroupVm.Id))
            {
                Workflow.Presets[targetGroupVm.Id] = new List<GroupPreset>();
            }

            var targetPresets = Workflow.Presets[targetGroupVm.Id];

            foreach (var presetToPaste in _presetClipboard)
            {
                var existingPreset = targetPresets.FirstOrDefault(p => p.Name.Equals(presetToPaste.Name, StringComparison.OrdinalIgnoreCase));

                if (existingPreset != null && merge)
                {
                    // Merge logic: update existing preset with new values
                    foreach (var valuePair in presetToPaste.Values)
                    {
                        existingPreset.Values[valuePair.Key] = valuePair.Value.DeepClone();
                    }
                }
                else
                {
                    // Overwrite logic (or add if it doesn't exist)
                    if (existingPreset != null)
                    {
                        targetPresets.Remove(existingPreset);
                    }
                    targetPresets.Add(presetToPaste.Clone());
                }
            }

            // Find the corresponding group view model and reload its presets to update the UI.
            var groupVmToUpdate = AllGroupViewModels.FirstOrDefault(vm => vm.Model == targetGroupVm.Model);
            if (groupVmToUpdate != null)
            {
                // This will trigger a save notification.
                groupVmToUpdate.ReloadPresetsAndNotify();
            }
        }
        
        private bool CanCut(object parameter)
        {
            if (parameter is ListBox listBox)
            {
                return listBox.SelectedItems.Count > 0;
            }
            if (parameter is WorkflowGroupTabViewModel)
            {
                return true;
            }
            return false;
        }

        private void HandleCut(object parameter)
        {
            _clipboard.Clear();
            _isCutOperation = false; // Reset

            if (parameter is ListBox listBox && listBox.SelectedItems.Count > 0)
            {
                _clipboard.AddRange(listBox.SelectedItems.OfType<WorkflowField>().Cast<object>());
            _isCutOperation = true;
        }
            else if (parameter is WorkflowGroupTabViewModel subTabVm)
            {
                _clipboard.Add(subTabVm);
                _isCutOperation = true;

                // Remove from the source group to complete the "cut" operation
                var sourceGroupVm = AllGroupViewModels.FirstOrDefault(g => g.Tabs.Contains(subTabVm));
                if (sourceGroupVm != null)
                {
                    // Remove from both the ViewModel and the Model to ensure UI and data are in sync
                    sourceGroupVm.Tabs.Remove(subTabVm);
                    sourceGroupVm.Model.Tabs.Remove(subTabVm.Model);
                }
            }
        }

        private void HandlePaste(object target)
        {
            if (!_clipboard.Any()) return;

            var firstItem = _clipboard.First();
            
            if (firstItem is WorkflowField)
            {
                var fieldsToPaste = _clipboard.OfType<WorkflowField>().ToList();
                if (!fieldsToPaste.Any()) return;

            WorkflowGroupViewModel targetGroupVm = null;
            WorkflowGroupTab targetSubTab = null;
                int insertIndex = -1; // Default to the end

            if (target is ListBox listBox && listBox.SelectedItem is WorkflowField targetField)
            {
                targetGroupVm = AllGroupViewModels.FirstOrDefault(g => g.Model.Tabs.Any(t => t.Fields.Contains(targetField)));
                if (targetGroupVm != null)
                {
                    targetSubTab = targetGroupVm.Model.Tabs.FirstOrDefault(t => t.Fields.Contains(targetField));
                    if (targetSubTab != null)
                    {
                        insertIndex = targetSubTab.Fields.IndexOf(targetField) + 1;
                    }
                }
            }
            else if (target is WorkflowGroupViewModel groupVm)
            {
                targetGroupVm = groupVm;
                targetSubTab = groupVm.SelectedTab?.Model ?? groupVm.Model.Tabs.FirstOrDefault();
            }
            else if (target is WorkflowGroupTabViewModel subTabVm)
            {
                targetSubTab = subTabVm.Model;
                targetGroupVm = AllGroupViewModels.FirstOrDefault(g => g.Tabs.Any(t => t.Model == targetSubTab));
            }
            else if (target is WorkflowTabDefinition tabDef)
            {
                targetGroupVm = SelectedTabGroups.FirstOrDefault();
                if (targetGroupVm == null)
                {
                    AddGroup();
                    targetGroupVm = SelectedTabGroups.FirstOrDefault();
                }

                if (targetGroupVm != null)
                {
                    targetSubTab = targetGroupVm.SelectedTab?.Model ?? targetGroupVm.Model.Tabs.FirstOrDefault();
                }
            }


            if (targetSubTab == null)
            {
                if (targetGroupVm != null)
                {
                    targetSubTab = new WorkflowGroupTab { Name = "Controls" };
                    targetGroupVm.Model.Tabs.Add(targetSubTab);
                    targetGroupVm.Tabs.Add(new WorkflowGroupTabViewModel(targetSubTab));
                    targetGroupVm.SelectedTab = targetGroupVm.Tabs.Last();
                }
                else return;
            }

                foreach (var field in fieldsToPaste)
            {
                if (_isCutOperation)
                {
                        RemoveField(field); // Remove from the original location
                }

                    var fieldClone = field.Clone();
                fieldClone.PropertyChanged += OnFieldPropertyChanged;

                if (insertIndex == -1 || insertIndex > targetSubTab.Fields.Count)
                {
                        targetSubTab.Fields.Add(fieldClone);
                    }
                else
                {
                    targetSubTab.Fields.Insert(insertIndex, fieldClone);
                        insertIndex++; // Increment index for the next item
                }
            }

            if (_isCutOperation)
            {
                    _clipboard.Clear();
            }
            _isCutOperation = false;
            UpdateAvailableFields();
        }
            else if (firstItem is WorkflowGroupTabViewModel draggedTabVm)
            {
                WorkflowGroupViewModel targetGroupVm = null;
                int insertIndex = -1; // Default to the end

                if (target is WorkflowGroupViewModel groupVm)
                {
                    targetGroupVm = groupVm;
                }
                else if (target is WorkflowGroupTabViewModel targetTabVm)
                {
                    // Find the owner group of the target tab
                    targetGroupVm = AllGroupViewModels.FirstOrDefault(g => g.Tabs.Contains(targetTabVm));
                    if (targetGroupVm != null)
                    {
                        insertIndex = targetGroupVm.Tabs.IndexOf(targetTabVm) + 1;
                    }
                }
        
                if (targetGroupVm != null)
                {
                    // Insert into the ViewModel collection (for UI)
                    if (insertIndex < 0 || insertIndex > targetGroupVm.Tabs.Count)
                    {
                        targetGroupVm.Tabs.Add(draggedTabVm);
                    }
                    else
                    {
                        targetGroupVm.Tabs.Insert(insertIndex, draggedTabVm);
                    }

                    // Insert into the Model collection (for saving)
                    if (insertIndex < 0 || insertIndex > targetGroupVm.Model.Tabs.Count)
                    {
                        targetGroupVm.Model.Tabs.Add(draggedTabVm.Model);
                    }
                    else
                    {
                        targetGroupVm.Model.Tabs.Insert(insertIndex, draggedTabVm.Model);
                    }

                    if (_isCutOperation)
                    {
                        _clipboard.Clear();
                    }
                    _isCutOperation = false;
                }
            }
        }
        
        public void AddFieldToGroupAtIndex(IList<WorkflowField> fields, WorkflowGroupViewModel groupVm, int targetIndex = -1)
        {
            if (groupVm == null) return;

            var targetTabVm = groupVm.SelectedTab ?? groupVm.Tabs.FirstOrDefault();
            if (targetTabVm == null)
            {
                var newTabModel = new WorkflowGroupTab { Name = "Controls" };
                groupVm.Model.Tabs.Add(newTabModel);
                targetTabVm = new WorkflowGroupTabViewModel(newTabModel);
                groupVm.Tabs.Add(targetTabVm);
            }
            
            foreach (var field in fields)
            {
                var fieldToAdd = field.Clone(); // Create a safe copy
                
                // For manual drag-drop, use the global setting
                if (!this.UseNodeTitlePrefix && fieldToAdd.Name.Contains("::"))
                {
                    fieldToAdd.Name = fieldToAdd.Name.Split(new[] { "::" }, 2, StringSplitOptions.None)[1];
                }

                AddFieldToSubTabAtIndex(fieldToAdd, targetTabVm.Model, targetIndex);
                if (targetIndex != -1) targetIndex++;
            }
        }

        private void OnWorkflowGroupsModelChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Keep the ViewModel collection in sync with the Model collection
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                foreach (WorkflowGroup newGroup in e.NewItems)
                {
                    AllGroupViewModels.Add(new WorkflowGroupViewModel(newGroup, Workflow, _settings));
                    // Subscribe to sub-tab changes for the new group
                    newGroup.Tabs.CollectionChanged += OnGroupSubTabsChanged;
                }
            }
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
            {
                foreach (WorkflowGroup oldGroup in e.OldItems)
                {
                    // Unsubscribe from sub-tab changes to prevent memory leaks
                    oldGroup.Tabs.CollectionChanged -= OnGroupSubTabsChanged;
                    
                    var vmToRemove = AllGroupViewModels.FirstOrDefault(vm => vm.Model == oldGroup);
                    if (vmToRemove != null)
                    {
                        AllGroupViewModels.Remove(vmToRemove);
                    }
                }
            }
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                AllGroupViewModels.Clear();
            }

            // Always refresh the UI assignments
            UpdateGroupAssignments();
        }
        
        /// <summary>
        /// Handles changes to a group's sub-tab collection.
        /// This is the key fix for updating available fields when a sub-tab is removed.
        /// </summary>
        private void OnGroupSubTabsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // When a sub-tab is removed from a group...
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
            {
                // ...we need to find its fields and unsubscribe from their events to prevent memory leaks.
                foreach (WorkflowGroupTab removedTab in e.OldItems)
                {
                    foreach (var field in removedTab.Fields)
                    {
                        field.PropertyChanged -= OnFieldPropertyChanged;
                    }
                }
            }
    
            // Any change to the sub-tab collection could affect which fields are "used".
            // Refresh the list of available fields.
            UpdateAvailableFields();
        }

        // Helper method to load a workflow from a file path.
        private static Workflow LoadWorkflowFromFile(string? relativePath)
        {
            var workflow = new Workflow();
            if (!string.IsNullOrEmpty(relativePath))
            {
                var fullPath = Path.Combine(Comfizen.Workflow.WorkflowsDir, relativePath);
                if (File.Exists(fullPath))
                {
                    workflow.LoadWorkflow(fullPath);
                }
            }
            return workflow;
        }

        private void AddVirtualField(WorkflowGroupViewModel groupVm, FieldType type)
        {
            if (groupVm == null) return;

            var group = groupVm.Model;

            // Get the model of the active sub-tab from the ViewModel.
            var targetTab = groupVm.SelectedTab?.Model;

            // Fallback logic: if no sub-tab is selected or exists,
            // use the first one or create a new one.
            if (targetTab == null)
            {
                targetTab = group.Tabs.FirstOrDefault();
                if (targetTab == null)
                {
                    targetTab = new WorkflowGroupTab { Name = "Controls" };
                    group.Tabs.Add(targetTab);
                }
            }

            string baseName = "New Field";
            switch (type)
            {
                case FieldType.Markdown: baseName = "Markdown Block"; break;
                case FieldType.ScriptButton: baseName = "New Action Button"; break;
                case FieldType.NodeBypass: baseName = "New Bypass Switch"; break;
                case FieldType.Label: baseName = "New Label"; break;
                case FieldType.Separator: baseName = "Separator"; break;
                case FieldType.Spoiler: baseName = "New Spoiler"; break;
                case FieldType.SpoilerEnd: baseName = "Spoiler End"; break;
            }
            string newName = baseName;
            int counter = 1;
    
            // Ensure a unique name within the tab
            while (targetTab.Fields.Any(f => f.Name == newName))
            {
                newName = $"{baseName} {++counter}";
            }

            var newField = new WorkflowField
            {
                Name = newName,
                Type = type,
                Path = $"virtual_{type.ToString().ToLower()}_{Guid.NewGuid()}",
                IsSelected = true 
            };

            if (type == FieldType.Markdown)
            {
                newField.DefaultValue = "# " + newName + "\n\nEdit this text.";
            }
            
            var lastSelectedField = targetTab.Fields.LastOrDefault(f => f.IsSelected);
            
            if (lastSelectedField != null)
            {
                int index = targetTab.Fields.IndexOf(lastSelectedField);
                
                lastSelectedField.IsSelected = false;

                if (index >= 0 && index < targetTab.Fields.Count)
                {
                    targetTab.Fields.Insert(index + 1, newField);
                }
                else
                {
                    targetTab.Fields.Add(newField);
                }
            }
            else
            {
                targetTab.Fields.Add(newField);
            }
        }
        
        // --- SCRIPTING METHODS ---
        private void TestSelectedScript()
        {
            if (SelectedActionName == null || string.IsNullOrWhiteSpace(SelectedActionScript.Text))
                return;

            Logger.Log($"--- Testing script action: '{SelectedActionName.Name}' ---");

            // For testing, we provide mock actions that log their calls to the console.
            Action<JObject> testQueuePrompt = (prompt) =>
            {
                Logger.LogToConsole($"[Py Test] ctx.queue() was called.");
            };

            Action<string> testApplyGlobalPreset = (presetName) =>
            {
                Logger.LogToConsole($"[Py Test] ctx.apply_global_preset called with: '{presetName}'");
            };

            Action<string, string> testApplyGroupPreset = (groupName, presetName) =>
            {
                Logger.LogToConsole($"[Py Test] ctx.apply_group_preset called for group '{groupName}' with preset: '{presetName}'");
            };

            // Create a script context with the current workflow state and the mock actions.
            var context = new ScriptContext(
                Workflow.LoadedApi,
                new Dictionary<string, object>(), // State is empty for tests
                _settings,
                testQueuePrompt,
                testApplyGlobalPreset,
                testApplyGroupPreset,
                null // output is not available outside the on_output_received hook
            );

            PythonScriptingService.Instance.Execute(SelectedActionScript.Text, context);

            Logger.Log($"--- Test finished for: '{SelectedActionName.Name}' ---");
        }
        
        private void OnSelectedHookChanged()
        {
            if (Workflow.Scripts.Hooks.TryGetValue(SelectedHookName ?? "", out var script))
                SelectedHookScript.Text = script;
            else
                SelectedHookScript.Text = string.Empty;
        }

        private void SaveHookScript()
        {
            if (!string.IsNullOrEmpty(SelectedHookName))
                Workflow.Scripts.Hooks[SelectedHookName] = SelectedHookScript.Text;
        }

        private void OnSelectedActionChanged()
        {
            // Находим элемент, который В ДАННЫЙ МОМЕНТ находится в режиме редактирования
            var currentlyRenaming = ActionNames.FirstOrDefault(a => a.IsRenaming);

            // Если такой элемент есть, и это НЕ тот, который мы только что выбрали,
            // завершаем его редактирование.
            if (currentlyRenaming != null && currentlyRenaming != SelectedActionName)
            {
                CommitActionRename(currentlyRenaming, currentlyRenaming.Name);
            }

            // Обновляем редактор скриптов для нового выбранного элемента
            if (SelectedActionName != null && Workflow.Scripts.Actions.TryGetValue(SelectedActionName.Name, out var script))
                SelectedActionScript.Text = script;
            else
                SelectedActionScript.Text = string.Empty;
        }

        private void SaveActionScript()
        {
            if (SelectedActionName != null)
                Workflow.Scripts.Actions[SelectedActionName.Name] = SelectedActionScript.Text;
        }

        private void AddNewAction()
        {
            var baseName = LocalizationService.Instance["UIConstructor_NewActionDefaultName"];
            string newActionName = baseName;
            int counter = 1;
            while (ActionNames.Any(a => a.Name == newActionName))
            {
                newActionName = $"{baseName}_{counter++}";
            }

            var newActionVm = new ActionNameViewModel(newActionName);
            ActionNames.Add(newActionVm);
            Workflow.Scripts.Actions[newActionName] = $"# {LocalizationService.Instance["UIConstructor_PythonPlaceholder"]}";
            SelectedActionName = newActionVm;
        }

        private void RemoveSelectedAction()
        {
            if (SelectedActionName != null)
            {
                Workflow.Scripts.Actions.Remove(SelectedActionName.Name);
                ActionNames.Remove(SelectedActionName);
                SelectedActionName = ActionNames.FirstOrDefault();
            }
        }

        private void RefreshActionNames()
        {
            ActionNames.Clear();
            foreach (var key in Workflow.Scripts.Actions.Keys.OrderBy(k => k))
            {
                ActionNames.Add(new ActionNameViewModel(key));
            }
        }
        
        public void StartActionRename(ActionNameViewModel actionVm)
        {
            if (actionVm == null) return;

            // Закрываем редактирование других элементов перед началом нового
            var otherRenaming = ActionNames.FirstOrDefault(a => a.IsRenaming);
            if (otherRenaming != null)
            {
                CommitActionRename(otherRenaming, otherRenaming.Name);
            }
            
            _originalActionName = actionVm.Name;
            actionVm.IsRenaming = true;
        }

        public void CommitActionRename(ActionNameViewModel actionVm, string newName)
        {
            if (actionVm == null || !actionVm.IsRenaming) return;

            actionVm.IsRenaming = false;

            if (string.IsNullOrWhiteSpace(newName) || newName == _originalActionName)
            {
                actionVm.Name = _originalActionName;
                return;
            }

            if (ActionNames.Any(a => a.Name == newName && a != actionVm))
            {
                MessageBox.Show(LocalizationService.Instance["UIConstructor_DuplicateActionNameError"], 
                    LocalizationService.Instance["UIConstructor_RenameFailedTitle"], 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                actionVm.Name = _originalActionName;
                return;
            }

            if (Workflow.Scripts.Actions.TryGetValue(_originalActionName, out var script))
            {
                Workflow.Scripts.Actions.Remove(_originalActionName);
                Workflow.Scripts.Actions[newName] = script;
            }
    
            actionVm.Name = newName;

            RefreshActionNamesInFields(_originalActionName, newName);
        }
        
        private void RefreshActionNamesInFields(string oldName, string newName)
        {
            foreach (var group in Workflow.Groups)
            {
                foreach (var tab in group.Tabs)
                {
                    foreach (var field in tab.Fields)
                    {
                        if (field.Type == FieldType.ScriptButton && field.ActionName == oldName)
                        {
                            field.ActionName = newName;
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Saves the workflow file and raises a global event to notify the main window of the change.
        /// This is used for auto-updates after modifying presets.
        /// </summary>
        private void TriggerWorkflowUpdate()
        {
            if (string.IsNullOrEmpty(NewWorkflowName)) return;

            try
            {
                var workflowFullPath = Path.Combine(Workflow.WorkflowsDir, NewWorkflowName + ".json");
                Workflow.SaveWorkflow(NewWorkflowName);
                GlobalEventManager.RaiseWorkflowSaved(workflowFullPath, WorkflowSaveType.LayoutOnly);
            }
            catch (Exception ex)
            {
                // Log the error without showing a disruptive message box to the user.
                Logger.Log(ex, "Failed to auto-save workflow during preset modification in UIConstructor.");
            }
        }

        public void CancelActionRename(ActionNameViewModel actionVm)
        {
            if (actionVm == null) return;
            actionVm.IsRenaming = false;
            actionVm.Name = _originalActionName; // Восстанавливаем старое имя
        }
        // --- END OF SCRIPTING METHODS ---
        
        private async void LoadModelSubTypesAsync()
        {
            try
            {
                var types = await _modelService.GetModelTypesAsync();
                ModelSubTypes.Clear();
                foreach (var type in types.OrderBy(t => t.Name))
                {
                    ModelSubTypes.Add(type.Name);
                }
            }
            catch (Exception)
            {
                // The error message is now handled by ModelService.
                // We just catch the exception to prevent the application from crashing.
                // The model list will simply remain empty.
            }
        }
        
        private void ToggleRename(object itemToRename)
        {
            if (_itemBeingRenamed != null && _itemBeingRenamed != itemToRename)
            {
                if (_itemBeingRenamed is WorkflowGroupViewModel gvm) gvm.IsRenaming = false;
                if (_itemBeingRenamed is WorkflowGroup g) g.IsRenaming = false;
                if (_itemBeingRenamed is WorkflowField f) f.IsRenaming = false;
                if (_itemBeingRenamed is WorkflowTabDefinition t) t.IsRenaming = false;
            }

            if (itemToRename is WorkflowGroupViewModel groupVm)
            {
                groupVm.IsRenaming = !groupVm.IsRenaming;
                _itemBeingRenamed = groupVm.IsRenaming ? groupVm : null;
            }
            else if (itemToRename is WorkflowGroup group)
            {
                group.IsRenaming = !group.IsRenaming;
                _itemBeingRenamed = group.IsRenaming ? group : null;
            }
            else if (itemToRename is WorkflowField field)
            {
                field.IsRenaming = !field.IsRenaming;
                _itemBeingRenamed = field.IsRenaming ? field : null;
            }
            // --- START OF CHANGE ---
            else if (itemToRename is WorkflowTabDefinition tab)
            {
                tab.IsRenaming = !tab.IsRenaming;
                _itemBeingRenamed = tab.IsRenaming ? tab : null;
            }
            // --- END OF CHANGE ---
        }

        private void LoadApiWorkflow()
        {
            // Check if we need to warn the user before clearing snapshots
            if (Workflow.Groups.SelectMany(g => g.Tabs).SelectMany(t => t.Fields).Any(f => f.Type == FieldType.NodeBypass))
            {
                var confirmation = MessageBox.Show(
                    LocalizationService.Instance["UIConstructor_ConfirmApiReplaceMessage"],
                    LocalizationService.Instance["UIConstructor_ConfirmApiReplaceTitle"],
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (confirmation != MessageBoxResult.Yes)
                {
                    return; // User cancelled the operation
                }
            }
            
            var dialog = new OpenFileDialog { Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*" };
            if (dialog.ShowDialog() == true)
            {
                Workflow.NodeConnectionSnapshots.Clear(); // This is now safe
                Workflow.LoadApiWorkflow(dialog.FileName);
                UpdateAvailableFields();
                UpdateWorkflowNodesList();
                ValidateFieldPaths();
                _apiWasReplaced = true;
                RefreshBypassNodeFields();
            }
            RefreshActionNames();
        }
        
        /// <summary>
        /// Replaces the API definition of the current workflow with a new one.
        /// </summary>
        /// <param name="apiJson">The JObject of the new API definition.</param>
        public void ReplaceApiWorkflow(JObject apiJson)
        {
            // Check if we need to warn the user before clearing snapshots
            if (Workflow.Groups.SelectMany(g => g.Tabs).SelectMany(t => t.Fields).Any(f => f.Type == FieldType.NodeBypass))
            {
                var confirmation = MessageBox.Show(
                    LocalizationService.Instance["UIConstructor_ConfirmApiReplaceMessage"],
                    LocalizationService.Instance["UIConstructor_ConfirmApiReplaceTitle"],
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return; // User cancelled the operation
                }
            }

            Workflow.NodeConnectionSnapshots.Clear(); // This is now safe
            Workflow.LoadApiWorkflow(apiJson);
            _apiWasReplaced = true;
    
            // Refresh all UI elements that depend on the API.
            UpdateAvailableFields();
            UpdateWorkflowNodesList();
            ValidateFieldPaths();
            UpdateExistingFieldMetadata();
            RefreshActionNames();
            RefreshBypassNodeFields();
        }
        
        /// <summary>
        /// Iterates through all existing fields in all groups and sub-tabs,
        /// and forces a UI update for NodeBypass fields.
        /// </summary>
        private void RefreshBypassNodeFields()
        {
            if (Workflow == null) return;

            foreach (var group in Workflow.Groups)
            {
                foreach (var tab in group.Tabs) // Iterate through new tab structure
                {
                    foreach (var field in tab.Fields)
                    {
                        if (field.Type == FieldType.NodeBypass)
                        {
                            // This notification triggers the MultiBinding that uses AvailableNodesConverter,
                            // causing the list of available nodes for this field to be requeried.
                            field.NotifyBypassNodeIdsChanged();
                        }
                    }
                }
            }
        }
        
        
        private void ValidateFieldPaths()
        {
            if (!Workflow.IsLoaded) return;

            var invalidFields = new List<string>();

            foreach (var group in Workflow.Groups)
            {
                // --- START OF CHANGE: Iterate through sub-tabs to find fields ---
                foreach (var tab in group.Tabs)
                {
                    foreach (var field in tab.Fields)
                    {
                        // Virtual fields don't have a real path, so they can't be invalid in this context.
                        if (field.Path.StartsWith("virtual_"))
                        {
                            field.IsInvalid = false;
                            continue;
                        }
                
                        var property = Workflow.GetPropertyByPath(field.Path);
                        if (property == null)
                        {
                            field.IsInvalid = true;
                            invalidFields.Add($"{group.Name} -> {field.Name}");
                        }
                        else
                        {
                            field.IsInvalid = false;
                        }
                    }
                }
            }

            if (invalidFields.Any())
            {
                var message = string.Format(
                    LocalizationService.Instance["UIConstructor_InvalidFieldsFoundMessage"],
                    string.Join("\n", invalidFields)
                );
                MessageBox.Show(message, LocalizationService.Instance["UIConstructor_InvalidFieldsFoundTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        
        /// <summary>
        /// Iterates through all existing fields in all groups and sub-tabs,
        /// and updates their metadata (NodeTitle, NodeType) based on the currently loaded API.
        /// </summary>
        private void UpdateExistingFieldMetadata()
        {
            if (!Workflow.IsLoaded || Workflow.LoadedApi == null) return;

            foreach (var group in Workflow.Groups)
            {
                foreach (var tab in group.Tabs)
                {
                    foreach (var field in tab.Fields)
                    {
                        // Virtual fields do not have a real path and are skipped.
                        if (field.Path.StartsWith("virtual_")) continue;

                        var pathParts = field.Path.Split('.');
                        if (pathParts.Length == 0) continue;

                        string nodeId = pathParts[0];
                        var nodeToken = Workflow.LoadedApi[nodeId];
                    
                        // If the node exists in the current API, update the field's metadata.
                        if (nodeToken != null)
                        {
                            field.NodeTitle = nodeToken["_meta"]?["title"]?.ToString() ?? "Untitled";
                            field.NodeType = nodeToken["class_type"]?.ToString() ?? "Unknown";
                        }
                        // If the node doesn't exist, the ValidateFieldPaths method will mark it as invalid.
                        // We don't need to clear the old title here, it's better to see what it was.
                    }
                }
            }
        }
        
        private void UpdateWorkflowNodesList()
        {
            WorkflowNodes.Clear();
            if (Workflow.LoadedApi == null) return;

            foreach (var prop in Workflow.LoadedApi.Properties())
            {
                var nodeId = prop.Name;
                var nodeTitle = prop.Value["_meta"]?["title"]?.ToString() ?? "Untitled";
                WorkflowNodes.Add(new NodeInfo { Id = nodeId, Title = nodeTitle });
            }
        }
        
        public void OnNodeSelectionChanged(WorkflowField field, NodeInfo nodeInfo, bool isSelected)
        {
            if (field == null || nodeInfo == null) return;

            // Теперь используем переданное значение isSelected
            if (isSelected)
            {
                if (!field.BypassNodeIds.Contains(nodeInfo.Id))
                {
                    field.BypassNodeIds.Add(nodeInfo.Id);
                }
            }
            else
            {
                field.BypassNodeIds.Remove(nodeInfo.Id);
            }
        }

        private void SaveWorkflow(Window window)
        {
            var workflowFullPath = Path.Combine(Workflow.WorkflowsDir, NewWorkflowName + ".json");
            
            var saveType = _apiWasReplaced ? WorkflowSaveType.ApiReplaced : WorkflowSaveType.LayoutOnly;

            if (saveType == WorkflowSaveType.ApiReplaced)
            {
                _sessionManager.ClearSession(workflowFullPath);
            }
            
            // --- START OF CHANGE: Use the correct save method ---
            // Replaced SaveWorkflow with SaveWorkflowWithCurrentState to ensure
            // that the live API definition (LoadedApi) is saved, not the original one.
            // This is crucial when loading a new API file or creating a workflow from scratch.
            Workflow.SaveWorkflowWithCurrentState(NewWorkflowName);
            // --- END OF CHANGE ---
            
            GlobalEventManager.RaiseWorkflowSaved(workflowFullPath, saveType);
            
            Logger.Log(LocalizationService.Instance["UIConstructor_SaveSuccessMessage"]);
            window?.Close();
        }

        private void ExportApiWorkflow()
        {
            if (Workflow.LoadedApi == null)
            {
                Logger.Log(LocalizationService.Instance["UIConstructor_ExportErrorMessage"], LogLevel.Error);
                return;
            }

            var dialog = new SaveFileDialog
            {
                FileName = (string.IsNullOrWhiteSpace(NewWorkflowName) ? "workflow_api" : NewWorkflowName) + ".json",
                Filter = "JSON File (*.json)|*.json",
                Title = LocalizationService.Instance["UIConstructor_ExportDialogTitle"]
            };
            if (dialog.ShowDialog() == true)
                try
                {
                    JObject promptToExport = GetPromptForDesignerExport();
                    if (promptToExport == null) return; // User cancelled

                    var jsonContent = promptToExport.ToString(Formatting.Indented);
                    File.WriteAllText(dialog.FileName, jsonContent);
                    Logger.Log(string.Format(LocalizationService.Instance["UIConstructor_ExportSuccessMessage"], dialog.FileName));
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, string.Format(LocalizationService.Instance["UIConstructor_SaveErrorMessage"], ex.Message));
                }
        }

        private JObject GetPromptForDesignerExport()
        {
            // Use OriginalApi if available (clean export), otherwise fall back to LoadedApi.
            // CanExecute on the command ensures at least LoadedApi is not null.
            JObject baseApi = Workflow.OriginalApi ?? Workflow.LoadedApi;
            
            // This check is a safeguard, but CanExecute should prevent this from being null.
            if (baseApi == null)
            {
                Logger.Log(LocalizationService.Instance["UIConstructor_ExportErrorMessage"], LogLevel.Error);
                return null;
            }

            bool hasBypassFields = Workflow.Groups
                .SelectMany(g => g.Tabs)
                .SelectMany(t => t.Fields)
                .Any(f => f.Type == FieldType.NodeBypass);

            if (!hasBypassFields)
            {
                return baseApi.DeepClone() as JObject;
            }
            
            var result = MessageBox.Show(
                LocalizationService.Instance["MainVM_ExportBypassMessage"],
                LocalizationService.Instance["MainVM_ExportBypassTitle"],
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            
            var clonedPrompt = baseApi.DeepClone() as JObject;

            switch (result)
            {
                case MessageBoxResult.Yes: // Restore connections
                    if (Workflow.NodeConnectionSnapshots != null)
                    {
                        foreach (var snapshot in Workflow.NodeConnectionSnapshots)
                        {
                            if (clonedPrompt[snapshot.Key]?["inputs"] is JObject inputs)
                            {
                                inputs.Merge(snapshot.Value.DeepClone(), new JsonMergeSettings
                                {
                                    MergeArrayHandling = MergeArrayHandling.Replace
                                });
                            }
                        }
                    }
                    return clonedPrompt;

                case MessageBoxResult.No: // Export as is
                    return clonedPrompt;

                case MessageBoxResult.Cancel:
                default:
                    return null;
            }
        }

        // --- START OF NEW/MODIFIED METHODS FOR TABS ---
        private void UpdateGroupAssignments()
        {
            if (Workflow == null || Workflow.Groups == null) return;

            Workflow.Tabs.CollectionChanged -= OnWorkflowTabsChanged;
            Workflow.Tabs.CollectionChanged += OnWorkflowTabsChanged;

            var allGroupIdsInTabs = Workflow.Tabs.SelectMany(t => t.GroupIds).ToHashSet();

            UnassignedGroups.Clear();
            foreach (var groupVm in AllGroupViewModels.Where(vm => !allGroupIdsInTabs.Contains(vm.Id)))
            {
                UnassignedGroups.Add(groupVm);
            }

            SelectedTabGroups.Clear();
            if (SelectedTab != null)
            {
                if (!Workflow.Tabs.Contains(SelectedTab))
                {
                    SelectedTab = null;
                    return;
                }

                var groupVMLookup = AllGroupViewModels.ToDictionary(vm => vm.Id);

                foreach (var groupId in SelectedTab.GroupIds)
                {
                    if (groupVMLookup.TryGetValue(groupId, out var groupVm))
                    {
                        SelectedTabGroups.Add(groupVm);
                    }
                }
            }
        }

        private void OnWorkflowGroupsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateGroupAssignments();
        }

        private void OnWorkflowTabsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateGroupAssignments();
        }

        private void AddNewTab()
        {
            var baseName = LocalizationService.Instance["UIConstructor_NewTabDefaultName"];
            string newTabName = baseName;
            int counter = 1;
            while (Workflow.Tabs.Any(t => t.Name == newTabName))
            {
                newTabName = $"{baseName} {++counter}";
            }
            var newTab = new WorkflowTabDefinition { Name = newTabName };
            Workflow.Tabs.Add(newTab);
            SelectedTab = newTab;
        }

        private void RemoveTab(WorkflowTabDefinition tab)
        {
            bool proceed = !_settings.ShowTabDeleteConfirmation || 
                           MessageBox.Show(
                               string.Format(LocalizationService.Instance["UIConstructor_ConfirmDeleteTabMessage"], tab.Name), 
                               LocalizationService.Instance["UIConstructor_ConfirmDeleteTabTitle"],
                               MessageBoxButton.YesNo, 
                               MessageBoxImage.Warning) == MessageBoxResult.Yes;

            if (tab == null || !proceed) return;
    
            // Find all ViewModels for groups on the tab being deleted.
            var groupVMsToRemove = AllGroupViewModels
                .Where(vm => tab.GroupIds.Contains(vm.Id))
                .ToList(); // Use ToList() to avoid modifying the collection while iterating.

            // Remove each group using the centralized internal method.
            // This will now trigger an update for each group removed.
            foreach (var groupVM in groupVMsToRemove)
            {
                RemoveGroupInternal(groupVM);
            }
            
            // Now, remove the tab definition itself.
            int index = Workflow.Tabs.IndexOf(tab);
            Workflow.Tabs.Remove(tab);
            
            // Select the next available tab or null.
            if (Workflow.Tabs.Any())
            {
                SelectedTab = Workflow.Tabs.ElementAtOrDefault(index) ?? Workflow.Tabs.Last();
            }
            else
            {
                SelectedTab = null;
            }
        }

        public void MoveGroupToTab(WorkflowGroupViewModel groupVM, WorkflowTabDefinition targetTab, int insertIndex = -1)
        {
            if (groupVM == null) return;
            var groupId = groupVM.Id;

            foreach (var tab in Workflow.Tabs)
            {
                if (tab.GroupIds.Contains(groupId))
                {
                    tab.GroupIds.Remove(groupId);
                    break;
                }
            }

            if (targetTab != null)
            {
                if (insertIndex < 0 || insertIndex > targetTab.GroupIds.Count)
                {
                    targetTab.GroupIds.Add(groupId);
                }
                else
                {
                    targetTab.GroupIds.Insert(insertIndex, groupId);
                }
            }

            UpdateGroupAssignments();
        }

        public void MoveTab(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || newIndex < 0 || oldIndex >= Workflow.Tabs.Count || newIndex > Workflow.Tabs.Count) return;
            if (oldIndex < newIndex) newIndex--;
            if (oldIndex == newIndex) return;
            Workflow.Tabs.Move(oldIndex, newIndex);
        }
        // --- END OF NEW/MODIFIED METHODS FOR TABS ---

        private void AddGroup()
        {
            var newGroupName = string.Format(LocalizationService.Instance["UIConstructor_NewGroupDefaultName"], Workflow.Groups.Count + 1);
            var newGroup = new WorkflowGroup { Name = newGroupName };

            var defaultTab = new WorkflowGroupTab { Name = "Controls" };
            newGroup.Tabs.Add(defaultTab);

            Workflow.Groups.Add(newGroup);

            if (SelectedTab != null)
            {
                var newGroupVm = AllGroupViewModels.FirstOrDefault(vm => vm.Model == newGroup);
                MoveGroupToTab(newGroupVm, SelectedTab);
            }
        }

        private void RemoveGroup(WorkflowGroupViewModel groupVm)
        {
            bool proceed = !_settings.ShowGroupDeleteConfirmation ||
                           (MessageBox.Show(
                               string.Format(LocalizationService.Instance["UIConstructor_ConfirmDeleteGroupMessage"], groupVm.Name),
                               LocalizationService.Instance["UIConstructor_ConfirmDeleteGroupTitle"],
                               MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);

            if (groupVm != null && proceed)
            {
                RemoveGroupInternal(groupVm);
            }
        }

        /// <summary>
        /// Centralized internal logic for removing a group and its fields.
        /// </summary>
        /// <param name="groupVm">The ViewModel of the group to remove.</param>
        /// <param name="performUpdate">Whether to immediately update the available fields list.</param>
        private void RemoveGroupInternal(WorkflowGroupViewModel groupVm, bool performUpdate = true)
        {
            if (groupVm == null) return;
            
            // Unsubscribe from property changed events for all fields in the group being removed
            // to prevent memory leaks and potential update issues.
            foreach (var subTab in groupVm.Model.Tabs)
            {
                foreach (var field in subTab.Fields)
                {
                    field.PropertyChanged -= OnFieldPropertyChanged;
                }
            }
    
            // Ensure the group is no longer associated with any tab.
            MoveGroupToTab(groupVm, null);
            // Remove the group's model from the master list. The ViewModel will update via the CollectionChanged event.
            Workflow.Groups.Remove(groupVm.Model); 
            
            if (performUpdate)
            {
                UpdateAvailableFields();
            }
        }

        private void RemoveField(WorkflowField field)
        {
            foreach (var group in Workflow.Groups)
            {
                var tab = group.Tabs.FirstOrDefault(t => t.Fields.Contains(field));
                if (tab != null)
                {
                    field.PropertyChanged -= OnFieldPropertyChanged;
                    tab.Fields.Remove(field);
                    UpdateAvailableFields();
                    return;
                }
            }
        }

        private void UpdateAvailableFields()
        {
            if (!Workflow.IsLoaded)
            {
                AvailableFields.Clear();
                return;
            }

            var allFields = Workflow.ParseFields();
            var usedFieldPaths = Workflow.Groups.SelectMany(g => g.Tabs).SelectMany(t => t.Fields).Select(f => f.Path).ToHashSet();
            var available = allFields.Where(f => !usedFieldPaths.Contains(f.Path));
            if (!string.IsNullOrWhiteSpace(SearchFilter))
            {
                string[] searchTerms = SearchFilter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                available = available.Where(f => searchTerms.All(term => 
                    f.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            AvailableFields.Clear();
            foreach (var field in available) AvailableFields.Add(field);
        }

        public void AddFieldToGroupAtIndex(WorkflowField field, WorkflowGroupViewModel groupVm, int targetIndex = -1)
        {
            if (groupVm == null) return;

            var targetTabVm = groupVm.SelectedTab; // Get the currently active tab
            if (targetTabVm == null)
            {
                // Fallback: if no tab is selected for some reason, use the first one.
                targetTabVm = groupVm.Tabs.FirstOrDefault();
            }

            if (targetTabVm == null)
            {
                // This case should not happen, but as a last resort, create a tab.
                var newTabModel = new WorkflowGroupTab { Name = "Controls" };
                groupVm.Model.Tabs.Add(newTabModel);
                targetTabVm = new WorkflowGroupTabViewModel(newTabModel);
                groupVm.Tabs.Add(targetTabVm);
            }

            AddFieldToSubTabAtIndex(field, targetTabVm.Model, targetIndex);
        }

        public void AddFieldToGroup(WorkflowField field, WorkflowGroupViewModel groupVm)
        {
            AddFieldToGroupAtIndex(field, groupVm);
        }

        public void AddFieldToSubTabAtIndex(WorkflowField field, WorkflowGroupTab targetTab, int targetIndex = -1)
        {
            if (field == null || targetTab == null || targetTab.Fields.Any(f => f.Path == field.Path)) return;

            // The passed 'field' object is now a clone with the name already processed by the caller.
            // This method just handles defaults and insertion.
            var newField = field; 

            var rawFieldName = newField.Name.Contains("::")
                ? newField.Name.Split(new[] { "::" }, 2, StringSplitOptions.None)[1]
                : newField.Name;

            if (_sliderDefaultsService.TryGetDefaults(newField.NodeType, rawFieldName, out var defaults))
            {
                newField.Type = defaults.Precision.HasValue ? FieldType.SliderFloat : FieldType.SliderInt;
                newField.MinValue = defaults.Min;
                newField.MaxValue = defaults.Max;
                newField.StepValue = defaults.Step;
                newField.Precision = defaults.Precision;
            }
            else if (rawFieldName.Equals("seed", StringComparison.OrdinalIgnoreCase)) newField.Type = FieldType.Seed;
            else if (rawFieldName.Equals("sampler_name", StringComparison.OrdinalIgnoreCase)) newField.Type = FieldType.Sampler;
            else if (rawFieldName.Equals("scheduler", StringComparison.OrdinalIgnoreCase)) newField.Type = FieldType.Scheduler;
            else if (rawFieldName.Equals("ckpt_name", StringComparison.OrdinalIgnoreCase)) { newField.Type = FieldType.Model; newField.ModelType = "checkpoints"; }
            else if (rawFieldName.StartsWith("lora_name", StringComparison.OrdinalIgnoreCase)) { newField.Type = FieldType.Model; newField.ModelType = "loras"; }
            else if (rawFieldName.StartsWith("clip_name", StringComparison.OrdinalIgnoreCase)) { newField.Type = FieldType.Model; newField.ModelType = "clip"; }
            else if (rawFieldName.Equals("unet_name", StringComparison.OrdinalIgnoreCase)) { newField.Type = FieldType.Model; newField.ModelType = "diffusion_models"; }
            else if (rawFieldName.Equals("control_net_name", StringComparison.OrdinalIgnoreCase)) { newField.Type = FieldType.Model; newField.ModelType = "controlnet"; }
            else if (rawFieldName.Equals("style_model_name", StringComparison.OrdinalIgnoreCase)) { newField.Type = FieldType.Model; newField.ModelType = "style_models"; }
            else if (rawFieldName.Equals("model_name", StringComparison.OrdinalIgnoreCase)) { newField.Type = FieldType.Model; newField.ModelType = "upscale_models"; }

            newField.PropertyChanged += OnFieldPropertyChanged;
            if (targetIndex < 0 || targetIndex >= targetTab.Fields.Count) targetTab.Fields.Add(newField);
            else targetTab.Fields.Insert(targetIndex, newField);
            UpdateAvailableFields();
        }
        
        /// <summary>
        /// Handles property changes on a WorkflowField, specifically for applying slider defaults.
        /// </summary>
        private void OnFieldPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(WorkflowField.Type) || sender is not WorkflowField field)
            {
                return;
            }

            if (field.Type == FieldType.SliderInt || field.Type == FieldType.SliderFloat)
            {
                ApplySliderDefaults(field);
            }
        }
        
        /// <summary>
        /// Applies default values to a slider field based on user-defined rules.
        /// </summary>
        private void ApplySliderDefaults(WorkflowField field)
        {
            if (Workflow.LoadedApi == null || string.IsNullOrEmpty(field.Path))
                return;

            string nodeType = null;
            var pathParts = field.Path.Split('.');
            if (pathParts.Length > 0)
            {
                string nodeId = pathParts[0];
                nodeType = Workflow.LoadedApi[nodeId]?["class_type"]?.ToString();
            }

            // The field name for matching is the raw name, not the display name.
            // Example: for "KSampler::steps", the name is "steps".
            string fieldName = field.Name.Contains("::") ? field.Name.Split(new[] { "::" }, 2, StringSplitOptions.None)[1] : field.Name;

            if (_sliderDefaultsService.TryGetDefaults(nodeType, fieldName, out var defaults))
            {
                field.MinValue = defaults.Min;
                field.MaxValue = defaults.Max;
                field.StepValue = defaults.Step;

                if (field.Type == FieldType.SliderFloat)
                {
                    // Only apply precision if it was specified in the rule.
                    if (defaults.Precision.HasValue)
                    {
                        field.Precision = defaults.Precision.Value;
                    }
                }
            }
        }

        //public void AddFieldToGroup(WorkflowField field, WorkflowGroup group)
        //{
        //    AddFieldToGroupAtIndex(field, group);
        //}

        public void MoveField(WorkflowField field, WorkflowGroupViewModel sourceGroupVM, WorkflowGroupViewModel targetGroupVM, int targetIndex = -1)
        {
            if (field == null || sourceGroupVM == null || targetGroupVM == null) return;
            var sourceTab = sourceGroupVM.Model.Tabs.FirstOrDefault(t => t.Fields.Contains(field));
            if (sourceTab == null) return;

            var targetTab = targetGroupVM.Model.Tabs.FirstOrDefault();
            if (targetTab == null)
            {
                targetTab = new WorkflowGroupTab { Name = "Controls" };
                targetGroupVM.Model.Tabs.Add(targetTab);
                // Also update the ViewModel
                targetGroupVM.Tabs.Add(new WorkflowGroupTabViewModel(targetTab));
            }

            MoveFieldToSubTab(field, sourceTab, targetTab, targetIndex);
        }

        public void MoveFieldToSubTab(WorkflowField field, WorkflowGroupTab sourceTab, WorkflowGroupTab targetTab, int targetIndex = -1)
        {
            if (field == null || sourceTab == null || targetTab == null) return;

            var oldIndex = sourceTab.Fields.IndexOf(field);
            if (oldIndex == -1) return;

            // Если поле уже в целевой вкладке, просто меняем его позицию
            if (sourceTab == targetTab)
            {
                if (targetIndex < 0 || targetIndex > sourceTab.Fields.Count) targetIndex = sourceTab.Fields.Count - 1;
                if (oldIndex == targetIndex) return;
                // Корректируем индекс, если перемещаем вниз
                if (oldIndex < targetIndex) targetIndex--;
                sourceTab.Fields.Move(oldIndex, targetIndex);
                return;
            }

            sourceTab.Fields.RemoveAt(oldIndex);

            var finalInsertIndex = targetIndex;
            if (finalInsertIndex < 0 || finalInsertIndex > targetTab.Fields.Count)
            {
                finalInsertIndex = targetTab.Fields.Count;
            }
            
            targetTab.Fields.Insert(finalInsertIndex, field);
        }
        
        public void MoveSubTab(WorkflowGroupTab tabToMove, WorkflowGroup ownerGroup, int newIndex)
        {
            if (tabToMove == null || ownerGroup == null) return;
    
            // Find the corresponding ViewModel for the owner group.
            var ownerVm = AllGroupViewModels.FirstOrDefault(vm => vm.Model == ownerGroup);
            if (ownerVm == null) return;
    
            // Find the corresponding ViewModel for the tab being moved.
            var tabVmToMove = ownerVm.Tabs.FirstOrDefault(tvm => tvm.Model == tabToMove);
            if (tabVmToMove == null) return;
    
            // 1. Manipulate the ViewModel's collection, which is bound to the UI.
            ownerVm.Tabs.Remove(tabVmToMove);
            if (newIndex < 0) newIndex = 0;
            if (newIndex > ownerVm.Tabs.Count) newIndex = ownerVm.Tabs.Count;
            ownerVm.Tabs.Insert(newIndex, tabVmToMove);
    
            // 2. Manipulate the underlying Model's collection to ensure the change is saved.
            ownerGroup.Tabs.Remove(tabToMove);
            ownerGroup.Tabs.Insert(newIndex, tabToMove);
        }
        
        public void MoveSubTabToGroup(WorkflowGroupTab tabToMove, WorkflowGroup sourceGroup, WorkflowGroup targetGroup, int newIndex = -1)
        {
            if (tabToMove == null || sourceGroup == null || targetGroup == null || sourceGroup == targetGroup) return;
    
            // Find the ViewModels for both source and target groups.
            var sourceVm = AllGroupViewModels.FirstOrDefault(vm => vm.Model == sourceGroup);
            var targetVm = AllGroupViewModels.FirstOrDefault(vm => vm.Model == targetGroup);
            if (sourceVm == null || targetVm == null) return;
    
            // Find the ViewModel for the tab being moved.
            var tabVmToMove = sourceVm.Tabs.FirstOrDefault(tvm => tvm.Model == tabToMove);
            if (tabVmToMove == null) return;
    
            // 1. Update the ViewModels' collections for the UI.
            sourceVm.Tabs.Remove(tabVmToMove);
            if (newIndex < 0 || newIndex > targetVm.Tabs.Count)
            {
                targetVm.Tabs.Add(tabVmToMove);
            }
            else
            {
                targetVm.Tabs.Insert(newIndex, tabVmToMove);
            }
    
            // 2. Update the Models' collections for persistence.
            sourceGroup.Tabs.Remove(tabToMove);
            if (newIndex < 0 || newIndex > targetGroup.Tabs.Count)
            {
                targetGroup.Tabs.Add(tabToMove);
            }
            else
            {
                targetGroup.Tabs.Insert(newIndex, tabToMove);
            }
        }

        public void MoveGroup(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || newIndex < 0 || oldIndex >= Workflow.Groups.Count ||
                newIndex > Workflow.Groups.Count) return;
            if (oldIndex < newIndex) newIndex--;
            if (oldIndex == newIndex) return;
            Workflow.Groups.Move(oldIndex, newIndex);
        }
    }

    /// <summary>
    /// Code-behind for the UIConstructor window.
    /// Handles drag-and-drop operations and user interactions for editing workflow layouts.
    /// </summary>
    public partial class UIConstructor : Window
    {
        private UIConstructorView _viewModel;
        
        private Point _startPoint;
        private Point _dragStartPoint;
        private bool _isDragging = false;
        private object _dragData;
        private bool _isCutOperation = false;
        
        private Border? _currentGroupHighlight;
        private Border? _lastFieldIndicator;
        private Border? _lastGroupIndicator;
        private Border? _lastTabIndicator;
        private Border? _lastSubTabIndicator;
        private Border? _lastIndicator;
        private CompletionWindow _completionWindow;
        
        static UIConstructor()
        {
            // Check if our highlighting is already registered
            if (HighlightingManager.Instance.GetDefinition("Python-Dark") == null)
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "Comfizen.Resources.Python-Dark.xshd";

                using (Stream s = assembly.GetManifestResourceStream(resourceName))
                {
                    if (s == null)
                    {
                        MessageBox.Show("FATAL: Could not find the Python-Dark.xshd highlighting resource.");
                        return;
                    }
                    using (XmlReader reader = new XmlTextReader(s))
                    {
                        var customHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                        HighlightingManager.Instance.RegisterHighlighting("Python-Dark", new[] { ".py" }, customHighlighting);
                    }
                }
            }
        }
        
        public UIConstructor()
        {
            InitializeComponent();
            WindowStartupLocation = WindowStartupLocation.Manual;
            _viewModel = new UIConstructorView();
            DataContext = _viewModel;
            this.Loaded += UIConstructor_Loaded;
            this.Closing += UIConstructor_Closing;
            AttachCompletionEvents();
            ApplyHyperlinksColor();
        }

        public UIConstructor(string workflowFileName)
        {
            InitializeComponent();
            WindowStartupLocation = WindowStartupLocation.Manual;
            _viewModel = new UIConstructorView(workflowFileName);
            DataContext = _viewModel;
            this.Loaded += UIConstructor_Loaded;
            this.Closing += UIConstructor_Closing;
            AttachCompletionEvents();
            ApplyHyperlinksColor();
        }
        
        // This is the new constructor that accepts the live workflow object.
        public UIConstructor(Workflow liveWorkflow, string workflowRelativePath)
        {
            InitializeComponent();
            WindowStartupLocation = WindowStartupLocation.Manual;
            _viewModel = new UIConstructorView(liveWorkflow, workflowRelativePath);
            DataContext = _viewModel;
            this.Loaded += UIConstructor_Loaded;
            this.Closing += UIConstructor_Closing;
            AttachCompletionEvents();
            ApplyHyperlinksColor();
        }
        
        private void AvailableField_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point position = e.GetPosition(null);
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is ListBox listBox)
                    {
                        var fieldsToDrag = listBox.SelectedItems.OfType<WorkflowField>().ToList();
                        if (fieldsToDrag.Any())
                        {
                            _isDragging = true;
                            try
                            {
                                var finalDragData = new DataObject();
                                finalDragData.SetData(typeof(List<WorkflowField>), fieldsToDrag);
                                DragDrop.DoDragDrop(listBox, finalDragData, DragDropEffects.Move);
                            }
                            finally
                            {
                                _isDragging = false;
                            }
                        }
                    }
                }
            }
        }
        
        private void ShowUniversalPopup(object sender, MouseButtonEventArgs e)
        {
            var popup = FindResource("UniversalContextMenuPopup") as Popup;
            if (popup == null) return;

            var clickedElement = sender as FrameworkElement;
            if (clickedElement == null) return;

            object targetForCommands = clickedElement.DataContext;

            if (clickedElement.DataContext is WorkflowField field)
            {
                var listBox = FindVisualParent<ListBox>(clickedElement);
                if (listBox != null)
                {
                    if (!listBox.SelectedItems.Contains(field))
                    {
                    listBox.SelectedItems.Clear();
                    listBox.SelectedItem = field;
                }
                    targetForCommands = listBox;
            }
            }
            
            _viewModel.PopupTarget = targetForCommands;
            popup.DataContext = _viewModel;
            popup.IsOpen = true;

            e.Handled = true;
        }
        
        private void UIConstructor_Loaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel?._settings == null) return;
            var settings = _viewModel._settings;

            // First, restore the size.
            if (settings.DesignerWindowWidth > 100 && settings.DesignerWindowHeight > 100)
            {
                this.Width = settings.DesignerWindowWidth;
                this.Height = settings.DesignerWindowHeight;
            }
    
            // Then, check if the saved position is valid.
            double savedLeft = settings.DesignerWindowLeft;
            double savedTop = settings.DesignerWindowTop;

            // A more robust way to check for visibility is to see if the window's rectangle
            // intersects with the virtual screen rectangle. This correctly handles all multi-monitor setups.
            var windowRect = new Rect(savedLeft, savedTop, this.Width, this.Height);
            var virtualScreenRect = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, 
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

            bool isPositionValid = virtualScreenRect.IntersectsWith(windowRect);

            if (isPositionValid)
            {
                this.Left = savedLeft;
                this.Top = savedTop;
            }
            
            if (settings.DesignerWindowState == WindowState.Maximized)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    this.WindowState = WindowState.Maximized;
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void UIConstructor_Closing(object sender, CancelEventArgs e)
        {
            var settings = _viewModel._settings;

            // We only want to save the window's geometry if it's in a predictable state.
            // Saving while Minimized can result in incorrect negative coordinates.
            if (this.WindowState == WindowState.Maximized)
            {
                settings.DesignerWindowState = WindowState.Maximized;
                // For a maximized window, save the RestoreBounds for correct restoration.
                settings.DesignerWindowWidth = this.RestoreBounds.Width;
                settings.DesignerWindowHeight = this.RestoreBounds.Height;
                settings.DesignerWindowLeft = this.RestoreBounds.Left;
                settings.DesignerWindowTop = this.RestoreBounds.Top;
            }
            else if (this.WindowState == WindowState.Normal)
            {
                settings.DesignerWindowState = WindowState.Normal;
                // For a normal window, save its current size and position.
                settings.DesignerWindowWidth = this.Width;
                settings.DesignerWindowHeight = this.Height;
                settings.DesignerWindowLeft = this.Left;
                settings.DesignerWindowTop = this.Top;
            }
            // If the window is Minimized, we don't save its geometry.
            // This preserves the last known good position and size.

            settings.UseNodeTitlePrefixInDesigner = _viewModel.UseNodeTitlePrefix;
            SettingsService.Instance.SaveSettings();
        }
        
        private void ApplyHyperlinksColor()
        {
            var yellowBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E6DB74"));
            yellowBrush.Freeze();

            HookScriptEditor.TextArea.TextView.LinkTextForegroundBrush = yellowBrush;
            ActionScriptEditor.TextArea.TextView.LinkTextForegroundBrush = yellowBrush;
        }
        
        private void AttachCompletionEvents()
        {
            HookScriptEditor.TextArea.TextEntered += TextArea_TextEntered;
            ActionScriptEditor.TextArea.TextEntered += TextArea_TextEntered;
        }
        
        private void Field_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (sender is FrameworkElement element && DataContext is UIConstructorView viewModel)
                {
                    // Execute the rename command directly.
                    viewModel.ToggleRenameCommand.Execute(element.DataContext);
                    // Mark the event as handled to prevent initiating a drag.
                    e.Handled = true; 
                    return; // Stop further processing.
                }
            }

            _dragStartPoint = e.GetPosition(null);
            _isDragging = false; 

            if (sender is Border element2 && element2.DataContext is WorkflowField)
            {
                var listBoxItem = FindVisualParent<ListBoxItem>(element2);
                if (listBoxItem != null && listBoxItem.IsSelected && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == ModifierKeys.None)
                {
                    var source = e.OriginalSource as DependencyObject;
                    while (source != null && source != listBoxItem)
                    {
                        // If the click was on any of these controls, do NOT handle the event.
                        // Let the control process the click normally.
                        if (source is TextBox || 
                            source is ComboBox || 
                            source is CheckBox || 
                            source is ButtonBase || // Catches Button and ToggleButton
                            source is Thumb)       // Catches slider thumbs
                        {
                            return; // Exit without setting e.Handled = true
                        }
                        source = VisualTreeHelper.GetParent(source);
                    }

                    // If we are here, the click was on a non-interactive part of the item.
                    // We handle the event to prevent the ListBox from clearing the selection,
                    // thus preserving it for a potential drag operation.
                    e.Handled = true;
                    }
                }
            }
        
        private void Field_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point position = e.GetPosition(null);
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is FrameworkElement element && element.DataContext is WorkflowField field)
                    {
                        var listBox = FindVisualParent<ListBox>(element);
                        if (listBox == null || listBox.SelectedItems.Count == 0) return;

                        var fieldsToDrag = listBox.SelectedItems.OfType<WorkflowField>().ToList();
                        
                        var expander = FindVisualParent<Expander>(element);
                        if (expander?.DataContext is WorkflowGroupViewModel groupVm)
                        {
                            _isDragging = true;
                            try
                            {
                                var finalDragData = new Tuple<List<WorkflowField>, WorkflowGroup>(fieldsToDrag, groupVm.Model);
                                DragDrop.DoDragDrop(element, finalDragData, DragDropEffects.Move);
                            }
                            finally
                            {
                                _isDragging = false;
                            }
                        }
                    }
                }
            }
        }
        
        // --- START OF NEW METHODS ---
        private void TabContent_DragEnter(object sender, DragEventArgs e)
        {
            // Allow dropping a group if a tab is selected
            if (e.Data.GetDataPresent(typeof(WorkflowGroup)) && _viewModel.SelectedTab != null)
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void TabContent_Drop(object sender, DragEventArgs e)
        {
            // Handle dropping a group from the unassigned list onto the current tab content area
            if (e.Data.GetData(typeof(Tuple<WorkflowGroupViewModel, WorkflowTabDefinition>)) is Tuple<WorkflowGroupViewModel, WorkflowTabDefinition> data && _viewModel.SelectedTab != null)
            {
                _viewModel.MoveGroupToTab(data.Item1, _viewModel.SelectedTab);
                e.Handled = true;
            }
        }
        // --- END OF NEW METHODS ---

        private void TextArea_TextEntered(object sender, TextCompositionEventArgs e)
        {
            // Вызываем автозавершение только при вводе точки
            if (e.Text != "." || _completionWindow != null)
            {
                return;
            }

            var textArea = sender as TextArea;
            if (textArea == null) return;

            // Получаем выражение слева от точки (например, "ctx" или "ctx.settings")
            string expression = GetFullExpressionBeforeCaret(textArea).TrimEnd('.');
            
            // Определяем .NET-тип этого выражения
            Type targetType = ResolveExpressionType(expression);

            if (targetType != null)
            {
                _completionWindow = new CompletionWindow(textArea);
                _completionWindow.StartOffset = textArea.Caret.Offset;
                
                // Используем наш хелпер для заполнения списка на основе определенного типа
                PopulateCompletionData(_completionWindow.CompletionList.CompletionData, targetType);

                // Если мы нашли члены для отображения, показываем окно
                if (_completionWindow.CompletionList.CompletionData.Any())
                {
                    // --- Здесь ваш код для стилизации окна (остается без изменений) ---
                    _completionWindow.Background = (Brush)Application.Current.FindResource("SecondaryBackground");
                    _completionWindow.BorderBrush = (Brush)Application.Current.FindResource("TertiaryBackground");
                    _completionWindow.BorderThickness = new Thickness(1);
                    var listBox = _completionWindow.CompletionList.ListBox;
                    listBox.Background = (Brush)Application.Current.FindResource("SecondaryBackground");
                    listBox.Foreground = (Brush)Application.Current.FindResource("TextBrush");
                    var itemStyle = new Style(typeof(ListBoxItem));
                    itemStyle.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent));
                    itemStyle.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, (Brush)Application.Current.FindResource("TextBrush")));
                    itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(4)));
                    itemStyle.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0)));
                    var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
                    selectedTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, (Brush)Application.Current.FindResource("PrimaryAccentBrush")));
                    itemStyle.Triggers.Add(selectedTrigger);
                    var mouseOverTrigger = new Trigger { Property = ListBoxItem.IsMouseOverProperty, Value = true };
                    mouseOverTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, (Brush)Application.Current.FindResource("TertiaryBackground")));
                    itemStyle.Triggers.Add(mouseOverTrigger);
                    listBox.ItemContainerStyle = itemStyle;
                    // --- Конец кода стилизации ---

                    _completionWindow.Show();
                    _completionWindow.Closed += (o, args) => { _completionWindow = null; };
                }
                else
                {
                    _completionWindow = null; // Нет членов для показа
                }
            }
        }
        
        /// <summary>
        /// Извлекает полное выражение доступа к члену перед курсором (например, "ctx.settings.SomeProp").
        /// </summary>
        private string GetFullExpressionBeforeCaret(TextArea textArea)
        {
            int offset = textArea.Caret.Offset;
            if (offset == 0) return string.Empty;

            // Ищем начало выражения, которое прерывается пробелом или началом документа.
            int start = offset - 1;
            while (start >= 0)
            {
                char c = textArea.Document.GetCharAt(start);
                if (char.IsWhiteSpace(c) || c == '(' || c == ')' || c == '[' || c == ']' || c == ',')
                {
                    start++;
                    break;
                }
                if (start == 0) break;
                start--;
            }

            return textArea.Document.GetText(start, offset - start);
        }

        /// <summary>
        /// Заполняет список автозавершения членами указанного типа .NET.
        /// </summary>
        private void PopulateCompletionData(ICollection<ICompletionData> completionData, Type type)
        {
            if (type == null) return;
            
            // Получаем только публичные члены экземпляра, объявленные в самом типе, чтобы избежать мусора из System.Object
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var member in members.OrderBy(m => m.Name))
            {
                // Отфильтровываем специальные имена, сгенерированные компилятором (конструкторы, методы доступа к свойствам и т.д.)
                if (member is MethodBase methodBase && methodBase.IsSpecialName)
                {
                    continue;
                }
                
                // Также отфильтровываем методы добавления/удаления событий
                if (member.Name.StartsWith("add_") || member.Name.StartsWith("remove_"))
                {
                     continue;
                }

                completionData.Add(new ScriptingCompletionData(member));
            }
        }


        /// <summary>
        /// Определяет .NET-тип для выражения доступа к члену (например, "ctx.settings").
        /// </summary>
        /// <param name="expression">Строка выражения.</param>
        /// <returns>Тип .NET конечного члена или null, если не удалось его определить.</returns>
        private Type ResolveExpressionType(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return null;

            var parts = expression.Split('.');
            if (parts.Length == 0 || parts[0] != "ctx") return null;

            Type currentType = typeof(ScriptContext);

            // Начинаем со второй части (так как первая - это "ctx")
            for (int i = 1; i < parts.Length; i++)
            {
                var partName = parts[i];
                if (string.IsNullOrEmpty(partName)) return currentType; // Выражение заканчивается на точку

                // Ищем свойство или метод без учета регистра для надежности
                var member = currentType.GetMember(partName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase).FirstOrDefault();
                if (member == null) return null; // Член не найден

                if (member is PropertyInfo prop)
                {
                    currentType = prop.PropertyType;
                }
                else if (member is MethodInfo method)
                {
                    currentType = method.ReturnType;
                }
                else if (member is FieldInfo field)
                {
                    currentType = field.FieldType;
                }
                else
                {
                    return null; // Это что-то другое, что мы не можем интроспектировать дальше (например, событие)
                }
            }
            
            return currentType;
        }

        private string GetWordBeforeCaret(TextArea textArea)
        {
            int offset = textArea.Caret.Offset;
            if (offset == 0) return string.Empty;

            int start = TextUtilities.GetNextCaretPosition(textArea.Document, offset, LogicalDirection.Backward, CaretPositioningMode.WordStart);
            
            if (start < 0) return string.Empty;
            
            return textArea.Document.GetText(start, offset - start);
        }
        
        // --- START OF CHANGES: Unified Auto-scroll handler ---
        private void HandleAutoScroll(DragEventArgs e)
        {
            const double scrollThreshold = 30.0;
            const double scrollSpeed = 5.0; // Slower, more controlled speed

            var scrollViewer = GroupsScrollViewer;
            if (scrollViewer == null) return;

            Point position = e.GetPosition(scrollViewer);

            if (position.Y < scrollThreshold)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollSpeed);
            }
            else if (position.Y > scrollViewer.ActualHeight - scrollThreshold)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollSpeed);
            }
        }

        private void AvailableField_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;
            
            if (sender is ListBox listBox && e.OriginalSource is DependencyObject source)
            {
                var listBoxItem = FindVisualParent<ListBoxItem>(source);
                if (listBoxItem == null) return;

                // This logic handles selection before a potential drag operation.
                if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == ModifierKeys.None)
                {
                    // If the item is already selected, we are likely initiating a drag
                    // of the entire selection. We handle the event to prevent the
                    // ListBox from clearing the other selected items.
                    if (listBoxItem.IsSelected)
                    {
                        e.Handled = true;
                    }
                    // If the item is not selected, this is a new single selection.
                    // Clear the previous selection and select the current item.
                    else
                    {
                        listBox.SelectedItems.Clear();
                        listBoxItem.IsSelected = true;
        }
                }
            }
        }
        
        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindVisualParent<T>(parentObject);
        }

        private void TabDefinition_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // START OF CHANGE: Check if the click originated on a button. If so, do nothing.
            // This allows button commands to fire without being intercepted by the drag-drop logic.
            if (e.OriginalSource is DependencyObject source)
            {
                var button = FindVisualParent<Button>(source);
                if (button != null)
                {
                    return;
                }
            }
            // END OF CHANGE

            if (sender is FrameworkElement element && element.DataContext is WorkflowTabDefinition tabDef)
            {
                // Select the tab on click
                _viewModel.SelectedTab = tabDef;

                // Handle drag for reordering
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    DragDrop.DoDragDrop(element, tabDef, DragDropEffects.Move);
                }
            }
        }
        
        private void TabName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element && DataContext is UIConstructorView viewModel)
            {
                if (element.DataContext is WorkflowTabDefinition tabDef)
                {
                    viewModel.ToggleRenameCommand.Execute(tabDef);
                    e.Handled = true;
                }
            }
        }

        private void TabList_DragOver(object sender, DragEventArgs e)
        {
            // Logic for showing drop indicator between tabs
            // This is more complex and can be added as a refinement. For now, we handle drop.
            e.Effects = e.Data.GetDataPresent(typeof(WorkflowTabDefinition)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }
        
        private void Tab_DragOver(object sender, DragEventArgs e)
        {
            HideTabDropIndicator();

            if (e.Data.GetDataPresent(typeof(WorkflowTabDefinition)) && sender is StackPanel element)
            {
                var position = e.GetPosition(element);

                var indicator = position.Y < element.ActualHeight / 2
                    ? FindVisualChild<Border>(element, "TabDropIndicatorBefore")
                    : FindVisualChild<Border>(element, "TabDropIndicatorAfter");
            
                if (indicator != null)
                {
                    indicator.Visibility = Visibility.Visible;
                    _lastTabIndicator = indicator;
                }
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Tab_DragLeave(object sender, DragEventArgs e)
        {
            HideTabDropIndicator();
            e.Handled = true;
        }

        private void Tab_Drop(object sender, DragEventArgs e)
        {
            if (sender is not StackPanel dropTarget ||
                dropTarget.DataContext is not WorkflowTabDefinition targetTab ||
                e.Data.GetData(typeof(WorkflowTabDefinition)) is not WorkflowTabDefinition draggedTab ||
                draggedTab == targetTab ||
                _viewModel == null)
            {
                HideAllIndicators();
                return;
            }

            var oldIndex = _viewModel.Workflow.Tabs.IndexOf(draggedTab);
            var targetIndex = _viewModel.Workflow.Tabs.IndexOf(targetTab);

            var indicatorAfter = FindVisualChild<Border>(dropTarget, "TabDropIndicatorAfter");
            if (indicatorAfter != null && indicatorAfter.Visibility == Visibility.Visible)
            {
                targetIndex++;
            }
        
            _viewModel.MoveTab(oldIndex, targetIndex);
        
            HideAllIndicators();
            e.Handled = true;
        }

        private void TabList_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(WorkflowTabDefinition)) is WorkflowTabDefinition draggedTab && _viewModel != null)
            {
                var oldIndex = _viewModel.Workflow.Tabs.IndexOf(draggedTab);
                _viewModel.MoveTab(oldIndex, _viewModel.Workflow.Tabs.Count);
                e.Handled = true;
            }
        }

        private void TabDefinition_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(WorkflowGroup)))
            {
                e.Effects = DragDropEffects.Move;
                if(sender is FrameworkElement fe) fe.Opacity = 0.7;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }
        
        private void TabDefinition_DragLeave(object sender, DragEventArgs e)
        {
            if(sender is FrameworkElement fe) fe.Opacity = 1.0;
        }

        private void TabDefinition_Drop(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is WorkflowTabDefinition targetTab)
            {
                fe.Opacity = 1.0;
                if (e.Data.GetData(typeof(Tuple<WorkflowGroupViewModel, WorkflowTabDefinition>)) is Tuple<WorkflowGroupViewModel, WorkflowTabDefinition> data)
                {
                    _viewModel.MoveGroupToTab(data.Item1, targetTab);
                    e.Handled = true;
                }
            }
        }

        private void UnassignedGroups_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(Tuple<WorkflowGroup, WorkflowTabDefinition>)))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void UnassignedGroups_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(Tuple<WorkflowGroupViewModel, WorkflowTabDefinition>)) is Tuple<WorkflowGroupViewModel, WorkflowTabDefinition> data)
            {
                // Move group to "unassigned" by passing a null target tab
                _viewModel.MoveGroupToTab(data.Item1, null);
                e.Handled = true;
            }
        }

        private void GroupHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source != null && source != sender as DependencyObject)
            {
                if (source is Button) return;
                source = VisualTreeHelper.GetParent(source);
            }

            if (sender is FrameworkElement element && element.DataContext is WorkflowGroupViewModel groupVm)
            {
                if (_viewModel.UnassignedGroups.Contains(groupVm))
                {
                    var dragData = new Tuple<WorkflowGroupViewModel, WorkflowTabDefinition>(groupVm, null);
                    DragDrop.DoDragDrop(element, dragData, DragDropEffects.Move);
                }
                else
                {
                    var dragData = new Tuple<WorkflowGroupViewModel, WorkflowTabDefinition>(groupVm, _viewModel.SelectedTab);
                    DragDrop.DoDragDrop(element, dragData, DragDropEffects.Move);
                }
            }
        }

        private void Group_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Do not handle the event, allowing it to bubble up to the parent window,
                // which is responsible for processing dropped files.
                return;
            }
            
            HandleAutoScroll(e);
            HideFieldDropIndicator();
            HideGroupDropIndicator();
            e.Effects = DragDropEffects.None;

            if (sender is not StackPanel dropTarget) return;

            if (e.Data.GetDataPresent(typeof(Tuple<WorkflowGroupViewModel, WorkflowTabDefinition>)))
            {
                var position = e.GetPosition(dropTarget);
                var indicator = position.Y < dropTarget.ActualHeight / 2
                    ? FindVisualChild<Border>(dropTarget, "GroupDropIndicatorBefore")
                    : FindVisualChild<Border>(dropTarget, "GroupDropIndicatorAfter");

                if (indicator != null)
                {
                    indicator.Visibility = Visibility.Visible;
                    _lastGroupIndicator = indicator;
                }
                e.Effects = DragDropEffects.Move;
            }
            else if (e.Data.GetDataPresent(typeof(List<WorkflowField>)) ||
                     e.Data.GetDataPresent(typeof(Tuple<List<WorkflowField>, WorkflowGroup>)))
            {
                var targetGroupVM = dropTarget.DataContext as WorkflowGroupViewModel;
                if (targetGroupVM != null && !targetGroupVM.Tabs.SelectMany(t => t.Model.Fields).Any())
                {
                    var expander = FindVisualChild<Expander>(dropTarget);
                    if (expander?.Content is Border contentBorder)
                    {
                        _currentGroupHighlight = contentBorder;
                        contentBorder.Background = new SolidColorBrush(Color.FromArgb(50, 0, 122, 204));
                    }
                }
                e.Effects = DragDropEffects.Move;
            }
            
            e.Handled = true;
        }

        private void Group_DragLeave(object sender, DragEventArgs e)
        {
            HideGroupDropIndicator();
            if (_currentGroupHighlight != null)
            {
                _currentGroupHighlight.Background = Brushes.Transparent;
                _currentGroupHighlight = null;
            }
        }

        private void Group_Drop(object sender, DragEventArgs e)
        {
            if (sender is not StackPanel dropTarget || dropTarget.DataContext is not WorkflowGroupViewModel targetGroupVM || DataContext is not UIConstructorView viewModel)
            {
                HideAllIndicators();
                return;
            }

            if (e.Data.GetData(typeof(Tuple<WorkflowGroupViewModel, WorkflowTabDefinition>)) is Tuple<WorkflowGroupViewModel, WorkflowTabDefinition> draggedTuple)
            {
                var sourceGroupVM = draggedTuple.Item1;
                if (sourceGroupVM != null && sourceGroupVM != targetGroupVM)
                {
                    var targetIndex = viewModel.SelectedTabGroups.IndexOf(targetGroupVM);

                    var indicatorAfter = FindVisualChild<Border>(dropTarget, "GroupDropIndicatorAfter");
                    if (indicatorAfter != null && indicatorAfter.Visibility == Visibility.Visible)
                    {
                        targetIndex++;
                    }
                    viewModel.MoveGroupToTab(sourceGroupVM, viewModel.SelectedTab, targetIndex);
                }
            }
            else if (e.Data.GetData(typeof(Tuple<List<WorkflowField>, WorkflowGroup>)) is Tuple<List<WorkflowField>, WorkflowGroup> draggedData)
            {
                // This is a MOVE operation from another group.
                var sourceGroupModel = draggedData.Item2;
                var fieldsToMove = draggedData.Item1;

                // Find the source tab for the first field.
                var sourceTab = sourceGroupModel.Tabs.FirstOrDefault(t => t.Fields.Contains(fieldsToMove.First()));
                if (sourceTab == null) return;

                // Determine the target tab within the target group.
                var targetTab = targetGroupVM.SelectedTab?.Model ?? targetGroupVM.Model.Tabs.FirstOrDefault();
                if (targetTab == null) return; // Can't drop if there's no sub-tab.

                // Move each field.
                foreach (var field in fieldsToMove)
                {
                    viewModel.MoveFieldToSubTab(field, sourceTab, targetTab, -1); // -1 adds to the end.
                }
            }
            else if (e.Data.GetData(typeof(List<WorkflowField>)) is List<WorkflowField> newFields)
            {
                viewModel.AddFieldToGroupAtIndex(newFields, targetGroupVM);
            }

            HideAllIndicators();
            e.Handled = true;
        }

        private void Field_DragOver(object sender, DragEventArgs e)
        {
            HandleAutoScroll(e);
            
            // --- START OF CHANGE: Also ignore file drag operations ---
            // If a file, group, or sub-tab is being dragged, do not show a drop indicator.
            if (e.Data.GetDataPresent(DataFormats.FileDrop) ||
                e.Data.GetDataPresent(typeof(Tuple<WorkflowGroupViewModel, WorkflowTabDefinition>)) ||
                e.Data.GetDataPresent(typeof(Tuple<WorkflowGroupTabViewModel, WorkflowGroupViewModel>)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return; // Stop processing here
            }
            // --- END OF CHANGE ---

            HideFieldDropIndicator();

            if (sender is not StackPanel element) return;

            var position = e.GetPosition(element);
            
            var indicator = position.Y < element.ActualHeight / 2
                ? FindVisualChild<Border>(element, "DropIndicatorBefore")
                : FindVisualChild<Border>(element, "DropIndicatorAfter");

            if (indicator != null)
            {
                indicator.Visibility = Visibility.Visible;
                _lastFieldIndicator = indicator;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void Field_DragLeave(object sender, DragEventArgs e)
        {
            HideFieldDropIndicator();
            e.Handled = true;
        }
        
        private void SubTabName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element && element.DataContext is WorkflowGroupTabViewModel tabVm)
            {
                // Для вкладок внутри групп нет глобальной команды, управляем напрямую
                tabVm.IsRenaming = true;
                e.Handled = true;
            }
        }

        private void GroupSubTabItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // On mouse down, we only record the starting point and the data to be dragged.
            // We do NOT start the drag-and-drop yet.
            _startPoint = e.GetPosition(null);
            
            if (sender is FrameworkElement element &&
                element.DataContext is WorkflowGroupTabViewModel tabVm &&
                FindVisualParent<Expander>(element)?.DataContext is WorkflowGroupViewModel groupVm)
            {
                _dragData = new Tuple<WorkflowGroupTabViewModel, WorkflowGroupViewModel>(tabVm, groupVm);
            }
            else
            {
                _dragData = null;
            }
        }
        
        private void GroupSubTabItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // This is where the drag-and-drop is actually initiated.
            if (e.LeftButton == MouseButtonState.Pressed && _dragData != null)
            {
                Point position = e.GetPosition(null);
                Vector diff = _startPoint - position;

                // Check if the mouse has moved far enough to be considered a drag.
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    // Start the drag operation.
                    if (sender is FrameworkElement element)
                    {
                        DragDrop.DoDragDrop(element, _dragData, DragDropEffects.Move);
                    }
                    // Reset the drag data to prevent this from firing again.
                    _dragData = null;
                }
            }
        }
        
        private void GroupSubTabItem_DragOver(object sender, DragEventArgs e)
        {
            HideSubTabDropIndicator();
            e.Effects = DragDropEffects.None;

            if (sender is not FrameworkElement dropTarget || dropTarget.DataContext is not WorkflowGroupTabViewModel targetTabVm) return;

            var targetGroupVm = FindVisualParent<Expander>(dropTarget)?.DataContext as WorkflowGroupViewModel;
            if (targetGroupVm == null) return;
        
            var position = e.GetPosition(dropTarget);

            // Allow reordering sub-tabs AND moving them between groups.
            if (e.Data.GetData(typeof(Tuple<WorkflowGroupTabViewModel, WorkflowGroupViewModel>)) is Tuple<WorkflowGroupTabViewModel, WorkflowGroupViewModel> draggedTabData)
            {
                var indicator = position.X < dropTarget.ActualWidth / 2
                    ? FindVisualChild<Border>(dropTarget, "SubTabDropIndicatorBefore")
                    : FindVisualChild<Border>(dropTarget, "SubTabDropIndicatorAfter");

                if (indicator != null)
                {
                    indicator.Visibility = Visibility.Visible;
                    _lastSubTabIndicator = indicator;
                }
                e.Effects = DragDropEffects.Move;
            }
            // Allow dropping a field onto a sub-tab.
            else if (e.Data.GetDataPresent(typeof(List<WorkflowField>)) || 
                     e.Data.GetDataPresent(typeof(Tuple<List<WorkflowField>, WorkflowGroup>)))
            {
                e.Effects = DragDropEffects.Move;
            }
        
            e.Handled = true;
        }

        private void GroupSubTabItem_DragLeave(object sender, DragEventArgs e)
        {
            HideSubTabDropIndicator();
        }

        private void GroupSubTabItem_Drop(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement dropTarget || dropTarget.DataContext is not WorkflowGroupTabViewModel targetTabVm)
            {
                HideAllIndicators();
                return;
            }
            
            var targetGroupVm = FindVisualParent<Expander>(dropTarget)?.DataContext as WorkflowGroupViewModel;
            if (targetGroupVm == null)
            {
                HideAllIndicators();
                return;
            }

            // --- START OF CHANGE: Determine drop position BEFORE hiding indicators ---
            Point position = e.GetPosition(dropTarget);
            bool dropAfter = position.X >= dropTarget.ActualWidth / 2;
            
            HideAllIndicators(); // Now it's safe to hide them.
            // --- END OF CHANGE ---

            if (e.Data.GetData(typeof(Tuple<WorkflowGroupTabViewModel, WorkflowGroupViewModel>)) is Tuple<WorkflowGroupTabViewModel, WorkflowGroupViewModel> draggedTabData)
            {
                var targetIndex = targetGroupVm.Tabs.IndexOf(targetTabVm);
                
                // --- START OF CHANGE: Use the calculated drop position ---
                if (dropAfter)
                {
                    targetIndex++;
                }
                // --- END OF CHANGE ---

                // If the source and target groups are the same, it's a reorder.
                if (draggedTabData.Item2 == targetGroupVm)
                {
                    _viewModel.MoveSubTab(draggedTabData.Item1.Model, targetGroupVm.Model, targetIndex);
                }
                // Otherwise, it's a move between groups.
                else
                {
                    _viewModel.MoveSubTabToGroup(draggedTabData.Item1.Model, draggedTabData.Item2.Model, targetGroupVm.Model, targetIndex);
                }
            }
            else if (e.Data.GetData(typeof(Tuple<List<WorkflowField>, WorkflowGroup>)) is Tuple<List<WorkflowField>, WorkflowGroup> fieldData)
            {
                var sourceTab = fieldData.Item2.Tabs.FirstOrDefault(t => t.Fields.Contains(fieldData.Item1.First()));
                if (sourceTab != null)
                {
                    foreach (var field in fieldData.Item1)
                    {
                        _viewModel.MoveFieldToSubTab(field, sourceTab, targetTabVm.Model, -1);
                }
            }
            }
            else if (e.Data.GetData(typeof(List<WorkflowField>)) is List<WorkflowField> newFields)
            {
                foreach (var newField in newFields)
                {
                    // Clone the field to avoid modifying the original in the available list
                    var fieldToAdd = newField.Clone();

                    // Respect the "Use Prefix" checkbox setting from the ViewModel
                    if (!_viewModel.UseNodeTitlePrefix && fieldToAdd.Name.Contains("::"))
                    {
                        fieldToAdd.Name = fieldToAdd.Name.Split(new[] { "::" }, 2, StringSplitOptions.None)[1];
                    }

                    // Add the potentially modified field to the end of the tab
                    _viewModel.AddFieldToSubTabAtIndex(fieldToAdd, targetTabVm.Model, -1);
                }
            }

            e.Handled = true;
        }

        private void GroupSubTabContent_Drop(object sender, DragEventArgs e)
        {
            HideAllIndicators();
            if (sender is not FrameworkElement dropTarget || dropTarget.DataContext is not WorkflowGroupTabViewModel targetTabVm) return;

            var targetGroupVm = FindVisualParent<Expander>(dropTarget)?.DataContext as WorkflowGroupViewModel;
            if (targetGroupVm == null) return;

            if (e.Data.GetData(typeof(Tuple<WorkflowGroupTabViewModel, WorkflowGroupViewModel>)) is Tuple<WorkflowGroupTabViewModel, WorkflowGroupViewModel> draggedTabData)
            {
                _viewModel.MoveSubTabToGroup(draggedTabData.Item1.Model, draggedTabData.Item2.Model, targetGroupVm.Model);
                e.Handled = true;
                return;
            }

            if (e.Data.GetData(typeof(Tuple<List<WorkflowField>, WorkflowGroup>)) is Tuple<List<WorkflowField>, WorkflowGroup> draggedData)
            {
                var sourceTab = draggedData.Item2.Tabs.FirstOrDefault(t => t.Fields.Contains(draggedData.Item1.First()));
                if (sourceTab != null)
                {
                    foreach (var field in draggedData.Item1)
                    {
                        _viewModel.MoveFieldToSubTab(field, sourceTab, targetTabVm.Model, -1);
                }
            }
            }
            else if (e.Data.GetData(typeof(List<WorkflowField>)) is List<WorkflowField> newFields)
            {
                foreach (var newField in newFields)
                {
                _viewModel.AddFieldToSubTabAtIndex(newField, targetTabVm.Model);
            }
            }

            e.Handled = true;
        }

        private void Field_Drop(object sender, DragEventArgs e)
        {
            // --- START OF CHANGE: Immediately exit if this is a file drop operation ---
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Let the main window's drop handler process the file.
                HideAllIndicators();
                return;
            }
            // --- END OF CHANGE ---
            
            if (sender is not StackPanel dropTargetElement || dropTargetElement.DataContext is not WorkflowField targetField || DataContext is not UIConstructorView viewModel)
            {
                HideAllIndicators();
                return;
            }

            var targetGroup = viewModel.Workflow.Groups.FirstOrDefault(g => g.Tabs.Any(t => t.Fields.Contains(targetField)));
            if (targetGroup == null) return;
            var targetTab = targetGroup.Tabs.FirstOrDefault(t => t.Fields.Contains(targetField));
            if (targetTab == null) return;
            
            var targetIndex = targetTab.Fields.IndexOf(targetField);
            
            var indicatorAfter = FindVisualChild<Border>(dropTargetElement, "DropIndicatorAfter");

            if (indicatorAfter != null && indicatorAfter.Visibility == Visibility.Visible) targetIndex++;

            if (e.Data.GetData(typeof(Tuple<List<WorkflowField>, WorkflowGroup>)) is Tuple<List<WorkflowField>, WorkflowGroup> draggedData)
            {
                var sourceTab = draggedData.Item2.Tabs.FirstOrDefault(t => t.Fields.Contains(draggedData.Item1.First()));
                if (sourceTab != null)
                {
                    foreach (var fieldToMove in draggedData.Item1)
                    {
                        viewModel.MoveFieldToSubTab(fieldToMove, sourceTab, targetTab, targetIndex);
                        if (targetIndex != -1) targetIndex++;
                }
            }
            }
            else if (e.Data.GetData(typeof(List<WorkflowField>)) is List<WorkflowField> newFields)
            {
                foreach (var newField in newFields)
                {
                    var fieldToAdd = newField.Clone(); // Create a safe copy to modify name

                    // Respect the "Use Prefix" checkbox setting from the ViewModel
                    if (!viewModel.UseNodeTitlePrefix && fieldToAdd.Name.Contains("::"))
                    {
                        fieldToAdd.Name = fieldToAdd.Name.Split(new[] { "::" }, 2, StringSplitOptions.None)[1];
                    }
                    
                    viewModel.AddFieldToSubTabAtIndex(fieldToAdd, targetTab, targetIndex);
                    if (targetIndex != -1) targetIndex++;
                }
            }

            HideAllIndicators();
            e.Handled = true;
        }
        
        private void DeleteField_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(Tuple<WorkflowField, WorkflowGroup>)))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void DeleteField_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(Tuple<List<WorkflowField>, WorkflowGroup>)) is Tuple<List<WorkflowField>, WorkflowGroup> draggedData &&
                DataContext is UIConstructorView viewModel)
            {
                foreach (var field in draggedData.Item1)
                {
                    viewModel.RemoveFieldFromGroupCommand.Execute(field);
                }
                e.Handled = true;
            }
        }
        
        private void HideAllIndicators()
        {
            HideFieldDropIndicator();
            HideGroupDropIndicator();
            // START OF CHANGES: Hide tab indicators as well
            HideTabDropIndicator();
            HideSubTabDropIndicator();
            // END OF CHANGES
            if (_currentGroupHighlight != null)
            {
                _currentGroupHighlight.Background = Brushes.Transparent;
                _currentGroupHighlight = null;
            }
        }
        
        private void HideSubTabDropIndicator()
        {
            if (_lastSubTabIndicator != null)
            {
                _lastSubTabIndicator.Visibility = Visibility.Collapsed;
                _lastSubTabIndicator = null;
            }
        }
        
        private void HideTabDropIndicator()
        {
            if (_lastTabIndicator != null)
            {
                _lastTabIndicator.Visibility = Visibility.Collapsed;
                _lastTabIndicator = null;
            }
        }

        private void HideFieldDropIndicator()
        {
            if (_lastFieldIndicator != null)
            {
                _lastFieldIndicator.Visibility = Visibility.Collapsed;
                _lastFieldIndicator = null;
            }
        }

        private void HideGroupDropIndicator()
        {
            if (_lastGroupIndicator != null)
            {
                _lastGroupIndicator.Visibility = Visibility.Collapsed;
                _lastGroupIndicator = null;
            }
        }
        
        private void GroupName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element && DataContext is UIConstructorView viewModel)
            {
                viewModel.ToggleRenameCommand.Execute(element.DataContext);
                e.Handled = true;
            }
        }

        private void FieldName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement element && DataContext is UIConstructorView viewModel)
            {
                viewModel.ToggleRenameCommand.Execute(element.DataContext);
                e.Handled = true;
            }
        }

        private void InlineTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox || DataContext is not UIConstructorView viewModel) return;
            var dataContext = textBox.DataContext;

            if (dataContext is ActionNameViewModel actionVm && actionVm.IsRenaming)
            {
                viewModel.CommitActionRename(actionVm, textBox.Text);
            }
            else if (dataContext is WorkflowGroupTabViewModel subTabVm && subTabVm.IsRenaming)
            {
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                subTabVm.IsRenaming = false;
            }
            else if ((dataContext is WorkflowGroupViewModel gvm && gvm.IsRenaming) ||
                     (dataContext is WorkflowGroup g && g.IsRenaming) ||
                     (dataContext is WorkflowField f && f.IsRenaming) ||
                     (dataContext is WorkflowTabDefinition t && t.IsRenaming))
            {
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                StopEditing(dataContext);
            }
        }

        private void InlineTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox || DataContext is not UIConstructorView viewModel) return;
            var dataContext = textBox.DataContext;
            
            if (e.Key == Key.Enter)
            {
                if (dataContext is ActionNameViewModel actionVm) viewModel.CommitActionRename(actionVm, textBox.Text);
                else if (dataContext is WorkflowGroupTabViewModel subTabVm)
                {
                    textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    subTabVm.IsRenaming = false;
                }
                else if (dataContext is WorkflowGroupViewModel || dataContext is WorkflowGroup || dataContext is WorkflowField || dataContext is WorkflowTabDefinition)
                {
                    textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    StopEditing(dataContext);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (dataContext is ActionNameViewModel actionVm) viewModel.CancelActionRename(actionVm);
                else if (dataContext is WorkflowGroupTabViewModel subTabVm)
                {
                    textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget(); // Revert
                    subTabVm.IsRenaming = false;
                }
                else if (dataContext is WorkflowGroup || dataContext is WorkflowField || dataContext is WorkflowTabDefinition)
                {
                    textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget(); // Revert changes
                    StopEditing(dataContext);
                }
                e.Handled = true;
            }
        }
        
        private void ActionItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is UIConstructorView viewModel && sender is ListBoxItem { DataContext: ActionNameViewModel actionVm })
            {
                viewModel.StartActionRename(actionVm);
                // Предотвращаем дальнейшую обработку клика, которая может сбить фокус
                e.Handled = true; 
            }
        }

        // private void CommitEdit(TextBox textBox)
        // {
        //     textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        //     StopEditing(textBox.DataContext, "IsRenaming");
        // }

        private void StopEditing(object dataContext)
        {
            if (dataContext is WorkflowGroupViewModel gvm) gvm.IsRenaming = false;
            if (dataContext is WorkflowGroup g) g.IsRenaming = false;
            if (dataContext is WorkflowField f) f.IsRenaming = false;
            if (dataContext is WorkflowTabDefinition t) t.IsRenaming = false;
        }

        private void InlineTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox) textBox.SelectAll();
        }

        private void Control_StopsBubble_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void GroupedField_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not WorkflowField field) return;

            var listBox = FindVisualParent<ListBox>(element);
            if (listBox == null) return;
            
            object dragData;
            if (listBox.SelectedItems.Count > 1 && listBox.SelectedItems.Contains(field))
            {
                dragData = listBox.SelectedItems.Cast<object>().ToList();
            }
            else
                {
                dragData = field;
            }
            
            var expander = FindVisualParent<Expander>(element);
                    if (expander?.DataContext is WorkflowGroupViewModel groupVm)
                    {
                var finalDragData = new DataObject();
                finalDragData.SetData(typeof(List<object>), dragData);
                finalDragData.SetData(typeof(WorkflowGroup), groupVm.Model);
                
                DragDrop.DoDragDrop(element, finalDragData, DragDropEffects.Move);
                    }
                }
        
        private void ShowColorPickerPopup(object sender, MouseButtonEventArgs e)
        {
            var popup = FindResource("ColorPickerPopup") as System.Windows.Controls.Primitives.Popup;
            if (popup == null) return;

            var clickedElement = sender as FrameworkElement;
            if (clickedElement == null) return;

            popup.DataContext = clickedElement.DataContext;
    
            popup.IsOpen = true;

            e.Handled = true;
        }
        
        private void ClosePopupOnClick(object sender, RoutedEventArgs e)
        {
            var element = sender as DependencyObject;
            while (element != null)
            {
                if (element is Popup popup)
                {
                    popup.IsOpen = false;
                    return;
                }
                element = LogicalTreeHelper.GetParent(element);
            }
        }

        // --- START OF FIX: Helper to reliably find named elements in a DataTemplate ---
        private static T FindVisualChild<T>(DependencyObject parent, string childName = null) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T typedChild && (string.IsNullOrEmpty(childName) || (child is FrameworkElement fe && fe.Name == childName)))
                {
                    return typedChild;
                }

                T childOfChild = FindVisualChild<T>(child, childName);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }
        // --- END OF FIX ---
        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    textBox.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                var filePath = files[0];
                if (Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var jsonContent = File.ReadAllText(filePath);
                        var apiJson = JObject.Parse(jsonContent);

                        // Heuristic check to ensure it's not a full Comfizen workflow.
                        if (apiJson["prompt"] == null || apiJson["promptTemplate"] == null)
                        {
                            // english: Get the ViewModel and replace the API of the current workflow.
                            if (DataContext is UIConstructorView vm)
                            {
                                vm.ReplaceApiWorkflow(apiJson);
                                MessageBox.Show(
                                    LocalizationService.Instance["UIConstructor_ApiReplacedSuccess"],
                                    LocalizationService.Instance["UIConstructor_ApiReplacedTitle"],
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(ex, $"Failed to process dropped JSON file in UIConstructor: {filePath}");
                        MessageBox.Show($"Error reading dropped JSON file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        e.Handled = true;
                    }
                }
            }
        }
        
        /// <summary>
        /// Handles the PreviewMouseWheel event for the inner field lists.
        /// This method intercepts the scroll event and forwards it to the main GroupsScrollViewer,
        /// allowing the user to scroll through the entire list of groups even when the mouse
        /// is over a specific group's field list. This creates a seamless "smart scroll" experience.
        /// </summary>
        private void FieldList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };
                // Manually raise the event on the main scroll viewer
                GroupsScrollViewer.RaiseEvent(eventArg);
            }
        }
    }
}