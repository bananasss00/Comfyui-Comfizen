// --- START OF FILE SliderCompareView.xaml.cs ---

using System.Globalization;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Data;

namespace Comfizen
{
    public partial class SliderCompareView : UserControl
    {
        public SliderCompareView()
        {
            InitializeComponent();
        }

        private void Image_DragOver(object sender, DragEventArgs e)
        {
            // Allow drop if the data is an ImageOutput from the gallery OR a file from the OS.
            e.Effects = e.Data.GetDataPresent(typeof(ImageOutput)) || e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true; // Mark as handled to prevent MainWindow from interfering.
        }

        /// <summary>
        /// Handles the drop event for the entire image canvas.
        /// Determines which side (Left or Right) the drop occurred on based on the cursor's position
        /// relative to the slider.
        /// </summary>
        private void Image_Drop(object sender, DragEventArgs e)
        {
            // 1. Get the ViewModel and ensure the sender is the Grid (our canvas).
            if (DataContext is not SliderCompareViewModel vm || sender is not Grid imageCanvas)
            {
                return;
            }

            // 2. Get the cursor's position relative to the canvas.
            Point dropPosition = e.GetPosition(imageCanvas);

            // 3. Calculate the slider's boundary in pixels.
            //    vm.SliderPosition is a percentage (0-100).
            //    imageCanvas.ActualWidth is the total pixel width of the canvas.
            double sliderBoundaryX = (vm.SliderPosition / 100.0) * imageCanvas.ActualWidth;

            // 4. Determine if the drop was on the "Left" or "Right" side.
            string target = (dropPosition.X < sliderBoundaryX) ? "Left" : "Right";

            // 5. Call the ViewModel's handler with the determined target.
            vm.HandleDrop(e, target);
            
            // 6. Mark the event as handled to stop it from bubbling up to the MainWindow.
            //    This is crucial to prevent the main window from trying to import the dropped file.
            e.Handled = true;
        }
    }

    // Вспомогательный конвертер для позиционирования слайдера
    public class MultiplyConverter : IValueConverter
    {
        public static readonly MultiplyConverter Instance = new MultiplyConverter();

        public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is double val && parameter is string paramStr && double.TryParse(paramStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double param))
            {
                return val * param;
            }
            return 0;
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new System.NotImplementedException();
        }
    }
}