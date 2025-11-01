using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Comfizen
{
    /// <summary>
    /// Converts values into a RectangleGeometry for clipping the LEFT image.
    /// The clipping rectangle starts from the left edge (0) and has a width
    /// corresponding to the slider's position.
    /// </summary>
    public class LeftClipConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3 ||
                !(values[0] is double sliderPosition) ||
                !(values[1] is double containerWidth) ||
                !(values[2] is double containerHeight))
            {
                return Geometry.Empty;
            }

            if (containerWidth <= 0 || containerHeight <= 0)
            {
                return Geometry.Empty;
            }

            double clipWidth = (sliderPosition / 100.0) * containerWidth;
            var clipRect = new Rect(0, 0, clipWidth, containerHeight);

            return new RectangleGeometry(clipRect);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts values into a RectangleGeometry for clipping the RIGHT image.
    /// The clipping rectangle starts from the slider's position and
    /// extends to the right edge of the container.
    /// </summary>
    public class RightClipConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3 ||
                !(values[0] is double sliderPosition) ||
                !(values[1] is double containerWidth) ||
                !(values[2] is double containerHeight))
            {
                return Geometry.Empty;
            }

            if (containerWidth <= 0 || containerHeight <= 0)
            {
                return Geometry.Empty;
            }

            // Calculate the starting X position for the clip (the slider's position).
            double clipX = (sliderPosition / 100.0) * containerWidth;

            // The clip width is the remaining space to the right edge.
            double clipWidth = containerWidth - clipX;

            // If the width is negative (due to floating point inaccuracies), treat it as zero.
            if (clipWidth < 0) clipWidth = 0;

            var clipRect = new Rect(clipX, 0, clipWidth, containerHeight);

            return new RectangleGeometry(clipRect);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}