using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Comfizen
{
    public static class TextBlockExtensions
    {
        public static readonly DependencyProperty BindableInlinesProperty =
            DependencyProperty.RegisterAttached(
                "BindableInlines",
                typeof(IEnumerable<LogMessageSegment>),
                typeof(TextBlockExtensions),
                new PropertyMetadata(null, OnBindableInlinesChanged));

        public static IEnumerable<LogMessageSegment> GetBindableInlines(DependencyObject obj)
        {
            return (IEnumerable<LogMessageSegment>)obj.GetValue(BindableInlinesProperty);
        }

        public static void SetBindableInlines(DependencyObject obj, IEnumerable<LogMessageSegment> value)
        {
            obj.SetValue(BindableInlinesProperty, value);
        }

        private static void OnBindableInlinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock textBlock) return;

            textBlock.Inlines.Clear();

            if (e.NewValue is not IEnumerable<LogMessageSegment> segments) return;

            foreach (var segment in segments)
            {
                var run = new Run(segment.Text);
                if (segment.Color.HasValue)
                {
                    // Set the color for the specific segment
                    run.Foreground = new SolidColorBrush(segment.Color.Value);
                }
        
                run.FontWeight = segment.FontWeight;
                run.TextDecorations = segment.TextDecorations;

                textBlock.Inlines.Add(run);
            }
        }
    }
}