using System;
using System.Collections.Specialized;
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
        private CancellationTokenSource _playerCts;
        private Point _tabDragStartPoint;
        private bool _isUserInteractingWithSlider = false;

        public MainWindow()
        {
            InitializeComponent();
            this.Closing += MainWindow_Closing;
            
            if (DataContext is MainViewModel vm && vm.ConsoleLogMessages is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged += ConsoleLogMessages_CollectionChanged;
            }
            
            PositionSlider.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(PositionSlider_PreviewMouseLeftButtonDown), true);

            Unosquare.FFME.Library.FFmpegDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
        }
        
        private void ConsoleLogMessages_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (ConsoleScrollViewer != null)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ConsoleScrollViewer.ScrollToEnd();
                }), DispatcherPriority.Background);
            }
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
                        viewModel?.SelectedTab?.FullScreen.OpenFullScreenCommand.Execute(item);
                    }
                }
            }

            _galleryDragStartPoint = null;
        }
        
        private void LvOutputs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.SelectedTab != null && sender is ListView lv)
            {
                vm.SelectedTab.ImageProcessing.SelectedItemsCount = lv.SelectedItems.Count;
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
        
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.SaveStateOnClose();
            }
        }
        
        private async void MediaElement_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Unosquare.FFME.MediaElement mediaElement) return;

            if (mediaElement.DataContext is ImageOutput io && io.Type == FileType.Video)
            {
                var mediaStream = io.GetMediaStream();
                if (mediaStream != null)
                {
                    try
                    {
                        await mediaElement.Open(mediaStream);
                        await mediaElement.Play();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"FFME Load Error: {ex.Message}");
                    }
                }
            }
        }

        private async void MediaElement_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is Unosquare.FFME.MediaElement mediaElement)
            {
                await mediaElement.Stop();
                await mediaElement.Close();
            }
        }
        
        private void FullScreenViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == FullScreenViewer || e.OriginalSource == SafeZone)
            {
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.SelectedTab?.FullScreen.CloseFullScreenCommand.Execute(null);
                }
            }
        }
        
        private async Task UpdateFullScreenPlayerSource(ImageOutput item, CancellationToken token)
        {
            await FullScreenMediaElement.Stop();
            await FullScreenMediaElement.Close();

            if (token.IsCancellationRequested) return;

            if (item?.Type == FileType.Video)
            {
                var mediaStream = item.GetMediaStream();
                if (mediaStream != null)
                {
                    try
                    {
                        await FullScreenMediaElement.Open(mediaStream);
                        
                        if (token.IsCancellationRequested)
                        {
                            await FullScreenMediaElement.Close();
                            return;
                        }

                        await FullScreenMediaElement.Play();
                    }
                    catch (Exception ex)
                    {
                        if (!token.IsCancellationRequested)
                        {
                            System.Diagnostics.Debug.WriteLine($"FFME FullScreen Error: {ex.Message}");
                        }
                    }
                }
            }
        }

        private async void RequestUpdatePlayerSource(ImageOutput newItem)
        {
            _playerCts?.Cancel();
            _playerCts = new CancellationTokenSource();
            var currentToken = _playerCts.Token;

            try
            {
                await UpdateFullScreenPlayerSource(newItem, currentToken);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("Player update task was cancelled.");
            }
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
                RequestUpdatePlayerSource(vm?.SelectedTab?.FullScreen.CurrentFullScreenImage);
            }
            else 
            {
                RequestUpdatePlayerSource(null);
            }
        }
        
        private void FullScreenMediaElement_MediaOpened(object sender, Unosquare.FFME.Common.MediaOpenedEventArgs e)
        {
            if (FullScreenMediaElement.DataContext is ImageOutput io)
            {
                io.Resolution = $"{FullScreenMediaElement.NaturalVideoWidth}x{FullScreenMediaElement.NaturalVideoHeight}";
            }

            if (FullScreenMediaElement.NaturalDuration.HasValue)
            {
                var totalDuration = FullScreenMediaElement.NaturalDuration.Value;
                DurationTextBlock.Text = FormatTimeSpan(totalDuration);
                PositionSlider.Maximum = totalDuration.TotalSeconds;
            }
            UpdateVideoStretchMode();
        }

        private void FullScreenMediaElement_PositionChanged(object sender, Unosquare.FFME.Common.PositionChangedEventArgs e)
        {
            if (!_isUserInteractingWithSlider)
            {
                PositionSlider.Value = e.Position.TotalSeconds;
                CurrentTimeTextBlock.Text = FormatTimeSpan(e.Position);
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

        private async void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            await FullScreenMediaElement.Seek(TimeSpan.FromSeconds(PositionSlider.Value));
            _isUserInteractingWithSlider = false;
        }

        // --- НАЧАЛО ИЗМЕНЕНИЯ: Новая реализация события ValueChanged ---
        private async void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Перематываем видео, только если пользователь взаимодействует со слайдером
            if (_isUserInteractingWithSlider)
            {
                var newPosition = TimeSpan.FromSeconds(e.NewValue);
                // Обновляем текстовое поле времени немедленно для лучшего отклика
                CurrentTimeTextBlock.Text = FormatTimeSpan(newPosition);
                // Отправляем команду на перемотку
                await FullScreenMediaElement.Seek(newPosition);
            }
        }
        // --- КОНЕЦ ИЗМЕНЕНИЯ ---

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
            if (DataContext is not MainViewModel viewModel || viewModel.SelectedTab == null) return;
            
            if (e.Key == Key.NumPad6) viewModel.SelectedTab.FullScreen.MoveNextCommand.Execute(null);
            else if (e.Key == Key.NumPad4) viewModel.SelectedTab.FullScreen.MovePreviousCommand.Execute(null);
            else if (e.Key == Key.NumPad5 && viewModel.SelectedTab.FullScreen.IsFullScreenOpen) await System.Threading.Tasks.Task.Run(() => viewModel.SelectedTab.FullScreen.SaveCurrentImageCommand.Execute(null));
            else if (e.Key == Key.Escape)
            {
                viewModel.SelectedTab.FullScreen.CloseFullScreenCommand.Execute(null);
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
    }
}