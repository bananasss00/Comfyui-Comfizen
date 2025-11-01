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
            e.Effects = e.Data.GetDataPresent(typeof(ImageOutput)) || e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void Image_Drop(object sender, DragEventArgs e)
        {
            if (DataContext is SliderCompareViewModel vm && sender is FrameworkElement fe && fe.Tag is string target)
            {
                vm.HandleDrop(e, target);
            }
        }
    }

    // Helper converter for slider positioning
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