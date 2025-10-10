using Newtonsoft.Json.Linq;
using PropertyChanged;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

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
        public ImageProcessingViewModel ImageProcessing { get; private set; }
        public FullScreenViewModel FullScreen { get; private set; }
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
            ImageProcessing = new ImageProcessingViewModel(comfyModel, settings);
            FullScreen = new FullScreenViewModel(comfyModel, settings, ImageProcessing.FilteredImageOutputs);
            
            // Инициализация контроллера
            InitializeController();
            
            // Асинхронная загрузка
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
            Workflow.LoadWorkflow(FilePath);
            var sessionJObject = _sessionManager.LoadSession(FilePath);
            if (sessionJObject != null && Workflow.LoadedApi != null)
            {
                Utils.MergeJsonObjects(Workflow.LoadedApi, sessionJObject);
            }
            await WorkflowInputsController.LoadInputs();
        }
        
        public void ResetState()
        {
            _sessionManager.ClearSession(this.FilePath);
            InitializeAsync(); // Просто перезагружаем с нуля
        }
        
        /// <summary>
        /// "Умно" перезагружает вкладку после сохранения воркфлоу в дизайнере.
        /// </summary>
        public async Task Reload(WorkflowSaveType saveType)
        {
            // 1. Сохраняем текущее состояние виджетов (если нужно)
            JObject? currentWidgetState = null;
            if (saveType == WorkflowSaveType.LayoutOnly && Workflow.LoadedApi != null)
            {
                currentWidgetState = Workflow.LoadedApi.DeepClone() as JObject;
            }

            // 2. Пересоздаем контроллер, чтобы очистить его внутреннее состояние
            InitializeController();
            
            // 3. Перезагружаем воркфлоу с диска (получаем новую разметку и, возможно, новый API)
            Workflow.LoadWorkflow(this.FilePath);

            // 4. Если нужно, восстанавливаем сохраненное состояние виджетов
            if (saveType == WorkflowSaveType.LayoutOnly && currentWidgetState != null)
            {
                // Применяем старые значения к новому (или тому же) API
                Utils.MergeJsonObjects(Workflow.LoadedApi, currentWidgetState);
            }
            else // ApiReplaced
            {
                // Сессия уже была очищена в UIConstructorView, так что здесь делать ничего не нужно.
                // LoadWorkflow уже загрузил новый API по умолчанию.
            }
            
            // 5. Перестраиваем UI на основе обновленных данных
            await WorkflowInputsController.LoadInputs();
        }

        // --- НАЧАЛО ИЗМЕНЕНИЯ ---
        public void UpdateAfterSettingsChange(AppSettings newSettings, ComfyuiModel newComfyModel, ModelService newModelService, SessionManager newSessionManager)
        {
            _settings = newSettings;
            _sessionManager = newSessionManager;
            _comfyModel = newComfyModel;
            _modelService = newModelService;

            // Просто вызываем "умную" перезагрузку с сохранением состояния
            Reload(WorkflowSaveType.LayoutOnly);
            
            // Обновляем сервисы, которые не зависят от контроллера напрямую
            this.ImageProcessing.Settings = newSettings;
            this.FullScreen = new FullScreenViewModel(_comfyModel, newSettings, this.ImageProcessing.FilteredImageOutputs);
        }
        // --- КОНЕЦ ИЗМЕНЕНИЯ ---
    }
}