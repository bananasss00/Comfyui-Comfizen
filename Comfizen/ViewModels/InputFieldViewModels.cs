using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Newtonsoft.Json.Linq;
using PropertyChanged;

namespace Comfizen
{
    public class InpaintFieldViewModel : InputFieldViewModel
    {
        public InpaintEditor Editor { get; }
        
        // Поля для хранения ссылок на оригинальные WorkflowField
        public WorkflowField ImageField { get; }
        public WorkflowField MaskField { get; }

        /// <summary>
        /// Конструктор для объединенного редактора или одиночного поля.
        /// </summary>
        /// <param name="primaryField">Основное поле (ImageInput или MaskInput).</param>
        /// <param name="pairedMaskField">Парное поле MaskInput (если есть).</param>
        /// <param name="property">JProperty от основного поля.</param>
        public InpaintFieldViewModel(WorkflowField primaryField, WorkflowField pairedMaskField, JProperty property) : base(primaryField, property)
        {
            // Определяем, какое поле за что отвечает
            if (primaryField.Type == FieldType.ImageInput)
            {
                ImageField = primaryField;
                MaskField = pairedMaskField; // Может быть null
            }
            else // primaryField.Type == FieldType.MaskInput
            {
                ImageField = null;
                MaskField = primaryField;
            }
            
            // Настраиваем InpaintEditor на основе наличия полей
            Editor = new InpaintEditor(
                imageEditingEnabled: ImageField != null,
                maskEditingEnabled: MaskField != null
            );
        }
    }
    
    [AddINotifyPropertyChangedInterface]
    public class GlobalSettingsViewModel : INotifyPropertyChanged
    {
        public string Header { get; set; } = LocalizationService.Instance["GlobalSettings_Header"];
        public bool IsExpanded { get; set; } = true;
        public bool IsVisible { get; set; } = false;

        public long WildcardSeed { get; set; } = Utils.GenerateSeed();
        public bool IsSeedLocked { get; set; } = false;
        
        public event PropertyChangedEventHandler PropertyChanged;
    }
    
    // --- ViewModel для группы ---
    [AddINotifyPropertyChangedInterface]
    public class WorkflowGroupViewModel : INotifyPropertyChanged
    {
        private readonly WorkflowGroup _model;
        public string Name { get; set; }
        public bool IsExpanded
        {
            get => _model.IsExpanded;
            set
            {
                if (_model.IsExpanded != value)
                {
                    _model.IsExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }
        public ObservableCollection<InputFieldViewModel> Fields { get; set; } = new();
        
        /// <summary>
        /// The highlight color for the group in HEX format.
        /// </summary>
        public string HighlightColor { get; set; }
        
        // Свойство для InpaintEditor
        public InpaintEditor InpaintEditorControl { get; set; }
        public bool HasInpaintEditor => InpaintEditorControl != null;
        
        public event PropertyChangedEventHandler PropertyChanged;
        public WorkflowGroupViewModel(WorkflowGroup model)
        {
            _model = model;
            Name = model.Name;
            HighlightColor = model.HighlightColor;
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    // --- Базовый класс для всех полей ввода ---
    public abstract class InputFieldViewModel : INotifyPropertyChanged
    {
        public string Name { get; }
        public string Path { get; } // START OF CHANGES: Add Path property
        public FieldType Type { get; protected set; }
        public JProperty Property { get; } 
        
        /// <summary>
        /// The highlight color for the field in HEX format.
        /// </summary>
        public string HighlightColor { get; set; }

        protected InputFieldViewModel(WorkflowField field, JProperty property)
        {
            Name = field.Name;
            Path = field.Path; // END OF CHANGES: Initialize Path property
            Property = property;
            HighlightColor = field.HighlightColor;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    // --- Конкретные реализации ---

    public class TextFieldViewModel : InputFieldViewModel
    {
        public string Value
        {
            get
            {
                var propValue = Property.Value;
                string text;

                switch (propValue.Type)
                {
                    case JTokenType.Float:
                        text = propValue.ToObject<double>().ToString("G", CultureInfo.InvariantCulture);
                        break;
                    case JTokenType.Integer:
                        text = propValue.ToObject<long>().ToString(CultureInfo.InvariantCulture);
                        break;
                    default:
                        text = propValue.ToString();
                        break;
                }

                if (text.Length > 1000 && (text.StartsWith("iVBOR") || text.StartsWith("/9j/") || text.StartsWith("UklG")))
                {
                    return string.Format(LocalizationService.Instance["TextField_Base64Placeholder"], text.Length / 1024);
                }
                return text;
            }
            set
            {
                if (value.StartsWith("[Base64 Image Data:") || value.StartsWith("[Данные изображения Base64:"))
                    return;

                // --- START OF FIX: Allow typing of decimal separators ---
                // Get the invariant decimal separator, which is '.'
                string decimalSeparator = CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator;
                
                // If the user is typing a number and just typed a decimal point
                // (e.g., "1."), we need to keep it as a string temporarily.
                // Otherwise, TryParse would convert "1." to the integer 1, and the dot would disappear.
                if (value.EndsWith(decimalSeparator))
                {
                    // Check if the part before the dot is a valid number.
                    string valueWithoutSeparator = value.Substring(0, value.Length - 1);
                    if (double.TryParse(valueWithoutSeparator, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                    {
                        // It's a valid partial number. Store as string and wait for more input.
                        Property.Value = new JValue(value);
                        return; // Exit early
                    }
                }
                // --- END OF FIX ---

                // When setting the value, try to parse it as a number using invariant culture.
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double numericValue))
                {
                    // If the parsed value is a whole number, store it as an integer to keep the JSON clean.
                    if (numericValue == Math.Truncate(numericValue))
                    {
                        Property.Value = new JValue(Convert.ToInt64(numericValue));
                    }
                    else
                    {
                        Property.Value = new JValue(numericValue);
                    }
                }
                else
                {
                    // If it cannot be parsed as a number, treat it as a string.
                    Property.Value = new JValue(value);
                }
            }
        }
        
        /// <summary>
        /// Public method to update the value from image bytes (for Drag&Drop and Paste).
        /// </summary>
        public void UpdateWithImageData(byte[] imageBytes)
        {
            if (imageBytes == null) return;
            var base64String = Convert.ToBase64String(imageBytes);
            Property.Value = new JValue(base64String);
            OnPropertyChanged(nameof(Value));
        }

        public TextFieldViewModel(WorkflowField field, JProperty property) : base(field, property)
        {
            Type = field.Type;
        }
    }

    public class MarkdownFieldViewModel : InputFieldViewModel
    {
        public string Value
        {
            get => Property.Value.ToString();
            set
            {
                if (Property.Value.ToString() != value)
                {
                    Property.Value = new JValue(value);
                    OnPropertyChanged(nameof(Value));
                }
            }
        }
    
        public MarkdownFieldViewModel(WorkflowField field, JProperty property) : base(field, property)
        {
            Type = FieldType.Markdown;
        }
    }

    public class SeedFieldViewModel : InputFieldViewModel
    {
        private readonly WorkflowField _field;
        public string Value
        {
            get => Property.Value.ToObject<long>().ToString(CultureInfo.InvariantCulture);
            set
            {
                if (long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var longValue) && Property.Value.ToObject<long>() != longValue)
                {
                    Property.Value = new JValue(longValue);
                    OnPropertyChanged(nameof(Value));
                }
            }
        }
        public bool IsLocked
        {
            get => _field.IsSeedLocked;
            set
            {
                if (_field.IsSeedLocked != value)
                {
                    _field.IsSeedLocked = value;
                    OnPropertyChanged(nameof(IsLocked));
                }
            }
        }
        public SeedFieldViewModel(WorkflowField field, JProperty property) : base(field, property)
        {
            Type = FieldType.Seed;
            _field = field;
        }
    }

    public class SliderFieldViewModel : InputFieldViewModel
    {
        // ========================================================== //
        //     НАЧАЛО ИЗМЕНЕНИЯ: Логика "привязки к шагу" в сеттере    //
        // ========================================================== //
        public double Value
        {
            get => Property.Value.ToObject<double>();
            set
            {
                // 1. Вычисляем "привязанное" к шагу значение
                var step = StepValue;
                if (step <= 0) step = 1e-9; // Защита от деления на ноль
                var snappedValue = System.Math.Round((value - MinValue) / step) * step + MinValue;

                // 2. Применяем округление в зависимости от типа (целое или с плавающей точкой)
                if (Type == FieldType.SliderInt)
                {
                    snappedValue = (long)System.Math.Round(snappedValue);
                }
                else // SliderFloat
                {
                    var precision = _field.Precision ?? 2;
                    snappedValue = System.Math.Round(snappedValue, precision);
                }
                
                // 3. Убедимся, что значение не вышло за пределы
                if (snappedValue < MinValue) snappedValue = MinValue;
                if (snappedValue > MaxValue) snappedValue = MaxValue;

                // 4. Обновляем JObject и уведомляем UI, только если значение действительно изменилось.
                // Это предотвращает бесконечные циклы обновлений.
                if (!JToken.DeepEquals(Property.Value, new JValue(snappedValue)))
                {
                    Property.Value = new JValue(snappedValue);
                    
                    // Уведомляем UI, что и сырое значение, и отформатированный текст изменились.
                    // WPF автоматически обновит положение слайдера до "привязанного" значения.
                    OnPropertyChanged(nameof(Value));
                    OnPropertyChanged(nameof(FormattedValue));
                }
            }
        }
        // ========================================================== //
        //     КОНЕЦ ИЗМЕНЕНИЯ                                        //
        // ========================================================== //

        /// <summary>
        /// Новое свойство, которое возвращает уже отформатированную строку для отображения.
        /// </summary>
        public string FormattedValue => Property.Value.ToObject<double>().ToString(StringFormat);
        
        public double MinValue { get; }
        public double MaxValue { get; }
        public double StepValue { get; }
        public string StringFormat { get; }
        private readonly WorkflowField _field;

        public SliderFieldViewModel(WorkflowField field, JProperty property) : base(field, property)
        {
            Type = field.Type;
            _field = field;
            MinValue = field.MinValue ?? 0;
            MaxValue = field.MaxValue ?? 100;
            StepValue = field.StepValue ?? (field.Type == FieldType.SliderInt ? 1 : 0.01);
            StringFormat = field.Type == FieldType.SliderInt ? "F0" : "F" + (field.Precision ?? 2);
        }
    }

    public class ComboBoxFieldViewModel : InputFieldViewModel
    {
        public string Value
        {
            get => Property.Value.ToString();
            set
            {
                if (Property.Value.ToString() != value)
                {
                    Property.Value = new JValue(value);
                    OnPropertyChanged(nameof(Value));
                }
            }
        }
        public List<string> ItemsSource { get; set; }

        public ComboBoxFieldViewModel(WorkflowField field, JProperty property) : base(field, property)
        {
            Type = field.Type;
            // ИСПРАВЛЕНИЕ: Сохраняем field в член класса
            _field = field; 
            ItemsSource = new List<string>();
        }

        public async Task LoadItemsAsync(ModelService modelService, AppSettings settings)
        {
            try
            {
                var finalItemsSource = new List<string>();

                if (Type == FieldType.Model)
                {
                    var types = await modelService.GetModelTypesAsync(); // This might throw an exception
                    var modelTypeInfo = types.FirstOrDefault(t => t.Name == _field.ModelType);
                    if (modelTypeInfo != null)
                    {
                        var models = await modelService.GetModelFilesAsync(modelTypeInfo);
                        finalItemsSource.AddRange(settings.SpecialModelValues);
                        finalItemsSource.AddRange(models);
                    }
                }
                else
                {
                    finalItemsSource = _field.Type switch
                    {
                        FieldType.Sampler => settings.Samplers,
                        FieldType.Scheduler => settings.Schedulers,
                        FieldType.ComboBox => _field.ComboBoxItems,
                        _ => new List<string>()
                    };
                }
                ItemsSource = finalItemsSource.Distinct().ToList();
                OnPropertyChanged(nameof(ItemsSource));
            }
            catch (Exception ex)
            {
                // Silently log the error. The user has already been notified by the ModelService.
                System.Diagnostics.Debug.WriteLine($"Failed to load items for combobox '{Name}': {ex.Message}");
            }
        }
        
        private readonly WorkflowField _field;
    }
    
    public class CheckBoxFieldViewModel : InputFieldViewModel
    {
        public bool IsChecked
        {
            get => Property.Value.ToObject<bool>();
            set
            {
                if (Property.Value.ToObject<bool>() != value)
                {
                    Property.Value = new JValue(value);
                    OnPropertyChanged(nameof(IsChecked));
                }
            }
        }
        public CheckBoxFieldViewModel(WorkflowField field, JProperty property) : base(field, property)
        {
            Type = FieldType.Any; 
        }
    }
    
    public class ScriptButtonFieldViewModel : InputFieldViewModel
    {
        public ICommand ExecuteScriptCommand { get; }
        public string ActionName { get; }

        public ScriptButtonFieldViewModel(WorkflowField field, JProperty property, ICommand command) : base(field, property)
        {
            Type = FieldType.ScriptButton;
            ActionName = field.ActionName;
            ExecuteScriptCommand = command;
        }
    }
}