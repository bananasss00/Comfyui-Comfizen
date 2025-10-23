using System;
using System.Collections.Specialized;
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
using Xceed.Wpf.Toolkit;

namespace Comfizen
{
    public partial class MainWindow : Window
    {
        private Point? _galleryDragStartPoint;
        private Point _tabDragStartPoint;
        private bool _isUserInteractingWithSlider = false;
        private DispatcherTimer _positionUpdateTimer;
        
        public MainWindow()
        {
            InitializeComponent();
            
            this.Closing += MainWindow_Closing;
            
            if (DataContext is MainViewModel vm && vm.ConsoleLogMessages is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged += ConsoleLogMessages_CollectionChanged;
                vm.GroupNavigationRequested += OnGroupNavigationRequested;
            }
            
            PositionSlider.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(PositionSlider_PreviewMouseLeftButtonDown), true);

            _positionUpdateTimer = new DispatcherTimer();
            _positionUpdateTimer.Interval = TimeSpan.FromMilliseconds(200);
            _positionUpdateTimer.Tick += PositionUpdateTimer_Tick;
        }
        
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.ImportStateFromFile(files[0]);
                }
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
        
        private void ListViewItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
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
                await viewModel.SaveStateOnCloseAsync();
            }
            InMemoryHttpServer.Instance.Stop();
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
                _positionUpdateTimer?.Start();
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
            _isUserInteractingWithSlider = true;
        }

        private void PositionSlider_DragStarted(object sender, DragStartedEventArgs e)
        {
            _isUserInteractingWithSlider = true;
        }

        private void PositionSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isUserInteractingWithSlider = false;
        }

        private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            FullScreenMediaElement.Position = TimeSpan.FromSeconds(PositionSlider.Value);
            _isUserInteractingWithSlider = false;
        }

        private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var newPosition = TimeSpan.FromSeconds(e.NewValue);
            CurrentTimeTextBlock.Text = FormatTimeSpan(newPosition);
            if (_isUserInteractingWithSlider)
            {
                FullScreenMediaElement.Position = newPosition;
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
            else if (e.Key == Key.NumPad5 && viewModel.FullScreen.IsFullScreenOpen) await System.Threading.Tasks.Task.Run(() => viewModel.FullScreen.SaveCurrentImageCommand.Execute(null));
            else if (e.Key == Key.Escape)
            {
                viewModel.FullScreen.CloseFullScreenCommand.Execute(null);
                this.Focus();
            }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) viewModel.QueueCommand.Execute(null);
            else if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                viewModel.PasteImageCommand.Execute(null);
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }
        
        private void OnGroupNavigationRequested(WorkflowGroupViewModel groupVm)
        {
            // --- START OF COMPLETE REWRITE OF THIS METHOD ---
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
                // 4. Find the ItemsControl inside the newly selected TabItem.
                // It will be the one whose DataContext is our target group view model.
                var tabItem = WorkflowTabsControl.ItemContainerGenerator.ContainerFromItem(targetTabLayout) as TabItem;
                if (tabItem == null) return;
        
                var itemsControl = FindVisualChild<ItemsControl>(tabItem);
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
            // --- END OF COMPLETE REWRITE ---
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

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var childOfChild = FindVisualChild<T>(child);
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
    }
}