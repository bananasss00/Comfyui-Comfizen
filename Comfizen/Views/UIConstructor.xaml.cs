using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Newtonsoft.Json;
using PropertyChanged;

namespace Comfizen
{
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
        public ObservableCollection<string> ModelSubTypes { get; } = new();
        private bool _apiWasReplaced = false;

        public UIConstructorView(string? workflowRelativePath = null)
        {
            var settingsService = new SettingsService();
            var settings = settingsService.LoadSettings();
            _sessionManager = new SessionManager(settings);
            _modelService = new ModelService(settings);
            
            LoadCommand = new RelayCommand(_ => LoadApiWorkflow());
            SaveWorkflowCommand = new RelayCommand(param => SaveWorkflow(param as Window), 
                _ => !string.IsNullOrWhiteSpace(NewWorkflowName) && Workflow.IsLoaded);
            ExportApiWorkflowCommand = new RelayCommand(_ => ExportApiWorkflow(), _ => Workflow.IsLoaded);
            AddGroupCommand = new RelayCommand(_ => AddGroup());
            RemoveGroupCommand = new RelayCommand(param => RemoveGroup(param as WorkflowGroup));
            RemoveFieldFromGroupCommand = new RelayCommand(param => RemoveField(param as WorkflowField));
            ToggleRenameCommand = new RelayCommand(ToggleRename);

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

        public string NewWorkflowName { get; set; }
        public string SearchFilter { get; set; }

        public ObservableCollection<WorkflowField> AvailableFields { get; } = new();

        public ObservableCollection<FieldType> FieldTypes { get; } =
            new(Enum.GetValues(typeof(FieldType)).Cast<FieldType>());

        public event PropertyChangedEventHandler? PropertyChanged;
        
        private async void LoadModelSubTypesAsync()
        {
            var types = await _modelService.GetModelTypesAsync();
            ModelSubTypes.Clear();
            foreach (var type in types.OrderBy(t => t.Name))
            {
                ModelSubTypes.Add(type.Name);
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

        public UIConstructor()
        {
            InitializeComponent();
            DataContext = new UIConstructorView();
        }

        public UIConstructor(string workflowFileName)
        {
            InitializeComponent();
            _viewModel = new UIConstructorView(workflowFileName);
            DataContext = _viewModel;
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
            HideFieldDropIndicator();
            HideGroupDropIndicator();
            e.Effects = DragDropEffects.None;

            var dropTarget = sender as StackPanel;
            if (dropTarget == null) return;

            if (e.Data.GetDataPresent(typeof(WorkflowGroup)))
            {
                var position = e.GetPosition(dropTarget);
                var indicator = position.Y < dropTarget.ActualHeight / 2
                    ? dropTarget.FindName("GroupDropIndicatorBefore") as Border
                    : dropTarget.FindName("GroupDropIndicatorAfter") as Border;

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
                if (dropTarget.FindName("GroupDropIndicatorBefore") is Border contentBorder)
                {
                    var innerBorder = VisualTreeHelper.GetChild(contentBorder.Parent, 1) as Border;
                    if (innerBorder != null)
                    {
                        _currentGroupHighlight = innerBorder;
                        innerBorder.Background = new SolidColorBrush(Color.FromArgb(50, 0, 122, 204));
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
            var dropTarget = sender as StackPanel;
            if (dropTarget?.DataContext is not WorkflowGroup targetGroup || DataContext is not UIConstructorView viewModel)
            {
                HideAllIndicators();
                return;
            }

            if (e.Data.GetData(typeof(WorkflowGroup)) is WorkflowGroup sourceGroup && sourceGroup != targetGroup)
            {
                var oldIndex = viewModel.Workflow.Groups.IndexOf(sourceGroup);
                var targetIndex = viewModel.Workflow.Groups.IndexOf(targetGroup);

                var indicatorAfter = dropTarget.FindName("GroupDropIndicatorAfter") as Border;
                if (indicatorAfter != null && indicatorAfter.Visibility == Visibility.Visible) targetIndex++;
                viewModel.MoveGroup(oldIndex, targetIndex);
            }
            else if (e.Data.GetData(typeof(Tuple<WorkflowField, WorkflowGroup>)) is Tuple<WorkflowField, WorkflowGroup>
                     draggedData)
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
            if (e.Data.GetDataPresent(typeof(WorkflowGroup)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            HideFieldDropIndicator();

            var element = sender as FrameworkElement;
            if (element == null) return;

            var position = e.GetPosition(element);

            var indicator = position.Y < element.ActualHeight / 2
                ? (element as StackPanel).FindName("DropIndicatorBefore") as Border
                : (element as StackPanel).FindName("DropIndicatorAfter") as Border;

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
            var dropTargetElement = sender as StackPanel;
            if (dropTargetElement?.DataContext is not WorkflowField targetField ||
                DataContext is not UIConstructorView viewModel)
            {
                HideAllIndicators();
                return;
            }

            var targetGroup = viewModel.Workflow.Groups.FirstOrDefault(g => g.Fields.Contains(targetField));
            if (targetGroup == null) return;

            var targetIndex = targetGroup.Fields.IndexOf(targetField);
            if (dropTargetElement.FindName("DropIndicatorAfter") is Border indicatorAfter &&
                indicatorAfter.Visibility == Visibility.Visible) targetIndex++;

            if (e.Data.GetData(typeof(Tuple<WorkflowField, WorkflowGroup>)) is Tuple<WorkflowField, WorkflowGroup>
                draggedData)
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
            if (sender is TextBox textBox) CommitEdit(textBox);
        }

        private void InlineTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                if (e.Key == Key.Enter)
                {
                    CommitEdit(textBox);
                }
                else if (e.Key == Key.Escape)
                {
                    textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                    StopEditing(textBox.DataContext, "IsRenaming");
                }
            }
        }

        private void CommitEdit(TextBox textBox)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            StopEditing(textBox.DataContext, "IsRenaming");
        }

        private void StopEditing(object dataContext, string propertyName)
        {
            if (dataContext is INotifyPropertyChanged npc) npc.GetType().GetProperty(propertyName)?.SetValue(npc, false);
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
    }
}