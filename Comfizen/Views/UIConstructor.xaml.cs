using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
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
using PropertyChanged;
using Formatting = Newtonsoft.Json.Formatting;

namespace Comfizen
{
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
        private readonly AppSettings _settings;
        private readonly SliderDefaultsService _sliderDefaultsService;
        public IHighlightingDefinition PythonSyntaxHighlighting { get; }
        public ObservableCollection<string> ModelSubTypes { get; } = new();
        private bool _apiWasReplaced = false;
        
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
        
        // Constructor for a NEW workflow.
        public UIConstructorView() : this(new Workflow(), null) { }

        // Constructor for EDITING from a file path.
        public UIConstructorView(string? workflowRelativePath) 
            : this(LoadWorkflowFromFile(workflowRelativePath), workflowRelativePath) { }
        
        // --- START OF NEW PROPERTIES FOR TABS ---
        public ObservableCollection<WorkflowGroup> UnassignedGroups { get; } = new();
        public ObservableCollection<WorkflowGroup> SelectedTabGroups { get; } = new();

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
        
        public UIConstructorView(Workflow liveWorkflow, string? workflowRelativePath)
        {
            // Assign the live workflow object directly.
            Workflow = liveWorkflow;
            
            // All readonly and get-only properties are initialized here, inside the constructor.
            PythonSyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Python-Dark") ?? HighlightingManager.Instance.GetDefinition("Python");
            
            var settingsService = new SettingsService();
            _settings = settingsService.LoadSettings();
            _sliderDefaultsService = new SliderDefaultsService(_settings.SliderDefaults);
            _sessionManager = new SessionManager(_settings);
            _modelService = new ModelService(_settings);
            
            if (!liveWorkflow.IsLoaded && !liveWorkflow.Tabs.Any() && !liveWorkflow.Groups.Any())
            {
                // Create a default tab
                var defaultTab = new WorkflowTabDefinition { Name = LocalizationService.Instance["UIConstructor_NewTabDefaultName"] };
                liveWorkflow.Tabs.Add(defaultTab);

                // Create a default group
                var defaultGroup = new WorkflowGroup { Name = string.Format(LocalizationService.Instance["UIConstructor_NewGroupDefaultName"], 1) };
                liveWorkflow.Groups.Add(defaultGroup);

                // Link the group to the tab
                defaultTab.GroupIds.Add(defaultGroup.Id);
            }
            
            LoadCommand = new RelayCommand(_ => LoadApiWorkflow());
            SaveWorkflowCommand = new RelayCommand(param => SaveWorkflow(param as Window), 
                _ => !string.IsNullOrWhiteSpace(NewWorkflowName) && Workflow.IsLoaded);
            ExportApiWorkflowCommand = new RelayCommand(_ => ExportApiWorkflow(), _ => Workflow.IsLoaded);
            AddGroupCommand = new RelayCommand(_ => AddGroup());
            RemoveGroupCommand = new RelayCommand(param => RemoveGroup(param as WorkflowGroup));
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
            
            // --- Other initializations ---
            AddMarkdownFieldCommand = new RelayCommand(param => AddVirtualField(param as WorkflowGroup, FieldType.Markdown));
            AddScriptButtonFieldCommand = new RelayCommand(param => AddVirtualField(param as WorkflowGroup, FieldType.ScriptButton));
            AddNodeBypassFieldCommand = new RelayCommand(param => AddVirtualField(param as WorkflowGroup, FieldType.NodeBypass));
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
                    if (target is WorkflowGroup group) group.HighlightColor = colorHex;
                    else if (target is WorkflowField field) field.HighlightColor = colorHex;
                }
            });
            ClearHighlightColorCommand = new RelayCommand(param =>
            {
                if (param is WorkflowGroup group) group.HighlightColor = null;
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

            // Attach event handlers
            // this.PropertyChanged += (s, e) => {
            //     if (e.PropertyName == nameof(SelectedHookName)) OnSelectedHookChanged();
            //     if (e.PropertyName == nameof(SelectedActionName)) OnSelectedActionChanged();
            //     if (e.PropertyName == nameof(SearchFilter) || e.PropertyName == nameof(Workflow)) UpdateAvailableFields();
            // };
            SelectedHookScript.TextChanged += (s, e) => SaveHookScript();
            SelectedActionScript.TextChanged += (s, e) => SaveActionScript();
            
            // --- START OF NEW COMMANDS ---
            AddTabCommand = new RelayCommand(_ => AddNewTab());
            RemoveTabCommand = new RelayCommand(param => RemoveTab(param as WorkflowTabDefinition));
            // --- END OF NEW COMMANDS ---

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
                RefreshActionNames();
                // --- ADDED: Initialize tabs and groups ---
                UpdateGroupAssignments();
                SelectedTab = Workflow.Tabs.FirstOrDefault();
                // ---
            }
            
            foreach (var group in Workflow.Groups)
            {
                foreach (var field in group.Fields)
                {
                    field.PropertyChanged += OnFieldPropertyChanged;
                }
            }
        }
        
        // The Workflow property is now set in the constructor.
        public Workflow Workflow { get; private set; }
        public ICommand LoadCommand { get; }
        public ICommand SaveWorkflowCommand { get; }
        public ICommand ExportApiWorkflowCommand { get; }
        public ICommand AddGroupCommand { get; }
        public ICommand RemoveGroupCommand { get; }
        public ICommand RemoveFieldFromGroupCommand { get; }
        public ICommand ToggleRenameCommand { get; }
        public ICommand SetHighlightColorCommand { get; }
        public ICommand ClearHighlightColorCommand { get; }
        public ICommand RemoveBypassNodeIdCommand { get; }
        public ICommand AddBypassNodeIdCommand { get; }
        public ObservableCollection<ColorInfo> ColorPalette { get; }

        public string NewWorkflowName { get; set; }
        public string SearchFilter { get; set; }

        public ObservableCollection<WorkflowField> AvailableFields { get; } = new();
        public ObservableCollection<NodeInfo> WorkflowNodes { get; } = new();

        public ObservableCollection<FieldType> FieldTypes { get; } =
            new(Enum.GetValues(typeof(FieldType)).Cast<FieldType>()
                .Where(t => t != FieldType.Markdown && t != FieldType.ScriptButton && t != FieldType.NodeBypass));

        public event PropertyChangedEventHandler? PropertyChanged;
        
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
        
        private void AddVirtualField(WorkflowGroup group, FieldType type)
        {
            if (group == null) return;

            string baseName = "New Field";
            switch (type)
            {
                case FieldType.Markdown:
                    baseName = "Markdown Block";
                    break;
                case FieldType.ScriptButton:
                    baseName = "New Action Button";
                    break;
                case FieldType.NodeBypass:
                    baseName = "New Bypass Switch";
                    break;
            }
            string newName = baseName;
            int counter = 1;
    
            // Гарантируем уникальное имя внутри группы
            while (group.Fields.Any(f => f.Name == newName))
            {
                newName = $"{baseName} {++counter}";
            }

            var newField = new WorkflowField
            {
                Name = newName,
                Type = type,
                // Создаем уникальный путь, который никогда не пересечется с реальным API
                Path = $"virtual_{type.ToString().ToLower()}_{Guid.NewGuid()}"
            };

            if (type == FieldType.Markdown)
            {
                newField.DefaultValue = "# " + newName + "\n\nEdit this text.";
            }

            group.Fields.Add(newField);
        }
        
        // --- SCRIPTING METHODS ---
        private void TestSelectedScript()
        {
            if (SelectedActionName == null || string.IsNullOrWhiteSpace(SelectedActionScript.Text))
                return;

            Logger.Log($"--- Testing script action: '{SelectedActionName.Name}' ---");

            // Для теста мы создаем контекст с текущим состоянием workflow.
            // Если workflow не загружен, API будет null, что тоже является валидным тестовым случаем.
            var context = new ScriptContext(
                Workflow.LoadedApi, 
                new Dictionary<string, object>(), // Состояние state для теста пустое
                _settings, 
                null // output недоступен вне реального запуска
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
                foreach (var field in group.Fields)
                {
                    if (field.Type == FieldType.ScriptButton && field.ActionName == oldName)
                    {
                        field.ActionName = newName;
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
                if (_itemBeingRenamed is WorkflowGroup g) g.IsRenaming = false;
                if (_itemBeingRenamed is WorkflowField f) f.IsRenaming = false;
                // --- START OF CHANGE ---
                if (_itemBeingRenamed is WorkflowTabDefinition t) t.IsRenaming = false;
                // --- END OF CHANGE ---
            }

            if (itemToRename is WorkflowGroup group)
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
            var dialog = new OpenFileDialog { Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*" };
            if (dialog.ShowDialog() == true)
            {
                Workflow.LoadApiWorkflow(dialog.FileName);
                UpdateAvailableFields();
                UpdateWorkflowNodesList();
                _apiWasReplaced = true;
            }
            RefreshActionNames();
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
                    var jsonContent = Workflow.LoadedApi.ToString(Formatting.Indented);
                    File.WriteAllText(dialog.FileName, jsonContent);
                    Logger.Log(string.Format(LocalizationService.Instance["UIConstructor_ExportSuccessMessage"], dialog.FileName));
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, string.Format(LocalizationService.Instance["UIConstructor_SaveErrorMessage"], ex.Message));
                }
        }
        
        // --- START OF NEW/MODIFIED METHODS FOR TABS ---
        private void UpdateGroupAssignments()
        {
            if (Workflow == null || Workflow.Groups == null) return;
            
            // Re-subscribe to collection changed events
            Workflow.Groups.CollectionChanged -= OnWorkflowGroupsChanged;
            Workflow.Groups.CollectionChanged += OnWorkflowGroupsChanged;
            Workflow.Tabs.CollectionChanged -= OnWorkflowTabsChanged;
            Workflow.Tabs.CollectionChanged += OnWorkflowTabsChanged;

            var allGroupIdsInTabs = Workflow.Tabs.SelectMany(t => t.GroupIds).ToHashSet();
            
            // Update Unassigned Groups
            UnassignedGroups.Clear();
            foreach (var group in Workflow.Groups.Where(g => !allGroupIdsInTabs.Contains(g.Id)))
            {
                UnassignedGroups.Add(group);
            }

            // Update Groups for the Selected Tab
            SelectedTabGroups.Clear();
            if (SelectedTab != null)
            {
                // Ensure the tab exists in the main workflow collection
                if (!Workflow.Tabs.Contains(SelectedTab))
                {
                    SelectedTab = null;
                    return;
                }

                // Create a lookup for quick access
                var groupLookup = Workflow.Groups.ToDictionary(g => g.Id);
                
                // Add groups in the order specified by the Tab's GroupIds
                foreach (var groupId in SelectedTab.GroupIds)
                {
                    if (groupLookup.TryGetValue(groupId, out var group))
                    {
                        SelectedTabGroups.Add(group);
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
            if (tab == null || MessageBox.Show(string.Format(LocalizationService.Instance["UIConstructor_ConfirmDeleteTabMessage"], tab.Name), 
                LocalizationService.Instance["UIConstructor_ConfirmDeleteTabTitle"],
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            
            int index = Workflow.Tabs.IndexOf(tab);
            Workflow.Tabs.Remove(tab);
            
            // Select the next available tab or null
            if (Workflow.Tabs.Any())
            {
                SelectedTab = Workflow.Tabs.ElementAtOrDefault(index) ?? Workflow.Tabs.Last();
            }
            else
            {
                SelectedTab = null;
            }
            
            UpdateGroupAssignments();
        }

        public void MoveGroupToTab(WorkflowGroup group, WorkflowTabDefinition targetTab, int insertIndex = -1)
        {
            if (group == null) return;
            
            // 1. Remove from any previous tab
            foreach (var tab in Workflow.Tabs)
            {
                if (tab.GroupIds.Contains(group.Id))
                {
                    tab.GroupIds.Remove(group.Id);
                    break;
                }
            }

            // 2. Add to the new tab if one is specified
            if (targetTab != null)
            {
                if (insertIndex < 0 || insertIndex > targetTab.GroupIds.Count)
                {
                    targetTab.GroupIds.Add(group.Id);
                }
                else
                {
                    targetTab.GroupIds.Insert(insertIndex, group.Id);
                }
            }

            // 3. Refresh UI
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
            Workflow.Groups.Add(newGroup);

            // --- START OF CHANGE: Add new group directly to the selected tab ---
            if (SelectedTab != null)
            {
                MoveGroupToTab(newGroup, SelectedTab);
            }
            // --- END OF CHANGE ---
        }

        private void RemoveGroup(WorkflowGroup group)
        {
            if (group != null && MessageBox.Show(string.Format(LocalizationService.Instance["UIConstructor_ConfirmDeleteGroupMessage"], group.Name), 
                    LocalizationService.Instance["UIConstructor_ConfirmDeleteGroupTitle"],
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                // --- MODIFIED: Also remove from any tab definition ---
                MoveGroupToTab(group, null); 
                Workflow.Groups.Remove(group);
                UpdateAvailableFields();
                // UpdateGroupAssignments() is called by the CollectionChanged event
            }
        }

        private void RemoveField(WorkflowField field)
        {
            foreach (var group in Workflow.Groups)
                if (group.Fields.Contains(field))
                {
                    field.PropertyChanged -= OnFieldPropertyChanged;
                    group.Fields.Remove(field);
                    UpdateAvailableFields();
                    return;
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
            var usedFieldPaths = Workflow.Groups.SelectMany(g => g.Fields).Select(f => f.Path).ToHashSet();
            var available = allFields.Where(f => !usedFieldPaths.Contains(f.Path));
            if (!string.IsNullOrWhiteSpace(SearchFilter))
                available = available.Where(f => f.Name.IndexOf(SearchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            AvailableFields.Clear();
            foreach (var field in available.OrderBy(f => f.Name)) AvailableFields.Add(field);
        }

        public void AddFieldToGroupAtIndex(WorkflowField field, WorkflowGroup group, int targetIndex = -1)
        {
            if (field == null || group == null || group.Fields.Any(f => f.Path == field.Path)) return;
            var newField = new WorkflowField { Name = field.Name, Path = field.Path, Type = FieldType.Any };

            var rawFieldName = field.Name.Contains("::") 
                ? field.Name.Split(new[] { "::" }, 2, StringSplitOptions.None)[1] 
                : field.Name;

            if (rawFieldName.Equals("seed", StringComparison.OrdinalIgnoreCase))
            {
                newField.Type = FieldType.Seed;
            }
            
            newField.PropertyChanged += OnFieldPropertyChanged;
            if (targetIndex < 0 || targetIndex >= group.Fields.Count) group.Fields.Add(newField);
            else group.Fields.Insert(targetIndex, newField);
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

        public void AddFieldToGroup(WorkflowField field, WorkflowGroup group)
        {
            AddFieldToGroupAtIndex(field, group);
        }

        public void MoveField(WorkflowField field, WorkflowGroup sourceGroup, WorkflowGroup targetGroup, int targetIndex = -1)
        {
            // Эта логика остается прежней, так как она работает на уровне полей внутри групп
            if (field == null || sourceGroup == null || targetGroup == null) return;
            var oldIndex = sourceGroup.Fields.IndexOf(field);
            if (oldIndex == -1) return;
            sourceGroup.Fields.RemoveAt(oldIndex);
            var finalInsertIndex = targetIndex;
            if (finalInsertIndex == -1) finalInsertIndex = targetGroup.Fields.Count;
            else if (sourceGroup == targetGroup && oldIndex < targetIndex) finalInsertIndex--;
            if (finalInsertIndex > targetGroup.Fields.Count) finalInsertIndex = targetGroup.Fields.Count;
            targetGroup.Fields.Insert(finalInsertIndex, field);
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
        
        private Border? _currentGroupHighlight;
        private Border? _lastFieldIndicator;
        private Border? _lastGroupIndicator;
        private Border? _lastTabIndicator;
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
            _viewModel = new UIConstructorView();
            DataContext = _viewModel;
            AttachCompletionEvents();
            ApplyHyperlinksColor();
        }

        public UIConstructor(string workflowFileName)
        {
            InitializeComponent();
            _viewModel = new UIConstructorView(workflowFileName);
            DataContext = _viewModel;
            AttachCompletionEvents();
            ApplyHyperlinksColor();
        }
        
        // This is the new constructor that accepts the live workflow object.
        public UIConstructor(Workflow liveWorkflow, string workflowRelativePath)
        {
            InitializeComponent();
            // Pass the live object directly to the ViewModel.
            _viewModel = new UIConstructorView(liveWorkflow, workflowRelativePath);
            DataContext = _viewModel;
            AttachCompletionEvents();
            ApplyHyperlinksColor();
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
            if (e.Data.GetData(typeof(WorkflowGroup)) is WorkflowGroup group && _viewModel.SelectedTab != null)
            {
                _viewModel.MoveGroupToTab(group, _viewModel.SelectedTab);
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
            if (sender is FrameworkElement element && element.DataContext is WorkflowField field)
                DragDrop.DoDragDrop(element, field, DragDropEffects.Move);
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
                if (e.Data.GetData(typeof(WorkflowGroup)) is WorkflowGroup group)
                {
                    _viewModel.MoveGroupToTab(group, targetTab);
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
            if (e.Data.GetData(typeof(Tuple<WorkflowGroup, WorkflowTabDefinition>)) is Tuple<WorkflowGroup, WorkflowTabDefinition> data)
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

            if (sender is FrameworkElement element && element.DataContext is WorkflowGroup group)
            {
                // If the group is in the "unassigned" list, we just drag the group.
                if (_viewModel.UnassignedGroups.Contains(group))
                {
                    DragDrop.DoDragDrop(element, group, DragDropEffects.Move);
                }
                // If it's in a tab, we drag a tuple containing the group and its tab.
                else
                {
                    var dragData = new Tuple<WorkflowGroup, WorkflowTabDefinition>(group, _viewModel.SelectedTab);
                    DragDrop.DoDragDrop(element, dragData, DragDropEffects.Move);
                }
            }
        }

        private void Group_DragOver(object sender, DragEventArgs e)
        {
            HandleAutoScroll(e);
            HideFieldDropIndicator();
            HideGroupDropIndicator();
            e.Effects = DragDropEffects.None;

            if (sender is not StackPanel dropTarget) return;
            
            // --- START OF CHANGE: Accept both group types for drag operations ---
            if (e.Data.GetDataPresent(typeof(WorkflowGroup)) || e.Data.GetDataPresent(typeof(Tuple<WorkflowGroup, WorkflowTabDefinition>)))
                // --- END OF CHANGE ---
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
            else if (e.Data.GetDataPresent(typeof(WorkflowField)) ||
                     e.Data.GetDataPresent(typeof(Tuple<WorkflowField, WorkflowGroup>)))
            {
                var targetGroup = dropTarget.DataContext as WorkflowGroup;
                if (targetGroup != null && targetGroup.Fields.Count == 0)
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
            if (sender is not StackPanel dropTarget || dropTarget.DataContext is not WorkflowGroup targetGroup || DataContext is not UIConstructorView viewModel)
            {
                HideAllIndicators();
                return;
            }

            // --- START OF CHANGE: Unified logic for moving/reordering groups ---
            WorkflowGroup sourceGroup = null;
            if (e.Data.GetData(typeof(Tuple<WorkflowGroup, WorkflowTabDefinition>)) is Tuple<WorkflowGroup, WorkflowTabDefinition> draggedTuple)
            {
                sourceGroup = draggedTuple.Item1;
            }
            else if (e.Data.GetData(typeof(WorkflowGroup)) is WorkflowGroup draggedGroup)
            {
                sourceGroup = draggedGroup;
            }

            if (sourceGroup != null && sourceGroup != targetGroup)
            {
                var targetIndex = viewModel.SelectedTabGroups.IndexOf(targetGroup);
                
                var indicatorAfter = FindVisualChild<Border>(dropTarget, "GroupDropIndicatorAfter");
                if (indicatorAfter != null && indicatorAfter.Visibility == Visibility.Visible)
                {
                    targetIndex++;
                }
                
                viewModel.MoveGroupToTab(sourceGroup, viewModel.SelectedTab, targetIndex);
            }
            // --- END OF CHANGE ---
            else if (e.Data.GetData(typeof(Tuple<WorkflowField, WorkflowGroup>)) is Tuple<WorkflowField, WorkflowGroup> draggedData)
            {
                viewModel.MoveField(draggedData.Item1, draggedData.Item2, targetGroup);
            }
            else if (e.Data.GetData(typeof(WorkflowField)) is WorkflowField newField)
            {
                viewModel.AddFieldToGroup(newField, targetGroup);
            }

            HideAllIndicators();
            e.Handled = true;
        }

        private void Field_DragOver(object sender, DragEventArgs e)
        {
            HandleAutoScroll(e);
            
            // If a group is being dragged, do not show a drop indicator between fields.
            // This prevents the user from thinking a group can be dropped inside another group.
            if (e.Data.GetDataPresent(typeof(WorkflowGroup)) || 
                e.Data.GetDataPresent(typeof(Tuple<WorkflowGroup, WorkflowTabDefinition>)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return; // Stop processing here
            }

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

        private void Field_Drop(object sender, DragEventArgs e)
        {
            if (sender is not StackPanel dropTargetElement || dropTargetElement.DataContext is not WorkflowField targetField || DataContext is not UIConstructorView viewModel)
            {
                HideAllIndicators();
                return;
            }

            var targetGroup = viewModel.Workflow.Groups.FirstOrDefault(g => g.Fields.Contains(targetField));
            if (targetGroup == null) return;

            var targetIndex = targetGroup.Fields.IndexOf(targetField);
            
            var indicatorAfter = FindVisualChild<Border>(dropTargetElement, "DropIndicatorAfter");

            if (indicatorAfter != null && indicatorAfter.Visibility == Visibility.Visible) targetIndex++;

            if (e.Data.GetData(typeof(Tuple<WorkflowField, WorkflowGroup>)) is Tuple<WorkflowField, WorkflowGroup> draggedData)
                viewModel.MoveField(draggedData.Item1, draggedData.Item2, targetGroup, targetIndex);
            else if (e.Data.GetData(typeof(WorkflowField)) is WorkflowField newField)
                viewModel.AddFieldToGroupAtIndex(newField, targetGroup, targetIndex);

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
            if (e.Data.GetData(typeof(Tuple<WorkflowField, WorkflowGroup>)) is Tuple<WorkflowField, WorkflowGroup> draggedData &&
                DataContext is UIConstructorView viewModel)
            {
                viewModel.RemoveFieldFromGroupCommand.Execute(draggedData.Item1);
                e.Handled = true;
            }
        }
        
        private void HideAllIndicators()
        {
            HideFieldDropIndicator();
            HideGroupDropIndicator();
            // START OF CHANGES: Hide tab indicators as well
            HideTabDropIndicator();
            // END OF CHANGES
            if (_currentGroupHighlight != null)
            {
                _currentGroupHighlight.Background = Brushes.Transparent;
                _currentGroupHighlight = null;
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

            // --- START OF CHANGE: Unified and corrected logic for all renameable types ---
            if (dataContext is ActionNameViewModel actionVm && actionVm.IsRenaming)
            {
                viewModel.CommitActionRename(actionVm, textBox.Text);
            }
            else if ((dataContext is WorkflowGroup g && g.IsRenaming) ||
                     (dataContext is WorkflowField f && f.IsRenaming) ||
                     (dataContext is WorkflowTabDefinition t && t.IsRenaming))
            {
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                StopEditing(dataContext);
            }
            // --- END OF CHANGE ---
        }

        private void InlineTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox || DataContext is not UIConstructorView viewModel) return;
            var dataContext = textBox.DataContext;
            
            // --- START OF CHANGE: Unified and corrected logic for all renameable types ---
            if (e.Key == Key.Enter)
            {
                if (dataContext is ActionNameViewModel actionVm)
                {
                    viewModel.CommitActionRename(actionVm, textBox.Text);
                }
                else if (dataContext is WorkflowGroup || dataContext is WorkflowField || dataContext is WorkflowTabDefinition)
                {
                    textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    StopEditing(dataContext);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (dataContext is ActionNameViewModel actionVm)
                {
                    viewModel.CancelActionRename(actionVm);
                }
                else if (dataContext is WorkflowGroup || dataContext is WorkflowField || dataContext is WorkflowTabDefinition)
                {
                    textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget(); // Revert changes
                    StopEditing(dataContext);
                }
                e.Handled = true;
            }
            // --- END OF CHANGE ---
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
            if (dataContext is WorkflowGroup g) g.IsRenaming = false;
            if (dataContext is WorkflowField f) f.IsRenaming = false;
            // --- START OF CHANGE ---
            if (dataContext is WorkflowTabDefinition t) t.IsRenaming = false;
            // --- END OF CHANGE ---
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
            if (sender is FrameworkElement element && element.DataContext is WorkflowField field)
            {
                var parentItemsControl = VisualTreeHelper.GetParent(element);
                while (parentItemsControl != null && !(parentItemsControl is ItemsControl))
                    parentItemsControl = VisualTreeHelper.GetParent(parentItemsControl);
                if (parentItemsControl is ItemsControl itemsControl && itemsControl.DataContext is WorkflowGroup group)
                {
                    var dragData = new Tuple<WorkflowField, WorkflowGroup>(field, group);
                    DragDrop.DoDragDrop(element, dragData, DragDropEffects.Move);
                }
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
    }
}