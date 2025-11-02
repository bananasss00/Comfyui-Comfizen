// --- START OF FILE SliderCompareView.xaml.cs ---

using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;

namespace Comfizen
{
    public partial class SliderCompareView : UserControl
    {
        private SliderCompareViewModel _viewModel;
        private readonly DispatcherTimer _timer;
        private bool _isDraggingSlider = false;

        public SliderCompareView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;

            // Timer for updating the slider position during playback
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += Timer_Tick;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from the old ViewModel
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }

            _viewModel = e.NewValue as SliderCompareViewModel;

            // Subscribe to the new ViewModel
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                // Initial update when view becomes visible
                UpdateMediaSources();
            }
        }
        
        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null) return;

            switch (e.PropertyName)
            {
                case nameof(SliderCompareViewModel.IsPlaying):
                    if (_viewModel.IsPlaying)
                    {
                        MediaElementLeft.Play();
                        MediaElementRight.Play();
                        _timer.Start();
                    }
                    else
                    {
                        MediaElementLeft.Pause();
                        MediaElementRight.Pause();
                        _timer.Stop();
                    }
                    break;
                case nameof(SliderCompareViewModel.CurrentPositionSeconds):
                    if (!_isDraggingSlider)
                    {
                        var newPosition = TimeSpan.FromSeconds(_viewModel.CurrentPositionSeconds);
                        MediaElementLeft.Position = newPosition;
                        MediaElementRight.Position = newPosition;
                    }
                    break;
                case nameof(SliderCompareViewModel.ImageLeft):
                case nameof(SliderCompareViewModel.ImageRight):
                    UpdateMediaSources();
                    break;
            }
        }
        
        private void UpdateMediaSources()
        {
            if (_viewModel == null) return;
            
            MediaElementLeft.Source = _viewModel.ImageLeft?.Type == FileType.Video ? _viewModel.ImageLeft.GetHttpUri() : null;
            MediaElementRight.Source = _viewModel.ImageRight?.Type == FileType.Video ? _viewModel.ImageRight.GetHttpUri() : null;
        }

        private void MediaElement_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            
            _timer.Stop();

            var durationLeft = MediaElementLeft.NaturalDuration.HasTimeSpan ? MediaElementLeft.NaturalDuration.TimeSpan.TotalSeconds : 0;
            var durationRight = MediaElementRight.NaturalDuration.HasTimeSpan ? MediaElementRight.NaturalDuration.TimeSpan.TotalSeconds : 0;
            _viewModel.MaxDurationSeconds = Math.Max(durationLeft, durationRight);

            // Sync initial position
            var initialPosition = TimeSpan.FromSeconds(_viewModel.CurrentPositionSeconds);
            MediaElementLeft.Position = initialPosition;
            MediaElementRight.Position = initialPosition;

            if (_viewModel.IsPlaying)
            {
                MediaElementLeft.Play();
                MediaElementRight.Play();
                _timer.Start();
            }
        }
        
        // --- ADDED: Method to handle looping ---
        private void MediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            // If we are still in "playing" mode, loop the video from the beginning.
            if (_viewModel != null && _viewModel.IsPlaying)
            {
                var newPosition = TimeSpan.Zero;
                MediaElementLeft.Position = newPosition;
                MediaElementRight.Position = newPosition;
                MediaElementLeft.Play();
                MediaElementRight.Play();
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_viewModel != null && !_isDraggingSlider && MediaElementLeft.NaturalDuration.HasTimeSpan)
            {
                // ADDED: Check to prevent updating position past the max duration, which can happen before looping
                if (MediaElementLeft.Position.TotalSeconds < _viewModel.MaxDurationSeconds)
                {
                    _viewModel.CurrentPositionSeconds = MediaElementLeft.Position.TotalSeconds;
                }
            }
        }
        
        private void PositionSlider_DragStarted(object sender, DragStartedEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private void PositionSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isDraggingSlider = false;
            if (_viewModel != null)
            {
                var newPosition = TimeSpan.FromSeconds(PositionSlider.Value);
                MediaElementLeft.Position = newPosition;
                MediaElementRight.Position = newPosition;
            }
        }

        private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSlider && _viewModel != null)
            {
                var newPosition = TimeSpan.FromSeconds(e.NewValue);
                MediaElementLeft.Position = newPosition;
                MediaElementRight.Position = newPosition;
            }
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

    // --- START OF CHANGE: ADDED Converters to get Width/Height from Resolution string ---
    public class ResolutionToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string resolution && !string.IsNullOrEmpty(resolution))
            {
                var parts = resolution.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out int width))
                {
                    return (double)width;
                }
            }
            return 1200.0; // Fallback width
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ResolutionToHeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string resolution && !string.IsNullOrEmpty(resolution))
            {
                var parts = resolution.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[1], out int height))
                {
                    return (double)height;
                }
            }
            return 800.0; // Fallback height
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
    // --- END OF CHANGE ---

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