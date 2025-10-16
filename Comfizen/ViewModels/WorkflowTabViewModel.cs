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
        
        // --- START OF CHANGES: ImageProcessing and FullScreen removed ---
        // public ImageProcessingViewModel ImageProcessing { get; private set; }
        // public FullScreenViewModel FullScreen { get; private set; }
        // --- END OF CHANGES ---
        
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
            
            // --- START OF CHANGES: Instantiation of local VMs removed ---
            // ImageProcessing = new ImageProcessingViewModel(comfyModel, settings);
            // FullScreen = new FullScreenViewModel(comfyModel, settings, ImageProcessing.FilteredImageOutputs);
            // --- END OF CHANGES ---
            
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
                if (sessionJObject != null && Workflow.LoadedApi != null)
                {
                    Utils.MergeJsonObjects(Workflow.LoadedApi, sessionJObject);
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
            
            await WorkflowInputsController.LoadInputs();
        }

        public void UpdateAfterSettingsChange(AppSettings newSettings, ComfyuiModel newComfyModel, ModelService newModelService, SessionManager newSessionManager)
        {
            _settings = newSettings;
            _sessionManager = newSessionManager;
            _comfyModel = newComfyModel;
            _modelService = newModelService;
            
            // Reload the tab state, keeping widget values
            Reload(WorkflowSaveType.LayoutOnly);
            
            // --- START OF CHANGES: No longer need to update local VMs ---
            // this.ImageProcessing.Settings = newSettings;
            // this.FullScreen = new FullScreenViewModel(_comfyModel, newSettings, this.ImageProcessing.FilteredImageOutputs);
            // --- END OF CHANGES ---
        }
    }
}
