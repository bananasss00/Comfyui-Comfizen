using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using PropertyChanged;
using Newtonsoft.Json.Linq;

namespace Comfizen
{
    public enum FileTypeFilter { All, Images, Video }
    public enum SortOption { NewestFirst, OldestFirst }

    public class WorkflowListHeader
    {
        public string Title { get; set; }
    }
    
    public enum LogFilterType { All, Comfy, Local, Python }

    [AddINotifyPropertyChangedInterface]
    public class MainViewModel : INotifyPropertyChanged
    {
        private ComfyuiModel _comfyuiModel;
        private AppSettings _settings;
        private readonly SettingsService _settingsService;
        private SessionManager _sessionManager;
        private ModelService _modelService;
        private ConsoleLogService _consoleLogService;

        /// <summary>
        /// Provides access to the ConsoleLogService instance for external configuration.
        /// </summary>
        public ConsoleLogService GetConsoleLogService() => _consoleLogService;
        
        public ImageProcessingViewModel ImageProcessing { get; private set; }
        public FullScreenViewModel FullScreen { get; private set; }

        public ObservableCollection<WorkflowTabViewModel> OpenTabs { get; } = new ObservableCollection<WorkflowTabViewModel>();

        private WorkflowTabViewModel _selectedTab;
        public WorkflowTabViewModel SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (_selectedTab == value) return;

                if (_selectedTab != null && _selectedTab.Workflow.IsLoaded && !_selectedTab.IsVirtual)
                {
                    // START OF CHANGE: Pass the active inner tab name
                    string activeInnerTabName = _selectedTab.WorkflowInputsController.SelectedTabLayout?.Header;
                    _sessionManager.SaveSession(_selectedTab.Workflow, _selectedTab.FilePath, activeInnerTabName);
                    // END OF CHANGE
                }

                _selectedTab = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTab)));
            }
        }
        
        public ObservableCollection<string> Workflows { get; set; } = new();
        
        public ObservableCollection<object> WorkflowDisplayList { get; set; } = new();
        
        private string _selectedWorkflowDisplay;
        public string SelectedWorkflowDisplay
        {
            get => _selectedWorkflowDisplay;
            set
            {
                if (_selectedWorkflowDisplay == value) return;
                _selectedWorkflowDisplay = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedWorkflowDisplay)));
            }
        }
        
        private string _workflowSearchText;
        public string WorkflowSearchText
        {
            get => _workflowSearchText;
            set
            {
                if (_workflowSearchText == value) return;
                _workflowSearchText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WorkflowSearchText)));
            }
        }

        private string _selectedWorkflow;
        public string SelectedWorkflow
        {
            get => _selectedWorkflow;
            set
            {
                if (_selectedWorkflow == value) return;
                _selectedWorkflow = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedWorkflow)));
            }
        }
        
        public ICommand ClearSessionCommand { get; }
        public ICommand OpenConstructorCommand { get; set; }
        public ICommand EditWorkflowCommand { get; set; } 
        public ICommand ExportCurrentStateCommand { get; }
        public ICommand DeleteWorkflowCommand { get; }
        public ICommand OpenSettingsCommand { get; set; }
        public ICommand QueueCommand { get; }
        public ICommand InterruptCommand { get; }
        public ICommand PasteImageCommand { get; }
        public ICommand CloseTabCommand { get; }
        public ICommand BlockNodeCommand { get; }
        public ICommand ClearLocalQueueCommand { get; }
        public ICommand ClearBlockedNodesCommand { get; }
        public ICommand OpenGroupNavigationCommand { get; }
        public ICommand GoToGroupCommand { get; }
        
        private LogFilterType _selectedLogFilterType = LogFilterType.All;
        public LogFilterType SelectedLogFilterType
        {
            get => _selectedLogFilterType;
            set
            {
                if (_selectedLogFilterType == value) return;
                _selectedLogFilterType = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedLogFilterType)));
                // Refresh the view when the filter changes
                ConsoleLogMessages?.Refresh();
            }
        }

        public bool IsGroupNavigationPopupOpen { get; set; }
        
        public event Action<WorkflowGroupViewModel> GroupNavigationRequested;
        public ICommand RefreshModelsCommand { get; }
        
        
        
        public int QueueSize { get; set; } = 1;
        public int MaxQueueSize { get; set; }

        public ObservableCollection<SeedControl> SeedControlEnumValues => new(Enum.GetValues(typeof(SeedControl)).Cast<SeedControl>().ToList());

        public int TotalTasks { get; set; }
        public int CompletedTasks { get; private set; }
        public int CurrentProgress { get; set; }
        public string EstimatedTimeRemaining { get; private set; }
        
        public ICommand SaveChangesToWorkflowCommand { get; private set; }
        
        public ICollectionView ConsoleLogMessages { get; private set; }
        private readonly ObservableCollection<LogMessage> _allConsoleLogMessages;
        public bool IsConsoleVisible { get; set; } = false;
        public ICommand ToggleConsoleCommand { get; }
        public ICommand ClearConsoleCommand { get; }
        public ICommand CopyConsoleCommand { get; }
        public ICommand OpenWildcardBrowserCommand { get; }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        public bool IsInfiniteQueueEnabled { get; set; } = false;

        public ICommand ToggleInfiniteQueueCommand { get; }
        
        private class PromptTask
        {
            /// <summary>
            /// The raw API JSON sent to the ComfyUI server.
            /// </summary>
            public string JsonPromptForApi { get; set; }

            /// <summary>
            /// The complete workflow state (including prompt, promptTemplate, and scripts)
            /// that should be associated with the output and saved to the file.
            /// </summary>
            public string FullWorkflowStateJson { get; set; }

            public WorkflowTabViewModel OriginTab { get; set; }
        }
        
        public MainViewModel()
        {
            _settingsService = new SettingsService();
            _settings = _settingsService.LoadSettings();

            _comfyuiModel = new ComfyuiModel(_settings);
            _modelService = new ModelService(_settings);
            
            ImageProcessing = new ImageProcessingViewModel(_comfyuiModel, _settings);
            FullScreen = new FullScreenViewModel(this, _comfyuiModel, _settings, ImageProcessing.FilteredImageOutputs);
            
            MaxQueueSize = _settings.MaxQueueSize;
            _sessionManager = new SessionManager(_settings);
            
            _consoleLogService = new ConsoleLogService(_settings);
            _allConsoleLogMessages = _consoleLogService.LogMessages;
            ConsoleLogMessages = CollectionViewSource.GetDefaultView(_allConsoleLogMessages);
            ConsoleLogMessages.Filter = FilterLogs;
            _consoleLogService.ConnectAsync();
            
            Logger.OnErrorLogged += ShowConsoleOnError;

            ToggleConsoleCommand = new RelayCommand(_ => IsConsoleVisible = !IsConsoleVisible);
            ClearConsoleCommand = new RelayCommand(_ => _allConsoleLogMessages.Clear());
            CopyConsoleCommand = new RelayCommand(CopyConsoleContent, _ => _allConsoleLogMessages.Any());
            
            CloseTabCommand = new RelayCommand(p => CloseTab(p as WorkflowTabViewModel));

            OpenConstructorCommand = new RelayCommand(o => {
                new UIConstructor().ShowDialog();
                UpdateWorkflows();
            });

            EditWorkflowCommand = new RelayCommand(o => {
                // Use Header as the initial "file name" for virtual tabs, which guides the "Save As" dialog.
                var relativePath = !SelectedTab.IsVirtual
                    ? Path.GetRelativePath(Workflow.WorkflowsDir, SelectedTab.FilePath)
                    : SelectedTab.Header;

                // START OF CHANGE: Create a clone of the workflow for editing
                // instead of passing the live object directly. This prevents
                // the original tab's state from being modified until the changes are explicitly saved.
                var workflowClone = SelectedTab.Workflow.Clone();
                new UIConstructor(workflowClone, relativePath).ShowDialog();
                // END OF CHANGE
                UpdateWorkflows();
            }, o => SelectedTab != null);

            OpenSettingsCommand = new RelayCommand(OpenSettings);
            
            InterruptCommand = new AsyncRelayCommand(
                async _ => await _comfyuiModel.Interrupt(),
                _ => TotalTasks > 0 && CompletedTasks < TotalTasks
            );
            
            ClearSessionCommand = new RelayCommand(o => ClearSessionForCurrentWorkflow(), 
                o => SelectedTab != null && !SelectedTab.IsVirtual);

            PasteImageCommand = new RelayCommand(
                _ => SelectedTab?.WorkflowInputsController.HandlePasteOperation(),
                _ => SelectedTab != null
            );
            
            RefreshModelsCommand = new RelayCommand(RefreshModels, 
                o => SelectedTab != null && SelectedTab.Workflow.IsLoaded && !SelectedTab.IsVirtual);
            
            SaveChangesToWorkflowCommand = new RelayCommand(SaveChangesToWorkflow, 
                // Disable this command for virtual tabs, as they have no "default file" to overwrite.
                o => SelectedTab != null && SelectedTab.Workflow.IsLoaded && !SelectedTab.IsVirtual);
            
            ExportCurrentStateCommand = new RelayCommand(ExportCurrentState, o => SelectedTab?.Workflow.IsLoaded ?? false);
            // Disable delete for virtual tabs (they are just closed, not deleted from disk).
            DeleteWorkflowCommand = new RelayCommand(DeleteSelectedWorkflow, _ => SelectedTab != null && !SelectedTab.IsVirtual);
            
            BlockNodeCommand = new RelayCommand(p =>
            {
                if (p is not ImageOutput imageOutput || SelectedTab == null) return;
                var nodeId = imageOutput.NodeId;
                if (string.IsNullOrEmpty(nodeId)) return;

                SelectedTab.Workflow.BlockedNodeIds.Add(nodeId);

                // Remove existing items from the gallery from this node
                var itemsToRemove = ImageProcessing.ImageOutputs.Where(item => item.NodeId == nodeId).ToList();
                foreach (var item in itemsToRemove)
                {
                    ImageProcessing.ImageOutputs.Remove(item);
                }
            }, p => p is ImageOutput);
            
            ClearBlockedNodesCommand = new RelayCommand(_ =>
            {
                if (SelectedTab == null) return;
                if (MessageBox.Show(LocalizationService.Instance["MainVM_ClearBlockedNodesConfirmMessage"],
                        LocalizationService.Instance["MainVM_ClearBlockedNodesConfirmTitle"],
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    SelectedTab.Workflow.BlockedNodeIds.Clear();
                    MessageBox.Show(LocalizationService.Instance["MainVM_ClearBlockedNodesSuccessMessage"],
                        LocalizationService.Instance["MainVM_ClearBlockedNodesSuccessTitle"],
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }, _ => SelectedTab != null && SelectedTab.Workflow.BlockedNodeIds.Any());
            
            OpenGroupNavigationCommand = new RelayCommand(_ =>
            {
                // --- START OF CHANGE: Check groups within all tabs ---
                if (SelectedTab?.WorkflowInputsController.TabLayoouts.SelectMany(t => t.Groups).Any() == true)
                    // --- END OF CHANGE ---
                {
                    IsGroupNavigationPopupOpen = true;
                }
            }, _ => SelectedTab != null);

            GoToGroupCommand = new RelayCommand(p =>
            {
                if (p is WorkflowGroupViewModel groupVm)
                {
                    GroupNavigationRequested?.Invoke(groupVm);
                    IsGroupNavigationPopupOpen = false;
                }
            });
            
            OpenWildcardBrowserCommand = new RelayCommand(param =>
            {
                if (param is not TextFieldViewModel fieldVm) return;
                
                // This is the ideal way to implement cursor-aware insertion.
                // However, the CommandParameter in XAML is bound to the ViewModel (fieldVm), not the TextBox control.
                // A more advanced solution might involve passing the control itself, but that breaks MVVM principles.
                // The most pragmatic approach is to handle the click event in the View's code-behind.
                //
                // For now, let's provide a simple fallback action. 
                // A better implementation is shown in the comments below for the View layer.
                Action<string> insertTextAction = (text) =>
                {
                    // This fallback will append text. The real solution is in the UI layer.
                    fieldVm.Value += text;
                };

                var hostWindow = new Views.WildcardBrowser
                {
                    Owner = Application.Current.MainWindow
                };
                
                var viewModel = new WildcardBrowserViewModel(hostWindow, insertTextAction);
                hostWindow.DataContext = viewModel;

                hostWindow.ShowDialog();
            });
            
            ToggleInfiniteQueueCommand = new RelayCommand(_ => IsInfiniteQueueEnabled = !IsInfiniteQueueEnabled);
            
            UpdateWorkflows(true);
            UpdateWorkflowDisplayList();
            
            GlobalEventManager.WorkflowSaved += OnWorkflowSaved;
            
            LocalizationService.Instance.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == "Item[]")
                {
                    UpdateWorkflowDisplayList();
                }
            };
            
            ClearLocalQueueCommand = new RelayCommand(x =>
            {
                // This command now only clears pending tasks and sets a flag.
                // It lets the processing loop finish the current task gracefully.
                _cancellationRequested = true;
                IsInfiniteQueueEnabled = false;
                _promptsQueue.Clear(); // Immediately remove all waiting tasks.

                CompletedTasks = 0;
                TotalTasks = 0;
                CurrentProgress = 0;
            
            }, canExecute: x => _promptsQueue.Any() || (_isProcessing && TotalTasks > CompletedTasks));
            
            QueueCommand = new AsyncRelayCommand(Queue, canExecute: x => SelectedTab?.Workflow.IsLoaded ?? false);
        }
        
        private bool FilterLogs(object item)
        {
            if (item is not LogMessage message) return false;

            return SelectedLogFilterType switch
            {
                LogFilterType.Comfy => message.Source == LogSource.ComfyUI,
                LogFilterType.Local => message.Source == LogSource.Application,
                LogFilterType.Python => message.Source == LogSource.Python, // ADDED: New case for Python
                LogFilterType.All => true,
                _ => true,
            };
        }
        
        private void ShowConsoleOnError()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsConsoleVisible = true;
            });
        }

        public void ImportStateFromFile(string filePath)
        {
            try
            {
                var fileBytes = File.ReadAllBytes(filePath);
                var jsonString = Utils.ReadStateFromImage(fileBytes);

                if (string.IsNullOrEmpty(jsonString))
                {
                    if (Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        jsonString = File.ReadAllText(filePath);
                    }
                    else
                    {
                        Logger.Log(LocalizationService.Instance["MainVM_ImportNoMetadataError"]);
                        return;
                    }
                }
        
                var data = JObject.Parse(jsonString);
                // Pass the original file name to be used as the tab header.
                ImportStateFromJObject(data, Path.GetFileName(filePath));
            }
            catch (Exception ex)
            {
                Logger.Log(ex, $"An error occurred while importing the state file: {filePath}");
                MessageBox.Show(string.Format(LocalizationService.Instance["MainVM_ImportGenericError"], ex.Message), LocalizationService.Instance["MainVM_ImportErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);            }
        }
        
        private void ImportStateFromJObject(JObject data, string sourceFileName)
        {
            var promptData = data["prompt"] as JObject;
            var uiDefinition = data["promptTemplate"]?.ToObject<ObservableCollection<WorkflowGroup>>();
            var scripts = data["scripts"]?.ToObject<ScriptCollection>() ?? new ScriptCollection();
            var tabs = data["tabs"]?.ToObject<ObservableCollection<WorkflowTabDefinition>>() ?? new ObservableCollection<WorkflowTabDefinition>();

            if (promptData == null || uiDefinition == null)
            {
                MessageBox.Show(LocalizationService.Instance["MainVM_ImportInvalidFileError"], LocalizationService.Instance["MainVM_ImportInvalidFileTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Create a new in-memory Workflow object.
            var importedWorkflow = new Workflow();
            importedWorkflow.SetWorkflowData(promptData, uiDefinition, scripts, tabs);
            
            // Create a new "virtual" tab using the new constructor.
            var newTab = new WorkflowTabViewModel(
                importedWorkflow, 
                sourceFileName, // Use the image name as the header.
                _comfyuiModel, 
                _settings, 
                _modelService, 
                _sessionManager
            );

            OpenTabs.Add(newTab);
            SelectedTab = newTab;
        }
        
        private async void OnWorkflowSaved(object sender, WorkflowSaveEventArgs e)
        {
            var savedFilePathNormalized = Path.GetFullPath(e.FilePath);

            // START OF FIX: Filter out virtual tabs before comparing file paths.
            // A virtual tab has no FilePath, so Path.GetFullPath would throw an exception.
            var tabToUpdate = OpenTabs.FirstOrDefault(t => 
                !t.IsVirtual && Path.GetFullPath(t.FilePath).Equals(savedFilePathNormalized, StringComparison.OrdinalIgnoreCase)
            );
            // END OF FIX
            
            if (tabToUpdate != null)
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await tabToUpdate.Reload(e.SaveType);
                });
            }
            
            UpdateWorkflows();
            UpdateWorkflowDisplayList();
        }
        
        public void OpenOrSwitchToWorkflow(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;
            
            var normalizedPath = relativePath.Replace('\\', '/');
            
            if (Path.IsPathRooted(normalizedPath))
            {
                var workflowsDirFullPath = Path.GetFullPath(Workflow.WorkflowsDir);
                var fileFullPath = Path.GetFullPath(normalizedPath);
                
                if (fileFullPath.StartsWith(workflowsDirFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    normalizedPath = Path.GetRelativePath(workflowsDirFullPath, fileFullPath).Replace('\\', '/');
                }
                else
                {
                    MessageBox.Show(
                        string.Format(LocalizationService.Instance["MainVM_WorkflowPathError"], fileFullPath, workflowsDirFullPath),
                        LocalizationService.Instance["General_Error"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }

            var fullPath = Path.Combine(Workflow.WorkflowsDir, normalizedPath);
            var fullPathNormalized = Path.GetFullPath(fullPath);
            
            var existingTab = OpenTabs.FirstOrDefault(t => 
                !t.IsVirtual && Path.GetFullPath(t.FilePath).Equals(fullPathNormalized, StringComparison.OrdinalIgnoreCase)
            );
            
            if (existingTab != null)
            {
                SelectedTab = existingTab;
            }
            else
            {
                var newTab = new WorkflowTabViewModel(fullPath, _comfyuiModel, _settings, _modelService, _sessionManager);
                OpenTabs.Add(newTab);
                SelectedTab = newTab;
                AddWorkflowToRecents(normalizedPath);
                UpdateWorkflowDisplayList();
            }
            
            SelectedWorkflow = null;
        }
        
        private void CloseTab(WorkflowTabViewModel tabToClose)
        {
            if (tabToClose == null) return;
            
            if (FullScreen.IsFullScreenOpen)
            {
                FullScreen.IsFullScreenOpen = false;
            }

            if (!tabToClose.IsVirtual && tabToClose.Workflow.IsLoaded)
            {
                // START OF CHANGE: Pass the active inner tab name
                string activeInnerTabName = tabToClose.WorkflowInputsController.SelectedTabLayout?.Header;
                _sessionManager.SaveSession(tabToClose.Workflow, tabToClose.FilePath, activeInnerTabName);
                // END OF CHANGE
            }
            
            OpenTabs.Remove(tabToClose);
        }

        private void RefreshModels(object obj)
        {
            if (SelectedTab == null || !SelectedTab.Workflow.IsLoaded) return;

            var workflowToReload = SelectedTab.FilePath;
            string activeInnerTabName = SelectedTab.WorkflowInputsController.SelectedTabLayout?.Header;
            _sessionManager.SaveSession(SelectedTab.Workflow, workflowToReload, activeInnerTabName);
            
            ModelService.ResetConnectionErrorFlag();
            ModelService.ClearCache();
            
            int tabIndex = OpenTabs.IndexOf(SelectedTab);
            OpenTabs.RemoveAt(tabIndex);

            var newTab = new WorkflowTabViewModel(workflowToReload, _comfyuiModel, _settings, _modelService, _sessionManager);
            OpenTabs.Insert(tabIndex, newTab);
            SelectedTab = newTab;
        }
        
        private void SaveChangesToWorkflow(object obj)
        {
            if (SelectedTab == null) return;

            var result = MessageBox.Show(
                string.Format(LocalizationService.Instance["MainVM_SaveConfirmationMessage"], SelectedTab.Header),
                LocalizationService.Instance["MainVM_SaveConfirmationTitle"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var fullPath = SelectedTab.FilePath;
                var relativePath = Path.GetRelativePath(Workflow.WorkflowsDir, fullPath);
                var relativePathWithoutExtension = Path.ChangeExtension(relativePath, null);
                
                SelectedTab.Workflow.SaveWorkflowWithCurrentState(relativePathWithoutExtension.Replace(Path.DirectorySeparatorChar, '/'));
                string activeInnerTabName = SelectedTab.WorkflowInputsController.SelectedTabLayout?.Header;
                _sessionManager.SaveSession(SelectedTab.Workflow, SelectedTab.FilePath, activeInnerTabName);

                Logger.Log(LocalizationService.Instance["MainVM_ValuesSavedMessage"]);
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Failed to save workflow with current state.");
                MessageBox.Show(string.Format(LocalizationService.Instance["MainVM_ErrorSavingMessage"], ex.Message),
                    LocalizationService.Instance["MainVM_ErrorSavingTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private const bool stripMeta = false;
        
        private void ExportCurrentState(object obj)
        {
            if (SelectedTab == null || !SelectedTab.Workflow.IsLoaded || SelectedTab.Workflow.LoadedApi == null)
            {
                Logger.Log(LocalizationService.Instance["MainVM_ExportErrorMessage"]);
                return;
            }

            var dialog = new SaveFileDialog
            {
                FileName = Path.GetFileNameWithoutExtension(SelectedTab.Header) + "_current.json",
                Filter = "JSON File (*.json)|*.json",
                Title = LocalizationService.Instance["MainVM_ExportDialogTitle"]
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // --- START OF REWORKED EXPORT LOGIC ---
                    // 1. Create a deep clone to avoid modifying the live workflow object.
                    var promptToExport = SelectedTab.Workflow.LoadedApi.DeepClone() as JObject;

                    // 2. Apply the current bypass settings to this clone.
                    SelectedTab.WorkflowInputsController.ApplyNodeBypass(promptToExport);

                    // 3. Remove all internal "_meta" properties for a clean export.
                    if (stripMeta)
                        Utils.StripAllMetaProperties(promptToExport);

                    // 4. Serialize the cleaned and bypassed object.
                    string jsonContent = promptToExport.ToString(Formatting.Indented);
                    // --- END OF REWORKED EXPORT LOGIC ---
                    
                    File.WriteAllText(dialog.FileName, jsonContent);
                    Logger.Log(string.Format(LocalizationService.Instance["MainVM_ExportSuccessMessage"], dialog.FileName));
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Failed to save exported state file.");
                }
            }
        }

        private async void OpenSettings(object obj)
        {
            var settingsWindow = new SettingsWindow { Owner = Application.Current.MainWindow };
            settingsWindow.ShowDialog();
            
            ModelService.ResetConnectionErrorFlag();
            ModelService.ClearCache();
            _settings = _settingsService.LoadSettings();
            MaxQueueSize = _settings.MaxQueueSize;
            
            _comfyuiModel = new ComfyuiModel(_settings);
            _modelService = new ModelService(_settings);
            _sessionManager = new SessionManager(_settings);
            
            ImageProcessing.Settings = _settings;
            FullScreen = new FullScreenViewModel(this, _comfyuiModel, _settings, ImageProcessing.FilteredImageOutputs);
            
            await _consoleLogService.ReconnectAsync(_settings);
            
            foreach (var tab in OpenTabs)
            {
                tab.UpdateAfterSettingsChange(_settings, _comfyuiModel, _modelService, _sessionManager);
            }
        }
        
        /// <summary>
        /// Copies the entire content of the console log to the clipboard.
        /// </summary>
        private void CopyConsoleContent(object obj)
        {
            // The CanExecute predicate already checks if there are any items,
            // but a defensive check here is good practice.
            if (_allConsoleLogMessages == null || !_allConsoleLogMessages.Any())
            {
                return;
            }

            var stringBuilder = new StringBuilder();
            
            // MODIFIED: Iterate over the filtered view, but cast each item to the correct type.
            // The ICollectionView returns items as 'object'.
            foreach (var item in ConsoleLogMessages)
            {
                if (item is LogMessage logMessage)
                {
                    // Concatenate all text parts from the segments
                    var lineText = string.Concat(logMessage.Segments.Select(s => s.Text));
            
                    // Format the line with timestamp and level for clarity
                    stringBuilder.AppendLine($"{logMessage.Timestamp:HH:mm:ss} [{logMessage.Level.ToString().ToUpper()}] {lineText}");
                }
            }

            try
            {
                Clipboard.SetText(stringBuilder.ToString());
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Failed to copy console content to clipboard");
            }
        }
        
        private readonly Stopwatch _queueStopwatch = new();
        private ConcurrentQueue<PromptTask> _promptsQueue = new();
        private bool _cancellationRequested = false;

        private readonly object _processingLock = new object();
        private bool _isProcessing = false;
        
        private List<PromptTask> CreatePromptTasks(WorkflowTabViewModel tab)
        {
            var tasks = new List<PromptTask>();

            for (int i = 0; i < QueueSize; i++)
            {
                // 1. Create a clone of the API prompt that will be modified for this specific task.
                var apiPromptForTask = tab.Workflow.JsonClone();
                
                // 2. Apply all per-task modifications (like seed randomization) to this clone.
                // After this call, apiPromptForTask contains the *actual* values that will be sent to the API.
                tab.WorkflowInputsController.ProcessSpecialFields(apiPromptForTask);
                
                tab.ExecuteHook("on_before_prompt_queue", apiPromptForTask);

                // 3. NOW, create the full state object using the MODIFIED prompt clone.
                // This ensures that the state we save to metadata is identical to what's used for generation.
                var fullStateForThisTask = new
                {
                    prompt = apiPromptForTask, // Use the modified prompt here
                    promptTemplate = tab.Workflow.Groups,
                    scripts = (tab.Workflow.Scripts.Hooks.Any() || tab.Workflow.Scripts.Actions.Any()) ? tab.Workflow.Scripts : null,
                    tabs = tab.Workflow.Tabs.Any() ? tab.Workflow.Tabs : null
                };
            
                // 4. Serialize this complete and correct state for embedding.
                string fullWorkflowStateJsonForThisTask = JsonConvert.SerializeObject(fullStateForThisTask, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.None });
                
                // 5. Add the new task with the correct data.
                tasks.Add(new PromptTask
                {
                    JsonPromptForApi = apiPromptForTask.ToString(), // This is sent to the server
                    FullWorkflowStateJson = fullWorkflowStateJsonForThisTask, // This is saved in the image
                    OriginTab = tab
                });
            }
            
            return tasks;
        }
        
        /// <summary>
        /// A thread-safe method to queue a prompt from any context (UI thread or background task).
        /// It prepares the task data and then dispatches the actual enqueuing logic to the UI thread.
        /// </summary>
        public void QueuePromptFromJObject(JObject prompt, WorkflowTabViewModel originTab)
        {
            if (prompt == null || originTab == null) return;

            // This part is thread-safe and can be done immediately.
            var fullState = new
            {
                prompt = prompt,
                promptTemplate = originTab.Workflow.Groups,
                scripts = (originTab.Workflow.Scripts.Hooks.Any() || originTab.Workflow.Scripts.Actions.Any()) ? originTab.Workflow.Scripts : null,
                tabs = originTab.Workflow.Tabs.Any() ? originTab.Workflow.Tabs : null
            };
    
            string fullWorkflowStateJson = JsonConvert.SerializeObject(fullState, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.None });

            var task = new PromptTask
            {
                JsonPromptForApi = prompt.ToString(),
                FullWorkflowStateJson = fullWorkflowStateJson,
                OriginTab = originTab
            };

            // Dispatch the critical part (modifying collections and starting the processor) to the UI thread.
            Application.Current.Dispatcher.Invoke(() => EnqueueTaskInternal(task));
        }
        
        /// <summary>
        /// The internal implementation for enqueuing a task. This method MUST be called on the UI thread.
        /// </summary>
        private void EnqueueTaskInternal(PromptTask task)
        {
            _promptsQueue.Enqueue(task);
            TotalTasks++; // This is now safe and will update the UI correctly.

            lock (_processingLock)
            {
                if (!_isProcessing)
                {
                    _isProcessing = true;
                    _ = Task.Run(ProcessQueueAsync);
                }
            }
        }
        
        private async Task Queue(object o)
        {
            if (SelectedTab == null || !SelectedTab.Workflow.IsLoaded) return;
        
            SelectedTab.ExecuteHook("on_queue_start", SelectedTab.Workflow.LoadedApi);
        
            var promptTasks = await Task.Run(() => CreatePromptTasks(SelectedTab));
            if (promptTasks.Count == 0) return;
        
            if (_cancellationRequested || (_promptsQueue.IsEmpty && !_isProcessing))
            {
                CompletedTasks = 0;
                TotalTasks = 0;
                CurrentProgress = 0;
                _queueStopwatch.Restart(); // Reset and start the stopwatch for a new batch
                EstimatedTimeRemaining = null; // Clear previous ETA
            }
            _cancellationRequested = false;
    
            foreach (var task in promptTasks)
            {
                _promptsQueue.Enqueue(task);
            }
            TotalTasks += promptTasks.Count;
    
            bool needsProcessing = false;
            lock (_processingLock)
            {
                if (!_isProcessing)
                {
                    _isProcessing = true;
                    needsProcessing = true;
                }
            }

            if (needsProcessing)
            {
                _ = ProcessQueueAsync(); 
            }
        }

        private async Task ProcessQueueAsync()
        {
            WorkflowTabViewModel lastTaskOriginTab = null; 
            
            try
            {
                while (true)
                {
                    if (_cancellationRequested) break;
    
                    if (_promptsQueue.TryDequeue(out var task))
                    {
                        lastTaskOriginTab = task.OriginTab;
                        
                        try
                        {
                            var promptForTask = JObject.Parse(task.JsonPromptForApi);
                            
                            await foreach (var io in _comfyuiModel.QueuePrompt(task.JsonPromptForApi))
                            {
                                // A check here is no longer needed as we want the task to finish
                                io.Prompt = task.FullWorkflowStateJson;

                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    if (task.OriginTab.Workflow.BlockedNodeIds.Contains(io.NodeId))
                                    {
                                        return;
                                    }
                                
                                    if (!this.ImageProcessing.ImageOutputs.Any(existing => existing.FilePath == io.FilePath))
                                    {
                                        this.ImageProcessing.ImageOutputs.Insert(0, io);
                                    }

                                    task.OriginTab?.ExecuteHook("on_output_received", promptForTask, io);
                                });
                            }
                        
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                CompletedTasks++;
                                CurrentProgress = (TotalTasks > 0) ? (CompletedTasks * 100) / TotalTasks : 0;
                            });

                            if (CompletedTasks > 0 && TotalTasks > CompletedTasks)
                            {
                                var elapsed = _queueStopwatch.Elapsed;
                                // Only show ETA after a second to get a more stable estimate
                                if (elapsed.TotalSeconds > 1)
                                {
                                    var timePerTask = elapsed / CompletedTasks;
                                    var remainingTime = timePerTask * (TotalTasks - CompletedTasks);
                                    EstimatedTimeRemaining = $"~{FormatEta(remainingTime)}";
                                }
                            }
                            else
                            {
                                // Clear when the last task is done or if there's only one task.
                                EstimatedTimeRemaining = null;
                            }
                            
                            await Application.Current.Dispatcher.InvokeAsync(() => task.OriginTab?.ExecuteHook("on_queue_finish", promptForTask));
                        }
                        catch (Exception ex)
                        {
                            Logger.Log(ex, "[Connection Error] Failed to queue prompt");
                            
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                MessageBox.Show(
                                    LocalizationService.Instance["MainVM_ConnectionErrorMessage"],
                                    LocalizationService.Instance["MainVM_ConnectionErrorTitle"],
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                            });
                        
                            _cancellationRequested = true; // Set flag on error
                            break;
                        }
                    }
                    else
                    {
                        if (IsInfiniteQueueEnabled && !_cancellationRequested && lastTaskOriginTab != null)
                        {
                            List<PromptTask> newTasks = null;
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                newTasks = CreatePromptTasks(lastTaskOriginTab);
                            });

                            if (newTasks != null)
                            {
                                foreach (var p in newTasks)
                                {
                                    _promptsQueue.Enqueue(p);
                                }
                                TotalTasks += newTasks.Count;
                            }
                            await Task.Delay(100);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                lock (_processingLock)
                {
                    _isProcessing = false;
                }
            
                _queueStopwatch.Stop();
                EstimatedTimeRemaining = null; 
            
                if (_cancellationRequested)
                {
                    _promptsQueue.Clear();
                
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        TotalTasks = 0;
                        CompletedTasks = 0;
                        CurrentProgress = 0;
                    });
                
                    IsInfiniteQueueEnabled = false;
                }
                else if (lastTaskOriginTab != null) 
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => lastTaskOriginTab.ExecuteHook("on_batch_finished", lastTaskOriginTab.Workflow.LoadedApi));
                }
            }
        }
        
        /// <summary>
        /// Formats a TimeSpan into a user-friendly ETA string (e.g., "1h 5m", "4m 32s", "12s").
        /// </summary>
        /// <param name="ts">The TimeSpan to format.</param>
        /// <returns>A formatted string representing the estimated time remaining.</returns>
        private string FormatEta(TimeSpan ts)
        {
            if (ts.TotalSeconds <= 0) return string.Empty;

            if (ts.TotalHours >= 1)
                return ts.ToString(@"h\h\ m\m");
            if (ts.TotalMinutes >= 1)
                return ts.ToString(@"m\m\ s\s");
        
            return $"{(int)ts.TotalSeconds}s";
        }

        private void AddWorkflowToRecents(string relativePath)
        {
            if (_settings.MaxRecentWorkflows <= 0) return;
            
            _settings.RecentWorkflows.Remove(relativePath);
            
            _settings.RecentWorkflows.Insert(0, relativePath);
            
            if (_settings.RecentWorkflows.Count > _settings.MaxRecentWorkflows)
            {
                _settings.RecentWorkflows = _settings.RecentWorkflows.Take(_settings.MaxRecentWorkflows).ToList();
            }
            
            _settingsService.SaveSettings(_settings);
        }

        private void UpdateWorkflowDisplayList()
        {
            WorkflowDisplayList.Clear();
            var recentWorkflows = _settings.RecentWorkflows
                .Where(r => Workflows.Contains(r))
                .ToList();

            if (recentWorkflows.Any())
            {
                WorkflowDisplayList.Add(new WorkflowListHeader { Title = LocalizationService.Instance["WorkflowList_Recent"] });
                foreach (var wf in recentWorkflows)
                {
                    WorkflowDisplayList.Add(wf);
                }
            }
            
            WorkflowDisplayList.Add(new WorkflowListHeader { Title = LocalizationService.Instance["WorkflowList_All"] });
            var allOtherWorkflows = Workflows.Except(recentWorkflows);
            foreach (var wf in allOtherWorkflows)
            {
                WorkflowDisplayList.Add(wf);
            }
        }

        private void UpdateWorkflows(bool initialLoad = false)
        {
            Workflows.Clear();
            if (!Directory.Exists(Workflow.WorkflowsDir))
            {
                Directory.CreateDirectory(Workflow.WorkflowsDir);
            }
            
            var baseDirPath = Path.GetFullPath(Workflow.WorkflowsDir);
            var files = Directory.EnumerateFiles(baseDirPath, "*.json", SearchOption.AllDirectories);
            var relativeFiles = files
                .Select(fullPath => Path.GetRelativePath(baseDirPath, fullPath))
                .Select(path => path.Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(path => path.Contains('/') ? 0 : 1)
                .ThenBy(path => path);

            foreach (var file in relativeFiles)
            {
                Workflows.Add(file);
            }
            
            if (initialLoad)
            {
                if (_settings.LastOpenWorkflows != null && _settings.LastOpenWorkflows.Any())
                {
                    foreach (var path in _settings.LastOpenWorkflows)
                    {
                        if (Workflows.Contains(path))
                        {
                            OpenOrSwitchToWorkflow(path);
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(_settings.LastActiveWorkflow))
                    {
                        var lastActiveFullPath = Path.GetFullPath(Path.Combine(Workflow.WorkflowsDir, _settings.LastActiveWorkflow));
                        
                        var activeTab = OpenTabs.FirstOrDefault(t => 
                            Path.GetFullPath(t.FilePath).Equals(lastActiveFullPath, StringComparison.OrdinalIgnoreCase)
                        );
                        
                        if (activeTab != null)
                        {
                            SelectedTab = activeTab;
                        }
                    }
                }
                else
                {
                    var lastOpened = _settings.RecentWorkflows.FirstOrDefault();
                    if (lastOpened != null && Workflows.Contains(lastOpened))
                    {
                        OpenOrSwitchToWorkflow(lastOpened);
                    }
                }
            }
            
            UpdateWorkflowDisplayList();
        }

        public async Task SaveStateOnCloseAsync()
        {
            GlobalEventManager.WorkflowSaved -= OnWorkflowSaved;
            
            Logger.OnErrorLogged -= ShowConsoleOnError;
            await _consoleLogService.DisconnectAsync();

            foreach (var tab in OpenTabs)
            {
                if (!tab.IsVirtual && tab.Workflow.IsLoaded && tab.Workflow.LoadedApi != null)
                {
                    // START OF CHANGE: Pass the active inner tab name
                    string activeInnerTabName = tab.WorkflowInputsController.SelectedTabLayout?.Header;
                    _sessionManager.SaveSession(tab.Workflow, tab.FilePath, activeInnerTabName);
                    // END OF CHANGE
                }
            }
            
            if (SelectedTab != null)
            {
                _settings.LastSeedControlState = SelectedTab.WorkflowInputsController.SelectedSeedControl;
            }
            
            _settings.LastOpenWorkflows = OpenTabs
                // Filter out virtual tabs from being saved into the last open list.
                .Where(t => !t.IsVirtual)
                .Select(t => Path.GetRelativePath(Workflow.WorkflowsDir, t.FilePath).Replace(Path.DirectorySeparatorChar, '/'))
                .ToList();

            _settings.LastActiveWorkflow = (SelectedTab != null && !SelectedTab.IsVirtual)
                ? Path.GetRelativePath(Workflow.WorkflowsDir, SelectedTab.FilePath).Replace(Path.DirectorySeparatorChar, '/') 
                : null;
            
            _settingsService.SaveSettings(_settings);
        }
        
        private void DeleteSelectedWorkflow(object obj)
        {
            var tabToDelete = SelectedTab;
            if (tabToDelete == null) return;

            var workflowName = tabToDelete.Header;
            var filePath = tabToDelete.FilePath;

            var message = string.Format(LocalizationService.Instance["MainVM_DeleteConfirmMessage"], workflowName);
            var caption = LocalizationService.Instance["MainVM_DeleteConfirmTitle"];
            var result = MessageBox.Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                CloseTab(tabToDelete);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                
                var relativePath = Path.GetRelativePath(Workflow.WorkflowsDir, filePath).Replace(Path.DirectorySeparatorChar, '/');
                if (_settings.RecentWorkflows.Contains(relativePath))
                {
                    _settings.RecentWorkflows.Remove(relativePath);
                    _settingsService.SaveSettings(_settings);
                }
            
                UpdateWorkflows();
            }
            catch (Exception ex)
            {
                var errorMsg = string.Format(LocalizationService.Instance["MainVM_DeleteErrorMessage"], ex.Message);
                var errorCaption = LocalizationService.Instance["MainVM_DeleteErrorTitle"];
                MessageBox.Show(errorMsg, errorCaption, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ClearSessionForCurrentWorkflow()
        {
            if (SelectedTab == null || !SelectedTab.Workflow.IsLoaded) return;
            
            SelectedTab.ResetState();
            
            MessageBox.Show(string.Format(LocalizationService.Instance["MainVM_SessionResetMessage"], SelectedTab.Header), 
                LocalizationService.Instance["MainVM_SessionResetTitle"], 
                MessageBoxButton.OK, 
                MessageBoxImage.Information);
        }
    }
}
