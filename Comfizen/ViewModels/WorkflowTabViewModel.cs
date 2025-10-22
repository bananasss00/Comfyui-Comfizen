using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using System.ComponentModel;
using System.IO;
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
        public string? FilePath { get; }
        
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

            // Instead of loading from file, we just load the UI controls.
            // No need for InitializeAsync().
            Task.Run(async () => {
                await WorkflowInputsController.LoadInputs();
                ExecuteHook("on_workflow_load", Workflow.LoadedApi);
            });
        }
        
        private void InitializeController()
        {
            WorkflowInputsController = new WorkflowInputsController(Workflow, _settings, _modelService, this)
            {
                SelectedSeedControl = _settings.LastSeedControlState
            };
        }
        
        private async void InitializeAsync()
        {
            try
            {
                // 1. Загружаем базовую структуру воркфлоу из файла
                Workflow.LoadWorkflow(FilePath);
                
                // 2. Пытаемся загрузить и применить данные сессии поверх базовой структуры
                var sessionData = _sessionManager.LoadSession(FilePath);
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
                    if (sessionData.BlockedNodeIds != null)
                    {
                        Workflow.BlockedNodeIds = sessionData.BlockedNodeIds;
                    }
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
                await WorkflowInputsController.LoadInputs();
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
            
            await WorkflowInputsController.LoadInputs();
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