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
        public IHighlightingDefinition PythonSyntaxHighlighting { get; }
        public ObservableCollection<string> ModelSubTypes { get; } = new();
        private bool _apiWasReplaced = false;
        
        // --- START OF SCRIPTING PROPERTIES ---
        public ObservableCollection<string> AvailableHooks { get; }
        public string SelectedHookName { get; set; }
        public TextDocument SelectedHookScript { get; set; } = new TextDocument();

        public ObservableCollection<ActionNameViewModel> ActionNames { get; } = new ObservableCollection<ActionNameViewModel>();
        public ActionNameViewModel SelectedActionName { get; set; }
        public TextDocument SelectedActionScript { get; set; } = new TextDocument();
        
        public ICommand AddActionCommand { get; }
        public ICommand RemoveActionCommand { get; }
        
        private string _originalActionName; // Для хранения имени перед переименованием
        
        public ICommand TestScriptCommand { get; }
        // --- END OF SCRIPTING PROPERTIES ---
        
        public ICommand AddMarkdownFieldCommand { get; }
        public ICommand AddScriptButtonFieldCommand { get; }
        
        public UIConstructorView(string? workflowRelativePath = null)
        {
            PythonSyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Python-Dark") ?? HighlightingManager.Instance.GetDefinition("Python");
            
            var settingsService = new SettingsService();
            _settings = settingsService.LoadSettings();
            _sessionManager = new SessionManager(_settings);
            _modelService = new ModelService(_settings);
            
            LoadCommand = new RelayCommand(_ => LoadApiWorkflow());
            SaveWorkflowCommand = new RelayCommand(param => SaveWorkflow(param as Window), 
                _ => !string.IsNullOrWhiteSpace(NewWorkflowName) && Workflow.IsLoaded);
            ExportApiWorkflowCommand = new RelayCommand(_ => ExportApiWorkflow(), _ => Workflow.IsLoaded);
            AddGroupCommand = new RelayCommand(_ => AddGroup());
            RemoveGroupCommand = new RelayCommand(param => RemoveGroup(param as WorkflowGroup));
            RemoveFieldFromGroupCommand = new RelayCommand(param => RemoveField(param as WorkflowField));
            ToggleRenameCommand = new RelayCommand(ToggleRename);
            
            // --- START OF SCRIPTING INITIALIZATION ---
            AvailableHooks = new ObservableCollection<string> { "on_workflow_load", "on_queue_start", "on_queue_finish", "on_output_received" };
            
            AddActionCommand = new RelayCommand(_ => AddNewAction());
            RemoveActionCommand = new RelayCommand(_ => RemoveSelectedAction(), _ => SelectedActionName != null);
            
            // Отслеживание изменений для хуков
            this.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(SelectedHookName)) OnSelectedHookChanged();
                if (e.PropertyName == nameof(SelectedActionName)) OnSelectedActionChanged();
            };
            
            // Сохранение скриптов при изменении текста
            SelectedHookScript.TextChanged += (s, e) => SaveHookScript();
            SelectedActionScript.TextChanged += (s, e) => SaveActionScript();
            
            TestScriptCommand = new RelayCommand(
                _ => TestSelectedScript(),
                _ => SelectedActionName != null && !string.IsNullOrWhiteSpace(SelectedActionScript.Text)
            );
            // --- END OF SCRIPTING INITIALIZATION ---
            
            // Initialize the color palette
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

            // Command to set the highlight color
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

            // Command to clear the highlight color
            ClearHighlightColorCommand = new RelayCommand(param =>
            {
                if (param is WorkflowGroup group) group.HighlightColor = null;
                else if (param is WorkflowField field) field.HighlightColor = null;
            });
            
            AddMarkdownFieldCommand = new RelayCommand(param => AddVirtualField(param as WorkflowGroup, FieldType.Markdown));
            AddScriptButtonFieldCommand = new RelayCommand(param => AddVirtualField(param as WorkflowGroup, FieldType.ScriptButton));

            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SearchFilter) || e.PropertyName == nameof(Workflow)) UpdateAvailableFields();
            };
            
            LoadModelSubTypesAsync();
            
            if (!string.IsNullOrEmpty(workflowRelativePath))
            {
                var fullPath = Path.Combine(Workflow.WorkflowsDir, workflowRelativePath);
                Workflow.LoadWorkflow(fullPath);

                NewWorkflowName = Path.ChangeExtension(workflowRelativePath, null);
                UpdateAvailableFields();
                RefreshActionNames();
            }
        }

        public Workflow Workflow { get; } = new();
        public ICommand LoadCommand { get; }
        public ICommand SaveWorkflowCommand { get; }
        public ICommand ExportApiWorkflowCommand { get; }
        public ICommand AddGroupCommand { get; }
        public ICommand RemoveGroupCommand { get; }
        public ICommand RemoveFieldFromGroupCommand { get; }
        public ICommand ToggleRenameCommand { get; }
        public ICommand SetHighlightColorCommand { get; }
        public ICommand ClearHighlightColorCommand { get; }
        public ObservableCollection<ColorInfo> ColorPalette { get; }

        public string NewWorkflowName { get; set; }
        public string SearchFilter { get; set; }

        public ObservableCollection<WorkflowField> AvailableFields { get; } = new();

        public ObservableCollection<FieldType> FieldTypes { get; } =
            new(Enum.GetValues(typeof(FieldType)).Cast<FieldType>());

        public event PropertyChangedEventHandler? PropertyChanged;
        
        private void AddVirtualField(WorkflowGroup group, FieldType type)
        {
            if (group == null) return;

            string baseName = type == FieldType.Markdown ? "Markdown Block" : "New Action Button";
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

            Logger.LogToConsole($"--- Testing script action: '{SelectedActionName.Name}' ---", LogLevel.Info, Colors.Magenta);

            // Для теста мы создаем контекст с текущим состоянием workflow.
            // Если workflow не загружен, API будет null, что тоже является валидным тестовым случаем.
            var context = new ScriptContext(
                Workflow.LoadedApi, 
                new Dictionary<string, object>(), // Состояние state для теста пустое
                _settings, 
                null // output недоступен вне реального запуска
            );
            
            PythonScriptingService.Instance.Execute(SelectedActionScript.Text, context);
            
            Logger.LogToConsole($"--- Test finished for: '{SelectedActionName.Name}' ---", LogLevel.Info, Colors.Magenta);
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
            }

            if (itemToRename is WorkflowGroup group)
            {
                group.IsRenaming = !group.IsRenaming;
                _itemBeingRenamed = group.IsRenaming ? group : null;
            }

            if (itemToRename is WorkflowField field)
            {
                field.IsRenaming = !field.IsRenaming;
                _itemBeingRenamed = field.IsRenaming ? field : null;
            }
        }

        private void LoadApiWorkflow()
        {
            var dialog = new OpenFileDialog { Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*" };
            if (dialog.ShowDialog() == true)
            {
                Workflow.LoadApiWorkflow(dialog.FileName);
                UpdateAvailableFields();
                _apiWasReplaced = true;
            }
            RefreshActionNames();
        }

        private void SaveWorkflow(Window window)
        {
            var workflowFullPath = Path.Combine(Workflow.WorkflowsDir, NewWorkflowName + ".json");
            
            var saveType = _apiWasReplaced ? WorkflowSaveType.ApiReplaced : WorkflowSaveType.LayoutOnly;

            if (saveType == WorkflowSaveType.ApiReplaced)
            {
                _sessionManager.ClearSession(workflowFullPath);
            }
            
            Workflow.SaveWorkflow(NewWorkflowName);
            
            GlobalEventManager.RaiseWorkflowSaved(workflowFullPath, saveType);
            
            MessageBox.Show(LocalizationService.Instance["UIConstructor_SaveSuccessMessage"], LocalizationService.Instance["UIConstructor_SaveSuccessTitle"], MessageBoxButton.OK, MessageBoxImage.Information);

            window?.Close();
        }

        private void ExportApiWorkflow()
        {
            if (Workflow.LoadedApi == null)
            {
                MessageBox.Show(LocalizationService.Instance["UIConstructor_ExportErrorMessage"], LocalizationService.Instance["UIConstructor_ExportErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show(string.Format(LocalizationService.Instance["UIConstructor_ExportSuccessMessage"], dialog.FileName), 
                        LocalizationService.Instance["UIConstructor_ExportSuccessTitle"],
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format(LocalizationService.Instance["UIConstructor_SaveErrorMessage"], ex.Message), 
                        LocalizationService.Instance["UIConstructor_SaveErrorTitle"], MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
        }

        private void AddGroup()
        {
            var newGroupName = string.Format(LocalizationService.Instance["UIConstructor_NewGroupDefaultName"], Workflow.Groups.Count + 1);
            Workflow.Groups.Add(new WorkflowGroup { Name = newGroupName });
        }

        private void RemoveGroup(WorkflowGroup group)
        {
            if (group != null && MessageBox.Show(string.Format(LocalizationService.Instance["UIConstructor_ConfirmDeleteGroupMessage"], group.Name), 
                LocalizationService.Instance["UIConstructor_ConfirmDeleteGroupTitle"],
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                Workflow.Groups.Remove(group);
                UpdateAvailableFields();
            }
        }

        private void RemoveField(WorkflowField field)
        {
            foreach (var group in Workflow.Groups)
                if (group.Fields.Contains(field))
                {
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
            if (targetIndex < 0 || targetIndex >= group.Fields.Count) group.Fields.Add(newField);
            else group.Fields.Insert(targetIndex, newField);
            UpdateAvailableFields();
        }

        public void AddFieldToGroup(WorkflowField field, WorkflowGroup group)
        {
            AddFieldToGroupAtIndex(field, group);
        }

        public void MoveField(WorkflowField field, WorkflowGroup sourceGroup, WorkflowGroup targetGroup,
            int targetIndex = -1)
        {
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
            DataContext = new UIConstructorView();
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

        private void GroupHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source != null && source != sender as DependencyObject)
            {
                if (source is Button) return;
                source = VisualTreeHelper.GetParent(source);
            }

            if (sender is FrameworkElement element && element.DataContext is WorkflowGroup group)
                DragDrop.DoDragDrop(element, group, DragDropEffects.Move);
        }

        private void Group_DragOver(object sender, DragEventArgs e)
        {
            HandleAutoScroll(e);
            HideFieldDropIndicator();
            HideGroupDropIndicator();
            e.Effects = DragDropEffects.None;

            if (sender is not StackPanel dropTarget) return;

            if (e.Data.GetDataPresent(typeof(WorkflowGroup)))
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

            if (e.Data.GetData(typeof(WorkflowGroup)) is WorkflowGroup sourceGroup && sourceGroup != targetGroup)
            {
                var oldIndex = viewModel.Workflow.Groups.IndexOf(sourceGroup);
                var targetIndex = viewModel.Workflow.Groups.IndexOf(targetGroup);
                
                var indicatorAfter = FindVisualChild<Border>(dropTarget, "GroupDropIndicatorAfter");
                
                if (indicatorAfter != null && indicatorAfter.Visibility == Visibility.Visible) targetIndex++;
                
                viewModel.MoveGroup(oldIndex, targetIndex);
            }
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

            if (e.Data.GetDataPresent(typeof(WorkflowGroup)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
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
            if (_currentGroupHighlight != null)
            {
                _currentGroupHighlight.Background = Brushes.Transparent;
                _currentGroupHighlight = null;
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
            // ==========================================================
            //     НАЧАЛО ИСПРАВЛЕНИЙ: Восстановленная логика
            // ==========================================================
            else if (dataContext is WorkflowGroup g && g.IsRenaming)
            {
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                StopEditing(g);
            }
            else if (dataContext is WorkflowField f && f.IsRenaming)
            {
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                StopEditing(f);
            }
            // ==========================================================
            //     КОНЕЦ ИСПРАВЛЕНИЙ
            // ==========================================================
        }

        private void InlineTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox || DataContext is not UIConstructorView viewModel) return;
            
            var dataContext = textBox.DataContext;

            if (e.Key == Key.Enter)
            {
                if (dataContext is ActionNameViewModel actionVm)
                {
                    viewModel.CommitActionRename(actionVm, textBox.Text);
                }
                // ==========================================================
                //     НАЧАЛО ИСПРАВЛЕНИЙ: Восстановленная логика
                // ==========================================================
                else if (dataContext is WorkflowGroup || dataContext is WorkflowField)
                {
                    // Принудительно обновляем источник привязки и выходим из режима редактирования
                    textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    StopEditing(dataContext);
                }
                // ==========================================================
                //     КОНЕЦ ИСПРАВЛЕНИЙ
                // ==========================================================
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (dataContext is ActionNameViewModel actionVm)
                {
                    viewModel.CancelActionRename(actionVm);
                }
                // ==========================================================
                //     НАЧАЛО ИСПРАВЛЕНИЙ: Восстановленная логика
                // ==========================================================
                else if (dataContext is WorkflowGroup || dataContext is WorkflowField)
                {
                    // Отменяем изменения и выходим из режима редактирования
                    textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                    StopEditing(dataContext);
                }
                // ==========================================================
                //     КОНЕЦ ИСПРАВЛЕНИЙ
                // ==========================================================
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
            if (dataContext is WorkflowGroup g) g.IsRenaming = false;
            if (dataContext is WorkflowField f) f.IsRenaming = false;
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
    }
}