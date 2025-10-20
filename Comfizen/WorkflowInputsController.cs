using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Windows.Controls.Primitives;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using System.Drawing;
using System.Drawing.Imaging;

namespace Comfizen;

[AddINotifyPropertyChangedInterface]
public class WorkflowInputsController : INotifyPropertyChanged
{
    private readonly WorkflowTabViewModel _parentTab; // Link to the parent
    private readonly AppSettings _settings;
    private readonly Workflow _workflow;
    private readonly ModelService _modelService;
    private bool _hasWildcardFields;
    private readonly List<InpaintFieldViewModel> _inpaintViewModels = new();
    
    public GlobalSettingsViewModel GlobalSettings { get; private set; }
    
    private readonly List<SeedFieldViewModel> _seedViewModels = new();

    private readonly List<string> _wildcardPropertyPaths = new();
    
    public ICommand ExecuteActionCommand { get; }

    public WorkflowInputsController(Workflow workflow, AppSettings settings, ModelService modelService, WorkflowTabViewModel parentTab)
    {
        _workflow = workflow;
        _settings = settings;
        _modelService = modelService;
        _parentTab = parentTab;
        
        ExecuteActionCommand = new RelayCommand(actionName =>
        {
            if (actionName is string name)
            {
                _parentTab.ExecuteAction(name);
            }
        });
        
        GlobalSettings = new GlobalSettingsViewModel();
    }
    
    public ObservableCollection<WorkflowGroupViewModel> InputGroups { get; set; } = new();

    public SeedControl SelectedSeedControl { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    
    public string CreatePromptTask()
    {
        var prompt = _workflow.JsonClone();
        ProcessSpecialFields(prompt);
        return prompt.ToString();
    }
    
    public void ProcessSpecialFields(JToken prompt)
    {
        ApplyWildcards(prompt);
        ApplyInpaintData(prompt);
        ApplySeedControl(prompt);
    }

    private void ApplyWildcards(JToken prompt)
    {
        if (!_hasWildcardFields) return;

        foreach (var wildcardProperty in _wildcardPropertyPaths)
        {
            var prop = Utils.GetJsonPropertyByPath((JObject)prompt, wildcardProperty);
            if (prop != null && prop.Value.Type == JTokenType.String)
            {
                var text = prop.Value.ToObject<string>();
                // Используем значение из ViewModel
                prop.Value = new JValue(Utils.ReplaceWildcards(text, GlobalSettings.WildcardSeed));
            }
        }
    }

    private void ApplyInpaintData(JToken prompt)
    {
        foreach (var vm in _inpaintViewModels)
        {
            // Если есть поле для изображения, обрабатываем его
            if (vm.ImageField != null)
            {
                var prop = Utils.GetJsonPropertyByPath((JObject)prompt, vm.ImageField.Path);
                if (prop != null)
                {
                    var base64Image = vm.Editor.GetImageAsBase64();
                    if (base64Image != null) prop.Value = new JValue(base64Image);
                }
            }

            // Если есть поле для маски, обрабатываем его
            if (vm.MaskField != null)
            {
                var prop = Utils.GetJsonPropertyByPath((JObject)prompt, vm.MaskField.Path);
                if (prop != null)
                {
                    var base64Mask = vm.Editor.GetMaskAsBase64();
                    if (base64Mask != null) prop.Value = new JValue(base64Mask);
                }
            }
        }
    }

    private void ApplySeedControl(JToken prompt)
    {
        if (SelectedSeedControl == SeedControl.Fixed) return;

        foreach (var seedVm in _seedViewModels)
        {
            if (seedVm.IsLocked) continue;

            // ИСПРАВЛЕНИЕ: Используем публичное свойство Property
            var prop = Utils.GetJsonPropertyByPath((JObject)prompt, seedVm.Property.Path);
            if (prop != null && long.TryParse(prop.Value.ToString(), out var currentValue))
            {
                var newValue = currentValue;
                switch (SelectedSeedControl)
                {
                    case SeedControl.Increment: newValue++; break;
                    case SeedControl.Decrement: newValue--; break;
                    case SeedControl.Randomize: newValue = Utils.GenerateSeed(); break;
                }
                
                prop.Value = new JValue(newValue);
                
                seedVm.Value = newValue.ToString();
            }
        }

        if (_hasWildcardFields && !GlobalSettings.IsSeedLocked)
        {
            var newSeed = GlobalSettings.WildcardSeed;
            switch (SelectedSeedControl)
            {
                case SeedControl.Increment: newSeed++; break;
                case SeedControl.Decrement: newSeed--; break;
                case SeedControl.Randomize: newSeed = Utils.GenerateSeed(); break;
            }
            // Обновляем UI через свойство ViewModel
            GlobalSettings.WildcardSeed = newSeed;
        }
    }

    public async Task LoadInputs()
    {
        CleanupInputs();

        _hasWildcardFields = _workflow.Groups.SelectMany(g => g.Fields)
            .Any(f => f.Type == FieldType.WildcardSupportPrompt);
        
        GlobalSettings.IsVisible = _hasWildcardFields;

        var comboBoxLoadTasks = new List<Task>();

        foreach (var group in _workflow.Groups)
        {
            var groupVm = new WorkflowGroupViewModel(group);
            var processedFields = new HashSet<WorkflowField>(); // Отслеживаем уже обработанные поля

            for (int i = 0; i < group.Fields.Count; i++)
            {
                var field = group.Fields[i];
                if (processedFields.Contains(field))
                {
                    continue; // Пропускаем, если это поле уже было обработано как часть пары
                }

                InputFieldViewModel fieldVm = null;
                var property = _workflow.GetPropertyByPath(field.Path);
                
                // ========================================================== //
                //     НАЧАЛО ИЗМЕНЕНИЯ: Логика "умного" группирования      //
                // ========================================================== //

                if (property != null)
                {
                    // Сценарий 1: Нашли ImageInput
                    if (field.Type == FieldType.ImageInput)
                    {
                        WorkflowField pairedMaskField = null;
                        if (i + 1 < group.Fields.Count && group.Fields[i + 1].Type == FieldType.MaskInput)
                        {
                            pairedMaskField = group.Fields[i + 1];
                        }

                        fieldVm = new InpaintFieldViewModel(field, pairedMaskField, property);
                        _inpaintViewModels.Add((InpaintFieldViewModel)fieldVm);
                        processedFields.Add(field);
                        if (pairedMaskField != null) processedFields.Add(pairedMaskField);
                    }
                    // Сценарий 2: Нашли MaskInput, которое не было частью пары
                    else if (field.Type == FieldType.MaskInput)
                    {
                        // Создаем одиночный редактор только для маски
                        fieldVm = new InpaintFieldViewModel(field, null, property);
                        _inpaintViewModels.Add((InpaintFieldViewModel)fieldVm);
                        processedFields.Add(field);
                    }
                    // Сценарий 3: Любое другое поле
                    else
                    {
                        fieldVm = CreateDefaultFieldViewModel(field, property);
                        processedFields.Add(field);
                    }
                }
                else
                {
                    if (field.Type == FieldType.Markdown)
                    {
                        fieldVm = new MarkdownFieldViewModel(field);
                        processedFields.Add(field);
                    }
                    else if (field.Type == FieldType.ScriptButton)
                    {
                        fieldVm = new ScriptButtonFieldViewModel(field, this.ExecuteActionCommand);
                        processedFields.Add(field);
                    }
                }

                // ========================================================== //
                //     КОНЕЦ ИЗМЕНЕНИЯ                                        //
                // ========================================================== //

                if (fieldVm != null)
                {
                    groupVm.Fields.Add(fieldVm);

                    if (fieldVm is ComboBoxFieldViewModel comboBoxVm)
                    {
                        comboBoxLoadTasks.Add(comboBoxVm.LoadItemsAsync(_modelService, _settings));
                    }
                }
            }
            
            if (groupVm.Fields.Any())
            {
                InputGroups.Add(groupVm);
            }
        }
        await Task.WhenAll(comboBoxLoadTasks);
    }
    
    private InputFieldViewModel CreateDefaultFieldViewModel(WorkflowField field, JProperty? prop)
    {
        switch (field.Type)
        {
            case FieldType.Markdown:
                return new MarkdownFieldViewModel(field);
                
            case FieldType.Seed:
                var seedVm = new SeedFieldViewModel(field, prop);
                _seedViewModels.Add(seedVm);
                return seedVm;
                
            case FieldType.Model:
            case FieldType.Sampler:
            case FieldType.Scheduler:
            case FieldType.ComboBox:
                return new ComboBoxFieldViewModel(field, prop);
                 
            case FieldType.SliderInt:
            case FieldType.SliderFloat:
                return new SliderFieldViewModel(field, prop);

            case FieldType.WildcardSupportPrompt:
                 _wildcardPropertyPaths.Add(prop.Path);
                 return new TextFieldViewModel(field, prop);
            
            case FieldType.Any: 
                 if (prop.Value.Type == JTokenType.Boolean)
                 {
                     return new CheckBoxFieldViewModel(field, prop);
                 }
                 return new TextFieldViewModel(field, prop);
            
            case FieldType.ScriptButton:
                // We pass the ExecuteActionCommand from the controller itself
                return new ScriptButtonFieldViewModel(field, this.ExecuteActionCommand);
            
            default:
                return new TextFieldViewModel(field, prop);
        }
    }

    public void HandlePasteOperation()
    {
        // Ищем редактор под мышкой в новом списке
        var targetEditor = _inpaintViewModels.Select(vm => vm.Editor)
            .FirstOrDefault(editor => editor.IsMouseOver && editor.CanAcceptImage);
        
        if (targetEditor == null)
        {
            // Если не нашли, берем первый доступный
            targetEditor = _inpaintViewModels.Select(vm => vm.Editor)
                .FirstOrDefault(e => e.CanAcceptImage);
        }
        
        if (targetEditor == null) return;
        
        var imageBytes = GetImageBytesFromClipboard();
        if (imageBytes != null)
        {
            targetEditor.SetSourceImage(imageBytes);
        }
    }

    /// <summary>
    /// Универсальный метод для извлечения изображения из буфера обмена.
    /// Обрабатывает форматы Image, DIB и FileDrop.
    /// </summary>
    /// <returns>Массив байт в формате PNG или null, если изображение не найдено.</returns>
    private byte[] GetImageBytesFromClipboard()
    {
        // 1. Сначала пытаемся получить изображение через System.Drawing (наиболее совместимый способ)
        try
        {
            if (System.Windows.Forms.Clipboard.ContainsImage())
            {
                // Используем System.Drawing.Image, т.к. он отлично парсит DIB и другие форматы
                using (var drawingImage = System.Windows.Forms.Clipboard.GetImage())
                {
                    if (drawingImage != null)
                    {
                        using (var ms = new MemoryStream())
                        {
                            // Сохраняем в MemoryStream в формате PNG (он сохраняет прозрачность)
                            drawingImage.Save(ms, ImageFormat.Png);
                            return ms.ToArray();
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // Игнорируем ошибки от System.Drawing и переходим к запасному варианту
        }

        // 2. Запасной вариант: проверяем на наличие скопированных файлов (стандартный WPF-способ)
        if (Clipboard.ContainsFileDropList())
        {
            var filePaths = Clipboard.GetFileDropList();
            if (filePaths != null && filePaths.Count > 0)
            {
                var filePath = filePaths[0];
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                if (new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }.Contains(extension))
                {
                    try { return File.ReadAllBytes(filePath); } catch { /* ignore */ }
                }
            }
        }

        // Если ничего не сработало, возвращаем null
        return null;
    }

    private void CleanupInputs()
    {
        _seedViewModels.Clear();
        _wildcardPropertyPaths.Clear();
        // _inpaintEditors.Clear(); // Заменено
        _inpaintViewModels.Clear();
        InputGroups.Clear();
        _hasWildcardFields = false;
        if (GlobalSettings != null)
        {
            GlobalSettings.IsVisible = false;
        }
    }
}