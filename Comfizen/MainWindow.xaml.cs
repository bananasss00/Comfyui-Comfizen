using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using Serilog;
using Xceed.Wpf.Toolkit;
using MessageBox = System.Windows.MessageBox;
using WindowState = System.Windows.WindowState;

namespace Comfizen
{
    public partial class MainWindow : Window
    {
        private Point? _galleryDragStartPoint;
        private Point _tabDragStartPoint;
        private Point? _queueDragStartPoint;
        private Border _lastQueueIndicator;
        private bool _isUserInteractingWithSlider = false;
        private DispatcherTimer _positionUpdateTimer;
        
        private bool _isConsoleScrolling;
        private Point _consoleScrollStartPoint;
        private double _consoleScrollStartOffset;
        
        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
            this.Closing += MainWindow_Closing;
            this.StateChanged += MainWindow_StateChanged;
            
            if (DataContext is MainViewModel { ConsoleLogMessages: INotifyCollectionChanged collection } vm2)
            {
                collection.CollectionChanged += ConsoleLogMessages_CollectionChanged;
                vm2.GroupNavigationRequested += OnGroupNavigationRequested;
            }
            
            PositionSlider.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(PositionSlider_PreviewMouseLeftButtonDown), true);

            _positionUpdateTimer = new DispatcherTimer();
            _positionUpdateTimer.Interval = TimeSpan.FromMilliseconds(200);
            _positionUpdateTimer.Tick += PositionUpdateTimer_Tick;
            
#if DEBUG
            // Run tests and print the report to the debug output
            var tester = new WildcardSystemTester();
            string report = tester.RunAllTests();
            Debug.WriteLine(report);
#endif
        }
        
        // Handles the start of a drag-to-scroll action on the console panel.
        private void Console_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // If the click originated from within the scrollbar, let it handle the event.
            if (e.OriginalSource is DependencyObject depObj && depObj.TryFindParent<ScrollBar>() != null)
            {
                return;
            }
    
            // If the user clicks directly on text, they probably want to select it, so we also do nothing.
            if (e.OriginalSource is TextBlock)
            {
                return;
            }

            // If the click is on the background, initiate the scroll drag.
            _isConsoleScrolling = true;
            _consoleScrollStartPoint = e.GetPosition(ConsoleScrollViewer);
            _consoleScrollStartOffset = ConsoleScrollViewer.VerticalOffset;
            ConsoleContentGrid.CaptureMouse();
            ConsoleContentGrid.Cursor = Cursors.ScrollNS;
            e.Handled = true;
        }

        // Handles the mouse movement during a drag-to-scroll action to update the scroll position.
        private void Console_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isConsoleScrolling && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPoint = e.GetPosition(ConsoleScrollViewer);
                Vector delta = currentPoint - _consoleScrollStartPoint;
                ConsoleScrollViewer.ScrollToVerticalOffset(_consoleScrollStartOffset - delta.Y);
            }
        }

        // Handles the end of a drag-to-scroll action, releasing mouse capture and resetting the cursor.
        private void Console_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isConsoleScrolling)
            {
                _isConsoleScrolling = false;
                ConsoleContentGrid.ReleaseMouseCapture();
                ConsoleContentGrid.Cursor = null;
            }
        }
        
        private void PresetPopup_Opened(object sender, EventArgs e)
        {
            if (sender is not Popup popup) return;
            
            var textBox = FindVisualChild<TextBox>(popup.Child);
            if (textBox != null)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }), DispatcherPriority.Input);
            }
        }
        
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.FullScreen.PropertyChanged += FullScreen_PropertyChanged;
                vm.ImageProcessing.PropertyChanged += ImageProcessing_PropertyChanged;
                
                var settings = vm.Settings;

                // Сначала восстанавливаем размер и позицию для "нормального" режима
                if (settings.MainWindowWidth > 100 && settings.MainWindowHeight > 100)
                {
                    this.Width = settings.MainWindowWidth;
                    this.Height = settings.MainWindowHeight;
                }

                if (settings.MainWindowLeft >= 0 && (settings.MainWindowLeft + this.Width) <= SystemParameters.VirtualScreenWidth)
                {
                    this.Left = settings.MainWindowLeft;
                }
                if (settings.MainWindowTop >= 0 && (settings.MainWindowTop + this.Height) <= SystemParameters.VirtualScreenHeight)
                {
                    this.Top = settings.MainWindowTop;
                }

                if (settings.MainWindowState == WindowState.Maximized)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        this.WindowState = WindowState.Maximized;
                    }), DispatcherPriority.Loaded);
                }
            }
        }
        
        private void ImageProcessing_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ImageProcessingViewModel.SelectedGalleryImage))
            {
                if (DataContext is MainViewModel vm && sender is ImageProcessingViewModel ipVm && ipVm.SelectedGalleryImage != null && vm.FullScreen.IsFullScreenOpen)
                {
                    // Use dispatcher to ensure UI is ready
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (lvOutputs.ItemContainerGenerator.ContainerFromItem(ipVm.SelectedGalleryImage) is ListViewItem item)
                        {
                            item.BringIntoView();
                        }
                        // If the container is not generated yet (virtualized), we can scroll to the item.
                        else
                        {
                            lvOutputs.ScrollIntoView(ipVm.SelectedGalleryImage);
                        }
                    }), DispatcherPriority.ApplicationIdle);
                }
            }
        }
        
        private void FullScreen_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FullScreenViewModel.IsPlaying))
            {
                if (DataContext is MainViewModel { FullScreen: { IsFullScreenOpen: true, CurrentFullScreenImage.Type: FileType.Video } } vm)
                {
                    if (vm.FullScreen.IsPlaying)
                    {
                        FullScreenMediaElement.Play();
                        _positionUpdateTimer.Start();
                    }
                    else
                    {
                        FullScreenMediaElement.Pause();
                        _positionUpdateTimer.Stop();
                    }
                }
            }
        }
        
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            // ADDED: If the slider compare view is open, let it handle the drag events.
            if (DataContext is MainViewModel vm && vm.SliderCompare.IsViewOpen)
            {
                // We don't set e.Handled = true here, allowing the event to continue to the child controls.
                return;
            }

            // Autoscroll logic
            const double scrollThreshold = 40.0;
            const double scrollSpeed = 8.0;
    
            Point position = e.GetPosition(ControlsScrollViewer);

            if (position.Y < scrollThreshold)
            {
                ControlsScrollViewer.ScrollToVerticalOffset(ControlsScrollViewer.VerticalOffset - scrollSpeed);
            }
            else if (position.Y > ControlsScrollViewer.ActualHeight - scrollThreshold)
            {
                ControlsScrollViewer.ScrollToVerticalOffset(ControlsScrollViewer.VerticalOffset + scrollSpeed);
            }

            // Allow drop for files OR for ImageOutput from the gallery
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(typeof(ImageOutput))
                ? DragDropEffects.Copy 
                : DragDropEffects.None;

            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            // ADDED: If the slider compare view is open, prevent the main window from handling the drop.
            if (DataContext is MainViewModel mainVm && mainVm.SliderCompare.IsViewOpen)
            {
                e.Handled = true;
                return;
            }

            if (DataContext is not MainViewModel viewModel) return;
    
            // Case 1: Drop from our own gallery
            if (e.Data.GetData(typeof(ImageOutput)) is ImageOutput imageOutput)
            {
                if (!string.IsNullOrEmpty(imageOutput.Prompt))
                {
                    try
                    {
                        var jObject = JObject.Parse(imageOutput.Prompt);
                        viewModel.ImportStateFromJObject(jObject, imageOutput.FileName);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(ex, "Failed to parse and import workflow from gallery item.");
                        MessageBox.Show(string.Format(LocalizationService.Instance["MainVM_ImportGenericError"], ex.Message), 
                            LocalizationService.Instance["MainVM_ImportFailedTitle"], 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show(LocalizationService.Instance["MainVM_ImportNoMetadataError"], 
                        LocalizationService.Instance["MainVM_ImportFailedTitle"], 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Warning);
                }
            }
            // Case 2: Drop from the file system
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                viewModel.ImportStateFromFile(files[0]);
            }
        }

        private void PositionUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (!_isUserInteractingWithSlider && FullScreenMediaElement.NaturalDuration.HasTimeSpan)
            {
                PositionSlider.Value = FullScreenMediaElement.Position.TotalSeconds;
            }
        }
        
        private void ConsoleLogMessages_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (ConsoleScrollViewer == null)
            {
                return;
            }
            
            bool isUserAtBottom = ConsoleScrollViewer.VerticalOffset >= ConsoleScrollViewer.ScrollableHeight - 5.0;
            
            if (!isUserAtBottom)
            {
                return;
            }
            
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ConsoleScrollViewer.ScrollToEnd();
            }), DispatcherPriority.Background);
        }
        
        /// <summary>
        /// Manually handles the mouse wheel scroll event for the console ListBox
        /// and redirects it to the parent ScrollViewer.
        /// </summary>
        private void ConsoleListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled)
            {
                return;
            }

            // Manually scroll the outer ScrollViewer
            ConsoleScrollViewer.ScrollToVerticalOffset(ConsoleScrollViewer.VerticalOffset - e.Delta);
            
            // Mark the event as handled to prevent the ListBox from processing it.
            e.Handled = true;
        }
        
        private void ListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!_galleryDragStartPoint.HasValue) return;
            
            var listViewItem = sender as ListViewItem;
            var listView = listViewItem?.TryFindParent<ListView>();

            if (listView != null && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (listView.SelectedItems.Count <= 1)
                {
                    if (listViewItem.DataContext is ImageOutput item)
                    {
                        var viewModel = DataContext as MainViewModel;
                        viewModel?.FullScreen.OpenFullScreenCommand.Execute(item);
                    }
                }
            }

            _galleryDragStartPoint = null;
        }
        
        private void LvOutputs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is ListView lv)
            {
                vm.ImageProcessing.SelectedItemsCount = lv.SelectedItems.Count;
            }
        }
        

        private void LvOutputs_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (sender is ListView listView)
                {
                    listView.SelectAll();
                    e.Handled = true;
                }
            }
        }
        
        private void QueueSizeUpDown_OnSpinned(object sender, SpinEventArgs e)
        {
            var upDown = sender as IntegerUpDown;
            if (upDown == null || !upDown.Value.HasValue) return;

            int currentValue = upDown.Value.Value;
            int newValue;
            int originalValue;

            if (e.Direction == SpinDirection.Increase)
            {
                originalValue = currentValue - 1;
                newValue = originalValue < 2 ? originalValue + 1 : originalValue * 2;
            }
            else // Decrease
            {
                originalValue = currentValue + 1;
                newValue = (int)Math.Ceiling(originalValue / 2.0);
            }

            if (newValue < upDown.Minimum) newValue = upDown.Minimum.Value;
            if (newValue > upDown.Maximum) newValue = upDown.Maximum.Value;
            
            upDown.Value = newValue;
            e.Handled = true;
        }
        
        private void ListViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _galleryDragStartPoint = e.GetPosition(null);
        }
        
        private void ListViewItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _galleryDragStartPoint.HasValue)
            {
                Point position = e.GetPosition(null);
                if (Math.Abs(position.X - _galleryDragStartPoint.Value.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _galleryDragStartPoint.Value.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is ListViewItem item && item.DataContext is ImageOutput imageOutput)
                    {
                        var data = new DataObject(typeof(ImageOutput), imageOutput);
                        DragDrop.DoDragDrop(item, data, DragDropEffects.Copy);
                        _galleryDragStartPoint = null; 
                    }
                }
            }
        }
        
        private async void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                foreach (var window in viewModel.UndockedWindows.Values.ToList())
                {
                    window.Close();
                }

                if (WindowState == WindowState.Maximized)
                {
                    viewModel.Settings.MainWindowState = WindowState.Maximized;
                    // For maximized, save RestoreBounds for consistent size restoration
                    viewModel.Settings.MainWindowWidth = this.RestoreBounds.Width;
                    viewModel.Settings.MainWindowHeight = this.RestoreBounds.Height;
                    viewModel.Settings.MainWindowLeft = this.RestoreBounds.Left;
                    viewModel.Settings.MainWindowTop = this.RestoreBounds.Top;
                }
                else // Normal or Minimized
                {
                    viewModel.Settings.MainWindowState = WindowState.Normal; // Always save as Normal if not Maximized
                    viewModel.Settings.MainWindowWidth = this.Width;
                    viewModel.Settings.MainWindowHeight = this.Height;
                    viewModel.Settings.MainWindowLeft = this.Left;
                    viewModel.Settings.MainWindowTop = this.Top;
                }

                await viewModel.SaveStateOnCloseAsync();
            }
            InMemoryHttpServer.Instance.Stop();

            Log.CloseAndFlush();
        }
        
        private void MediaElement_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MediaElement mediaElement) return;
            if (mediaElement.DataContext is not ImageOutput io || io.Type != FileType.Video) return;

            try
            {
                var videoUri = io.GetHttpUri();
                if (videoUri != null)
                {
                    mediaElement.Source = videoUri;
                    mediaElement.Play();
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, $"MediaElement Load Error: {io.FileName}");
            }
        }

        private void MediaElement_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is MediaElement mediaElement)
            {
                mediaElement.Stop();
                mediaElement.Close();
            }
        }
        
        private void MediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (sender is MediaElement mediaElement)
            {
                mediaElement.Position = TimeSpan.Zero;
                mediaElement.Play();
            }
        }
        
        private void FullScreenViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == FullScreenViewer || e.OriginalSource == SafeZone)
            {
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.FullScreen.CloseFullScreenCommand.Execute(null);
                }
            }
        }
        
        private void UpdateFullScreenPlayerSource(ImageOutput item)
        {
            FullScreenMediaElement.Stop();
            FullScreenMediaElement.Close();
            _positionUpdateTimer?.Stop();

            if (item?.Type == FileType.Video)
            {
                try
                {
                    var videoUri = item.GetHttpUri();
                    if (videoUri != null)
                    {
                        FullScreenMediaElement.Source = videoUri;
                        FullScreenMediaElement.Play();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, $"Failed to open video: {item?.FileName}");
                }
            }
        }

        private void RequestUpdatePlayerSource(ImageOutput newItem)
        {
            UpdateFullScreenPlayerSource(newItem);
        }

        private void FullScreenMediaElement_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            RequestUpdatePlayerSource(e.NewValue as ImageOutput);
        }

        private void FullScreenViewer_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue == true)
            {
                var vm = DataContext as MainViewModel;
                RequestUpdatePlayerSource(vm?.FullScreen.CurrentFullScreenImage);
                // Ensure focus for keyboard events, just like in SliderCompareView
                Dispatcher.BeginInvoke(new Action(() => FullScreenViewer.Focus()), DispatcherPriority.Input);
            }
            else 
            {
                RequestUpdatePlayerSource(null);
            }
        }
    
        private void FullScreenMediaElement_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (FullScreenMediaElement.DataContext is ImageOutput io)
            {
                io.Resolution = $"{FullScreenMediaElement.NaturalVideoWidth}x{FullScreenMediaElement.NaturalVideoHeight}";
            }

            if (FullScreenMediaElement.NaturalDuration.HasTimeSpan)
            {
                var totalDuration = FullScreenMediaElement.NaturalDuration.TimeSpan;
                DurationTextBlock.Text = FormatTimeSpan(totalDuration);
                PositionSlider.Maximum = totalDuration.TotalSeconds;
            
                if (DataContext is MainViewModel vm && vm.FullScreen.IsPlaying)
                {
                    _positionUpdateTimer?.Start();
                }
            }
            else
            {
                DurationTextBlock.Text = "??:??";
                PositionSlider.Maximum = 0;
            }
            UpdateVideoStretchMode();
        }
        
        private void FullScreenMediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (sender is MediaElement mediaElement)
            {
                mediaElement.Position = TimeSpan.Zero;
                mediaElement.Play();
            }
        }
        
        private void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.FullScreen.IsPlaying)
            {
                vm.FullScreen.IsPlaying = false;
            }
        
            _isUserInteractingWithSlider = true;
    
            if (sender is not Slider slider) return;
        
            if (e.OriginalSource is Thumb)
            {
                return;
            }
        
            slider.CaptureMouse();
            Point position = e.GetPosition(slider);
            double newValue = (position.X / slider.ActualWidth) * slider.Maximum;
            slider.Value = Math.Max(0, Math.Min(slider.Maximum, newValue));
            e.Handled = true;
        }

// english: Handles mouse movement when scrubbing to update the slider's position.
        private void PositionSlider_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isUserInteractingWithSlider && sender is Slider slider && slider.IsMouseCaptured)
            {
                Point position = e.GetPosition(slider);
                double newValue = (position.X / slider.ActualWidth) * slider.Maximum;
                slider.Value = Math.Max(0, Math.Min(slider.Maximum, newValue));
            }
        }

        private void PositionSlider_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.FullScreen.IsPlaying)
            {
                vm.FullScreen.IsPlaying = false;
            }
            _isUserInteractingWithSlider = true;
        }

        private void PositionSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isUserInteractingWithSlider = false;
        }

        private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider slider && slider.IsMouseCaptured)
            {
                slider.ReleaseMouseCapture();
            }
            _isUserInteractingWithSlider = false;
        }

        private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var newPosition = TimeSpan.FromSeconds(e.NewValue);
            CurrentTimeTextBlock.Text = FormatTimeSpan(newPosition);

            // This check ensures we only apply the frame-update logic while the user is dragging.
            if (!_isUserInteractingWithSlider) return;

            FullScreenMediaElement.Position = newPosition;

            // The definitive fix for live scrubbing on pause.
            if (DataContext is MainViewModel { FullScreen.IsPlaying: false })
            {
                // Start playing from the new position to load the frame
                FullScreenMediaElement.Play();

                // Immediately schedule a pause operation. It will execute after the frame is rendered.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    FullScreenMediaElement.Pause();
                }), DispatcherPriority.Input);
            }
        }

        private string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return ts.ToString(@"h\:mm\:ss");
            return ts.ToString(@"mm\:ss");
        }
        
        private void FullScreenViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateVideoStretchMode();
        }

        private void UpdateVideoStretchMode()
        {
            if (FullScreenMediaElement.NaturalVideoWidth > 0 &&
                FullScreenMediaElement.NaturalVideoHeight > 0)
            {
                double videoWidth = FullScreenMediaElement.NaturalVideoWidth;
                double videoHeight = FullScreenMediaElement.NaturalVideoHeight;
                double screenWidth = FullScreenViewer.ActualWidth;
                double screenHeight = FullScreenViewer.ActualHeight;

                if (videoWidth <= screenWidth && videoHeight <= screenHeight)
                {
                    FullScreenMediaElement.Stretch = Stretch.None;
                }
                else 
                {
                    FullScreenMediaElement.Stretch = Stretch.Uniform;
                }
            }
            else 
            {
                FullScreenMediaElement.Stretch = Stretch.Uniform;
            }
        }
        
        private void FullScreenViewer_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel) return;
    
            if (e.Key == Key.Space)
            {
                if (viewModel.FullScreen.PlayPauseCommand.CanExecute(null))
                {
                    viewModel.FullScreen.PlayPauseCommand.Execute(null);
                }
                // Mark the event as handled to prevent any default behavior.
                e.Handled = true;
            }
        }

        private async void MainWindow_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel) return;

            if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
            {
                viewModel.OpenGroupNavigationCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.NumPad6) viewModel.FullScreen.MoveNextCommand.Execute(null);
            else if (e.Key == Key.NumPad4) viewModel.FullScreen.MovePreviousCommand.Execute(null);
            else if (e.Key == Key.NumPad5)
            {
                if (viewModel.FullScreen.IsFullScreenOpen)
                {
                    if (viewModel.FullScreen.SaveCurrentImageCommand.CanExecute(null))
                    {
                        await Task.Run(() => viewModel.FullScreen.SaveCurrentImageCommand.Execute(null));
                    }
                }
                else
                {
                    // Save selected items from the gallery if not in fullscreen
                    if (lvOutputs.SelectedItems.Count > 0)
                    {
                        var command = viewModel.ImageProcessing.SaveSelectedImagesCommand;
                        var parameter = lvOutputs.SelectedItems; // Pass all selected items
                        if (command.CanExecute(parameter))
                        {
                            // The command is async, so we just execute it (fire-and-forget)
                            command.Execute(parameter);
                        }
                    }
                }
            }
            else if (e.Key == Key.Escape)
            {
                viewModel.FullScreen.CloseFullScreenCommand.Execute(null);
                this.Focus();
            }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (MainViewModel.GlobalQueueCommand != null && MainViewModel.GlobalQueueCommand.CanExecute(null))
                {
                    MainViewModel.GlobalQueueCommand.Execute(null);
                    // Mark the event as handled to prevent other controls from processing it (e.g., adding a newline to a TextBox).
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                viewModel.PasteImageCommand.Execute(null);
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }
        
        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                if (this.WindowState == WindowState.Minimized)
                {
                    foreach (var window in viewModel.UndockedWindows.Values)
                    {
                        window.Hide();
                    }
                }
                else // Restored to Normal or Maximized
                {
                    foreach (var window in viewModel.UndockedWindows.Values)
                    {
                        window.Show();
                        // If an undocked window was also minimized, restore it to Normal state.
                        if (window.WindowState == WindowState.Minimized)
                        {
                            window.WindowState = WindowState.Normal;
                        }
                    }
                }
            }
        }
        
        private void OnGroupNavigationRequested(WorkflowGroupViewModel groupVm)
        {
            var mainVm = DataContext as MainViewModel;
            if (mainVm?.SelectedTab?.WorkflowInputsController == null) return;
        
            // 1. Find the tab layout ViewModel that contains the target group.
            var targetTabLayout = mainVm.SelectedTab.WorkflowInputsController.TabLayoouts
                .FirstOrDefault(tabLayout => tabLayout.Groups.Contains(groupVm));
        
            if (targetTabLayout == null) return;
        
            // 2. Programmatically select the correct tab in the UI.
            WorkflowTabsControl.SelectedItem = targetTabLayout;
        
            // 3. Defer the rest of the logic until the UI has updated to show the new tab's content.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var itemsControl = FindVisualChild<ItemsControl>(WorkflowTabsControl);
                if (itemsControl == null) return;
        
                // 5. Find the Expander for the specific group within that ItemsControl.
                var expanderContainer = itemsControl.ItemContainerGenerator.ContainerFromItem(groupVm) as FrameworkElement;
                if (expanderContainer == null) return;
        
                var expander = FindVisualChild<Expander>(expanderContainer);
                if (expander != null)
                {
                    // 6. Ensure the group is visible and scroll it into view.
                    expander.IsExpanded = true;
                    expander.BringIntoView();
                }
            }), DispatcherPriority.ContextIdle);
        }
        
        private void GroupNavigationListBoxItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && 
                item.DataContext is WorkflowGroupViewModel groupVm &&
                this.DataContext is MainViewModel viewModel)
            {
                if (viewModel.GoToGroupCommand.CanExecute(groupVm))
                {
                    viewModel.GoToGroupCommand.Execute(groupVm);
                }
            }
        }
        
        /// <summary>
        /// Handles smart scrolling for the main controls area.
        /// It allows the parent ScrollViewer to scroll when a child control (like a TextBox or InpaintEditor)
        /// has reached its own scroll limit.
        /// </summary>
        private void ControlsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source && source.TryFindParent<AdvancedPromptEditor>() != null)
            {
                // If the event came from inside our new editor, do nothing and let the editor handle it.
                return;
            }
            
            // If the Ctrl key is held down, do nothing here.
            // This allows child controls (like InpaintEditor) to handle this specific hotkey
            // for features like changing brush size, without interference from this parent handler.
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                return;
            }
            
            if (e.Handled)
            {
                return;
            }

            var mainScroller = sender as ScrollViewer;
            if (mainScroller == null) return;

            var sourceElement = e.OriginalSource as DependencyObject;
            ScrollViewer innerScroller = null;

            // --- UNIFIED SCROLLER DETECTION LOGIC ---

            // First, check for the special case: MarkdownDisplay
            var markdownDisplay = sourceElement.TryFindParent<MarkdownDisplay>();
            if (markdownDisplay != null)
            {
                // Find the actual ScrollViewer INSIDE the FlowDocumentScrollViewer's template
                innerScroller = FindVisualChild<ScrollViewer>(markdownDisplay);
            }
            else
            {
                // For all other controls (like InpaintEditor, etc.), use the standard parent search.
                innerScroller = sourceElement.TryFindParent<ScrollViewer>();
            }

            // --- UNIFIED HANDLING LOGIC ---

            // If there's no inner scroller, or if it's the main scroller itself, exit.
            if (innerScroller == null || innerScroller.Equals(mainScroller))
            {
                return;
            }
            
            // If scrolling DOWN
            if (e.Delta < 0)
            {
                // And if the inner scroller CAN still scroll down
                if (innerScroller.VerticalOffset < innerScroller.ScrollableHeight)
                {
                    // Then do nothing, let the inner scroller handle the event.
                }
                else
                {
                    // If it's already at the bottom, take control and scroll the main scroller.
                    mainScroller.ScrollToVerticalOffset(mainScroller.VerticalOffset - e.Delta);
                    e.Handled = true;
                }
            }
            // If scrolling UP
            else if (e.Delta > 0)
            {
                // And if the inner scroller CAN still scroll up
                if (innerScroller.VerticalOffset > 0)
                {
                    // Then do nothing.
                }
                else
                {
                    // If it's already at the top, take control and scroll the main scroller.
                    mainScroller.ScrollToVerticalOffset(mainScroller.VerticalOffset - e.Delta);
                    e.Handled = true;
                }
            }
        }

        private static T FindVisualChild<T>(DependencyObject parent, string childName = null) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild && (string.IsNullOrEmpty(childName) || (child is FrameworkElement fe && fe.Name == childName)))
                {
                    return typedChild;
                }

                T childOfChild = FindVisualChild<T>(child, childName);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }
        
        private void FilterableComboBox_ItemSelected(object sender, string selectedItem)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.OpenOrSwitchToWorkflow(selectedItem);
            }
        }

        private void TabItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _tabDragStartPoint = e.GetPosition(null);
        }

        private void TabItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is TabItem sourceTab)
            {
                Point position = e.GetPosition(null);
                if (Math.Abs(position.X - _tabDragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _tabDragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sourceTab.DataContext is WorkflowTabViewModel draggedTabVM)
                    {
                        DragDrop.DoDragDrop(sourceTab, draggedTabVM, DragDropEffects.Move);
                    }
                }
            }
        }

        private void TabItem_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(WorkflowTabViewModel)))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            
            e.Handled = true;
        }

        private void TabItem_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(WorkflowTabViewModel)) is WorkflowTabViewModel sourceTabVM &&
                sender is TabItem targetTab &&
                targetTab.DataContext is WorkflowTabViewModel targetTabVM &&
                DataContext is MainViewModel mainVM)
            {
                if (sourceTabVM != targetTabVM)
                {
                    int oldIndex = mainVM.OpenTabs.IndexOf(sourceTabVM);
                    int newIndex = mainVM.OpenTabs.IndexOf(targetTabVM);

                    if (oldIndex != -1 && newIndex != -1)
                    {
                        mainVM.OpenTabs.Move(oldIndex, newIndex);
                    }
                }
            }
        }
        
        private void TabItem_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                if (sender is TabItem tabItem && 
                    tabItem.DataContext is WorkflowTabViewModel tabViewModel &&
                    DataContext is MainViewModel mainViewModel)
                {
                    mainViewModel.CloseTabCommand.Execute(tabViewModel);
                    e.Handled = true;
                }
            }
        }

        private void TabItem_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(WorkflowTabViewModel)))
            {
                if (sender is TabItem tabItem && 
                    tabItem.DataContext is WorkflowTabViewModel tabVm &&
                    DataContext is MainViewModel mainVm)
                {
                    mainVm.SelectedTab = tabVm;
                }
            }
        }

        private void WorkflowField_ButtonClick(object sender, RoutedEventArgs e)
        {
            // We only care about events that came from a Button.
            if (e.OriginalSource is not Button clickedButton)
            {
                return;
            }

            // Check if this is the specific button we're looking for by its Tag.
            if (clickedButton.Tag as string == "OpenWildcardBrowser")
            {
                // We found our button! Now execute the logic to open the browser.
        
                if (clickedButton.DataContext is not TextFieldViewModel fieldVm) return;

                // The button and textbox are siblings inside a Grid defined in the DataTemplate
                var parentGrid = clickedButton.Parent as Grid;
                var wildcardTextBox = parentGrid?.Children.OfType<TextBox>().FirstOrDefault();

                Action<string> insertAction = (textToInsert) =>
                {
                    if (wildcardTextBox != null)
                    {
                        int caretIndex = wildcardTextBox.CaretIndex;
                        wildcardTextBox.Text = wildcardTextBox.Text.Insert(caretIndex, textToInsert);
                        wildcardTextBox.CaretIndex = caretIndex + textToInsert.Length;
                        wildcardTextBox.Focus();
                    }
                    else
                    {
                        // Fallback if TextBox is somehow not found
                        fieldVm.Value += textToInsert;
                    }
                };

                var hostWindow = new Comfizen.Views.WildcardBrowser { Owner = this };
                var viewModel = new WildcardBrowserViewModel(hostWindow, insertAction);
                hostWindow.DataContext = viewModel;
                hostWindow.ShowDialog();

                // Mark the event as handled so it doesn't bubble up further.
                e.Handled = true;
            }
        }

        private void LvOutputs_KeyDown(object sender, KeyEventArgs e)
        {
            // ADDED: Handler for the Delete key on the gallery.
            if (e.Key == Key.Delete)
            {
                if (DataContext is MainViewModel vm && lvOutputs.SelectedItems.Count > 0)
                {
                    var command = vm.ImageProcessing.DeleteSelectedImagesCommand;
                    // The command expects an IList, and SelectedItems is already one.
                    var parameter = lvOutputs.SelectedItems; 
                    if (command.CanExecute(parameter))
                    {
                        command.Execute(parameter);
                    }
                }
            }
        }
        
        private void QueueItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _queueDragStartPoint = e.GetPosition(null);
        }

        private void QueueItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _queueDragStartPoint.HasValue)
            {
                Point position = e.GetPosition(null);
                if (Math.Abs(position.X - _queueDragStartPoint.Value.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _queueDragStartPoint.Value.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is ListBoxItem item && item.DataContext is QueueItemViewModel vm)
                    {
                        DragDrop.DoDragDrop(item, vm, DragDropEffects.Move);
                        _queueDragStartPoint = null;
                    }
                }
            }
        }

        private void QueueItem_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(QueueItemViewModel)))
            {
                e.Effects = DragDropEffects.Move;

                if (sender is ListBoxItem item)
                {
                    // Hide previous indicator
                    if (_lastQueueIndicator != null)
                        _lastQueueIndicator.Visibility = Visibility.Collapsed;

                    var position = e.GetPosition(item);
                    var indicator = position.Y < item.ActualHeight / 2
                        ? FindVisualChild<Border>(item, "DropIndicatorBefore")
                        : FindVisualChild<Border>(item, "DropIndicatorAfter");

                    if (indicator != null)
                    {
                        indicator.Visibility = Visibility.Visible;
                        _lastQueueIndicator = indicator;
                    }
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void QueueItem_DragLeave(object sender, DragEventArgs e)
        {
            if (_lastQueueIndicator != null)
            {
                _lastQueueIndicator.Visibility = Visibility.Collapsed;
                _lastQueueIndicator = null;
            }
        }
        
        private void QueueItem_Drop(object sender, DragEventArgs e)
        {
            if (sender is ListBoxItem targetItem && DataContext is MainViewModel viewModel)
            {
                var draggedVm = e.Data.GetData(typeof(QueueItemViewModel)) as QueueItemViewModel;
                var targetVm = targetItem.DataContext as QueueItemViewModel;

                if (draggedVm != null && targetVm != null && draggedVm != targetVm)
                {
                    var oldIndex = viewModel.PendingQueueItems.IndexOf(draggedVm);
                    var targetIndex = viewModel.PendingQueueItems.IndexOf(targetVm);

                    if (oldIndex != -1 && targetIndex != -1)
                    {
                        // Adjust index based on which indicator was visible
                        var indicator = FindVisualChild<Border>(targetItem, "DropIndicatorAfter");
                        if (indicator != null && indicator.Visibility == Visibility.Visible)
                        {
                            targetIndex++;
                        }
                        
                        if (oldIndex < targetIndex)
                        {
                            targetIndex--;
                        }

                        viewModel.PendingQueueItems.Move(oldIndex, targetIndex);
                    }
                }
            }
            
            if (_lastQueueIndicator != null)
            {
                _lastQueueIndicator.Visibility = Visibility.Collapsed;
                _lastQueueIndicator = null;
            }
            e.Handled = true;
        }
    }
}