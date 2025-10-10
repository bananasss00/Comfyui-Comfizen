using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;

namespace Comfizen
{
    /// <summary>
    /// Manages application localization by loading and providing translated strings.
    /// This is a singleton class.
    /// </summary>
    public class LocalizationService : INotifyPropertyChanged
    {
        /// <summary>
        /// Gets the singleton instance of the LocalizationService.
        /// </summary>
        public static LocalizationService Instance { get; } = new LocalizationService();

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        private Dictionary<string, string> _localizedStrings = new Dictionary<string, string>();
        
        /// <summary>
        /// The absolute path to the languages directory.
        /// </summary>
        private static readonly string LangsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "langs");

        private static readonly Lazy<IEnumerable<CultureInfo>> _supportedLanguages = new Lazy<IEnumerable<CultureInfo>>(GetSupportedLanguages);

        private LocalizationService() { }

        /// <summary>
        /// Gets the translated string for the specified key.
        /// </summary>
        public string this[string key]
        {
            get
            {
                if (_localizedStrings.TryGetValue(key, out var value))
                {
                    return value;
                }
                return $"[{key}]";
            }
        }

        /// <summary>
        /// Gets the currently active language culture.
        /// </summary>
        public CultureInfo CurrentLanguage { get; private set; } = CultureInfo.InvariantCulture; // Use Invariant initially

        /// <summary>
        /// Gets an enumeration of all supported languages found in the 'langs' directory.
        /// This list is loaded only once on first access.
        /// </summary>
        public IEnumerable<CultureInfo> SupportedLanguages => _supportedLanguages.Value;

        private static IEnumerable<CultureInfo> GetSupportedLanguages()
        {
            if (!Directory.Exists(LangsDir))
            {
                return Enumerable.Empty<CultureInfo>();
            }

            // Always include English as a fallback
            var cultures = new HashSet<CultureInfo> { new CultureInfo("en") };

            var files = Directory.GetFiles(LangsDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension);
            
            foreach(var langCode in files)
            {
                 try 
                 { 
                    cultures.Add(new CultureInfo(langCode)); 
                 }
                 catch { /* Ignore invalid lang codes in filenames */ }
            }

            return cultures.OrderBy(c => c.NativeName).ToList();
        }

        /// <summary>
        /// Sets the application's current language.
        /// </summary>
        /// <param name="langCode">The language code (e.g., "en", "ru").</param>
        public void SetLanguage(string langCode)
        {
            try
            {
                // Default to "en" if the provided code is invalid or empty
                if (string.IsNullOrEmpty(langCode))
                {
                    langCode = "en";
                }

                var culture = new CultureInfo(langCode);
                CurrentLanguage = culture;
                Thread.CurrentThread.CurrentUICulture = culture;

                string twoLetterCode = culture.TwoLetterISOLanguageName;
                string filePath = Path.Combine(LangsDir, $"{twoLetterCode}.json");
                
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    _localizedStrings = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                }
                else
                {
                    // If the requested language file is not found, try to fall back to English
                    filePath = Path.Combine(LangsDir, "en.json");
                    if (File.Exists(filePath))
                    {
                        string json = File.ReadAllText(filePath);
                        _localizedStrings = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    }
                    else
                    {
                        _localizedStrings.Clear(); // No language files found at all
                    }
                }

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            }
            catch (Exception ex)
            {
                Logger.Log(ex, $"Failed to set language to '{langCode}'");
                _localizedStrings.Clear();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            }
        }
    }
}