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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using Microsoft.Win32;
using PropertyChanged;

namespace Comfizen
{
    public enum FileTypeFilter { All, Images, Video }
    public enum SortOption { NewestFirst, OldestFirst }

    /// <summary>
    /// Represents a header item in the workflow selection ComboBox.
    /// </summary>
    public class WorkflowListHeader
    {
        public string Title { get; set; }
    }

    /// <summary>
    /// The main ViewModel for the application's main window.
    /// Manages application state, including open tabs, workflows, and global commands.
    /// </summary>
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
        /// Gets the collection of currently open workflow tabs.
        /// </summary>
        public ObservableCollection<WorkflowTabViewModel> OpenTabs { get; } = new ObservableCollection<WorkflowTabViewModel>();

        private WorkflowTabViewModel _selectedTab;
        /// <summary>
        /// Gets or sets the currently selected workflow tab.
        /// Saves the session of the previously selected tab when changed.
        /// </summary>
        public WorkflowTabViewModel SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (_selectedTab == value) return;

                if (_selectedTab != null && _selectedTab.Workflow.IsLoaded)
                {
                    _sessionManager.SaveSession(_selectedTab.Workflow.LoadedApi, _selectedTab.FilePath);
                }

                _selectedTab = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTab)));
            }
        }
        
        /// <summary>
        /// Gets or sets the collection of all available workflow file paths (relative).
        /// </summary>
        public ObservableCollection<string> Workflows { get; set; } = new();
        
        /// <summary>
        /// Gets or sets the collection of items to display in the workflow ComboBox, including headers and workflow paths.
        /// </summary>
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
        public ICommand OpenSettingsCommand { get; set; }
        public ICommand QueueCommand => new AsyncRelayCommand(Queue, canExecute: x => SelectedTab?.Workflow.IsLoaded ?? false);
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
        public ICommand OpenWildcardBrowserCommand { get; }
        
        public event PropertyChangedEventHandler? PropertyChanged;

        public MainViewModel()
        {
            _settingsService = new SettingsService();
            _settings = _settingsService.LoadSettings();

            _comfyuiModel = new ComfyuiModel(_settings);
            _modelService = new ModelService(_settings);
            
            MaxQueueSize = _settings.MaxQueueSize;
            _sessionManager = new SessionManager(_settings);
            
            _consoleLogService = new ConsoleLogService(_settings);
            ConsoleLogMessages = _consoleLogService.LogMessages;
            _consoleLogService.ConnectAsync();

            ToggleConsoleCommand = new RelayCommand(_ => IsConsoleVisible = !IsConsoleVisible);
            ClearConsoleCommand = new RelayCommand(_ => ConsoleLogMessages.Clear());
            
            CloseTabCommand = new RelayCommand(p => CloseTab(p as WorkflowTabViewModel));

            OpenConstructorCommand = new RelayCommand(o => {
                new UIConstructor().ShowDialog();
                UpdateWorkflows();
            });

            EditWorkflowCommand = new RelayCommand(o => {
                var relativePath = Path.GetRelativePath(Workflow.WorkflowsDir, SelectedTab.FilePath);
                new UIConstructor(relativePath).ShowDialog();
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
        
        /// <summary>
        /// Opens a new tab for the specified workflow or switches to it if already open.
        /// </summary>
        /// <param name="relativePath">The relative path to the workflow file.</param>
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

            if (tabToClose.FullScreen.IsFullScreenOpen)
            {
                tabToClose.FullScreen.IsFullScreenOpen = false;
            }

            if (tabToClose.Workflow.IsLoaded)
            {
                _sessionManager.SaveSession(tabToClose.Workflow.LoadedApi, tabToClose.FilePath);
            }
            
            OpenTabs.Remove(tabToClose);
        }

        private void RefreshModels(object obj)
        {
            if (SelectedTab == null || !SelectedTab.Workflow.IsLoaded) return;

            var workflowToReload = SelectedTab.FilePath;
            _sessionManager.SaveSession(SelectedTab.Workflow.LoadedApi, workflowToReload);
            
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
                
                SelectedTab.Workflow.SaveWorkflow(relativePathWithoutExtension.Replace(Path.DirectorySeparatorChar, '/'));
                _sessionManager.SaveSession(SelectedTab.Workflow.LoadedApi, SelectedTab.FilePath);

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
            
            ModelService.ClearCache();
            _settings = _settingsService.LoadSettings();
            MaxQueueSize = _settings.MaxQueueSize;
            
            _comfyuiModel = new ComfyuiModel(_settings);
            _modelService = new ModelService(_settings);
            _sessionManager = new SessionManager(_settings);
            
            await _consoleLogService.ReconnectAsync(_settings);
            
            foreach (var tab in OpenTabs)
            {
                tab.UpdateAfterSettingsChange(_settings, _comfyuiModel, _modelService, _sessionManager);
            }
        }

        private ConcurrentQueue<string> _promptsQueue = new();
        private bool _canceledTasks = false;
        public ICommand ClearQueueCommand => new RelayCommand(x =>
        {
            InterruptCommand.Execute(null);
            _promptsQueue.Clear();
            _canceledTasks = true;
            CompletedTasks = TotalTasks = 0;
        }, canExecute: x => TotalTasks > 0);

        private SemaphoreSlim _queueSemaphore = new(1, 1);

        private async Task Queue(object o)
        {
            if (SelectedTab == null || !SelectedTab.Workflow.IsLoaded) return;

            _canceledTasks = false;
            var prompts = CreatePromptTasks().ToList();
            foreach (var prompt in prompts)
            {
                _promptsQueue.Enqueue(prompt);
            }

            TotalTasks += prompts.Count;

            await _queueSemaphore.WaitAsync();
            try
            {
                while (_promptsQueue.TryDequeue(out string prompt))
                {
                    try
                    {
                        await foreach (var io in _comfyuiModel.QueuePrompt(prompt))
                        {
                            SelectedTab.ImageProcessing.ImageOutputs.Insert(0, io);
                        }

                        if (!_canceledTasks)
                        {
                            CompletedTasks++;
                            CurrentProgress = (CompletedTasks * 100) / TotalTasks;

                            if (TotalTasks == CompletedTasks)
                            {
                                CompletedTasks = TotalTasks = 0;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Connection Error] Failed to queue prompt: {ex.Message}");
            
                        MessageBox.Show(
                            LocalizationService.Instance["MainVM_ConnectionErrorMessage"], 
                            LocalizationService.Instance["MainVM_ConnectionErrorTitle"], 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Error);
                        
                        _promptsQueue.Clear();
                        TotalTasks = 0;
                        CompletedTasks = 0;
                        break; 
                    }
                }
            }
            finally
            {
                _queueSemaphore.Release();
            }
        }

        private IEnumerable<string> CreatePromptTasks()
        {
            for (int i = 0; i < QueueSize; i++)
            {
                var prompt = SelectedTab.Workflow.JsonClone();
                SelectedTab.WorkflowInputsController.ProcessSpecialFields(prompt);
                yield return prompt.ToString();
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

        /// <summary>
        /// Saves the application's state before closing.
        /// </summary>
        public void SaveStateOnClose()
        {
            GlobalEventManager.WorkflowSaved -= OnWorkflowSaved;
            _consoleLogService.DisconnectAsync().Wait();

            foreach (var tab in OpenTabs)
            {
                if (tab.Workflow.IsLoaded && !string.IsNullOrEmpty(tab.FilePath) && tab.Workflow.LoadedApi != null)
                {
                    _sessionManager.SaveSession(tab.Workflow.LoadedApi, tab.FilePath);
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