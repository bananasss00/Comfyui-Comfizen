using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace Comfizen
{
    public partial class SliderCompareView : UserControl
    {
        private SliderCompareViewModel _viewModel;
        private readonly DispatcherTimer _timer;
        private bool _isScrubbingPositionSlider = false;

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
            
            // When the view opens, set focus to the root grid to capture keyboard events.
            if (e.PropertyName == nameof(SliderCompareViewModel.IsViewOpen) && _viewModel.IsViewOpen)
            {
                // Use BeginInvoke to ensure focus is set after the UI has been rendered.
                Dispatcher.BeginInvoke(new Action(() => RootGrid.Focus()), DispatcherPriority.Input);
            }
            
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
                    // Update position only if the user is NOT currently dragging the slider.
                    // This prevents the timer from fighting with user input.
                    if (!_isScrubbingPositionSlider)
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
            
            // Check if both media elements are ready before enabling playback controls.
            if (MediaElementLeft.NaturalDuration.HasTimeSpan && MediaElementRight.NaturalDuration.HasTimeSpan)
            {
                _viewModel.IsMediaReady = true;
            }
            
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
            if (_viewModel != null && !_isScrubbingPositionSlider && MediaElementLeft.NaturalDuration.HasTimeSpan)
            {
                // Check to prevent updating position past the max duration, which can happen just before looping
                if (MediaElementLeft.Position.TotalSeconds < _viewModel.MaxDurationSeconds)
                {
                    _viewModel.CurrentPositionSeconds = MediaElementLeft.Position.TotalSeconds;
                }
            }
        }

        private void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Pause playback as soon as the user interacts with the slider.
            if (_viewModel != null && _viewModel.IsPlaying)
            {
                _viewModel.IsPlaying = false;
            }

            if (sender is not Slider slider) return;

            // If the user clicked directly on the draggable thumb, 
            // let the default behavior and the Thumb.DragStarted event handle it.
            if (e.OriginalSource is Thumb)
            {
                return;
            }

            // For clicks on the track:
            // 1. Set the scrubbing flag.
            _isScrubbingPositionSlider = true;
            // 2. Capture the mouse immediately. This is the crucial change to allow dragging.
            slider.CaptureMouse();
            // 3. Now, calculate and set the new value.
            Point position = e.GetPosition(slider);
            double newValue = (position.X / slider.ActualWidth) * slider.Maximum;
            slider.Value = Math.Max(0, Math.Min(slider.Maximum, newValue));

            e.Handled = true;
        }
        
        // Handles mouse movement when scrubbing to update the slider's position.
        private void PositionSlider_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isScrubbingPositionSlider && sender is Slider slider && slider.IsMouseCaptured)
            {
                Point position = e.GetPosition(slider);
                double newValue = (position.X / slider.ActualWidth) * slider.Maximum;
                slider.Value = Math.Max(0, Math.Min(slider.Maximum, newValue));
            }
        }

        // Releases mouse capture when the user finishes scrubbing.
        private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isScrubbingPositionSlider && sender is Slider slider)
            {
                _isScrubbingPositionSlider = false;
                slider.ReleaseMouseCapture();
            }
        }
        
        private void PositionSlider_DragStarted(object sender, DragStartedEventArgs e)
        {
            _isScrubbingPositionSlider = true;
        }
        
        private void PositionSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isScrubbingPositionSlider = false;
        }

        private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_viewModel == null) return;
            
            var newPosition = TimeSpan.FromSeconds(e.NewValue);

            // Set the new position for both videos
            MediaElementLeft.Position = newPosition;
            MediaElementRight.Position = newPosition;

            // --- THE DEFINITIVE FIX FOR LIVE SCRUBBING ON PAUSE ---
            // A simple .Position change on a paused MediaElement does not update the frame.
            // A synchronous Play()/Pause() is unreliable due to a race condition with the render thread.
            // The solution is to schedule the Pause() call on the Dispatcher, giving the UI
            // time to process the Play() command and render the new frame before pausing again.
            if (!_viewModel.IsPlaying)
            {
                // Start playing from the new position to load the frame
                MediaElementLeft.Play();
                MediaElementRight.Play();

                // Immediately schedule a pause operation. It will execute after the frame is rendered.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    MediaElementLeft.Pause();
                    MediaElementRight.Pause();
                }), DispatcherPriority.Input); // Input priority is suitable for responsive UI
            }

            // Also update the ViewModel property so the time text updates live.
            _viewModel.CurrentPositionSeconds = e.NewValue;
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
        
        // english: Handles key presses on the root grid to control video playback.
        private void RootGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_viewModel == null) return;
    
            // english: If the spacebar is pressed, execute the PlayPause command, but only if it's allowed.
            if (e.Key == Key.Space)
            {
                if (_viewModel.PlayPauseCommand.CanExecute(null))
                {
                    _viewModel.PlayPauseCommand.Execute(null);
                }
                // english: Mark the event as handled to prevent any default behavior (like clicking a focused button).
                e.Handled = true;
            }
        }
        
        // Handles the mouse click on the slider track to move the thumb directly to that point.
        private void Slider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // english: If the user clicked directly on the draggable thumb, let the default behavior handle it.
            if (e.OriginalSource is Thumb)
            {
                return;
            }

            if (DataContext is not SliderCompareViewModel vm || sender is not Slider slider)
            {
                return;
            }

            // english: Calculate the new position based on the click location.
            Point position = e.GetPosition(slider);
            double newValue = (position.X / slider.ActualWidth) * 100;
            vm.SliderPosition = Math.Max(0, Math.Min(100, newValue));

            // english: Capture the mouse to handle dragging along the track.
            slider.CaptureMouse();
            e.Handled = true;
        }

        // Handles mouse movement while the button is pressed to allow dragging anywhere on the track.
        private void Slider_MouseMove(object sender, MouseEventArgs e)
        {
            if (DataContext is not SliderCompareViewModel vm || sender is not Slider slider)
            {
                return;
            }

            // english: If we have captured the mouse and the left button is down, update the slider position.
            if (slider.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
            {
                Point position = e.GetPosition(slider);
                double newValue = (position.X / slider.ActualWidth) * 100;
                vm.SliderPosition = Math.Max(0, Math.Min(100, newValue));
            }
        }

        // Releases the mouse capture when the user releases the mouse button.
        private void Slider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider && slider.IsMouseCaptured)
            {
                slider.ReleaseMouseCapture();
            }
        }
    }

    // Converters to get Width/Height from a "WidthxHeight" resolution string
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

    // Helper converter for slider positioning
    public class MultiplyConverter : IValueConverter
    {
        public static readonly MultiplyConverter Instance = new MultiplyConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double val && parameter is string paramStr && double.TryParse(paramStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double param))
            {
                return val * param;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}