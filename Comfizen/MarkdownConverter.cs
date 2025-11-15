// --- File: MarkdownConverter.cs ---

using System;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Wpf;
using Markdig.Renderers.Wpf.Inlines;
using Markdig.Syntax;

namespace Comfizen
{
    /// <summary>
    /// A custom renderer for ListBlock elements.
    /// This fixes two bugs in Markdig.Wpf:
    /// 1. It prevents a crash when a list's StartIndex is 0, which is invalid in WPF.
    /// 2. It ensures the correct WPF document structure (List -> ListItem -> Paragraph)
    ///    is always generated, preventing "Invalid child" exceptions.
    /// </summary>
    public class CustomListRenderer : ListRenderer
    {
        protected override void Write(WpfRenderer renderer, ListBlock listBlock)
        {
            var list = new List();

            if (listBlock.IsOrdered)
            {
                var firstItem = listBlock.FirstOrDefault() as ListItemBlock;
                int start = firstItem?.Order ?? 1;
                
                // FIX 1: Ensure StartIndex is always 1 or greater.
                list.StartIndex = Math.Max(1, start);
                list.MarkerStyle = TextMarkerStyle.Decimal;
            }
            else
            {
                list.MarkerStyle = TextMarkerStyle.Disc;
            }

            renderer.Push(list);

            // FIX 2: Manually iterate through list items to enforce the correct WPF hierarchy.
            // This prevents paragraphs from being added directly to a list.
            foreach (var block in listBlock)
            {
                var item = (ListItemBlock)block;
                var listItem = new ListItem();
                renderer.Push(listItem);
                renderer.WriteChildren(item);
                renderer.Pop();
            }
            
            renderer.Pop();
        }
    }

    public static class MarkdownConverter
    {
        private static readonly MarkdownPipeline _pipeline = CreatePipeline();

        private static MarkdownPipeline CreatePipeline()
        {
            return new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();
        }

        /// <summary>
        /// Converts a Markdown string and populates an existing WPF FlowDocument.
        /// This approach ensures that the target document's resources (styles) are preserved.
        /// </summary>
        /// <param name="markdownText">The markdown text to convert.</param>
        /// <param name="flowDocument">The target FlowDocument to populate. Its existing blocks will be cleared.</param>
        public static void ConvertTo(string markdownText, FlowDocument flowDocument)
        {
            // Always clear previous content first.
            flowDocument.Blocks.Clear();

            if (string.IsNullOrEmpty(markdownText))
            {
                // If markdown is empty, we're done.
                return;
            }

            // 1. Parse the markdown text into a syntax tree document.
            var document = Markdig.Markdown.Parse(markdownText, _pipeline);
            
            // 2. Create a WpfRenderer, crucially associating it with our EXISTING FlowDocument.
            var renderer = new WpfRenderer(flowDocument);

            // 3. Modify the renderers to use our custom link logic.
            renderer.ObjectRenderers.Replace<LinkInlineRenderer>(new CustomLinkRenderer());
            
            // 4. Replace the default ListRenderer with our custom, safe one to fix all list-related bugs.
            renderer.ObjectRenderers.Replace<ListRenderer>(new CustomListRenderer());

            // 5. Render the parsed document into the FlowDocument.
            renderer.Render(document);
        }
    }
}