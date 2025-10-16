using System;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

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
        public string FilePath { get; }

        public Workflow Workflow { get; private set; }
        
        public WorkflowInputsController WorkflowInputsController { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

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
            
            InitializeAsync();
        }
        
        private void InitializeController()
        {
            WorkflowInputsController = new WorkflowInputsController(Workflow, _settings, _modelService)
            {
                SelectedSeedControl = _settings.LastSeedControlState
            };
        }
        
        private async void InitializeAsync()
        {
            try
            {
                Workflow.LoadWorkflow(FilePath);
                var sessionJObject = _sessionManager.LoadSession(FilePath);
                if (sessionJObject != null)
                {
                    Workflow.LoadedApi = sessionJObject;
                }
                await WorkflowInputsController.LoadInputs();
            }
            catch (Exception ex)
            {
                var message = string.Format(LocalizationService.Instance["ModelService_ErrorFetchModelTypes"], ex.Message);
                var title = LocalizationService.Instance["General_Error"];
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        public void ResetState()
        {
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
            
            Workflow.LoadWorkflow(this.FilePath);

            if (saveType == WorkflowSaveType.LayoutOnly && currentWidgetState != null)
            {
                Utils.MergeJsonObjects(Workflow.LoadedApi, currentWidgetState);
            }
            else
            {
                // Also reload session after a full API replacement to get the latest values
                var sessionJObject = _sessionManager.LoadSession(FilePath);
                if (sessionJObject != null)
                {
                    Workflow.LoadedApi = sessionJObject;
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