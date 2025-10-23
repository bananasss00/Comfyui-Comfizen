// --- File: CustomLinkRenderer.cs ---

using Markdig.Renderers.Wpf;
using Markdig.Renderers.Wpf.Inlines;
using Markdig.Syntax.Inlines;
using System;
using System.Windows.Documents;
using Markdig.Renderers;

namespace Comfizen
{
    /// <summary>
    /// A custom renderer for LinkInline elements.
    /// This implementation overrides the default link creation process to ensure the original,
    /// raw URL from the markdown is always stored in the Hyperlink's Tag property.
    /// This is essential for custom URI schemes like "wf://" that System.Uri cannot parse.
    /// </summary>
    public class CustomLinkRenderer : LinkInlineRenderer
    {
        protected override void Write(WpfRenderer renderer, LinkInline obj)
        {
            // FIX: Use Uri.TryCreate for safe parsing without exceptions.
            // This correctly handles both valid URIs (http) and our custom ones (wf://).
            Uri.TryCreate(obj.Url, UriKind.RelativeOrAbsolute, out Uri uri);
            
            var hyperlink = new Hyperlink
            {
                // Assign the Uri object if it was successfully created, otherwise it will be null.
                NavigateUri = uri 
            };
            
            // THE CRITICAL FIX: Always store the original, raw URL string from the markdown
            // into the Tag property. This is the source of truth for our click handler.
            hyperlink.Tag = obj.Url;

            // This part replicates the base renderer's logic:
            // 1. Push the hyperlink onto the stack to act as a container for its children (the link text).
            // 2. Render the children.
            // 3. Pop the hyperlink from the stack.
            renderer.Push(hyperlink);
            renderer.WriteChildren(obj);
            renderer.Pop();
        }
    }
}