// IntegerUpDownSmartSpinBehavior.cs

using System;
using System.Windows;
using Xceed.Wpf.Toolkit;
using Xceed.Wpf.Toolkit.Core.Input;

namespace Comfizen
{
    public static class IntegerUpDownSmartSpinBehavior
    {
        public static readonly DependencyProperty EnableSmartSpinProperty =
            DependencyProperty.RegisterAttached(
                "EnableSmartSpin",
                typeof(bool),
                typeof(IntegerUpDownSmartSpinBehavior),
                new PropertyMetadata(false, OnEnableSmartSpinChanged));

        public static bool GetEnableSmartSpin(DependencyObject obj)
        {
            return (bool)obj.GetValue(EnableSmartSpinProperty);
        }

        public static void SetEnableSmartSpin(DependencyObject obj, bool value)
        {
            obj.SetValue(EnableSmartSpinProperty, value);
        }

        private static void OnEnableSmartSpinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not IntegerUpDown upDown) return;

            // Detach the handler first to prevent multiple subscriptions
            upDown.Spinned -= UpDown_Spinned;

            // Attach the handler if the property is set to true
            if ((bool)e.NewValue)
            {
                upDown.Spinned += UpDown_Spinned;
            }
        }

        private static void UpDown_Spinned(object sender, SpinEventArgs e)
        {
            if (sender is not IntegerUpDown upDown || !upDown.Value.HasValue) return;

            int currentValue = upDown.Value.Value;
            int newValue;

            // The logic is based on the value *before* the spin action occurred.
            int originalValue = (e.Direction == SpinDirection.Increase)
                ? currentValue - (upDown.Increment ?? 1)
                : currentValue + (upDown.Increment ?? 1);

            if (e.Direction == SpinDirection.Increase)
            {
                // If the original value was small, increment by 1. Otherwise, double it.
                newValue = originalValue < 2 ? originalValue + 1 : originalValue * 2;
            }
            else // Decrease
            {
                // Halve the original value.
                newValue = (int)Math.Ceiling(originalValue / 2.0);
            }

            // Clamp the new value within the defined min/max range.
            if (upDown.Minimum.HasValue && newValue < upDown.Minimum.Value) newValue = upDown.Minimum.Value;
            if (upDown.Maximum.HasValue && newValue > upDown.Maximum.Value) newValue = upDown.Maximum.Value;

            upDown.Value = newValue;
            e.Handled = true; // Prevent the default increment behavior
        }
    }
}