// --- File: CustomLinkRenderer.cs ---

using Markdig.Renderers.Wpf;
using Markdig.Renderers.Wpf.Inlines;
using Markdig.Syntax.Inlines;
using System;
using System.Windows; // Required for FrameworkContentElement
using System.Windows.Documents;
using Markdig.Renderers;
using Markdig.Wpf; // Required for the Styles class

namespace Comfizen
{
    /// <summary>
    /// A custom renderer for LinkInline elements.
    /// This implementation overrides the default link creation process to ensure the original,
    /// raw URL from the markdown is always stored in the Hyperlink's Tag property
    /// AND that the correct style from the document resources is applied.
    /// </summary>
    public class CustomLinkRenderer : LinkInlineRenderer
    {
        protected override void Write(WpfRenderer renderer, LinkInline obj)
        {
            Uri.TryCreate(obj.Url, UriKind.RelativeOrAbsolute, out Uri uri);
            
            var hyperlink = new Hyperlink
            {
                NavigateUri = uri,
                Tag = obj.Url
            };

            // THE FIX: Explicitly apply the style defined by the key in our XAML.
            // This tells the new hyperlink to find the style resource associated
            // with Styles.HyperlinkStyleKey and apply it.
            hyperlink.SetResourceReference(FrameworkContentElement.StyleProperty, Styles.HyperlinkStyleKey);
            
            // This part remains the same
            renderer.Push(hyperlink);
            renderer.WriteChildren(obj);
            renderer.Pop();
        }
    }
}