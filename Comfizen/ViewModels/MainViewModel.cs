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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
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

    [AddINotifyPropertyChangedInterface]
    public class MainViewModel : INotifyPropertyChanged
    {
        private ComfyuiModel _comfyuiModel;
        private AppSettings _settings;
        private readonly SettingsService _settingsService;
        private SessionManager _sessionManager;
        private ModelService _modelService;
        private ConsoleLogService _consoleLogService;
        
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

                if (_selectedTab != null && _selectedTab.Workflow.IsLoaded)
                {
                    _sessionManager.SaveSession(_selectedTab.Workflow.LoadedApi, _selectedTab.Workflow.Groups, _selectedTab.FilePath);
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
        
        public ICommand RefreshModelsCommand { get; }
        public int QueueSize { get; set; } = 1;
        public int MaxQueueSize { get; set; }

        public ObservableCollection<SeedControl> SeedControlEnumValues => new(Enum.GetValues(typeof(SeedControl)).Cast<SeedControl>().ToList());

        public int TotalTasks { get; set; }
        public int CompletedTasks { get; private set; }
        public int CurrentProgress { get; set; }
        
        public ICommand SaveChangesToWorkflowCommand { get; private set; }
        
        public ObservableCollection<LogMessage> ConsoleLogMessages { get; private set; }
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
            ConsoleLogMessages = _consoleLogService.LogMessages;
            _consoleLogService.ConnectAsync();

            Logger.ConsoleLogServiceInstance = _consoleLogService;
            Logger.OnErrorLogged += ShowConsoleOnError;

            ToggleConsoleCommand = new RelayCommand(_ => IsConsoleVisible = !IsConsoleVisible);
            ClearConsoleCommand = new RelayCommand(_ => ConsoleLogMessages.Clear());
            CopyConsoleCommand = new RelayCommand(CopyConsoleContent, _ => ConsoleLogMessages.Any());
            
            CloseTabCommand = new RelayCommand(p => CloseTab(p as WorkflowTabViewModel));

            OpenConstructorCommand = new RelayCommand(o => {
                new UIConstructor().ShowDialog();
                UpdateWorkflows();
            });

            EditWorkflowCommand = new RelayCommand(o => {
                var relativePath = Path.GetRelativePath(Workflow.WorkflowsDir, SelectedTab.FilePath);
                // Pass the live Workflow object from the selected tab directly to the constructor.
                var liveWorkflow = SelectedTab.Workflow;
                new UIConstructor(liveWorkflow, relativePath).ShowDialog();
                UpdateWorkflows();
            }, o => SelectedTab != null);

            OpenSettingsCommand = new RelayCommand(OpenSettings);
            
            InterruptCommand = new AsyncRelayCommand(
                async _ => await _comfyuiModel.Interrupt(),
                _ => TotalTasks > 0 && CompletedTasks < TotalTasks
            );
            
            ClearSessionCommand = new RelayCommand(o => ClearSessionForCurrentWorkflow(), 
                o => SelectedTab != null);

            PasteImageCommand = new RelayCommand(
                _ => SelectedTab?.WorkflowInputsController.HandlePasteOperation(),
                _ => SelectedTab != null
            );
            
            RefreshModelsCommand = new RelayCommand(RefreshModels, o => SelectedTab?.Workflow.IsLoaded ?? false);
            
            SaveChangesToWorkflowCommand = new RelayCommand(SaveChangesToWorkflow, 
                o => SelectedTab != null && SelectedTab.Workflow.IsLoaded);
            
            ExportCurrentStateCommand = new RelayCommand(ExportCurrentState, o => SelectedTab?.Workflow.IsLoaded ?? false);
            DeleteWorkflowCommand = new RelayCommand(DeleteSelectedWorkflow, _ => SelectedTab != null);
            
            OpenWildcardBrowserCommand = new RelayCommand(param =>
            {
                if (param is not TextFieldViewModel fieldVm) return;

                var hostWindow = new Window
                {
                    Title = LocalizationService.Instance["WildcardBrowser_Title"], 
                    Width = 350, Height = 500,
                    Owner = Application.Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Style = (Style)Application.Current.FindResource("CustomWindowStyle")
                };
                var viewModel = new WildcardBrowserViewModel(hostWindow);
                hostWindow.DataContext = viewModel;
                hostWindow.Content = new ContentControl
                    { Template = (ControlTemplate)Application.Current.FindResource("WildcardBrowserTemplate") };
            
                if (hostWindow.ShowDialog() == true && !string.IsNullOrEmpty(viewModel.SelectedWildcardTag))
                {
                    fieldVm.Value += viewModel.SelectedWildcardTag;
                }
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
            QueueCommand = new RelayCommand(Queue, canExecute: x => SelectedTab?.Workflow.IsLoaded ?? false);
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
                        MessageBox.Show("No Comfizen state metadata was found in the file.", "Import Failed", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                }
        
                var data = JObject.Parse(jsonString);
                ImportStateFromJObject(data);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while importing the state file: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async void ImportStateFromJObject(JObject data)
        {
            var promptData = data["prompt"] as JObject;
            var uiDefinition = data["promptTemplate"]?.ToObject<ObservableCollection<WorkflowGroup>>();
            
            var scripts = data["scripts"]?.ToObject<ScriptCollection>() ?? new ScriptCollection();

            if (promptData == null || uiDefinition == null)
            {
                MessageBox.Show("The imported file is not a valid Comfizen state file or is corrupted.", "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            var fingerprint = _sessionManager.GenerateFingerprint(uiDefinition, scripts);
            var existingWorkflowPath = _sessionManager.FindWorkflowByFingerprint(fingerprint);

            if (existingWorkflowPath != null)
            {
                _sessionManager.SaveSession(promptData, uiDefinition, existingWorkflowPath);
                var relativePath = Path.GetRelativePath(Workflow.WorkflowsDir, existingWorkflowPath).Replace(Path.DirectorySeparatorChar, '/');
                OpenOrSwitchToWorkflow(relativePath);
    
                var tab = OpenTabs.FirstOrDefault(t => Path.GetFullPath(t.FilePath).Equals(Path.GetFullPath(existingWorkflowPath), StringComparison.OrdinalIgnoreCase));
                if (tab != null)
                {
                    await tab.Reload(WorkflowSaveType.ApiReplaced);
                }

                MessageBox.Show($"Session for workflow '{Path.GetFileName(existingWorkflowPath)}' has been successfully imported.", "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Save New Workflow",
                    Filter = "Workflow Files (*.json)|*.json",
                    InitialDirectory = Path.GetFullPath(Workflow.WorkflowsDir),
                    FileName = "imported_workflow.json"
                };
                if (dialog.ShowDialog() == true)
                {
                    var newWorkflowPath = dialog.FileName;
                    
                    var workflowData = new { prompt = promptData, promptTemplate = uiDefinition, scripts = scripts };

                    var workflowJson = JsonConvert.SerializeObject(workflowData, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.Indented });
                    File.WriteAllText(newWorkflowPath, workflowJson);
                    
                    UpdateWorkflows();
                    var newWorkflowRelativePath = Path.GetRelativePath(Workflow.WorkflowsDir, newWorkflowPath).Replace(Path.DirectorySeparatorChar, '/');
                    OpenOrSwitchToWorkflow(newWorkflowRelativePath);

                    MessageBox.Show($"New workflow '{Path.GetFileName(newWorkflowPath)}' has been successfully imported.", "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        
        private async void OnWorkflowSaved(object sender, WorkflowSaveEventArgs e)
        {
            var savedFilePathNormalized = Path.GetFullPath(e.FilePath);

            var tabToUpdate = OpenTabs.FirstOrDefault(t => 
                Path.GetFullPath(t.FilePath).Equals(savedFilePathNormalized, StringComparison.OrdinalIgnoreCase)
            );
            
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

            var fullPath = Path.Combine(Workflow.WorkflowsDir, relativePath);
            var fullPathNormalized = Path.GetFullPath(fullPath);

            var existingTab = OpenTabs.FirstOrDefault(t => 
                Path.GetFullPath(t.FilePath).Equals(fullPathNormalized, StringComparison.OrdinalIgnoreCase)
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
                AddWorkflowToRecents(relativePath);
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

            if (tabToClose.Workflow.IsLoaded)
            {
                _sessionManager.SaveSession(tabToClose.Workflow.LoadedApi, tabToClose.Workflow.Groups, tabToClose.FilePath);
            }
            
            OpenTabs.Remove(tabToClose);
        }

        private void RefreshModels(object obj)
        {
            if (SelectedTab == null || !SelectedTab.Workflow.IsLoaded) return;

            var workflowToReload = SelectedTab.FilePath;
            _sessionManager.SaveSession(SelectedTab.Workflow.LoadedApi, SelectedTab.Workflow.Groups, workflowToReload);
            
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
                _sessionManager.SaveSession(SelectedTab.Workflow.LoadedApi, SelectedTab.Workflow.Groups, SelectedTab.FilePath);

                MessageBox.Show(LocalizationService.Instance["MainVM_ValuesSavedMessage"],
                    LocalizationService.Instance["MainVM_ValuesSavedTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(LocalizationService.Instance["MainVM_ErrorSavingMessage"], ex.Message),
                    LocalizationService.Instance["MainVM_ErrorSavingTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        
        private void ExportCurrentState(object obj)
        {
            if (SelectedTab == null || !SelectedTab.Workflow.IsLoaded || SelectedTab.Workflow.LoadedApi == null)
            {
                MessageBox.Show(LocalizationService.Instance["MainVM_ExportErrorMessage"], LocalizationService.Instance["MainVM_ExportErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
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
                    string jsonContent = SelectedTab.Workflow.LoadedApi.ToString(Formatting.Indented);
                    File.WriteAllText(dialog.FileName, jsonContent);
                    MessageBox.Show(string.Format(LocalizationService.Instance["MainVM_ExportSuccessMessage"], dialog.FileName), LocalizationService.Instance["MainVM_ExportSuccessTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format(LocalizationService.Instance["MainVM_ExportSaveErrorMessage"], ex.Message), LocalizationService.Instance["MainVM_ExportSaveErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (ConsoleLogMessages == null || !ConsoleLogMessages.Any())
            {
                return;
            }

            var stringBuilder = new StringBuilder();
            foreach (var logMessage in ConsoleLogMessages)
            {
                // Concatenate all text parts from the segments
                var lineText = string.Concat(logMessage.Segments.Select(s => s.Text));
        
                // Format the line with timestamp and level for clarity
                stringBuilder.AppendLine($"{logMessage.Timestamp:HH:mm:ss} [{logMessage.Level.ToString().ToUpper()}] {lineText}");
            }

            try
            {
                Clipboard.SetText(stringBuilder.ToString());
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Failed to copy console content to clipboard");
                MessageBox.Show("Could not copy to clipboard.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private ConcurrentQueue<PromptTask> _promptsQueue = new();
        private bool _canceledTasks = false;
        public ICommand ClearQueueCommand => new RelayCommand(x =>
        {
            _canceledTasks = true;
            InterruptCommand.Execute(null);
            IsInfiniteQueueEnabled = false;
        
            lock (_processingLock)
            {
                if (!_isProcessing)
                {
                    _promptsQueue.Clear();
                    TotalTasks = CompletedTasks = 0;
                    CurrentProgress = 0;
                }
            }
        }, canExecute: x => TotalTasks > 0 && CompletedTasks < TotalTasks);

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
                    scripts = (tab.Workflow.Scripts.Hooks.Any() || tab.Workflow.Scripts.Actions.Any()) ? tab.Workflow.Scripts : null
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
        
        public void QueuePromptFromJObject(JObject prompt, WorkflowTabViewModel originTab)
        {
            if (prompt == null || originTab == null) return;

            var fullState = new
            {
                prompt = prompt,
                promptTemplate = originTab.Workflow.Groups,
                scripts = (originTab.Workflow.Scripts.Hooks.Any() || originTab.Workflow.Scripts.Actions.Any()) ? originTab.Workflow.Scripts : null
            };
    
            string fullWorkflowStateJson = JsonConvert.SerializeObject(fullState, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.None });

            var task = new PromptTask
            {
                JsonPromptForApi = prompt.ToString(),
                FullWorkflowStateJson = fullWorkflowStateJson,
                OriginTab = originTab
            };

            _promptsQueue.Enqueue(task);
            TotalTasks++;

            lock (_processingLock)
            {
                if (!_isProcessing)
                {
                    _isProcessing = true;
                    _ = Task.Run(ProcessQueueAsync);
                }
            }
        }
        
        private void Queue(object o)
        {
            if (SelectedTab == null || !SelectedTab.Workflow.IsLoaded) return;
            
            SelectedTab.ExecuteHook("on_queue_start", SelectedTab.Workflow.LoadedApi);
            
            var promptTasks = CreatePromptTasks(SelectedTab);
            if (promptTasks.Count == 0) return;
            
            if (_canceledTasks || (_promptsQueue.IsEmpty && !_isProcessing))
            {
                CompletedTasks = 0;
                TotalTasks = 0;
                CurrentProgress = 0;
            }
            _canceledTasks = false;
        
            foreach (var task in promptTasks)
            {
                _promptsQueue.Enqueue(task);
            }
            TotalTasks += promptTasks.Count;
        
            lock (_processingLock)
            {
                if (!_isProcessing)
                {
                    _isProcessing = true;
                    _ = Task.Run(ProcessQueueAsync);
                }
            }
        }

        private async Task ProcessQueueAsync()
        {
            WorkflowTabViewModel lastTaskOriginTab = null; 
            
            try
            {
                while (true)
                {
                    if (_canceledTasks) break;
        
                    if (_promptsQueue.TryDequeue(out var task))
                    {
                        lastTaskOriginTab = task.OriginTab;
                        
                        try
                        {
                            var promptForTask = JObject.Parse(task.JsonPromptForApi);
                            
                            await foreach (var io in _comfyuiModel.QueuePrompt(task.JsonPromptForApi))
                            {
                                if (_canceledTasks) break;
                                
                                io.Prompt = task.FullWorkflowStateJson;

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    if (!this.ImageProcessing.ImageOutputs.Any(existing => existing.FilePath == io.FilePath))
                                    {
                                        this.ImageProcessing.ImageOutputs.Insert(0, io);
                                    }

                                    task.OriginTab?.ExecuteHook("on_output_received", promptForTask, io);
                                });
                            }
        
                            if (_canceledTasks) break;
        
                            CompletedTasks++;
                            CurrentProgress = (TotalTasks > 0) ? (CompletedTasks * 100) / TotalTasks : 0;
                            
                            task.OriginTab?.ExecuteHook("on_queue_finish", promptForTask);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log(ex, "[Connection Error] Failed to queue prompt");
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show(
                                    LocalizationService.Instance["MainVM_ConnectionErrorMessage"],
                                    LocalizationService.Instance["MainVM_ConnectionErrorTitle"],
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                            });
                            
                            _canceledTasks = true;
                            break;
                        }
                    }
                    else
                    {
                        if (IsInfiniteQueueEnabled && !_canceledTasks && lastTaskOriginTab != null)
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
        
                if (_canceledTasks)
                {
                    _promptsQueue.Clear();
                    TotalTasks = CompletedTasks = 0;
                    CurrentProgress = 0;
                    IsInfiniteQueueEnabled = false;
                }
                else if (lastTaskOriginTab != null) // Queue batch finished
                {
                    lastTaskOriginTab.ExecuteHook("on_batch_finished", lastTaskOriginTab.Workflow.LoadedApi);
                }
            }
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
                if (tab.Workflow.IsLoaded && !string.IsNullOrEmpty(tab.FilePath) && tab.Workflow.LoadedApi != null)
                {
                    _sessionManager.SaveSession(tab.Workflow.LoadedApi, tab.Workflow.Groups, tab.FilePath);
                }
            }
            
            if (SelectedTab != null)
            {
                _settings.LastSeedControlState = SelectedTab.WorkflowInputsController.SelectedSeedControl;
            }
            
            _settings.LastOpenWorkflows = OpenTabs
                .Select(t => Path.GetRelativePath(Workflow.WorkflowsDir, t.FilePath).Replace(Path.DirectorySeparatorChar, '/'))
                .ToList();

            _settings.LastActiveWorkflow = SelectedTab != null 
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