using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PropertyChanged;

namespace Comfizen
{
    /// <summary>
    /// ViewModel for the SettingsWindow.
    /// Handles the logic for managing and saving application settings.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly SettingsService _settingsService;
        private AppSettings _settings;

        public ObservableCollection<string> Samplers { get; set; }
        public ObservableCollection<string> Schedulers { get; set; }

        public string NewSampler { get; set; }
        public string SelectedSampler { get; set; }
        public string NewScheduler { get; set; }
        public string SelectedScheduler { get; set; }

        public ICommand AddSamplerCommand { get; }
        public ICommand RemoveSamplerCommand { get; }
        public ICommand MoveSamplerUpCommand { get; }
        public ICommand MoveSamplerDownCommand { get; }

        public ICommand AddSchedulerCommand { get; }
        public ICommand RemoveSchedulerCommand { get; }
        public ICommand MoveSchedulerUpCommand { get; }
        public ICommand MoveSchedulerDownCommand { get; }
        
        public string ServerAddress { get; set; }
        public string SavedImagesDirectory { get; set; }
        public bool SavePromptWithFile { get; set; }
        public bool RemoveBase64OnSave { get; set; }
        public bool SaveImagesFlat { get; set; }
        public ImageSaveFormat SaveFormat { get; set; }
        public int PngCompressionLevel { get; set; }
        public int WebpQuality { get; set; }
        public int JpgQuality { get; set; }
        public bool CompressAnyFieldImagesToJpg { get; set; }
        public int AnyFieldJpgCompressionQuality { get; set; }
        public IEnumerable<ImageSaveFormat> ImageSaveFormatValues => System.Enum.GetValues(typeof(ImageSaveFormat)).Cast<ImageSaveFormat>();
        public int MaxRecentWorkflows { get; set; }
        public int MaxQueueSize { get; set; }
        public bool ShowDeleteConfirmation { get; set; }
        public bool ShowPresetDeleteConfirmation { get; set; }
        public List<string> ModelExtensions { get; set; }
        public List<string> SpecialModelValues { get; set; }
        public ICommand SaveCommand { get; }

        /// <summary>
        /// Gets or sets the currently selected language.
        /// When set, it applies the new language via the LocalizationService.
        /// </summary>
        public string Language
        {
            get => _settings.Language;
            set
            {
                if (_settings.Language != value)
                {
                    _settings.Language = value;
                    LocalizationService.Instance.SetLanguage(value);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
                }
            }
        }

        /// <summary>
        /// Gets the list of available languages for the UI.
        /// </summary>
        public IEnumerable<CultureInfo> SupportedLanguages => LocalizationService.Instance.SupportedLanguages;
        
        /// <summary>
        /// Gets or sets the multi-line string for slider default rules.
        /// </summary>
        public string SliderDefaults { get; set; }


        public event PropertyChangedEventHandler? PropertyChanged;

        public SettingsViewModel()
        {
            _settingsService = SettingsService.Instance;
            _settings = _settingsService.Settings;
            
            // Load all settings from the settings object
            ServerAddress = _settings.ServerAddress;
            SavedImagesDirectory = _settings.SavedImagesDirectory;
            SavePromptWithFile = _settings.SavePromptWithFile;
            RemoveBase64OnSave = _settings.RemoveBase64OnSave;
            SaveImagesFlat = _settings.SaveImagesFlat;
            Samplers = new ObservableCollection<string>(_settings.Samplers);
            Schedulers = new ObservableCollection<string>(_settings.Schedulers);
            SaveFormat = _settings.SaveFormat;
            PngCompressionLevel = _settings.PngCompressionLevel;
            WebpQuality = _settings.WebpQuality;
            JpgQuality = _settings.JpgQuality;
            CompressAnyFieldImagesToJpg = _settings.CompressAnyFieldImagesToJpg;
            AnyFieldJpgCompressionQuality = _settings.AnyFieldJpgCompressionQuality;
            MaxQueueSize = _settings.MaxQueueSize;
            MaxRecentWorkflows = _settings.MaxRecentWorkflows;
            ShowDeleteConfirmation = _settings.ShowDeleteConfirmation;
            ShowPresetDeleteConfirmation = _settings.ShowPresetDeleteConfirmation;
            ModelExtensions = new List<string>(_settings.ModelExtensions);
            SpecialModelValues = new List<string>(_settings.SpecialModelValues);
            SliderDefaults = string.Join("\n", _settings.SliderDefaults);
            
            AddSamplerCommand = new RelayCommand(
                _ => {
                    if (!string.IsNullOrWhiteSpace(NewSampler) && !Samplers.Contains(NewSampler))
                    {
                        Samplers.Add(NewSampler);
                        NewSampler = string.Empty;
                    }
                },
                _ => !string.IsNullOrWhiteSpace(NewSampler)
            );

            RemoveSamplerCommand = new RelayCommand(
                param => {
                    if (param is ListBox listBox && listBox.SelectedItems.Count > 0)
                    {
                        var itemsToRemove = listBox.SelectedItems.Cast<string>().ToList();
                        foreach (var item in itemsToRemove)
                        {
                            Samplers.Remove(item);
                        }
                    }
                }
            );

            MoveSamplerUpCommand = new RelayCommand(
                _ => {
                    int index = Samplers.IndexOf(SelectedSampler);
                    Samplers.Move(index, index - 1);
                },
                _ => SelectedSampler != null && Samplers.IndexOf(SelectedSampler) > 0
            );

            MoveSamplerDownCommand = new RelayCommand(
                _ => {
                    int index = Samplers.IndexOf(SelectedSampler);
                    Samplers.Move(index, index + 1);
                },
                _ => SelectedSampler != null && Samplers.IndexOf(SelectedSampler) < Samplers.Count - 1
            );

            AddSchedulerCommand = new RelayCommand(
                _ => {
                    if (!string.IsNullOrWhiteSpace(NewScheduler) && !Schedulers.Contains(NewScheduler))
                    {
                        Schedulers.Add(NewScheduler);
                        NewScheduler = string.Empty;
                    }
                },
                _ => !string.IsNullOrWhiteSpace(NewScheduler)
            );

            RemoveSchedulerCommand = new RelayCommand(
                param => {
                    if (param is ListBox listBox && listBox.SelectedItems.Count > 0)
                    {
                        var itemsToRemove = listBox.SelectedItems.Cast<string>().ToList();
                        foreach (var item in itemsToRemove)
                        {
                            Schedulers.Remove(item);
                        }
                    }
                }
            );
            
            MoveSchedulerUpCommand = new RelayCommand(
                _ => {
                    int index = Schedulers.IndexOf(SelectedScheduler);
                    Schedulers.Move(index, index - 1);
                },
                _ => SelectedScheduler != null && Schedulers.IndexOf(SelectedScheduler) > 0
            );

            MoveSchedulerDownCommand = new RelayCommand(
                _ => {
                    int index = Schedulers.IndexOf(SelectedScheduler);
                    Schedulers.Move(index, index + 1);
                },
                _ => SelectedScheduler != null && Schedulers.IndexOf(SelectedScheduler) < Schedulers.Count - 1
            );

            SaveCommand = new RelayCommand(
                param => {
                    // Save all settings back to the settings object
                    _settings.ServerAddress = ServerAddress;
                    _settings.SavedImagesDirectory = SavedImagesDirectory;
                    _settings.SavePromptWithFile = SavePromptWithFile;
                    _settings.RemoveBase64OnSave = RemoveBase64OnSave;
                    _settings.SaveImagesFlat = SaveImagesFlat;
                    _settings.Samplers = Samplers.ToList();
                    _settings.Schedulers = Schedulers.ToList();
                    _settings.SaveFormat = SaveFormat;
                    _settings.PngCompressionLevel = PngCompressionLevel;
                    _settings.WebpQuality = WebpQuality;
                    _settings.JpgQuality = JpgQuality;
                    _settings.CompressAnyFieldImagesToJpg = CompressAnyFieldImagesToJpg;
                    _settings.AnyFieldJpgCompressionQuality = AnyFieldJpgCompressionQuality;
                    _settings.MaxQueueSize = MaxQueueSize;
                    _settings.MaxRecentWorkflows = MaxRecentWorkflows;
                    _settings.ShowDeleteConfirmation = ShowDeleteConfirmation;
                    _settings.ShowPresetDeleteConfirmation = ShowPresetDeleteConfirmation;
                    _settings.ModelExtensions = ModelExtensions;
                    _settings.SpecialModelValues = SpecialModelValues;
                    _settings.Language = Language;
                    _settings.SliderDefaults = SliderDefaults?.Split('\n')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList() ?? new List<string>();

                    _settingsService.SaveSettings();
                    if (param is Window window)
                    {
                        window.Close();
                    }
                }
            );
        }
    }
}