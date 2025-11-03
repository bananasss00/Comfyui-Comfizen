using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using Newtonsoft.Json;

namespace Comfizen
{
    /// <summary>
    /// Defines the format for saving images.
    /// </summary>
    public enum ImageSaveFormat
    {
        Png,
        Webp,
        Jpg
    }
    
    /// <summary>
    /// Represents all user-configurable settings for the application.
    /// </summary>
    public class AppSettings
    {
        public string SavedImagesDirectory { get; set; }
        public List<string> Samplers { get; set; } = new List<string>();
        public List<string> Schedulers { get; set; } = new List<string>();
        public string SessionsDirectory { get; set; }
        public bool SavePromptWithFile { get; set; } = true;
        
        public bool RemoveBase64OnSave { get; set; } = false;
        public ImageSaveFormat SaveFormat { get; set; } = ImageSaveFormat.Jpg;
        public int PngCompressionLevel { get; set; } = 6;
        public int WebpQuality { get; set; } = 83;
        public int JpgQuality { get; set; } = 90;
        
        public int MaxRecentWorkflows { get; set; } = 10;
        public List<string> RecentWorkflows { get; set; } = new List<string>();
        
        public int MaxQueueSize { get; set; } = 100;
        public bool ShowDeleteConfirmation { get; set; } = true;
        public SeedControl LastSeedControlState { get; set; } = SeedControl.Fixed;
        public List<string> ModelExtensions { get; set; }
        
        public bool IsConsoleVisible { get; set; } = false;
        public double GalleryThumbnailSize { get; set; } = 128.0;
        
        public List<string> LastOpenWorkflows { get; set; } = new List<string>();
        public string LastActiveWorkflow { get; set; }
        public string ServerAddress { get; set; } = "127.0.0.1:8188";
        public List<string> SpecialModelValues { get; set; } = new List<string>();
        
        /// <summary>
        /// Gets or sets the selected language code (e.g., "en").
        /// </summary>
        public string Language { get; set; }
        
        /// <summary>
        /// A list of rules for default slider values.
        /// Format: "[NodeType::]FieldName=min max step [precision]"
        /// </summary>
        public List<string> SliderDefaults { get; set; }
        
        // Main window dimensions
        public double MainWindowHeight { get; set; } = 768;
        public double MainWindowWidth { get; set; } = 1366;
        public WindowState MainWindowState { get; set; } = WindowState.Normal;
        public double MainWindowLeft { get; set; } = 100;
        public double MainWindowTop { get; set; } = 100;
        
        // Designer window dimensions
        public double DesignerWindowHeight { get; set; } = 700;
        public double DesignerWindowWidth { get; set; } = 1000;
        public WindowState DesignerWindowState { get; set; } = WindowState.Normal;
        public double DesignerWindowLeft { get; set; } = 150;
        public double DesignerWindowTop { get; set; } = 150;
        
        // Designer settings
        public bool UseNodeTitlePrefixInDesigner { get; set; } = true;
    }

    /// <summary>
    /// Manages loading and saving application settings from a JSON file.
    /// </summary>
    public class SettingsService
    {
        private static readonly Lazy<SettingsService> _instance = new Lazy<SettingsService>(() => new SettingsService());
        public static SettingsService Instance => _instance.Value;
        
        private const string SettingsFileName = "settings.json";
        private static readonly string SettingsFilePath = Path.Combine(Directory.GetCurrentDirectory(), SettingsFileName);
        
        /// <summary>
        /// Gets the single, application-wide instance of AppSettings.
        /// </summary>
        public AppSettings Settings { get; private set; }

        /// <summary>
        /// Private constructor to prevent external instantiation.
        /// Loads settings upon creation.
        /// </summary>
        private SettingsService()
        {
            Settings = LoadSettings();
        }
        
        private List<string> GetDefaultSamplers() => new List<string> { "euler", "euler_ancestral", "heun", "heunpp2", "dpm_2", "dpm_2_ancestral", "lms", "dpm_fast", "dpm_adaptive", "dpmpp_2s_ancestral", "dpmpp_sde", "dpmpp_sde_gpu", "dpmpp_2m", "dpmpp_2m_sde", "dpmpp_2m_sde_gpu", "dpmpp_3m_sde", "dpmpp_3m_sde_gpu", "ddpm", "lcm" };
        private List<string> GetDefaultSchedulers() => new List<string> { "normal", "karras", "exponential", "sgm_uniform", "simple", "ddim_uniform", "ddim", "uni_pc", "uni_pc_bh2" };
        private string GetDefaultSaveDirectory() => Path.Combine(Directory.GetCurrentDirectory(), "api_saves");
        private string GetDefaultSessionsDirectory() => Path.Combine(Directory.GetCurrentDirectory(), "sessions");
        
        /// <summary>
        /// Loads settings from the settings file, or creates a default file if it doesn't exist.
        /// </summary>
        /// <returns>The loaded AppSettings object.</returns>
        private AppSettings LoadSettings()
        {
            var sliderDefaults = new List<string>()
            {
                "KSampler::cfg=1.0 15.0 0.5 2",
                "cfg=1.0 15.0 0.5 2",
                "denoise=0 1 0.05 2",
                "steps=4 50 1",
                "width=512 4096 128",
                "height=512 4096 128",
                "strength_model=0 1 0.05 2",
                "weight=0 1 0.05 2",
                "start_at=0 1 0.05 2",
                "end_at=0 1 0.05 2",
            };
            
            if (!File.Exists(SettingsFilePath))
            {
                var initialLanguage = InitialLanguage();

                var defaultSettings = new AppSettings
                {
                    SavedImagesDirectory = GetDefaultSaveDirectory(),
                    Samplers = GetDefaultSamplers(),
                    Schedulers = GetDefaultSchedulers(),
                    SessionsDirectory = GetDefaultSessionsDirectory(),
                    SavePromptWithFile = true,
                    RemoveBase64OnSave = false,
                    SaveFormat = ImageSaveFormat.Webp,
                    PngCompressionLevel = 6,
                    WebpQuality = 83,
                    JpgQuality = 90, 
                    MaxRecentWorkflows = 10,
                    RecentWorkflows = new List<string>(),
                    MaxQueueSize = 100,
                    ShowDeleteConfirmation = true,
                    LastSeedControlState = SeedControl.Fixed,
                    ModelExtensions = new List<string> { ".safetensors", ".ckpt", ".pt", ".gguf" },
                    IsConsoleVisible = false,
                    GalleryThumbnailSize = 128.0,
                    LastOpenWorkflows = new List<string>(),
                    LastActiveWorkflow = null,
                    ServerAddress = "127.0.0.1:8188",
                    SpecialModelValues = new List<string> { "None" },
                    Language = initialLanguage,
                    SliderDefaults = sliderDefaults,
                    MainWindowHeight = 768,
                    MainWindowWidth = 1366,
                    MainWindowState = WindowState.Normal,
                    MainWindowLeft = 100,
                    MainWindowTop = 100,
                    DesignerWindowHeight = 700,
                    DesignerWindowWidth = 1000,
                    DesignerWindowState = WindowState.Normal,
                    DesignerWindowLeft = 150,
                    DesignerWindowTop = 150,
                    UseNodeTitlePrefixInDesigner = true
                };
                
                var json = JsonConvert.SerializeObject(defaultSettings, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);
                return defaultSettings;
            }

            var jsonRead = File.ReadAllText(SettingsFilePath);
            var settings = JsonConvert.DeserializeObject<AppSettings>(jsonRead);

            // This block ensures that new settings are added to existing user files
            // without overwriting their existing preferences.
            bool needsResave = false;
            
            if (settings.SpecialModelValues == null || !settings.SpecialModelValues.Any()) { settings.SpecialModelValues = new List<string> { "None" }; needsResave = true; }
            if (settings.ModelExtensions == null) { settings.ModelExtensions = new List<string> { ".safetensors", ".ckpt", ".pt", ".gguf" }; needsResave = true; }
            if (string.IsNullOrEmpty(settings.SavedImagesDirectory)) { settings.SavedImagesDirectory = GetDefaultSaveDirectory(); needsResave = true; }
            if (string.IsNullOrEmpty(settings.SessionsDirectory)) { settings.SessionsDirectory = GetDefaultSessionsDirectory(); needsResave = true; }
            if (settings.MaxRecentWorkflows <= 0) { settings.MaxRecentWorkflows = 10; needsResave = true; }
            if (settings.RecentWorkflows == null) { settings.RecentWorkflows = new List<string>(); needsResave = true; }
            if (settings.Samplers == null) { settings.Samplers = GetDefaultSamplers(); needsResave = true; }
            if (settings.Schedulers == null) { settings.Schedulers = GetDefaultSchedulers(); needsResave = true; }
            if (settings.LastOpenWorkflows == null) { settings.LastOpenWorkflows = new List<string>(); needsResave = true; }
            if (string.IsNullOrEmpty(settings.ServerAddress)) { settings.ServerAddress = "127.0.0.1:8188"; needsResave = true; }
            if (string.IsNullOrEmpty(settings.Language)) { settings.Language = InitialLanguage(); needsResave = true; }
            if (settings.GalleryThumbnailSize == 0.0) { settings.GalleryThumbnailSize = 128.0; needsResave = true; }
            if (settings.SliderDefaults == null) { settings.SliderDefaults = sliderDefaults; needsResave = true; }
            if (settings.MainWindowHeight <= 0) { settings.MainWindowHeight = 768; needsResave = true; }
            if (settings.MainWindowWidth <= 0) { settings.MainWindowWidth = 1366; needsResave = true; }
            if (settings.DesignerWindowHeight <= 0) { settings.DesignerWindowHeight = 700; needsResave = true; }
            if (settings.DesignerWindowWidth <= 0) { settings.DesignerWindowWidth = 1000; needsResave = true; }
            if (settings.MainWindowLeft <= 0) { settings.MainWindowLeft = 100; needsResave = true; }
            if (settings.MainWindowTop <= 0) { settings.MainWindowTop = 100; needsResave = true; }
            if (settings.DesignerWindowLeft <= 0) { settings.DesignerWindowLeft = 150; needsResave = true; }
            if (settings.DesignerWindowTop <= 0) { settings.DesignerWindowTop = 150; needsResave = true; }

            if (needsResave)
            {
                var jsonResave = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, jsonResave);
            }

            return settings;
        }

        private static string InitialLanguage()
        {
            var systemLangCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var supportedLangCodes = LocalizationService.Instance.SupportedLanguages
                .Select(c => c.TwoLetterISOLanguageName)
                .ToList();
            var initialLanguage = supportedLangCodes.Contains(systemLangCode) 
                ? systemLangCode 
                : "en";
            return initialLanguage;
        }

        /// <summary>
        /// Saves the current state of the single AppSettings instance to the file.
        /// </summary>
        public void SaveSettings() // Method signature changed
        {
            var json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
            File.WriteAllText(SettingsFilePath, json);
        }
    }
}