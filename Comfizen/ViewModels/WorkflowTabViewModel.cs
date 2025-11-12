using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json;

namespace Comfizen
{
    [AddINotifyPropertyChangedInterface]
    public class WorkflowTabViewModel : INotifyPropertyChanged
    {
        private SessionManager _sessionManager;
        private ComfyuiModel _comfyModel;
        private AppSettings _settings;
        private ModelService _modelService;

        public string Header { get; set; }
        public string? FilePath { get; set; }
        
        [JsonIgnore]
        public bool IsRenaming { get; set; }
        
        public string EditableHeader
        {
            get => RelativePathTooltip;
            set
            {
                // The setter logic is handled by the RenameWorkflow command,
                // this property is primarily for binding to the TextBox.
            }
        }
        
        public string RelativePathTooltip
        {
            get
            {
                if (IsVirtual)
                {
                    // For virtual tabs (from imported images), just show the header.
                    return Header;
                }
                try
                {
                    var relativePath = Path.GetRelativePath(Workflow.WorkflowsDir, FilePath);
                    var pathWithoutExtension = Path.ChangeExtension(relativePath, null);
                    // Ensure consistent path separators
                    return pathWithoutExtension.Replace(Path.DirectorySeparatorChar, '/');
                }
                catch
                {
                    // Fallback in case of any path errors
                    return Header;
                }
            }
        }

        public Workflow Workflow { get; private set; }
        
        public WorkflowInputsController WorkflowInputsController { get; set; }
        
        [JsonIgnore]
        public bool IsVirtual => string.IsNullOrEmpty(FilePath);

        public event PropertyChangedEventHandler PropertyChanged;
        
        private readonly Dictionary<string, object> _scriptState = new Dictionary<string, object>();
        public ICommand ExecuteActionCommand { get; }

        public WorkflowTabViewModel(string filePath, ComfyuiModel comfyModel, AppSettings settings, ModelService modelService, SessionManager sessionManager)
        {
            FilePath = filePath;
            Header = Path.GetFileNameWithoutExtension(filePath.Replace(Path.DirectorySeparatorChar, '/'));
            
            _comfyModel = comfyModel;
            _settings = settings;
            _modelService = modelService;
            _sessionManager = sessionManager;

            Workflow = new Workflow();
            
            InitializeController();
            
            ExecuteActionCommand = new RelayCommand(actionName => ExecuteAction(actionName as string));
            
            InitializeAsync();
        }
        
        // New constructor for creating "virtual" tabs from an in-memory workflow.
        public WorkflowTabViewModel(Workflow preloadedWorkflow, string header, ComfyuiModel comfyModel, AppSettings settings, ModelService modelService, SessionManager sessionManager)
        {
            FilePath = null; // This indicates a virtual tab.
            Header = header;

            _comfyModel = comfyModel;
            _settings = settings;
            _modelService = modelService;
            _sessionManager = sessionManager;

            Workflow = preloadedWorkflow; // Use the provided workflow object directly.

            InitializeController();
            
            ExecuteActionCommand = new RelayCommand(actionName => ExecuteAction(actionName as string));

            // START OF CHANGE: Use an async void method for initialization
            // This mirrors the behavior of the file-based constructor and avoids Task.Run,
            // which can cause threading issues with UI updates.
            InitializeFromPreloadedAsync();
            // END OF CHANGE
        }
        
        // START OF CHANGE: New method for initializing virtual tabs
        /// <summary>
        /// Asynchronously initializes the UI for a pre-loaded ("virtual") workflow.
        /// </summary>
        private async void InitializeFromPreloadedAsync()
        {
            // For virtual tabs, just populate the hooks. There's no session state to apply.
            WorkflowInputsController.PopulateHooks(Workflow.Scripts);
            await WorkflowInputsController.LoadInputs();
            foreach (var group in WorkflowInputsController.TabLayoouts.SelectMany(t => t.Groups))
            {
                group.LoadPresets();
            }
            ExecuteHook("on_workflow_load", Workflow.LoadedApi);
        }
        // END OF CHANGE

        private void InitializeController()
        {
            WorkflowInputsController = new WorkflowInputsController(Workflow, _settings, _modelService, this)
            {
                SelectedSeedControl = _settings.LastSeedControlState
            };
            
            WorkflowInputsController.PresetsModifiedInGroup += OnPresetsModified;
        }
        
        /// <summary>
        /// Handles the event when presets are changed and triggers an auto-save of the workflow file.
        /// </summary>
        private void OnPresetsModified()
        {
            // When presets are modified (e.g., from the UI Constructor),
            // tell the controller to re-scan for global presets.
            WorkflowInputsController.DiscoverGlobalPresets();

            if (IsVirtual)
            {
                return;
            }

            try
            {
                var relativePath = Path.GetRelativePath(Workflow.WorkflowsDir, FilePath);
                var relativePathWithoutExtension = Path.ChangeExtension(relativePath, null);
            
                // This method saves the *current* state of the workflow, including the new presets.
                Workflow.SaveWorkflow(relativePathWithoutExtension.Replace(Path.DirectorySeparatorChar, '/'));
            
                Logger.Log($"Presets auto-saved to workflow file: '{Header}'");
            }
            catch (Exception ex)
            {
                Logger.Log(ex, $"Failed to auto-save workflow after preset change for '{Header}'.");
            }
        }
        
        private async void InitializeAsync()
        {
            try
            {
                Workflow.LoadWorkflow(FilePath);
                
                var sessionData = _sessionManager.LoadSession(FilePath);
                if (sessionData != null)
                {
                    if (sessionData.ApiState != null) Workflow.LoadedApi = sessionData.ApiState;
                    if (sessionData.GroupsState != null) Workflow.Groups = sessionData.GroupsState;
                    if (sessionData.BlockedNodeIds != null) Workflow.BlockedNodeIds = sessionData.BlockedNodeIds;
                }
                
                // Populate hooks based on the loaded workflow scripts.
                WorkflowInputsController.PopulateHooks(Workflow.Scripts);
                // Apply the saved enabled/disabled states from the session.
                if (sessionData?.HookStates != null)
                {
                    WorkflowInputsController.GlobalControls.ApplyHookStates(sessionData.HookStates);
                }

                // --- НАЧАЛО ИЗМЕНЕНИЯ: Добавлена миграция после загрузки сессии ---
                // 3. Выполняем миграцию. Этот код теперь сработает как на данных из файла,
                //    так и на перезаписанных данных из сессии.
                if (Workflow.LoadedApi != null)
                {
                    foreach (var group in Workflow.Groups)
                    {
                        foreach (var field in group.Fields)
                        {
                            if (field.Type == FieldType.Markdown && string.IsNullOrEmpty(field.DefaultValue))
                            {
                                var prop = Utils.GetJsonPropertyByPath(Workflow.LoadedApi, field.Path);
                                if (prop != null && prop.Value.Type == JTokenType.String)
                                {
                                    field.DefaultValue = prop.Value.ToString();
                                    prop.Value = ""; // Очищаем старое место
                                }
                            }
                        }
                    }
                }
                // --- КОНЕЦ ИЗМЕНЕНИЯ ---

                // 4. Загружаем контролы в UI
                await WorkflowInputsController.LoadInputs(sessionData?.LastActiveTabName);
                foreach (var group in WorkflowInputsController.TabLayoouts.SelectMany(t => t.Groups))
                {
                    group.LoadPresets();
                }
                ExecuteHook("on_workflow_load", Workflow.LoadedApi);
            }
            catch (Exception ex)
            {
                var message = string.Format(LocalizationService.Instance["ModelService_ErrorFetchModelTypes"], ex.Message);
                var title = LocalizationService.Instance["General_Error"];
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        public void ExecuteHook(string hookName, JObject? prompt = null, ImageOutput? output = null)
        {
            // Check if the hook is enabled in the UI before executing.
            var hookToggle = WorkflowInputsController.GlobalControls.ImplementedHooks.FirstOrDefault(h => h.HookName == hookName);
            if (hookToggle != null && !hookToggle.IsEnabled)
            {
                return; // Hook is disabled, do nothing.
            }

            if (Workflow.Scripts.Hooks.TryGetValue(hookName, out var script))
            {
                var contextPrompt = prompt ?? Workflow.LoadedApi;
                ExecuteScript(script, contextPrompt, output);
            }
        }

        public void ExecuteAction(string actionName)
        {
            if (Workflow.Scripts.Actions.TryGetValue(actionName, out var script))
            {
                ExecuteScript(script, Workflow.LoadedApi);
            }
        }
        
        public void QueuePromptFromScript(JObject prompt)
        {
            if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm)
            {
                mainVm.QueuePromptFromJObject(prompt, this);
            }
        }

        private void ExecuteScript(string script, JObject? prompt, ImageOutput? output = null)
        {
            if (prompt == null)
            {
                return; 
            }

            var context = new ScriptContext(prompt, _scriptState, _settings, QueuePromptFromScript, output);
            PythonScriptingService.Instance.Execute(script, context);
        }
        
        public void ResetState()
        {
            if (IsVirtual) return;
            
            _sessionManager.ClearSession(this.FilePath);
            InitializeAsync();
        }
        
        public async Task Reload(WorkflowSaveType saveType)
        {
            JObject? currentWidgetState = null;
            if (saveType == WorkflowSaveType.LayoutOnly && Workflow.LoadedApi != null)
            {
                currentWidgetState = Workflow.LoadedApi.DeepClone() as JObject;
            }

            InitializeController();

            if (!IsVirtual)
            {
                Workflow.LoadWorkflow(this.FilePath);
            }

            if (saveType == WorkflowSaveType.LayoutOnly && currentWidgetState != null)
            {
                Utils.MergeJsonObjects(Workflow.LoadedApi, currentWidgetState);
            }
            else
            {
                if (!IsVirtual)
                {
                    // Also reload session after a full API replacement to get the latest values
                    var sessionJObject = _sessionManager.LoadSession(FilePath);
                    if (sessionJObject != null)
                    {
                        Workflow.LoadedApi = sessionJObject.ApiState;
                        if (sessionJObject.GroupsState != null)
                        {
                            Workflow.Groups = sessionJObject.GroupsState;
                        }
                    }
                }
            }
            
            // START OF CHANGE: Also update Reload to pass the last active tab from the reloaded session
            var sessionData = _sessionManager.LoadSession(this.FilePath);
            
            // Re-populate hooks and apply their saved state after reloading the workflow file.
            WorkflowInputsController.PopulateHooks(Workflow.Scripts);
            if (sessionData?.HookStates != null)
            {
                WorkflowInputsController.GlobalControls.ApplyHookStates(sessionData.HookStates);
            }
            // END OF CHANGE

            if (saveType == WorkflowSaveType.LayoutOnly && currentWidgetState != null)
            {
                Utils.MergeJsonObjects(Workflow.LoadedApi, currentWidgetState);
            }
            else
            {
                if (!IsVirtual)
                {
                    // sessionJObject is renamed to sessionData
                    if (sessionData != null)
                    {
                        if (sessionData.ApiState != null)
                        {
                            Workflow.LoadedApi = sessionData.ApiState;
                        }
                        if (sessionData.GroupsState != null)
                        {
                            Workflow.Groups = sessionData.GroupsState;
                        }
                    }
                }
            }
            
            // Pass the loaded tab name here as well
            await WorkflowInputsController.LoadInputs(sessionData?.LastActiveTabName);
        }

        public void UpdateAfterSettingsChange(AppSettings newSettings, ComfyuiModel newComfyModel, ModelService newModelService, SessionManager newSessionManager)
        {
            _settings = newSettings;
            _sessionManager = newSessionManager;
            _comfyModel = newComfyModel;
            _modelService = newModelService;
            
            Reload(WorkflowSaveType.LayoutOnly);
        }
    }
}