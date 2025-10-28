using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    }

    /// <summary>
    /// Manages loading and saving application settings from a JSON file.
    /// </summary>
    public class SettingsService
    {
        private const string SettingsFileName = "settings.json";
        private static readonly string SettingsFilePath = Path.Combine(Directory.GetCurrentDirectory(), SettingsFileName);
        
        private List<string> GetDefaultSamplers() => new List<string> { "euler", "euler_ancestral", "heun", "heunpp2", "dpm_2", "dpm_2_ancestral", "lms", "dpm_fast", "dpm_adaptive", "dpmpp_2s_ancestral", "dpmpp_sde", "dpmpp_sde_gpu", "dpmpp_2m", "dpmpp_2m_sde", "dpmpp_2m_sde_gpu", "dpmpp_3m_sde", "dpmpp_3m_sde_gpu", "ddpm", "lcm" };
        private List<string> GetDefaultSchedulers() => new List<string> { "normal", "karras", "exponential", "sgm_uniform", "simple", "ddim_uniform", "ddim", "uni_pc", "uni_pc_bh2" };
        private string GetDefaultSaveDirectory() => Path.Combine(Directory.GetCurrentDirectory(), "api_saves");
        private string GetDefaultSessionsDirectory() => Path.Combine(Directory.GetCurrentDirectory(), "sessions");
        
        /// <summary>
        /// Loads settings from the settings file, or creates a default file if it doesn't exist.
        /// </summary>
        /// <returns>The loaded AppSettings object.</returns>
        public AppSettings LoadSettings()
        {
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
                    SliderDefaults = new List<string>()
                };
                
                SaveSettings(defaultSettings);
                return defaultSettings;
            }

            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonConvert.DeserializeObject<AppSettings>(json);

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
            if (settings.SliderDefaults == null) { settings.SliderDefaults = new List<string>() {"KSampler::cfg=1.0 15.0 0.5 2"}; needsResave = true; }

            if (needsResave)
            {
                SaveSettings(settings);
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
        /// Saves the provided settings object to the settings file.
        /// </summary>
        /// <param name="settings">The AppSettings object to save.</param>
        public void SaveSettings(AppSettings settings)
        {
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(SettingsFilePath, json);
        }
    }
}