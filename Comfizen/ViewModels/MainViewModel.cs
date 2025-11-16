using System;
using System.Collections;
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
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic.Logging;
using Microsoft.Win32;
using PropertyChanged;
using Newtonsoft.Json.Linq;

namespace Comfizen
{
    public enum FileTypeFilter { All, Images, Video }
    public enum SortOption { NewestFirst, OldestFirst, Similarity }

    public class WorkflowListHeader
    {
        public string Title { get; set; }
    }
    
    public enum LogFilterType { All, Comfy, Local, Python }
    

    public class SerializablePromptTask
    {
        public string JsonPromptForApi { get; set; }
        public string FullWorkflowStateJson { get; set; }
        public string OriginTabFilePath { get; set; } // Store path instead of the whole ViewModel
        public bool IsGridTask { get; set; }
        public string XValue { get; set; }
        public string YValue { get; set; }
        public MainViewModel.XYGridConfig GridConfig { get; set; }
    }


    [AddINotifyPropertyChangedInterface]
    public class MainViewModel : INotifyPropertyChanged
    {
        public static ICommand GlobalQueueCommand { get; private set; }
        
        private ComfyuiModel _comfyuiModel;
        private AppSettings _settings;
        private readonly SettingsService _settingsService;
        private SessionManager _sessionManager;
        private ModelService _modelService;
        private ConsoleLogService _consoleLogService;
        
        public AppSettings Settings => _settings;
        public Dictionary<WorkflowGroupViewModel, Window> UndockedWindows => _undockedWindows;

        /// <summary>
        /// Provides access to the ConsoleLogService instance for external configuration.
        /// </summary>
        public ConsoleLogService GetConsoleLogService() => _consoleLogService;
        
        public ImageProcessingViewModel ImageProcessing { get; private set; }
        public FullScreenViewModel FullScreen { get; private set; }
        public SliderCompareViewModel SliderCompare { get; private set; }

        public ObservableCollection<WorkflowTabViewModel> OpenTabs { get; } = new ObservableCollection<WorkflowTabViewModel>();

        private WorkflowTabViewModel _selectedTab;
        public WorkflowTabViewModel SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (_selectedTab == value) return;

                // Asynchronously save the state of the tab we are leaving
                var oldTab = _selectedTab;
                if (oldTab != null && oldTab.Workflow.IsLoaded && !oldTab.IsVirtual)
                {
                    Task.Run(async () =>
                    {
                        await oldTab.WorkflowInputsController.PrepareForSessionSaveAsync();
                        string activeInnerTabName = oldTab.WorkflowInputsController.SelectedTabLayout?.Header;
                        var hookStates = oldTab.WorkflowInputsController.GlobalControls.GetHookStates();
                        _sessionManager.SaveSession(oldTab.Workflow, oldTab.FilePath, activeInnerTabName, hookStates);
                    });
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
        public ICommand TogglePauseQueueCommand { get; }
        public ICommand ClearBlockedNodesCommand { get; }
        public ICommand OpenGroupNavigationCommand { get; }
        public ICommand GoToGroupCommand { get; }
        public ICommand CompareSelectedImagesCommand { get; }
        public ICommand DeleteSelectedQueueItemCommand { get; }
        
        public ICollectionView FilteredPendingQueueItemsView { get; }
        public string QueueSearchText { get; set; }
        
        public ObservableCollection<WorkflowGroupViewModel> AllNavigableGroups { get; } = new ObservableCollection<WorkflowGroupViewModel>();
        public ICollectionView FilteredNavigableGroupsView { get; private set; }
        public string GroupNavigationSearchText { get; set; }
        
        public ObservableCollection<QueueItemViewModel> PendingQueueItems { get; } = new ObservableCollection<QueueItemViewModel>();
        public QueueItemViewModel CurrentTaskVm { get; private set; }
        
        public void UpdateQueueItemIndexes()
        {
            for (int i = 0; i < PendingQueueItems.Count; i++)
            {
                PendingQueueItems[i].DisplayIndex = i + 1;
            }
        }
        
        
        private QueueItemViewModel _selectedQueueItem;
        public QueueItemViewModel SelectedQueueItem
        {
            get => _selectedQueueItem;
            set
            {
                if (_selectedQueueItem != value)
                {
                    _selectedQueueItem = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedQueueItem)));
                }
            }
        }
        
        public IEnumerable<WorkflowGroupViewModel> AllGroupsInSelectedTab => 
            SelectedTab?.WorkflowInputsController.TabLayoouts
                .SelectMany(tabLayout => tabLayout.Groups)
                .OrderBy(g => g.Name)
                .ToList() ?? Enumerable.Empty<WorkflowGroupViewModel>();
        
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
        private readonly DispatcherTimer _etaUpdateTimer;
        
        public ICommand SaveChangesToWorkflowCommand { get; private set; }
        
        public ICollectionView ConsoleLogMessages { get; private set; }
        private readonly ObservableCollection<LogMessage> _allConsoleLogMessages;
        public bool IsConsoleVisible { get; set; } = false;
        public ICommand ToggleConsoleCommand { get; }
        public ICommand ClearConsoleCommand { get; }
        public ICommand CopyConsoleCommand { get; }
        public ICommand CopyConsoleItemCommand { get; }
        public ICommand HideConsoleCommand { get; }
        public ICommand OpenWildcardBrowserCommand { get; }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        
        public bool IsInfiniteQueueEnabled { get; set; } = false;
        public bool IsQueuePaused { get; set; } = false;
        public bool IsQueueManagerVisible { get; set; } = false;
        public ICommand ToggleQueueManagerCommand { get; }
        public ICommand HideQueueManagerCommand { get; }

        public ICommand ToggleInfiniteQueueCommand { get; }
        
        public ICommand CopyWorkflowLinkCommand { get; }
        
        public ICommand RenameWorkflowCommand { get; }
        
        // Helper classes to manage grid processing state
        private class GridCellResult
        {
            public ImageOutput ImageOutput { get; set; }
            public string XValue { get; set; }
            public string YValue { get; set; }
        }

        public class XYGridConfig
        {
            public string XAxisField { get; set; }
            public string XAxisPath { get; set; }
            public IReadOnlyList<string> XValues { get; set; }
            public string YAxisField { get; set; }
            public string YAxisPath { get; set; }
            public IReadOnlyList<string> YValues { get; set; }
            public bool CreateGridImage { get; set; }
        }
        
        public class PromptTask
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
            
            public bool IsGridTask { get; set; }
            public string XValue { get; set; }
            public string YValue { get; set; }
            public XYGridConfig GridConfig { get; set; }
        }
        // ADD: Command for undocking/redocking groups
        public ICommand ToggleUndockGroupCommand { get; }
        // ADD: Dictionary to keep track of floating windows
        private readonly Dictionary<WorkflowGroupViewModel, Window> _undockedWindows = new();
        private Point _nextUndockPosition = new Point(100, 100);
        private double _currentRowMaxHeight = 0;
        private const double WindowSpacing = 10;
        private const double InitialLeft = 100;
        private const double InitialTop = 100;
        
        public ICommand OpenHelpCommand { get; }
        
        /// <summary>
        /// A transient flag to prevent re-entrant calls during the async shutdown process.
        /// </summary>
        [JsonIgnore] 
        public bool IsShuttingDown { get; set; } = false;
        
        public MainViewModel()
        {
            _settingsService = SettingsService.Instance;
            _settings = _settingsService.Settings;

            _comfyuiModel = new ComfyuiModel(_settings);
            _modelService = new ModelService(_settings);
            
            ImageProcessing = new ImageProcessingViewModel(_comfyuiModel, _settings);
            ImageProcessing.GalleryThumbnailSize = _settings.GalleryThumbnailSize;
            IsConsoleVisible = _settings.IsConsoleVisible;
            FullScreen = new FullScreenViewModel(this, ImageProcessing, _comfyuiModel, _settings, ImageProcessing.FilteredImageOutputs);
            FullScreen.PropertyChanged += FullScreen_PropertyChanged;
            SliderCompare = new SliderCompareViewModel();
            
            MaxQueueSize = _settings.MaxQueueSize;
            _sessionManager = new SessionManager(_settings);
            
            _consoleLogService = new ConsoleLogService(_settings);
            _consoleLogService.OnLogReceived += HandleHighPriorityLog;
            _allConsoleLogMessages = _consoleLogService.LogMessages;
            ConsoleLogMessages = CollectionViewSource.GetDefaultView(_allConsoleLogMessages);
            ConsoleLogMessages.Filter = FilterLogs;
            _consoleLogService.ConnectAsync();
            
            _etaUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _etaUpdateTimer.Tick += EtaUpdateTimer_Tick;
            
            ToggleConsoleCommand = new RelayCommand(_ => IsConsoleVisible = !IsConsoleVisible);
            ClearConsoleCommand = new RelayCommand(_ => _allConsoleLogMessages.Clear());
            CopyConsoleCommand = new RelayCommand(CopyConsoleContent, _ => _allConsoleLogMessages.Any());
            CopyConsoleItemCommand = new RelayCommand(CopyConsoleItem);
            HideConsoleCommand = new RelayCommand(_ => IsConsoleVisible = false);
            ToggleQueueManagerCommand = new RelayCommand(_ => IsQueueManagerVisible = !IsQueueManagerVisible);
            HideQueueManagerCommand = new RelayCommand(_ => IsQueueManagerVisible = false);
            
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
                _ => _isProcessing
            );
            
            ClearSessionCommand = new RelayCommand(o => {
                if (o is WorkflowTabViewModel tab) ClearSessionForWorkflow(tab);
            }, o => o is WorkflowTabViewModel tab && !tab.IsVirtual);

            PasteImageCommand = new RelayCommand(
                _ => SelectedTab?.WorkflowInputsController.HandlePasteOperation(),
                _ => SelectedTab != null
            );
            
            RefreshModelsCommand = new RelayCommand(RefreshModels, 
                o => SelectedTab != null && SelectedTab.Workflow.IsLoaded && !SelectedTab.IsVirtual);
            
            SaveChangesToWorkflowCommand = new RelayCommand(o => {
                if (o is WorkflowTabViewModel tab) SaveChangesToWorkflow(tab);
            }, o => o is WorkflowTabViewModel tab && !tab.IsVirtual && tab.Workflow.IsLoaded);
            
            ExportCurrentStateCommand = new RelayCommand(o => {
                if (o is WorkflowTabViewModel tab) ExportCurrentState(tab);
            }, o => o is WorkflowTabViewModel tab && tab.Workflow.IsLoaded);
            // Disable delete for virtual tabs (they are just closed, not deleted from disk).
            DeleteWorkflowCommand = new RelayCommand(o => {
                if (o is WorkflowTabViewModel tab) DeleteWorkflow(tab);
            }, o => o is WorkflowTabViewModel tab && !tab.IsVirtual);
            
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
            
            ClearBlockedNodesCommand = new RelayCommand(o =>
            {
                if (o is WorkflowTabViewModel tab)
                {
                    tab.Workflow.BlockedNodeIds.Clear();
                    Logger.LogToConsole(LocalizationService.Instance["MainVM_ClearBlockedNodesSuccessMessage"]);
                }
            }, o => o is WorkflowTabViewModel tab && tab.Workflow.BlockedNodeIds.Any());
            
            TogglePauseQueueCommand = new RelayCommand(_ => IsQueuePaused = !IsQueuePaused, _ => _isProcessing);
            
            OpenGroupNavigationCommand = new RelayCommand(_ =>
            {
                if (SelectedTab?.WorkflowInputsController.TabLayoouts.SelectMany(t => t.Groups).Any() == true)
                {
                    GroupNavigationSearchText = ""; // Clear search on open
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
            
            DeleteSelectedQueueItemCommand = new RelayCommand(param =>
            {
                if (param is QueueItemViewModel item)
                {
                    PendingQueueItems.Remove(item);
                    
                    if (TotalTasks > 0)
                    {
                        TotalTasks--;
                    }
                }
            }, param => param is QueueItemViewModel);
            
            FilteredPendingQueueItemsView = CollectionViewSource.GetDefaultView(PendingQueueItems);
            FilteredPendingQueueItemsView.Filter = FilterQueueItems;
  
            PendingQueueItems.CollectionChanged += (sender, args) => UpdateQueueItemIndexes();;
            
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
            UpdateAllNavigableGroups();
            
            LoadPersistedQueue();
            
            OpenTabs.CollectionChanged += (s, e) => UpdateAllNavigableGroups();
            this.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SelectedTab))
                {
                    UpdateAllNavigableGroups();
                }
                else if (e.PropertyName == nameof(GroupNavigationSearchText))
                {
                    FilteredNavigableGroupsView?.Refresh();
                }
                else if (e.PropertyName == nameof(QueueSearchText))
                {
                    FilteredPendingQueueItemsView?.Refresh();
                }
            };
            
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
                PendingQueueItems.Clear();

                CompletedTasks = 0;
                TotalTasks = 0;
                CurrentProgress = 0;
            
            }, canExecute: x => PendingQueueItems.Any() || (_isProcessing && TotalTasks > CompletedTasks));
            
            QueueCommand = new AsyncRelayCommand(Queue, canExecute: x => SelectedTab?.Workflow.IsLoaded ?? false);
            
            GlobalQueueCommand = QueueCommand;
            
            ToggleUndockGroupCommand = new RelayCommand(ToggleUndockGroup);
            
            CopyWorkflowLinkCommand = new RelayCommand(p =>
            {
                if (p is WorkflowTabViewModel tab)
                {
                    try
                    {
                        // Get the relative path, normalize it, remove the extension, and URI-encode it
                        var relativePath = Path.GetRelativePath(Workflow.WorkflowsDir, tab.FilePath);
                        var pathWithoutExtension = Path.ChangeExtension(relativePath, null);
                        var normalizedPath = pathWithoutExtension.Replace(Path.DirectorySeparatorChar, '/');
                        var encodedPath = Uri.EscapeDataString(normalizedPath);
                    
                        // Format the final markdown link and copy to clipboard
                        var markdownLink = $"[{tab.Header}](wf://{encodedPath}.json)";
                        Clipboard.SetText(markdownLink);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(ex, "Failed to create or copy workflow markdown link.");
                    }
                }
            }, p => p is WorkflowTabViewModel tab && !tab.IsVirtual);
            
            OpenHelpCommand = new RelayCommand(OpenHelp);
            
            CompareSelectedImagesCommand = new RelayCommand(param => {
                if (param is not IList selectedItems || selectedItems.Count < 1) return;
            
                var images = selectedItems.Cast<ImageOutput>().ToList();

                if (images.Count == 1)
                {
                    // If only one image is selected, open it on the left side.
                    SliderCompare.Open(images[0]);
                }
                else
                {
                    // If two (or more) are selected, use the first two.
                    SliderCompare.Open(images[0], images[1]);
                }
            
            }, param => param is IList list && list.Count >= 1); // Can execute if 1 or more images are selected.
            
            RenameWorkflowCommand = new RelayCommand(p => {
                if (p is WorkflowTabViewModel tab)
                {
                    // Set the IsRenaming flag to show the TextBox
                    tab.IsRenaming = true; 
                }
            }, p => p is WorkflowTabViewModel tab && !tab.IsVirtual);
        }
        
        /// <summary>
        /// Handles the logic of renaming a workflow file and updating all related application state.
        /// Supports moving the file to a subdirectory by including slashes in the new name.
        /// </summary>
        /// <param name="tab">The view model of the tab being renamed.</param>
        /// <param name="newName">The desired new name, which can include a relative path (e.g., "subdir/new_name").</param>
        public void RenameWorkflow(WorkflowTabViewModel tab, string newName)
        {
            if (tab == null || !tab.IsRenaming) return;

            tab.IsRenaming = false;
            // Store old display name for logs and potential revert
            string oldHeaderForDisplay = tab.Header; 
            string normalizedNewName = newName.Replace('\\', '/');

            // CORRECTED CHECK: Compare the new normalized name against the old relative path tooltip.
            if (string.IsNullOrWhiteSpace(normalizedNewName) || normalizedNewName.Equals(tab.RelativePathTooltip, StringComparison.OrdinalIgnoreCase))
            {
                return; // No change, do nothing.
            }

            try
            {
                var oldRelativePath = Path.GetRelativePath(Workflow.WorkflowsDir, tab.FilePath).Replace(Path.DirectorySeparatorChar, '/');
                var newRelativePath = normalizedNewName + ".json";
                
                var oldFullPath = Path.GetFullPath(tab.FilePath);
                var newFullPath = Path.GetFullPath(Path.Combine(Workflow.WorkflowsDir, newRelativePath));

                if (File.Exists(newFullPath))
                {
                    MessageBox.Show($"A workflow named '{normalizedNewName}' already exists.", "Rename Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Ensure the target directory exists
                var newDirectory = Path.GetDirectoryName(newFullPath);
                if (!string.IsNullOrEmpty(newDirectory))
                {
                    Directory.CreateDirectory(newDirectory);
                }
                
                // Perform the rename/move
                File.Move(oldFullPath, newFullPath);
                Logger.Log($"Renamed workflow '{oldHeaderForDisplay}' to '{normalizedNewName}'.");

                // Clear old session file
                _sessionManager.ClearSession(oldFullPath);

                // Update tab properties
                tab.Header = Path.GetFileNameWithoutExtension(normalizedNewName);
                // This will force RelativePathTooltip to update as well
                typeof(WorkflowTabViewModel).GetProperty(nameof(WorkflowTabViewModel.FilePath)).SetValue(tab, newFullPath, null);
                
                // Update the main workflows list
                UpdateWorkflows();

                // Update recent workflows list
                var recentIndex = Settings.RecentWorkflows.IndexOf(oldRelativePath);
                if (recentIndex != -1)
                {
                    Settings.RecentWorkflows[recentIndex] = newRelativePath;
                }
                _settingsService.SaveSettings();
                UpdateWorkflowDisplayList();
            }
            catch (Exception ex)
            {
                Logger.Log(ex, $"Failed to rename workflow '{oldHeaderForDisplay}'.");
                MessageBox.Show($"Error renaming workflow: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                tab.Header = oldHeaderForDisplay; // Revert header on failure
            }
        }
        
        private void CopyConsoleItem(object item)
        {
            if (item is LogMessage logMessage)
            {
                var lineText = string.Concat(logMessage.Segments.Select(s => s.Text));
                try
                {
                    Clipboard.SetText(lineText);
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Failed to copy console item to clipboard");
                }
            }
        }

        private void EtaUpdateTimer_Tick(object sender, EventArgs e)
        {
            // This logic is moved from the processing loop to be called every second.
            if (_isProcessing && CompletedTasks > 0 && TotalTasks > CompletedTasks)
            {
                var elapsed = _queueStopwatch.Elapsed;
                var timePerTask = elapsed.TotalSeconds / CompletedTasks;
                var remainingSeconds = timePerTask * (TotalTasks - CompletedTasks);
                var remainingTime = TimeSpan.FromSeconds(remainingSeconds);
                EstimatedTimeRemaining = $"~{FormatEta(remainingTime)}";
            }
            // Don't clear the ETA here, it will be cleared when the queue finishes.
        }

        private void HandleHighPriorityLog(LogLevel level)
        {
            // This method can be called from a background thread, so we dispatch the UI update.
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsConsoleVisible = true;
            });
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
                        Logger.Log(LocalizationService.Instance["MainVM_ImportNoMetadataError"], LogLevel.Error);
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
        
        private void PatchPromptWithOriginalTexts(JObject prompt, Dictionary<string, string> originalTexts)
        {
            if (prompt == null || originalTexts == null) return;

            foreach (var entry in originalTexts)
            {
                var prop = Utils.GetJsonPropertyByPath(prompt, entry.Key);
                if (prop != null)
                {
                    prop.Value = new JValue(entry.Value);
                }
            }
        }
        
        public void ImportStateFromJObject(JObject data, string sourceFileName)
        {
            // --- START OF NEW LOGIC: Handle composite grid prompt ---
            JObject workflowData;
            JObject gridConfig = null;

            if (data["workflow"] is JObject wData) // Check for new composite format
            {
                workflowData = wData;
                gridConfig = data["grid_config"] as JObject;
            }
            else // Handle old format for backward compatibility
            {
                workflowData = data;
            }
            // --- END OF NEW LOGIC ---

            // Use workflowData instead of data from here on
            var promptData = workflowData["prompt"] as JObject;
            var uiDefinition = workflowData["promptTemplate"]?.ToObject<ObservableCollection<WorkflowGroup>>();
            var scripts = workflowData["scripts"]?.ToObject<ScriptCollection>() ?? new ScriptCollection();
            var tabs = workflowData["tabs"]?.ToObject<ObservableCollection<WorkflowTabDefinition>>() ?? new ObservableCollection<WorkflowTabDefinition>();
            var presets = workflowData["presets"]?.ToObject<Dictionary<Guid, List<GroupPreset>>>() ?? new Dictionary<Guid, List<GroupPreset>>();
            var nodeConnectionSnapshots = workflowData["nodeConnectionSnapshots"]?.ToObject<Dictionary<string, JObject>>() ?? new Dictionary<string, JObject>();
            var advancedPromptOriginalTexts = workflowData["advancedPromptOriginalTexts"]?.ToObject<Dictionary<string, string>>();
            
            if (promptData == null || uiDefinition == null)
            {
                MessageBox.Show(LocalizationService.Instance["MainVM_ImportInvalidFileError"], LocalizationService.Instance["MainVM_ImportInvalidFileTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            PatchPromptWithOriginalTexts(promptData, advancedPromptOriginalTexts);
            
            // Create a new in-memory Workflow object.
            var importedWorkflow = new Workflow();
            importedWorkflow.SetWorkflowData(promptData, uiDefinition, scripts, tabs, presets, nodeConnectionSnapshots);
            
            // Create a new "virtual" tab using the new constructor.
            var newTab = new WorkflowTabViewModel(
                importedWorkflow, 
                sourceFileName, // Use the image name as the header.
                _comfyuiModel, 
                _settings, 
                _modelService, 
                _sessionManager
            );

            // --- START OF NEW LOGIC: Apply grid config if it exists ---
            if (gridConfig != null)
            {
                var controller = newTab.WorkflowInputsController;
                try
                {
                    controller.IsXyGridEnabled = true;

                    string xAxisPath = gridConfig["x_axis_field_path"]?.ToString();
                    string yAxisPath = gridConfig["y_axis_field_path"]?.ToString();
                    
                    if (!string.IsNullOrEmpty(xAxisPath))
                    {
                        controller.SelectedXField = controller.GridableFields.FirstOrDefault(f => f?.Path == xAxisPath);
                    }

                    if (!string.IsNullOrEmpty(yAxisPath))
                    {
                        controller.SelectedYField = controller.GridableFields.FirstOrDefault(f => f?.Path == yAxisPath);
                    }

                    controller.XValues = string.Join(Environment.NewLine, gridConfig["x_values"]?.ToObject<List<string>>() ?? new List<string>());
                    controller.YValues = string.Join(Environment.NewLine, gridConfig["y_values"]?.ToObject<List<string>>() ?? new List<string>());
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Failed to restore XY Grid settings from imported image.");
                }
            }
            // --- END OF NEW LOGIC ---

            OpenTabs.Add(newTab);
            SelectedTab = newTab;
        }
        
        private void OnWorkflowSaved(object sender, WorkflowSaveEventArgs e)
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
                Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await tabToUpdate.Reload(e.SaveType);
                    UpdateAllNavigableGroups(); // Refresh navigable groups after reload
                });
            }
            else
            {
                var relativePath = Path.GetRelativePath(Workflow.WorkflowsDir, e.FilePath).Replace(Path.DirectorySeparatorChar, '/');
                OpenOrSwitchToWorkflow(relativePath);
            }
            
            UpdateWorkflows();
            UpdateWorkflowDisplayList();
        }
        
        public void OpenOrSwitchToWorkflow(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;
            
            // If the incoming path doesn't have an extension, assume .json.
            // This makes the system resilient to issues like the one in FileNameTrimmerConverter.
            if (!Path.HasExtension(relativePath))
            {
                relativePath += ".json";
            }
            
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
                string activeInnerTabName = tabToClose.WorkflowInputsController.SelectedTabLayout?.Header;
                var hookStates = tabToClose.WorkflowInputsController.GlobalControls.GetHookStates();
                _sessionManager.SaveSession(tabToClose.Workflow, tabToClose.FilePath, activeInnerTabName, hookStates);
            }
            
            OpenTabs.Remove(tabToClose);
            UpdateAllNavigableGroups(); // Refresh the list after a tab is closed
        }
        
        private void UpdateAllNavigableGroups()
        {
            AllNavigableGroups.Clear();
            if (SelectedTab?.WorkflowInputsController == null) return;

            // This now iterates through ALL groups in the currently active main tab.
            foreach (var layout in SelectedTab.WorkflowInputsController.TabLayoouts)
            {
                foreach (var groupVm in layout.Groups)
                {
                    // Assign a parent name for grouping in the UI
                    groupVm.ParentTabName = layout.Header;
                    AllNavigableGroups.Add(groupVm);
                }
            }
            
            if (FilteredNavigableGroupsView == null)
            {
                FilteredNavigableGroupsView = CollectionViewSource.GetDefaultView(AllNavigableGroups);
                FilteredNavigableGroupsView.GroupDescriptions.Add(new PropertyGroupDescription("ParentTabName"));
                FilteredNavigableGroupsView.Filter = GroupFilterPredicate;
            }
            
            FilteredNavigableGroupsView.Refresh();
            OnPropertyChanged(nameof(AllGroupsInSelectedTab)); // Keep this for now if anything else uses it
        }
        
        private bool GroupFilterPredicate(object item)
        {
            if (string.IsNullOrWhiteSpace(GroupNavigationSearchText))
            {
                return true; // No filter, show all
            }

            if (item is not WorkflowGroupViewModel groupVm)
            {
                return false;
            }

            // Multi-word search logic
            var searchTerms = GroupNavigationSearchText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return searchTerms.All(term => 
                groupVm.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        
        private bool FilterQueueItems(object item)
        {
            if (string.IsNullOrWhiteSpace(QueueSearchText))
            {
                return true;
            }

            if (item is not QueueItemViewModel vm)
            {
                return false;
            }
            
            var searchTerms = QueueSearchText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // Check if all search terms are found in the item's data
            return searchTerms.All(term =>
            {
                // Check against workflow name
                if (vm.WorkflowName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                
                // Check against details (field names and new values)
                if (vm.Details.Any(detail => 
                        (detail.DisplayName?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (detail.NewValue?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)))
                {
                    return true;
                }

                return false;
            });
        }

        private void RefreshModels(object obj)
        {
            if (SelectedTab == null || !SelectedTab.Workflow.IsLoaded) return;

            var workflowToReload = SelectedTab.FilePath;
            string activeInnerTabName = SelectedTab.WorkflowInputsController.SelectedTabLayout?.Header;
            var hookStates = SelectedTab.WorkflowInputsController.GlobalControls.GetHookStates();
            _sessionManager.SaveSession(SelectedTab.Workflow, workflowToReload, activeInnerTabName, hookStates);
            
            ModelService.ResetConnectionErrorFlag();
            ModelService.ClearCache();
            
            int tabIndex = OpenTabs.IndexOf(SelectedTab);
            OpenTabs.RemoveAt(tabIndex);

            var newTab = new WorkflowTabViewModel(workflowToReload, _comfyuiModel, _settings, _modelService, _sessionManager);
            OpenTabs.Insert(tabIndex, newTab);
            SelectedTab = newTab;
        }
        
        private void SaveChangesToWorkflow(WorkflowTabViewModel tab)
        {
            if (tab == null) return;

            var result = MessageBox.Show(
                string.Format(LocalizationService.Instance["MainVM_SaveConfirmationMessage"], tab.Header),
                LocalizationService.Instance["MainVM_SaveConfirmationTitle"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var fullPath = tab.FilePath;
                var relativePath = Path.GetRelativePath(Workflow.WorkflowsDir, fullPath);
                var relativePathWithoutExtension = Path.ChangeExtension(relativePath, null);
        
                tab.Workflow.SaveWorkflowWithCurrentState(relativePathWithoutExtension.Replace(Path.DirectorySeparatorChar, '/'));
                string activeInnerTabName = tab.WorkflowInputsController.SelectedTabLayout?.Header;
                var hookStates = tab.WorkflowInputsController.GlobalControls.GetHookStates();
                _sessionManager.SaveSession(tab.Workflow, tab.FilePath, activeInnerTabName, hookStates);

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
        
        private void ExportCurrentState(WorkflowTabViewModel tab)
        {
            if (tab == null || !tab.Workflow.IsLoaded || tab.Workflow.LoadedApi == null)
            {
                Logger.Log(LocalizationService.Instance["MainVM_ExportErrorMessage"], LogLevel.Error);
                return;
            }

            var dialog = new SaveFileDialog
            {
                FileName = Path.GetFileNameWithoutExtension(tab.Header) + "_current.json",
                Filter = "JSON File (*.json)|*.json",
                Title = LocalizationService.Instance["MainVM_ExportDialogTitle"]
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    JObject promptToExport = GetPromptForExport(tab);

                    if (promptToExport == null) return; // User cancelled

                    if (stripMeta)
                        Utils.StripAllMetaProperties(promptToExport);
                    string jsonContent = promptToExport.ToString(Formatting.Indented);
                    File.WriteAllText(dialog.FileName, jsonContent);
                    Logger.Log(string.Format(LocalizationService.Instance["MainVM_ExportSuccessMessage"], dialog.FileName));
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Failed to save exported state file.");
                }
            }
        }
        
        private JObject GetPromptForExport(WorkflowTabViewModel tab)
        {
            var controller = tab.WorkflowInputsController;
            var workflow = tab.Workflow;
            
            bool hasBypassFields = workflow.Groups
                .SelectMany(g => g.Tabs)
                .SelectMany(t => t.Fields)
                .Any(f => f.Type == FieldType.NodeBypass);

            if (!hasBypassFields)
            {
                var prompt = workflow.LoadedApi.DeepClone() as JObject;
                controller.ApplyNodeBypass(prompt); // Apply "as is"
                return prompt;
            }

            var result = MessageBox.Show(
                LocalizationService.Instance["MainVM_ExportBypassMessage"],
                LocalizationService.Instance["MainVM_ExportBypassTitle"],
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            var clonedPrompt = workflow.LoadedApi.DeepClone() as JObject;
            var bypassService = new NodeBypassService(controller.ObjectInfo, workflow.NodeConnectionSnapshots, controller.TabLayoouts);

            switch (result)
            {
                case MessageBoxResult.Yes: // Restore connections
                    bypassService.RestoreOriginalNodeConnections(clonedPrompt);
                    return clonedPrompt;
                case MessageBoxResult.No: // Export as is
                    bypassService.ApplyBypass(clonedPrompt);
                    return clonedPrompt;
                case MessageBoxResult.Cancel: // Cancel
                default:
                    return null;
            }
        }

        private async void OpenSettings(object obj)
        {
            var oldExtensions = _settings.ModelExtensions;
            var settingsWindow = new SettingsWindow { Owner = Application.Current.MainWindow };
            settingsWindow.ShowDialog();

            if (_settings.ModelExtensions != oldExtensions)
            {
                ModelService.ResetConnectionErrorFlag();
                ModelService.ClearCache();
            }

            // _settings = _settingsService.LoadSettings();
            MaxQueueSize = _settings.MaxQueueSize;
            
            // _comfyuiModel = new ComfyuiModel(_settings);
            // _modelService = new ModelService(_settings);
            // _sessionManager = new SessionManager(_settings);
            //
            // ImageProcessing.Settings = _settings;
            // FullScreen = new FullScreenViewModel(this, _comfyuiModel, _settings, ImageProcessing.FilteredImageOutputs);
            
            // await _consoleLogService.ReconnectAsync(_settings);
            
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

        private bool _cancellationRequested = false;
        private PromptTask _currentTask; // --- ADDED: To track the currently executing task ---

        private readonly object _processingLock = new object();
        private bool _isProcessing = false;
        
        /// <summary>
        /// Helper to convert a string value from the UI into the appropriate JToken type.
        /// </summary>
        private JToken ConvertValueToJToken(string stringValue, InputFieldViewModel fieldVm)
        {
            // Handle boolean conversion for CheckBoxFieldViewModel which represents "Any" type booleans
            if (fieldVm is CheckBoxFieldViewModel)
            {
                switch (stringValue.Trim().ToLowerInvariant())
                {
                    case "true":
                    case "on":
                    case "1":
                    case "yes":
                        return new JValue(true);
                    case "false":
                    case "off":
                    case "0":
                    case "no":
                        return new JValue(false);
                }
                // Fallback to bool.TryParse if it's a standard format
                if (bool.TryParse(stringValue, out bool boolVal))
                {
                    return new JValue(boolVal);
                }
            }
            
            // Try parsing as an integer first
            if (long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longVal))
            {
                return new JValue(longVal);
            }
            // Then try as a floating-point number
            if (double.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double doubleVal))
            {
                return new JValue(doubleVal);
            }
            // Fall back to a string
            return new JValue(stringValue);
        }
        
        private bool ConvertStringToBool(string value)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "true":
                case "on":
                case "1":
                case "yes":
                case "enabled":
                    return true;
                // All other values ("false", "off", "0", "disabled", etc.) will be false
                default:
                    return false;
            }
        }
        
        private async Task<List<PromptTask>> CreatePromptTasks(WorkflowTabViewModel tab)
        {
            var tasks = new List<PromptTask>();
            var controller = tab.WorkflowInputsController;

            Dictionary<string, string> GetAdvancedPromptOriginalTexts(JObject prompt)
            {
                var originalTexts = new Dictionary<string, string>();
                if (controller.WildcardPropertyPaths == null) return originalTexts;
                
                foreach (var path in controller.WildcardPropertyPaths)
                {
                    var prop = Utils.GetJsonPropertyByPath(prompt, path);
                    if (prop != null && prop.Value.Type == JTokenType.String)
                    {
                        originalTexts[path] = prop.Value.ToObject<string>();
                    }
                }
                return originalTexts;
            }

            if (controller.IsXyGridEnabled && controller.SelectedXField != null && !string.IsNullOrWhiteSpace(controller.XValues))
            {
                var xValuesList = controller.XValues.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).Where(v => !string.IsNullOrEmpty(v)).ToList();
                var yValuesList = new List<string> { "" };

                if (controller.SelectedYField != null && !string.IsNullOrWhiteSpace(controller.YValues))
                {
                    yValuesList = controller.YValues.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).Where(v => !string.IsNullOrEmpty(v)).ToList();
                }

                var gridConfigForBatch = new XYGridConfig
                {
                    XAxisField = controller.SelectedXField?.Name,
                    XAxisPath = controller.SelectedXField?.Path,
                    XValues = xValuesList,
                    YAxisField = controller.SelectedYField?.Name,
                    YAxisPath = controller.SelectedYField?.Path,
                    YValues = yValuesList,
                    CreateGridImage = controller.XyGridCreateGridImage
                };

                var pathsToIgnore = new HashSet<string>();
                if (controller.SelectedXField != null) pathsToIgnore.Add(controller.SelectedXField.Path);
                if (controller.SelectedYField != null) pathsToIgnore.Add(controller.SelectedYField.Path);

                var allBypassVms = tab.WorkflowInputsController.TabLayoouts
                    .SelectMany(t => t.Groups)
                    .SelectMany(g => g.Tabs.SelectMany(subTab => subTab.Fields))
                    .OfType<NodeBypassFieldViewModel>()
                    .ToList();


                foreach (var yValue in yValuesList)
                {
                    foreach (var xValue in xValuesList)
                    {
                        var xBypassVmInstance = controller.SelectedXField is NodeBypassFieldViewModel xBypassVm
                            ? allBypassVms.FirstOrDefault(vm => vm.Path == xBypassVm.Path)
                            : null;
                
                        var yBypassVmInstance = controller.SelectedYField is NodeBypassFieldViewModel yBypassVm
                            ? allBypassVms.FirstOrDefault(vm => vm.Path == yBypassVm.Path)
                            : null;
                
                        bool? originalXState = xBypassVmInstance?.IsEnabled;
                        bool? originalYState = yBypassVmInstance?.IsEnabled;
                        
                        try
                        {
                             var apiPromptForTask = tab.Workflow.JsonClone();
    
                            // Apply X value
                            if (xBypassVmInstance != null)
                            {
                                xBypassVmInstance.IsEnabled = ConvertStringToBool(xValue);
                            }
                            else if (controller.SelectedXField != null)
                            {
                                var xProp = Utils.GetJsonPropertyByPath(apiPromptForTask, controller.SelectedXField.Path);
                                if (xProp != null)
                                {
                                    xProp.Value = ConvertValueToJToken(xValue, controller.SelectedXField);
                                }
                            }

                            // Apply Y value if applicable
                            if (yBypassVmInstance != null)
                            {
                                yBypassVmInstance.IsEnabled = ConvertStringToBool(yValue);
                            }
                            else if (controller.SelectedYField != null)
                            {
                                var yProp = Utils.GetJsonPropertyByPath(apiPromptForTask, controller.SelectedYField.Path);
                                if (yProp != null)
                                {
                                    yProp.Value = ConvertValueToJToken(yValue, controller.SelectedYField);
                                }
                            }

                            var advancedPromptOriginalTexts = GetAdvancedPromptOriginalTexts(apiPromptForTask);

                            await tab.WorkflowInputsController.ProcessSpecialFieldsAsync(apiPromptForTask, pathsToIgnore);
                            tab.ExecuteHook("on_before_prompt_queue", apiPromptForTask);
                            
                            var fullStateForThisTask = new
                            {
                                prompt = apiPromptForTask,
                                promptTemplate = tab.Workflow.Groups,
                                scripts = (tab.Workflow.Scripts.Hooks.Any() || tab.Workflow.Scripts.Actions.Any()) ? tab.Workflow.Scripts : null,
                                tabs = tab.Workflow.Tabs.Any() ? tab.Workflow.Tabs : null,
                                presets = tab.Workflow.Presets.Any() ? tab.Workflow.Presets : null,
                                nodeConnectionSnapshots = tab.Workflow.NodeConnectionSnapshots.Any() ? tab.Workflow.NodeConnectionSnapshots : null,
                                advancedPromptOriginalTexts = advancedPromptOriginalTexts.Any() ? advancedPromptOriginalTexts : null
                            };
                            string fullWorkflowStateJsonForThisTask = JsonConvert.SerializeObject(fullStateForThisTask, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.None });

                            tasks.Add(new PromptTask
                            {
                                JsonPromptForApi = apiPromptForTask.ToString(),
                                FullWorkflowStateJson = fullWorkflowStateJsonForThisTask,
                                OriginTab = tab,
                                IsGridTask = true,
                                XValue = xValue,
                                YValue = yValue,
                                GridConfig = gridConfigForBatch
                            });
                        }
                        finally
                        {
                            // Restore original state
                            if (xBypassVmInstance != null && originalXState.HasValue)
                            {
                                xBypassVmInstance.IsEnabled = originalXState.Value;
                            }
                            if (yBypassVmInstance != null && originalYState.HasValue)
                            {
                                yBypassVmInstance.IsEnabled = originalYState.Value;
                            }
                        }
                    }
                }
                return tasks;
            }
            
            for (int i = 0; i < QueueSize; i++)
            {
                // 1. Create a clone of the API prompt that will be modified for this specific task.
                var apiPromptForTask = tab.Workflow.JsonClone();
                
                var advancedPromptOriginalTexts = GetAdvancedPromptOriginalTexts(apiPromptForTask);

                await tab.WorkflowInputsController.ProcessSpecialFieldsAsync(apiPromptForTask);
                
                tab.ExecuteHook("on_before_prompt_queue", apiPromptForTask);

                // 3. NOW, create the full state object using the MODIFIED prompt clone.
                // This ensures that the state we save to metadata is identical to what's used for generation.
                var fullStateForThisTask = new
                {
                    prompt = apiPromptForTask, // Use the modified prompt here
                    promptTemplate = tab.Workflow.Groups,
                    scripts = (tab.Workflow.Scripts.Hooks.Any() || tab.Workflow.Scripts.Actions.Any()) ? tab.Workflow.Scripts : null,
                    tabs = tab.Workflow.Tabs.Any() ? tab.Workflow.Tabs : null,
                    presets = tab.Workflow.Presets.Any() ? tab.Workflow.Presets : null,
                    nodeConnectionSnapshots = tab.Workflow.NodeConnectionSnapshots.Any() ? tab.Workflow.NodeConnectionSnapshots : null,
                    advancedPromptOriginalTexts = advancedPromptOriginalTexts.Any() ? advancedPromptOriginalTexts : null
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
                tabs = originTab.Workflow.Tabs.Any() ? originTab.Workflow.Tabs : null,
                presets = originTab.Workflow.Presets.Any() ? originTab.Workflow.Presets : null,
                nodeConnectionSnapshots = originTab.Workflow.NodeConnectionSnapshots.Any() ? originTab.Workflow.NodeConnectionSnapshots : null
            };
    
            string fullWorkflowStateJson = JsonConvert.SerializeObject(fullState, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.None });

            var task = new PromptTask
            {
                JsonPromptForApi = prompt.ToString(),
                FullWorkflowStateJson = fullWorkflowStateJson,
                OriginTab = originTab
            };

            // Dispatch the critical part (modifying collections and starting the processor) to the UI thread.
            Application.Current.Dispatcher.Invoke(() => EnqueueTaskInternal(task, originTab.Workflow.JsonClone()));
        }
        
        /// <summary>
        /// The internal implementation for enqueuing a task. This method MUST be called on the UI thread.
        /// </summary>
        private void EnqueueTaskInternal(PromptTask task, JObject templatePrompt)
        {
            var queueItem = new QueueItemViewModel(task, task.OriginTab.Header, templatePrompt);
            PopulateQueueItemDetails(queueItem);
            PendingQueueItems.Add(queueItem);
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
        
        private async Task Queue(object o)
        {
            if (SelectedTab == null || !SelectedTab.Workflow.IsLoaded) return;
            
            var templatePrompt = SelectedTab.Workflow.JsonClone();
        
            SelectedTab.ExecuteHook("on_queue_start", SelectedTab.Workflow.LoadedApi);
        
            var promptTasks = await CreatePromptTasks(SelectedTab);
            if (promptTasks.Count == 0) return;
        
            if (_cancellationRequested || (PendingQueueItems.Count == 0 && !_isProcessing))
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
                var queueItem = new QueueItemViewModel(task, SelectedTab.Header, templatePrompt);
                PopulateQueueItemDetails(queueItem);
                PendingQueueItems.Add(queueItem);
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
        
        private void OpenHelp(object o)
        {
            string helpTabHeader = LocalizationService.Instance["Help_TabHeader"];
            
            var existingHelpTab = OpenTabs.FirstOrDefault(t => t.Header == helpTabHeader);
            if (existingHelpTab != null)
            {
                SelectedTab = existingHelpTab;
                return;
            }
            
            var helpWorkflow = new Workflow();
            
            var helpGroup = new WorkflowGroup { Name = "Comfizen Help" };
            var markdownField = new WorkflowField
            {
                Name = helpTabHeader,
                Type = FieldType.Markdown,
                DefaultValue = LocalizationService.Instance["Help_Content"],
                MaxDisplayLines = null
            };
            helpGroup.Fields.Add(markdownField);
            helpWorkflow.Groups.Add(helpGroup);
            
            var helpTab = new WorkflowTabViewModel(
                helpWorkflow,
                helpTabHeader,
                _comfyuiModel,
                _settings,
                _modelService,
                _sessionManager
            );
            
            OpenTabs.Add(helpTab);
            SelectedTab = helpTab;
        }

        private async Task ProcessQueueAsync()
        {
            WorkflowTabViewModel lastTaskOriginTab = null; 
            List<GridCellResult> gridResults = null;
            XYGridConfig gridConfig = null;
            
            try
            {
                _etaUpdateTimer.Start();
                
                while (true)
                {
                    while (IsQueuePaused && !_cancellationRequested)
                    {
                        await Task.Delay(500);
                    }
                    
                    if (_cancellationRequested) break;
    
                    QueueItemViewModel taskVm = null;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        taskVm = PendingQueueItems.FirstOrDefault();
                        if (taskVm != null)
                        {
                            PendingQueueItems.RemoveAt(0);
                            CurrentTaskVm = taskVm;
                        }
                    });

                    if (taskVm != null)
                    {
                        var task = taskVm.Task;
                        _currentTask = task;
                        lastTaskOriginTab = task.OriginTab;
                        
                        if (task.IsGridTask && gridResults == null)
                        {
                            gridResults = new List<GridCellResult>();
                            gridConfig = task.GridConfig; 
                        }
                        
                        try
                        {
                            var promptForTask = JObject.Parse(task.JsonPromptForApi);
                            
                            await foreach (var io in _comfyuiModel.QueuePrompt(task.JsonPromptForApi))
                            {
                                // A check here is no longer needed as we want the task to finish
                                io.Prompt = task.FullWorkflowStateJson;
                                if (task.OriginTab?.Workflow.LoadedApi?[io.NodeId] is JObject nodeData)
                                {
                                    io.NodeTitle = nodeData["_meta"]?["title"]?.ToString() ?? "Untitled";
                                    io.NodeType = nodeData["class_type"]?.ToString();
                                }
                                
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    if (task.OriginTab.Workflow.BlockedNodeIds.Contains(io.NodeId))
                                    {
                                        return;
                                    }
                                    
                                    if (task.IsGridTask)
                                    {
                                        // For grid tasks, collect results instead of adding them to the gallery immediately
                                        gridResults.Add(new GridCellResult
                                        {
                                            ImageOutput = io,
                                            XValue = task.XValue,
                                            YValue = task.YValue
                                        });

                                        // START OF CHANGE: Conditionally add individual images to the gallery
                                        if (task.OriginTab.WorkflowInputsController.XyGridShowIndividualImages)
                                        {
                                            // Use VisualHash to prevent adding duplicates
                                            if (!this.ImageProcessing.ImageOutputs.Any(existing => existing.VisualHash == io.VisualHash))
                                            {
                                                this.ImageProcessing.ImageOutputs.Insert(0, io);
                                            }
                                        }
                                        // END OF CHANGE
                                    }
                                    else
                                    {
                                        // START OF CHANGE: Use VisualHash to prevent adding duplicates for regular tasks as well
                                        if (!this.ImageProcessing.ImageOutputs.Any(existing =>
                                                existing.VisualHash == io.VisualHash))
                                        {
                                            this.ImageProcessing.ImageOutputs.Insert(0, io);
                                        }
                                        // END OF CHANGE
                                    }

                                    task.OriginTab?.ExecuteHook("on_output_received", promptForTask, io);
                                });
                            }
                        
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                CompletedTasks++;
                                CurrentProgress = (TotalTasks > 0) ? (CompletedTasks * 100) / TotalTasks : 0;
                            });

                            // if (CompletedTasks > 0 && TotalTasks > CompletedTasks)
                            // {
                            //     var elapsed = _queueStopwatch.Elapsed;
                            //     // Only show ETA after a second to get a more stable estimate
                            //     if (elapsed.TotalSeconds > 1)
                            //     {
                            //         var timePerTask = elapsed / CompletedTasks;
                            //         var remainingTime = timePerTask * (TotalTasks - CompletedTasks);
                            //         EstimatedTimeRemaining = $"~{FormatEta(remainingTime)}";
                            //     }
                            // }
                            // else
                            // {
                            //     // Clear when the last task is done or if there's only one task.
                            //     EstimatedTimeRemaining = null;
                            // }
                            
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
                            newTasks = await CreatePromptTasks(lastTaskOriginTab);

                            if (newTasks != null && newTasks.Any())
                            {
                                // ADD NEW TASKS TO UI QUEUE
                                foreach (var p in newTasks)
                                {
                                    var templatePrompt = lastTaskOriginTab.Workflow.JsonClone();
                                    var queueItem = new QueueItemViewModel(p, lastTaskOriginTab.Header, templatePrompt);
                                    await Application.Current.Dispatcher.InvokeAsync(() =>
                                    {
                                        PopulateQueueItemDetails(queueItem);
                                        PendingQueueItems.Add(queueItem);
                                    });
                                }
                                TotalTasks += newTasks.Count;
                                await Task.Delay(100);
                            }
                            else
                            {
                                break; // No new tasks generated, so stop the queue.
                            }
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
                _etaUpdateTimer.Stop();
                
                await Application.Current.Dispatcher.InvokeAsync(() => CurrentTaskVm = null);
                
                // After the queue is empty, check if we need to generate a grid image
                if (gridResults != null && gridResults.Any() && gridConfig.CreateGridImage)
                {
                    try
                    {
                        // Check if any of the results are videos. If so, we cannot create an image grid.
                        if (gridResults.Any(r => r.ImageOutput.Type == FileType.Video))
                        {
                            Logger.Log("XY Grid image creation was skipped because the output was video.", LogLevel.Warning);
                        }
                        else
                        {
                            var gridImageBytes = Utils.CreateImageGrid(
                                gridResults.Select(r => new Utils.GridCellResult { ImageOutput = r.ImageOutput, XValue = r.XValue, YValue = r.YValue }).ToList(),
                                gridConfig.XAxisField, gridConfig.XValues,
                                gridConfig.YAxisField, gridConfig.YValues);

                            if (gridImageBytes != null)
                            {
                                var firstTaskResult = gridResults.First();
                                
                                string promptForGrid;
                                try
                                {
                                    var workflowJson = JObject.Parse(firstTaskResult.ImageOutput.Prompt);
                                    var gridConfigData = new JObject
                                    {
                                        ["x_axis_field_path"] = gridConfig.XAxisPath,
                                        ["x_axis_field_name"] = gridConfig.XAxisField,
                                        ["x_values"] = JArray.FromObject(gridConfig.XValues),
                                        ["y_axis_field_path"] = gridConfig.YAxisPath,
                                        ["y_axis_field_name"] = gridConfig.YAxisField,
                                        ["y_values"] = JArray.FromObject(gridConfig.YValues)
                                    };

                                    var compositePrompt = new JObject
                                    {
                                        ["workflow"] = workflowJson,
                                        ["grid_config"] = gridConfigData
                                    };
                                    promptForGrid = compositePrompt.ToString(Formatting.None);
                                }
                                catch (Exception ex)
                                {
                                    Logger.Log(ex, "Failed to create composite JSON for XY Grid image. Falling back to simple prompt.");
                                    promptForGrid = firstTaskResult.ImageOutput.Prompt; // Fallback
                                }

                                var gridImageOutput = new ImageOutput
                                {
                                    ImageBytes = gridImageBytes,
                                    FileName = $"{LocalizationService.Instance["XYGrid_GeneratedImageName"]}_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                                    Prompt = promptForGrid,
                                    VisualHash = Utils.ComputePixelHash(gridImageBytes)
                                };

                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    this.ImageProcessing.ImageOutputs.Insert(0, gridImageOutput);
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(ex, "Failed to create XY Grid image. The processing queue will continue.");
                    }
                }
                
                lock (_processingLock)
                {
                    _isProcessing = false;
                }
                _currentTask = null; // Clear the current task
            
                _queueStopwatch.Stop();
                EstimatedTimeRemaining = null; 
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsQueuePaused = false; // Always reset pause state when queue finishes
                });
            
                if (_cancellationRequested)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => PendingQueueItems.Clear());
                
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
        /// Populates the Details collection of a QueueItemViewModel by comparing its task state
        /// against the original workflow file state.
        /// </summary>
        private void PopulateQueueItemDetails(QueueItemViewModel item)
        {
            item.Details.Clear();
            if (item == null) return;

            try
            {
                var taskPrompt = JObject.Parse(item.Task.JsonPromptForApi);
                var originalApiPrompt = item.Task.OriginTab.Workflow.OriginalApi;

                if (taskPrompt == null || originalApiPrompt == null) return;

                var allFields = item.Task.OriginTab.Workflow.Groups
                .SelectMany(g => g.Tabs)
                .SelectMany(t => t.Fields);

                // Use SelectTokens for a deep search, which is more robust
                foreach (var taskToken in taskPrompt.SelectTokens("..*").OfType<JValue>())
                {
                    var path = taskToken.Path;
                    var templateToken = originalApiPrompt.SelectToken(path);

                    if (templateToken is JValue && !Utils.AreJTokensEquivalent(taskToken, templateToken))
                    {
                        var field = allFields.FirstOrDefault(f => f.Path == path);
                        var detail = new QueueItemDetailViewModel
                        {
                            FieldPath = path,
                            DisplayName = field?.Name ?? path.Split('.').Last(),
                            NewValue = taskToken.ToString(Formatting.None).Trim('"'),
                            NodeTitle = field?.NodeTitle,
                            NodeType = field?.NodeType
                        };
                        
                        string newValueString = taskToken.ToString(Formatting.None).Trim('"');
                        if (newValueString.Length > 1000 && (newValueString.StartsWith("iVBOR") || newValueString.StartsWith("/9j/") || newValueString.StartsWith("UklG")))
                        {
                            detail.NewValue = string.Format(LocalizationService.Instance["TextField_Base64Placeholder"], newValueString.Length / 1024);
                        }
                        else
                        {
                            detail.NewValue = newValueString;
                        }
                        
                        item.Details.Add(detail);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Failed to generate queue item details.");
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
            
            _settingsService.SaveSettings();
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
                            !t.IsVirtual && Path.GetFullPath(t.FilePath).Equals(lastActiveFullPath, StringComparison.OrdinalIgnoreCase)
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
        /// Resets the starting position for the next undocked window.
        /// Called when all undocked windows are closed.
        /// </summary>
        private void ResetUndockPositioning()
        {
            _nextUndockPosition = new Point(InitialLeft, InitialTop);
            _currentRowMaxHeight = 0;
        }
        
        /// <summary>
        /// Handles the logic for undocking a group into a new window or redocking it by closing the existing window.
        /// </summary>
        /// <param name="parameter">The WorkflowGroupViewModel to toggle.</param>
        private void ToggleUndockGroup(object parameter)
        {
            if (parameter is not WorkflowGroupViewModel groupVm) return;

            // If the window for this group already exists, close it to re-dock.
            if (_undockedWindows.TryGetValue(groupVm, out var existingWindow))
            {
                existingWindow.Close();
            }
            // Otherwise, create a new floating window for it.
            else
            {
                // --- START OF REWORKED PLACEMENT LOGIC ---
                var screenWidth = SystemParameters.WorkArea.Width;
                var screenHeight = SystemParameters.WorkArea.Height;
                const double defaultWindowWidth = 400; 
                const double estimatedWindowHeight = 300; // An estimate since height is dynamic

                // Check if the next window would go off the right edge of the screen
                if ((_nextUndockPosition.X + defaultWindowWidth) > screenWidth)
                {
                    // Line break: Move to the next row
                    _nextUndockPosition.X = InitialLeft;
                    _nextUndockPosition.Y += _currentRowMaxHeight + WindowSpacing;
                    _currentRowMaxHeight = 0; // Reset max height for the new row
                }

                // Check if the new row would go off the bottom of the screen
                if ((_nextUndockPosition.Y + estimatedWindowHeight) > screenHeight)
                {
                    // We've run out of non-overlapping space. Reset to the top-left to start cascading/overlapping.
                    ResetUndockPositioning();
                }
                
                var floatingWindow = new Window
                {
                    Title = groupVm.Name,
                    DataContext = groupVm, // The ViewModel for the window
                    Content = groupVm,     // The content to be templated
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = _nextUndockPosition.X,
                    Top = _nextUndockPosition.Y,
                    Topmost = true,
                    Width = defaultWindowWidth,
                    SizeToContent = SizeToContent.Height,
                    MaxHeight = SystemParameters.WorkArea.Height * 0.9,
                    MinWidth = 300,
                    MinHeight = 50,
                    Background = (Brush)Application.Current.FindResource("PrimaryBackground"),
                    Foreground = (Brush)Application.Current.FindResource("TextBrush"),
                    Style = (Style)Application.Current.FindResource("ToolWindowStyle"),
                    ContentTemplate = (DataTemplate)Application.Current.FindResource("UndockedGroupTemplate"),
                    ShowInTaskbar = false
                };
                
                // After the window is shown and rendered, update the placement state
                floatingWindow.Loaded += (s, e) => {
                    // Update the max height of the current row
                    _currentRowMaxHeight = Math.Max(_currentRowMaxHeight, floatingWindow.ActualHeight);
                    // Update the X position for the next window in the same row
                    _nextUndockPosition.X += floatingWindow.ActualWidth + WindowSpacing;
                };

                // --- END OF REWORKED PLACEMENT LOGIC ---

                floatingWindow.Closed += FloatingWindow_Closed;

                _undockedWindows[groupVm] = floatingWindow;
                groupVm.IsUndocked = true;
                floatingWindow.Show();
            }
        }

        /// Event handler for when a floating group window is closed.
        /// This triggers the re-docking logic.
        /// </summary>
        private void FloatingWindow_Closed(object sender, EventArgs e)
        {
            if (sender is not Window window || window.DataContext is not WorkflowGroupViewModel groupVm) return;

            // Clean up event handler to prevent memory leaks
            window.Closed -= FloatingWindow_Closed;
    
            // Check if the window is still in our tracking dictionary
            if (_undockedWindows.ContainsKey(groupVm))
            {
                groupVm.IsUndocked = false;
                _undockedWindows.Remove(groupVm);
            }
    
            // If this was the last undocked window, reset the smart placement logic.
            if (!_undockedWindows.Any())
            {
                ResetUndockPositioning();
            }

            // Explicitly activate the main window to restore focus.
            Application.Current.MainWindow?.Activate();
        }

        public async Task SaveStateOnCloseAsync()
        {
            GlobalEventManager.WorkflowSaved -= OnWorkflowSaved;
            
            _consoleLogService.OnLogReceived -= HandleHighPriorityLog;
            await _consoleLogService.DisconnectAsync();
            
            // --- START OF CHANGE: Save pending queue on close ---
            var queueToSave = new List<SerializablePromptTask>();
            if (_currentTask != null) // Save the currently executing task first
            {
                queueToSave.Add(new SerializablePromptTask
                {
                    JsonPromptForApi = _currentTask.JsonPromptForApi,
                    FullWorkflowStateJson = _currentTask.FullWorkflowStateJson,
                    OriginTabFilePath = !_currentTask.OriginTab.IsVirtual ? Path.GetRelativePath(Workflow.WorkflowsDir, _currentTask.OriginTab.FilePath).Replace(Path.DirectorySeparatorChar, '/') : null,
                    IsGridTask = _currentTask.IsGridTask,
                    XValue = _currentTask.XValue,
                    YValue = _currentTask.YValue,
                    GridConfig = _currentTask.GridConfig
                });
            }

            queueToSave.AddRange(PendingQueueItems.Select(vm => new SerializablePromptTask
            {
                JsonPromptForApi = vm.Task.JsonPromptForApi,
                FullWorkflowStateJson = vm.Task.FullWorkflowStateJson,
                OriginTabFilePath = !vm.Task.OriginTab.IsVirtual ? Path.GetRelativePath(Workflow.WorkflowsDir, vm.Task.OriginTab.FilePath).Replace(Path.DirectorySeparatorChar, '/') : null,
                IsGridTask = vm.Task.IsGridTask,
                XValue = vm.Task.XValue,
                YValue = vm.Task.YValue,
                GridConfig = vm.Task.GridConfig
            }));

            var queueFilePath = Path.Combine(Directory.GetCurrentDirectory(), "queue.json");
            if (queueToSave.Any())
            {
                var json = JsonConvert.SerializeObject(queueToSave, Formatting.Indented);
                await File.WriteAllTextAsync(queueFilePath, json);
            }
            else if (File.Exists(queueFilePath))
            {
                File.Delete(queueFilePath); // Clean up if queue is empty
            }
            // --- END OF CHANGE ---

            if (SelectedTab != null && !SelectedTab.IsVirtual && SelectedTab.Workflow.IsLoaded)
            {
                await SelectedTab.WorkflowInputsController.PrepareForSessionSaveAsync();
                string activeInnerTabName = SelectedTab.WorkflowInputsController.SelectedTabLayout?.Header;
                var hookStates = SelectedTab.WorkflowInputsController.GlobalControls.GetHookStates();
                _sessionManager.SaveSession(SelectedTab.Workflow, SelectedTab.FilePath, activeInnerTabName, hookStates);
            }
            
            if (SelectedTab != null)
            {
                _settings.LastSeedControlState = SelectedTab.WorkflowInputsController.SelectedSeedControl;
            }
            
            _settings.IsConsoleVisible = this.IsConsoleVisible;
            _settings.GalleryThumbnailSize = this.ImageProcessing.GalleryThumbnailSize;
            
            // Restore the logic for saving tab order and the active tab here.
            // This is the only safe place to do it.
            _settings.LastOpenWorkflows = OpenTabs
                .Where(t => !t.IsVirtual)
                .Select(t => Path.GetRelativePath(Workflow.WorkflowsDir, t.FilePath).Replace(Path.DirectorySeparatorChar, '/'))
                .ToList();

            _settings.LastActiveWorkflow = (SelectedTab != null && !SelectedTab.IsVirtual)
                ? Path.GetRelativePath(Workflow.WorkflowsDir, SelectedTab.FilePath).Replace(Path.DirectorySeparatorChar, '/') 
                : null;
            
            _settingsService.SaveSettings();
        }
        
        private void DeleteWorkflow(WorkflowTabViewModel tabToDelete)
        {
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
                    _settingsService.SaveSettings();
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
        
        private void ClearSessionForWorkflow(WorkflowTabViewModel tab)
        {
            if (tab == null || !tab.Workflow.IsLoaded) return;
    
            tab.ResetState();
    
            MessageBox.Show(string.Format(LocalizationService.Instance["MainVM_SessionResetMessage"], tab.Header), 
                LocalizationService.Instance["MainVM_SessionResetTitle"], 
                MessageBoxButton.OK, 
                MessageBoxImage.Information);
        }
        
        // --- START OF CHANGE: New method to load queue from file ---
        private void LoadPersistedQueue()
        {
            var queueFilePath = Path.Combine(Directory.GetCurrentDirectory(), "queue.json");
            if (!File.Exists(queueFilePath)) return;

            try
            {
                var json = File.ReadAllText(queueFilePath);
                var loadedTasks = JsonConvert.DeserializeObject<List<SerializablePromptTask>>(json);

                if (loadedTasks == null || !loadedTasks.Any()) return;

                foreach (var st in loadedTasks)
                {
                    // Find the origin tab by its file path
                    var originTab = OpenTabs.FirstOrDefault(t => !t.IsVirtual && Path.GetRelativePath(Workflow.WorkflowsDir, t.FilePath).Replace(Path.DirectorySeparatorChar, '/') == st.OriginTabFilePath);
                    if (originTab != null)
                    {
                        var task = new PromptTask
                        {
                            JsonPromptForApi = st.JsonPromptForApi,
                            FullWorkflowStateJson = st.FullWorkflowStateJson,
                            OriginTab = originTab,
                            IsGridTask = st.IsGridTask,
                            XValue = st.XValue,
                            YValue = st.YValue,
                            GridConfig = st.GridConfig
                        };

                        EnqueueTaskInternal(task, originTab.Workflow.JsonClone());
                    }
                }

                if (PendingQueueItems.Any())
                {
                    Logger.Log($"Restored {PendingQueueItems.Count} tasks from the previous session's queue.");
                }

                // Delete the file after successfully loading to prevent re-execution
                File.Delete(queueFilePath);
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Failed to load persisted queue from queue.json. The file might be corrupt.");
                if (File.Exists(queueFilePath))
                {
                    try { File.Delete(queueFilePath); } catch {}
                }
            }
        }
        // --- END OF CHANGE ---
        
        /// <summary>
        /// Handles the import of a raw ComfyUI API JSON file by opening the UI Constructor.
        /// </summary>
        /// <param name="jsonContent">The string content of the API JSON file.</param>
        /// <param name="sourceFilePath">The path of the file being imported, used to set the default name.</param>
        public void ImportApiWorkflow(string jsonContent, string sourceFilePath)
        {
            try
            {
                var apiJson = JObject.Parse(jsonContent);
                var workflowName = Path.GetFileNameWithoutExtension(sourceFilePath);
                
                // We use the dispatcher to open the window AFTER the current drop event has fully completed.
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // This code will run as a new task on the UI thread,
                    // once the original DragDrop operation has finished.
                    var constructorViewModel = new UIConstructorView(apiJson, workflowName);
                    var constructorWindow = new UIConstructor
                    {
                        DataContext = constructorViewModel,
                        Owner = Application.Current.MainWindow
                    };
                    constructorWindow.ShowDialog();
            
                    // Refresh the main list of workflows in case the user saved the new one.
                    UpdateWorkflows();
                    
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (JsonReaderException)
            {
                MessageBox.Show(
                    LocalizationService.Instance["MainVM_ImportInvalidJsonError"],
                    LocalizationService.Instance["MainVM_ImportErrorTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Failed during raw API workflow import.");
                MessageBox.Show(
                    string.Format(LocalizationService.Instance["MainVM_ImportGenericError"], ex.Message),
                    LocalizationService.Instance["MainVM_ImportErrorTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        
        private void FullScreen_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FullScreenViewModel.IsFullScreenOpen))
            {
                // When fullscreen is toggled, hide or show all undocked windows to prevent them from overlapping.
                bool isFullScreen = FullScreen.IsFullScreenOpen;
                foreach (var window in _undockedWindows.Values)
                {
                    if (isFullScreen)
                    {
                        window.Hide();
                    }
                    else
                    {
                        window.Show();
                    }
                }
            }
        }
    }
}
