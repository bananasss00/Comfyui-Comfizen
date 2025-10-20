using System.Windows.Documents;
using Markdig.Wpf; 

namespace Comfizen
{
    public static class MarkdownConverter
    {
        /// <summary>
        /// Converts a Markdown string to a WPF FlowDocument using the Markdig.Wpf library.
        /// </summary>
        /// <param name="markdownText">The Markdown text to convert.</param>
        /// <returns>A styled FlowDocument.</returns>
        public static FlowDocument ToFlowDocument(string markdownText)
        {
            // Добавляем проверку на null или пустую строку для надежности
            if (string.IsNullOrEmpty(markdownText))
            {
                return new FlowDocument(); // Возвращаем пустой документ, чтобы избежать сбоя
            }

            // Use the real Markdig.Wpf implementation
            return Markdig.Wpf.Markdown.ToFlowDocument(markdownText);
        }
    }
}