using System.Windows;
using System.Windows.Input;

namespace Comfizen
{
    public static class InputBindingBehavior
    {
        public static readonly DependencyProperty InputBindingsProperty =
            DependencyProperty.RegisterAttached(
                "InputBindings",
                typeof(InputBindingCollection),
                typeof(InputBindingBehavior),
                new FrameworkPropertyMetadata(new InputBindingCollection(), OnInputBindingsChanged));

        public static InputBindingCollection GetInputBindings(DependencyObject obj)
        {
            return (InputBindingCollection)obj.GetValue(InputBindingsProperty);
        }

        public static void SetInputBindings(DependencyObject obj, InputBindingCollection value)
        {
            obj.SetValue(InputBindingsProperty, value);
        }

        private static void OnInputBindingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element) return;

            // Clear existing bindings that might have been set by this behavior
            element.InputBindings.Clear();

            if (e.NewValue is InputBindingCollection newBindings)
            {
                // Add the new bindings from the style to the element's actual InputBindings collection
                foreach (InputBinding binding in newBindings)
                {
                    element.InputBindings.Add(binding);
                }
            }
        }
    }
}