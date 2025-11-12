// FocusBehavior.cs

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Comfizen
{
    public static class FocusBehavior
    {
        public static readonly DependencyProperty FocusOnOpenProperty =
            DependencyProperty.RegisterAttached(
                "FocusOnOpen", 
                typeof(bool), 
                typeof(FocusBehavior),
                new PropertyMetadata(false, OnFocusOnOpenChanged));

        public static bool GetFocusOnOpen(DependencyObject obj)
        {
            return (bool)obj.GetValue(FocusOnOpenProperty);
        }

        public static void SetFocusOnOpen(DependencyObject obj, bool value)
        {
            obj.SetValue(FocusOnOpenProperty, value);
        }

        private static void OnFocusOnOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element) return;

            if ((bool)e.NewValue)
            {
                element.Loaded += OnElementLoaded;
            }
            else
            {
                element.Loaded -= OnElementLoaded;
            }
        }

        private static void OnElementLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element) return;

            // Use Dispatcher to ensure the element is fully rendered and visible before focusing
            element.Dispatcher.BeginInvoke(new Action(() =>
            {
                element.Focus();
                if (element is TextBox textBox)
                {
                    textBox.SelectAll();
                }
            }), DispatcherPriority.Input);
        }
    }
}