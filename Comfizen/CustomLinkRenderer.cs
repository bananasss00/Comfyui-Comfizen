// --- File: CustomLinkRenderer.cs ---

using Markdig.Renderers.Wpf;
using Markdig.Syntax.Inlines;
using System;
using System.Windows; // Required for FrameworkContentElement
using System.Windows.Documents;
using System.Windows.Media; // Required for BrushConverter
using Markdig.Renderers;
using Markdig.Renderers.Wpf.Inlines;
using Markdig.Wpf; // Required for the Styles class

namespace Comfizen
{
    /// <summary>
    /// A custom renderer for LinkInline elements.
    /// Handles standard links and adds support for text coloring using "[text](color:red)" syntax.
    /// </summary>
    public class CustomLinkRenderer : LinkInlineRenderer
    {
        protected override void Write(WpfRenderer renderer, LinkInline obj)
        {
            // --- START OF NEW FEATURE: Text Coloring ---
            // Check if the URL is a color directive (e.g., "color:red" or "color:#FF0000")
            if (obj.Url != null && obj.Url.StartsWith("color:", StringComparison.OrdinalIgnoreCase))
            {
                var colorString = obj.Url.Substring(6).Trim(); // Remove "color:" prefix
                
                // Create a Span (a neutral inline container)
                var span = new Span();

                try
                {
                    // Try to convert the string to a Brush (supports named colors "Red" and Hex "#FF0000")
                    var brushConverter = new BrushConverter();
                    if (brushConverter.ConvertFromString(colorString) is Brush brush)
                    {
                        span.Foreground = brush;
                    }
                }
                catch 
                {
                    // If the color is invalid, we just ignore it and render default text color.
                    // You might want to log this: Logger.Log($"Invalid markdown color: {colorString}");
                }

                // Render the content inside the colored Span
                renderer.Push(span);
                renderer.WriteChildren(obj);
                renderer.Pop();
                
                return; // Exit, do not render as a hyperlink
            }
            // --- END OF NEW FEATURE ---

            // Standard Hyperlink Rendering Logic (Original Code)
            Uri.TryCreate(obj.Url, UriKind.RelativeOrAbsolute, out Uri uri);
            
            var hyperlink = new Hyperlink
            {
                NavigateUri = uri,
                Tag = obj.Url
            };

            // Explicitly apply the style defined by the key in our XAML.
            hyperlink.SetResourceReference(FrameworkContentElement.StyleProperty, Styles.HyperlinkStyleKey);
            
            renderer.Push(hyperlink);
            renderer.WriteChildren(obj);
            renderer.Pop();
        }
    }
}