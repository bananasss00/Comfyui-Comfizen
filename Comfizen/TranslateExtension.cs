using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace Comfizen
{
    /// <summary>
    /// A custom markup extension that provides translated strings from the LocalizationService.
    /// Usage in XAML: {local:Translate MyTextKey}
    /// </summary>
    public class TranslateExtension : MarkupExtension
    {
        /// <summary>
        /// Gets or sets the key for the translated string.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TranslateExtension"/> class.
        /// </summary>
        /// <param name="key">The key for the translation.</param>
        public TranslateExtension(string key)
        {
            Key = key;
        }

        /// <summary>
        /// Returns a binding object that can be used to provide the translated value for the specified key.
        /// </summary>
        /// <param name="serviceProvider">A service provider helper.</param>
        /// <returns>The binding object.</returns>
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding($"[{Key}]")
            {
                Source = LocalizationService.Instance,
                Mode = BindingMode.OneWay
            };
            return binding.ProvideValue(serviceProvider);
        }
    }
}