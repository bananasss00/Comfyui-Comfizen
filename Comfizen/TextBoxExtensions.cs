// TextBoxExtensions.cs

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Comfizen
{
    /// <summary>
    /// Provides an attached property to add placeholder text to a TextBox.
    /// </summary>
    public static class TextBoxExtensions
    {
        public static readonly DependencyProperty PlaceholderTextProperty =
            DependencyProperty.RegisterAttached(
                "PlaceholderText",
                typeof(string),
                typeof(TextBoxExtensions),
                new PropertyMetadata(null, OnPlaceholderTextChanged));

        public static string GetPlaceholderText(DependencyObject obj)
        {
            return (string)obj.GetValue(PlaceholderTextProperty);
        }

        public static void SetPlaceholderText(DependencyObject obj, string value)
        {
            obj.SetValue(PlaceholderTextProperty, value);
        }

        private static void OnPlaceholderTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox textBox) return;

            textBox.Loaded -= TextBox_Loaded;
            textBox.Loaded += TextBox_Loaded;
            textBox.TextChanged -= TextBox_TextChanged;
            textBox.TextChanged += TextBox_TextChanged;
        }

        private static void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateAdorner(sender as TextBox);
        }

        private static void TextBox_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateAdorner(sender as TextBox);
        }

        private static void UpdateAdorner(TextBox textBox)
        {
            if (textBox == null) return;
            var layer = AdornerLayer.GetAdornerLayer(textBox);
            if (layer == null) return;

            // Remove existing adorner
            var adorners = layer.GetAdorners(textBox);
            if (adorners != null)
            {
                foreach (var adorner in adorners)
                {
                    if (adorner is PlaceholderAdorner)
                    {
                        layer.Remove(adorner);
                    }
                }
            }

            // Add new adorner if needed
            if (string.IsNullOrEmpty(textBox.Text))
            {
                layer.Add(new PlaceholderAdorner(textBox, GetPlaceholderText(textBox)));
            }
        }

        private class PlaceholderAdorner : Adorner
        {
            private readonly TextBlock _placeholderTextBlock;

            public PlaceholderAdorner(UIElement adornedElement, string placeholderText) : base(adornedElement)
            {
                IsHitTestVisible = false;
                _placeholderTextBlock = new TextBlock
                {
                    Text = placeholderText,
                    Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(7, 5, 5, 5), // Align with TextBox padding
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            protected override int VisualChildrenCount => 1;
            protected override Visual GetVisualChild(int index) => _placeholderTextBlock;

            protected override Size ArrangeOverride(Size finalSize)
            {
                _placeholderTextBlock.Arrange(new Rect(finalSize));
                return finalSize;
            }
        }
    }
}