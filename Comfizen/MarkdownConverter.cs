// --- File: MarkdownConverter.cs ---

using System.Windows.Documents;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Wpf;
using Markdig.Renderers.Wpf.Inlines;

namespace Comfizen
{
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

            // 4. Render the parsed document into the FlowDocument.
            renderer.Render(document);
        }
    }
}