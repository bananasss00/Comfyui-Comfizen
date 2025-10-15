using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Xceed.Wpf.Toolkit;
using MessageBox = System.Windows.MessageBox;
using WindowState = System.Windows.WindowState;

namespace Comfizen
{
    /// <summary>
    /// A user control for image inpainting and sketching.
    /// Provides two canvases (mask and sketch) on top of a source image.
    /// </summary>
    public partial class InpaintEditor : UserControl
    {
        private enum EditingMode { Mask, Sketch }
        private EditingMode _currentMode = EditingMode.Mask;
        
        private class UndoAction
        {
            public StrokeCollection Added { get; set; }
            public StrokeCollection Removed { get; set; }
        }

        private readonly Stack<UndoAction> _maskUndoStack = new Stack<UndoAction>();
        private readonly Stack<UndoAction> _maskRedoStack = new Stack<UndoAction>();
        private readonly Stack<UndoAction> _sketchUndoStack = new Stack<UndoAction>();
        private readonly Stack<UndoAction> _sketchRedoStack = new Stack<UndoAction>();
        private bool _isUndoRedoOperation = false;

        private byte[] _sourceImageBytes;
        private Color _maskDisplayColor = Colors.Red;
        private Color _sketchBrushColor = Colors.Black;
        
        private readonly bool _imageEditingEnabled;
        private readonly bool _maskEditingEnabled;
        
        private bool _isFullScreen = false;
        private Window _fullScreenWindow;
        private object _originalContent; 
        
        private bool _isPanning = false;
        private Point _panStartPoint;
        private Point _panStartOffset;
        
        // Store brush settings for each mode separately
        private double _maskBrushSize = 30.0;
        private double _maskOpacity = 1.0;
        private double _sketchBrushSize = 10.0;
        private double _sketchOpacity = 1.0;

        /// <summary>
        /// Gets a value indicating whether this editor can accept an image via drag-drop or paste.
        /// </summary>
        public bool CanAcceptImage => _imageEditingEnabled;

        public InpaintEditor() : this(imageEditingEnabled: true, maskEditingEnabled: true)
        {
        }
        
        public InpaintEditor(bool imageEditingEnabled, bool maskEditingEnabled)
        {
            InitializeComponent();
            
            _imageEditingEnabled = imageEditingEnabled;
            _maskEditingEnabled = maskEditingEnabled;

            ImageGrid.Cursor = Cursors.None;

            BrushSizeSlider.ValueChanged += OnBrushPropertyChanged;
            ZoomSlider.ValueChanged += OnBrushPropertyChanged;
            OpacitySlider.ValueChanged += OpacitySlider_OnValueChanged;

            MaskCanvas.Strokes.StrokesChanged += MaskStrokes_Changed;
            SketchCanvas.Strokes.StrokesChanged += SketchStrokes_Changed;

            ImageGrid.MouseEnter += Canvas_MouseEnter;
            ImageGrid.MouseLeave += Canvas_MouseLeave;
            ImageGrid.MouseMove += Canvas_MouseMove;
            ImageGrid.PreviewMouseRightButtonDown += Canvas_PreviewMouseRightButtonDown;
            ImageGrid.PreviewMouseRightButtonUp += Canvas_PreviewMouseRightButtonUp;
            
            ImageGrid.PreviewMouseDown += ImageGrid_PreviewMouseDown;
            ImageGrid.PreviewMouseUp += ImageGrid_PreviewMouseUp;
        }

        private InkCanvas ActiveCanvas => _currentMode == EditingMode.Mask ? MaskCanvas : SketchCanvas;
        
        /// <summary>
        /// Sets the source image for the editor from a byte array.
        /// </summary>
        /// <param name="imageBytes">The byte array of the image.</param>
        public void SetSourceImage(byte[] imageBytes)
        {
            try
            {
                _sourceImageBytes = imageBytes;
                var image = new BitmapImage();
                using (var ms = new MemoryStream(_sourceImageBytes))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = ms;
                    image.EndInit();
                }
                SourceImage.Source = image;
                
                ClearMask_Click(null, null);
                ClearSketch_Click(null, null);

                ImageGrid.Width = image.PixelWidth;
                ImageGrid.Height = image.PixelHeight;
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(LocalizationService.Instance["Inpaint_ErrorLoadingImageMessage"], ex.Message), 
                                LocalizationService.Instance["Inpaint_ErrorLoadingImageTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InpaintEditor_Loaded(object sender, RoutedEventArgs e)
        {
            ConfigureUI();
            UpdateCanvasesState();
        }
        
        private void InpaintEditor_OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_isFullScreen)
            {
                ExitFullScreen();
            }
        }
        
        private void ConfigureUI()
        {
            LoadImageButton.Visibility = _imageEditingEnabled ? Visibility.Visible : Visibility.Collapsed;
            ClearSketchButton.Visibility = _imageEditingEnabled ? Visibility.Visible : Visibility.Collapsed;
            ClearMaskButton.Visibility = _maskEditingEnabled ? Visibility.Visible : Visibility.Collapsed;
        
            if (!_imageEditingEnabled && _maskEditingEnabled)
            {
                ModeSelectionGroup.Visibility = Visibility.Collapsed;
                MaskRadioButton.IsChecked = true;
                Mode_Changed(MaskRadioButton, null);
                
                DimensionPanel.Visibility = Visibility.Visible;
                UpdateMaskCanvasSize();
            }
            else if (_imageEditingEnabled && !_maskEditingEnabled)
            {
                ModeSelectionGroup.Visibility = Visibility.Collapsed;
                if (SketchRadioButton != null)
                {
                    SketchRadioButton.IsChecked = true;
                    Mode_Changed(SketchRadioButton, null);
                }
            }
            else 
            {
                ModeSelectionGroup.Visibility = Visibility.Visible;
                MaskRadioButton.IsChecked = true;
                Mode_Changed(MaskRadioButton, null);
            }
        }

        private void Dimensions_Changed(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (!IsLoaded) return;
            UpdateMaskCanvasSize();
        }

        private void UpdateMaskCanvasSize()
        {
            ImageGrid.Width = MaskWidthUpDown.Value ?? 512;
            ImageGrid.Height = MaskHeightUpDown.Value ?? 512;
        }
        
        private void ShowAllLayers_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            UpdateCanvasesState();
        }

        private void UpdateCanvasesState()
        {
            if (!IsLoaded) return;

            bool showAll = ShowAllLayersCheckBox.IsChecked == true;

            if (showAll)
            {
                MaskCanvas.Visibility = Visibility.Visible;
                SketchCanvas.Visibility = Visibility.Visible;
            }
            else
            {
                MaskCanvas.Visibility = (_currentMode == EditingMode.Mask) ? Visibility.Visible : Visibility.Collapsed;
                SketchCanvas.Visibility = (_currentMode == EditingMode.Sketch) ? Visibility.Visible : Visibility.Collapsed;
            }

            MaskCanvas.IsHitTestVisible = (_currentMode == EditingMode.Mask);
            SketchCanvas.IsHitTestVisible = (_currentMode == EditingMode.Sketch);
        }
        
        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || sender is not RadioButton radioButton || radioButton.IsChecked != true) return;

            _currentMode = (radioButton.Tag.ToString() == "Mask") ? EditingMode.Mask : EditingMode.Sketch;

            SketchColorPanel.Visibility = (_currentMode == EditingMode.Sketch) ? Visibility.Visible : Visibility.Collapsed;
            FeatherPanel.Visibility = (_currentMode == EditingMode.Mask) ? Visibility.Visible : Visibility.Collapsed;

            UpdateCanvasesState();
            
            if (_currentMode == EditingMode.Mask)
            {
                // Suppress events while changing slider values programmatically
                BrushSizeSlider.ValueChanged -= OnBrushPropertyChanged;
                OpacitySlider.ValueChanged -= OpacitySlider_OnValueChanged;

                BrushSizeSlider.Value = _maskBrushSize;
                OpacitySlider.Value = _maskOpacity;
        
                // Restore event handlers
                BrushSizeSlider.ValueChanged += OnBrushPropertyChanged;
                OpacitySlider.ValueChanged += OpacitySlider_OnValueChanged;
            }
            else // Sketch Mode
            {
                // Suppress events while changing slider values programmatically
                BrushSizeSlider.ValueChanged -= OnBrushPropertyChanged;
                OpacitySlider.ValueChanged -= OpacitySlider_OnValueChanged;

                BrushSizeSlider.Value = _sketchBrushSize;
                OpacitySlider.Value = _sketchOpacity;
        
                // Restore event handlers
                BrushSizeSlider.ValueChanged += OnBrushPropertyChanged;
                OpacitySlider.ValueChanged += OpacitySlider_OnValueChanged;
            }

            UpdateDrawingAttributes();
            UpdateBrushCursorVisual();
            UpdateUndoRedoButtons();
        }

        private void OnBrushPropertyChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            
            if (_currentMode == EditingMode.Mask)
            {
                _maskBrushSize = BrushSizeSlider.Value;
            }
            else
            {
                _sketchBrushSize = BrushSizeSlider.Value;
            }
    
            UpdateDrawingAttributes();
            UpdateBrushCursorVisual();
        }

        private void OpacitySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            
            if (_currentMode == EditingMode.Mask)
            {
                _maskOpacity = OpacitySlider.Value;
            }
            else
            {
                _sketchOpacity = OpacitySlider.Value;
            }

            UpdateDrawingAttributes();
        }

        private void SketchColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            if (e.NewValue.HasValue)
            {
                _sketchBrushColor = e.NewValue.Value;
                UpdateDrawingAttributes();
                UpdateBrushCursorVisual();
            }
        }
        
        private void UpdateDrawingAttributes()
        {
            if (!IsLoaded) return;

            var alpha = (byte)(OpacitySlider.Value * 255);
            var baseColor = (_currentMode == EditingMode.Mask) ? _maskDisplayColor : _sketchBrushColor;
            var color = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
    
            double currentZoom = ZoomSlider.Value;
            if (currentZoom == 0) currentZoom = 1;
            double correctedBrushSize = BrushSizeSlider.Value / currentZoom;

            var brushAttrs = new DrawingAttributes
            {
                Color = color,
                Width = correctedBrushSize,
                Height = correctedBrushSize,
                IsHighlighter = false
            };
            
            var eraserShape = new EllipseStylusShape(correctedBrushSize, correctedBrushSize);

            MaskCanvas.DefaultDrawingAttributes = brushAttrs;
            SketchCanvas.DefaultDrawingAttributes = brushAttrs;
            MaskCanvas.EraserShape = eraserShape;
            SketchCanvas.EraserShape = eraserShape;
        }
        
        private void UpdateBrushCursorVisual()
        {
            if (!IsLoaded) return;

            double desiredSize = BrushSizeSlider.Value;
            double currentZoom = ZoomSlider.Value;
            if (currentZoom == 0) currentZoom = 1;
            double visualSize = desiredSize / currentZoom;

            BrushCursor.Width = visualSize;
            BrushCursor.Height = visualSize;
            var brushColor = (_currentMode == EditingMode.Mask) ? _maskDisplayColor : _sketchBrushColor;
            BrushCursor.Fill = new SolidColorBrush(brushColor) { Opacity = 0.5 };
        }
        
        private void Canvas_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            ActiveCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;

            var fakeLmbDownEvent = new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, MouseButton.Left, e.StylusDevice)
            {
                RoutedEvent = Mouse.MouseDownEvent,
                Source = e.Source
            };

            ActiveCanvas.RaiseEvent(fakeLmbDownEvent);
            e.Handled = true;
        }

        private void Canvas_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var fakeLmbUpEvent = new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, MouseButton.Left, e.StylusDevice)
            {
                RoutedEvent = Mouse.MouseUpEvent,
                Source = e.Source
            };

            ActiveCanvas.RaiseEvent(fakeLmbUpEvent);
            ActiveCanvas.EditingMode = InkCanvasEditingMode.Ink;
            e.Handled = true;
        }

        private void Canvas_MouseEnter(object sender, MouseEventArgs e)
        {
            if (e.RightButton != MouseButtonState.Pressed && e.MiddleButton != MouseButtonState.Pressed)
            {
                BrushCursor.Visibility = Visibility.Visible;
            }
        }

        private void Canvas_MouseLeave(object sender, MouseEventArgs e)
        {
            BrushCursor.Visibility = Visibility.Collapsed;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            // --- NEW: Eyedropper color preview logic ---
            if (EyedropperToggle.IsChecked == true && SourceImage.Source != null)
            {
                BrushCursor.Visibility = Visibility.Visible;
                Point pos = e.GetPosition(ImageGrid);
                double radius = BrushCursor.Width / 2.0;
                BrushCursorTransform.X = pos.X - radius;
                BrushCursorTransform.Y = pos.Y - radius;

                Point posOnImage = e.GetPosition(SourceImage);
                if (SourceImage.Source is BitmapSource bmpSource &&
                    posOnImage.X >= 0 && posOnImage.Y >= 0 &&
                    posOnImage.X < bmpSource.PixelWidth && posOnImage.Y < bmpSource.PixelHeight)
                {
                    try
                    {
                        var bytesPerPixel = (bmpSource.Format.BitsPerPixel + 7) / 8;
                        if (bytesPerPixel < 3) return; // Not enough color info
                        
                        var pixelData = new byte[bytesPerPixel];
                        bmpSource.CopyPixels(new Int32Rect((int)posOnImage.X, (int)posOnImage.Y, 1, 1), pixelData, bytesPerPixel, 0);
                        
                        var pickedColor = Color.FromArgb(255, pixelData[2], pixelData[1], pixelData[0]); // BGRA or BGR -> RGB
                        BrushCursor.Fill = new SolidColorBrush(pickedColor) { Opacity = 0.75 };
                    }
                    catch (Exception)
                    {
                        // Silently ignore formats we can't handle to prevent crashes
                    }
                }
                return; // Prevent other mouse move logic from running
            }
            
            if (_isPanning && e.MiddleButton == MouseButtonState.Pressed)
            {
                if (EditorScrollViewer == null) return;
                
                Point currentPoint = e.GetPosition(EditorScrollViewer);
                Vector delta = currentPoint - _panStartPoint;

                EditorScrollViewer.ScrollToHorizontalOffset(_panStartOffset.X - delta.X);
                EditorScrollViewer.ScrollToVerticalOffset(_panStartOffset.Y - delta.Y);
                return;
            }
            
            if (e.RightButton == MouseButtonState.Pressed)
            {
                BrushCursor.Visibility = Visibility.Collapsed;
                return;
            }
            
            BrushCursor.Visibility = Visibility.Visible;
            Point finalPos = e.GetPosition(ImageGrid);
            double finalRadius = BrushCursor.Width / 2.0;
            BrushCursorTransform.X = finalPos.X - finalRadius;
            BrushCursorTransform.Y = finalPos.Y - finalRadius;
        }
        
        // --- NEW: Handle Ctrl+MouseWheel to change brush size ---
        /// <summary>
        /// Handles the mouse wheel scroll event on the image grid to change brush size.
        /// </summary>
        private void ImageGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                double step = 5;
                if (e.Delta > 0)
                {
                    BrushSizeSlider.Value += step;
                }
                else if (e.Delta < 0)
                {
                    BrushSizeSlider.Value -= step;
                }
                
                e.Handled = true;
            }
        }

        private void ImageGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                if (EditorScrollViewer == null) return;

                _isPanning = true;
                _panStartPoint = e.GetPosition(EditorScrollViewer);
                _panStartOffset = new Point(EditorScrollViewer.HorizontalOffset, EditorScrollViewer.VerticalOffset);
                
                ImageGrid.CaptureMouse(); 
                ImageGrid.Cursor = Cursors.ScrollAll;
                BrushCursor.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }

        private void ImageGrid_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && _isPanning)
            {
                _isPanning = false;
                ImageGrid.ReleaseMouseCapture();
                ImageGrid.Cursor = Cursors.None;
                e.Handled = true;
            }
        }

        public static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            return parent ?? FindParent<T>(parentObject);
        }
        
        private void MaskStrokes_Changed(object sender, StrokeCollectionChangedEventArgs e)
        {
            if (_isUndoRedoOperation) return;
            var action = new UndoAction { Added = e.Added, Removed = e.Removed };
            _maskUndoStack.Push(action);
            _maskRedoStack.Clear();
            UpdateUndoRedoButtons();
        }

        private void SketchStrokes_Changed(object sender, StrokeCollectionChangedEventArgs e)
        {
            if (_isUndoRedoOperation) return;
            var action = new UndoAction { Added = e.Added, Removed = e.Removed };
            _sketchUndoStack.Push(action);
            _sketchRedoStack.Clear();
            UpdateUndoRedoButtons();
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            var activeUndoStack = (_currentMode == EditingMode.Mask) ? _maskUndoStack : _sketchUndoStack;
            var activeRedoStack = (_currentMode == EditingMode.Mask) ? _maskRedoStack : _sketchRedoStack;

            if (activeUndoStack.Count == 0) return;

            var lastAction = activeUndoStack.Pop();
            _isUndoRedoOperation = true;
            
            ActiveCanvas.Strokes.Remove(lastAction.Added);
            ActiveCanvas.Strokes.Add(lastAction.Removed);

            _isUndoRedoOperation = false;
            activeRedoStack.Push(lastAction);
            UpdateUndoRedoButtons();
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            var activeUndoStack = (_currentMode == EditingMode.Mask) ? _maskUndoStack : _sketchUndoStack;
            var activeRedoStack = (_currentMode == EditingMode.Mask) ? _maskRedoStack : _sketchRedoStack;
            
            if (activeRedoStack.Count == 0) return;

            var actionToRedo = activeRedoStack.Pop();
            _isUndoRedoOperation = true;

            ActiveCanvas.Strokes.Add(actionToRedo.Added);
            ActiveCanvas.Strokes.Remove(actionToRedo.Removed);
            
            _isUndoRedoOperation = false;
            activeUndoStack.Push(actionToRedo);
            UpdateUndoRedoButtons();
        }

        private void UpdateUndoRedoButtons()
        {
            var activeUndoStack = (_currentMode == EditingMode.Mask) ? _maskUndoStack : _sketchUndoStack;
            var activeRedoStack = (_currentMode == EditingMode.Mask) ? _maskRedoStack : _sketchRedoStack;

            UndoButton.IsEnabled = activeUndoStack.Any();
            RedoButton.IsEnabled = activeRedoStack.Any();
        }
        
        private void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (EyedropperToggle.IsChecked != true || SourceImage.Source == null) return;
            
            e.Handled = true;
            
            Point pos = e.GetPosition(SourceImage);
            if (SourceImage.Source is not BitmapSource bmpSource) return;

            if (pos.X < 0 || pos.Y < 0 || pos.X >= bmpSource.PixelWidth || pos.Y >= bmpSource.PixelHeight) return;

            var bytesPerPixel = (bmpSource.Format.BitsPerPixel + 7) / 8;
            var pixelData = new byte[bytesPerPixel];
            bmpSource.CopyPixels(new Int32Rect((int)pos.X, (int)pos.Y, 1, 1), pixelData, bytesPerPixel, 0);

            var pickedColor = Color.FromArgb(255, pixelData[2], pixelData[1], pixelData[0]);
            
            SketchColorPicker.SelectedColor = pickedColor;
            EyedropperToggle.IsChecked = false;
        }

        private void LoadImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All Files|*.*" };
            if (dialog.ShowDialog() == true)
            {
                LoadImageFromFile(dialog.FileName);
            }
        }

        private void ClearMask_Click(object sender, RoutedEventArgs e)
        {
            MaskCanvas.Strokes.Clear();
            _maskUndoStack.Clear();
            _maskRedoStack.Clear();
            UpdateUndoRedoButtons();
        }
        
        private void ClearSketch_Click(object sender, RoutedEventArgs e)
        {
            SketchCanvas.Strokes.Clear();
            _sketchUndoStack.Clear();
            _sketchRedoStack.Clear();
            UpdateUndoRedoButtons();
        }
        
        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(typeof(ImageOutput))
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void Grid_Drop(object sender, DragEventArgs e)
        {
            if (!_imageEditingEnabled) return;

            if (e.Data.GetData(typeof(ImageOutput)) is ImageOutput imageOutput && imageOutput.ImageBytes != null)
            {
                SetSourceImage(imageOutput.ImageBytes);
            }
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                LoadImageFromFile(files[0]);
            }
        }

        private void LoadImageFromFile(string filePath)
        {
            var validExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };
            if (!validExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant())) return;

            SetSourceImage(File.ReadAllBytes(filePath));
        }
        
        private void FullScreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isFullScreen)
                ExitFullScreen();
            else
                EnterFullScreen();
        }

        private void EnterFullScreen()
        {
            if (_isFullScreen) return;

            _originalContent = this.Content;
            this.Content = null;

            _fullScreenWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                WindowState = WindowState.Maximized,
                Topmost = true,
                Background = (SolidColorBrush)Application.Current.Resources["PrimaryBackground"],
                Title = LocalizationService.Instance["Inpaint_TitleFullscreen"]
            };

            _fullScreenWindow.Content = _originalContent;

            _fullScreenWindow.PreviewKeyDown += (s, e) => {
                if (e.Key == Key.Escape)
                {
                    // If eyedropper is active, let the RootGrid's handler deal with it.
                    // By not handling the event here, we allow it to continue to the RootGrid.
                    if (EyedropperToggle.IsChecked == true)
                    {
                        return;
                    }
            
                    // If eyedropper is NOT active, exit full screen and stop the event.
                    e.Handled = true;
                    ExitFullScreen();
                }
            };

            FullScreenButton.Content = "\uE73F"; 
            FullScreenButton.ToolTip = LocalizationService.Instance["Inpaint_ExitFullscreen"];
            _isFullScreen = true;

            _fullScreenWindow.Show();
            ActiveCanvas.Focus();
        }

        private void ExitFullScreen()
        {
            if (!_isFullScreen || _fullScreenWindow == null) return;
            
            var contentToRestore = _fullScreenWindow.Content;
            _fullScreenWindow.Content = null;
            
            this.Content = contentToRestore;
            _originalContent = null; 
            
            _fullScreenWindow.Close();
            _fullScreenWindow = null;
            
            FullScreenButton.Content = "\uE740";
            FullScreenButton.ToolTip = LocalizationService.Instance["Inpaint_Fullscreen"];
            _isFullScreen = false;
        }
        
        // --- NEW: Handle Escape key press to cancel eyedropper ---
        /// <summary>
        /// Handles key presses for the entire control, specifically to cancel the eyedropper.
        /// </summary>
        private void RootGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && EyedropperToggle.IsChecked == true)
            {
                EyedropperToggle.IsChecked = false;
                e.Handled = true;
            }
        }

        // --- NEW: Update brush cursor when eyedropper is deactivated ---
        /// <summary>
        /// Restores the normal brush cursor visual when the eyedropper is turned off.
        /// </summary>
        private void EyedropperToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateBrushCursorVisual();
        }

        /// <summary>
        /// Exports the source image combined with the sketch canvas as a Base64 string.
        /// If no sketch is present, returns the original source image.
        /// </summary>
        /// <returns>A Base64 encoded PNG string, or null if no source image is available.</returns>
        public string GetImageAsBase64()
        {
            if (!_imageEditingEnabled) return null;
            
            if (_sourceImageBytes == null) return null;

            if (SketchCanvas.Strokes.Count == 0)
            {
                return Convert.ToBase64String(_sourceImageBytes);
            }

            if (SourceImage.Source is not BitmapSource bmpSource) return null;
            
            var originalSketchVisibility = SketchCanvas.Visibility;
            try
            {
                SketchCanvas.Visibility = Visibility.Visible;
                SketchCanvas.Measure(new Size(ImageGrid.ActualWidth, ImageGrid.ActualHeight));
                SketchCanvas.Arrange(new Rect(0, 0, ImageGrid.ActualWidth, ImageGrid.ActualHeight));

                int width = bmpSource.PixelWidth;
                int height = bmpSource.PixelHeight;

                var drawingVisual = new DrawingVisual();
                using (var drawingContext = drawingVisual.RenderOpen())
                {
                    drawingContext.DrawImage(bmpSource, new Rect(0, 0, width, height));
                
                    if (SketchCanvas.ActualWidth > 0 && SketchCanvas.ActualHeight > 0)
                    {
                        double scaleX = width / SketchCanvas.ActualWidth;
                        double scaleY = height / SketchCanvas.ActualHeight;
                        drawingContext.PushTransform(new ScaleTransform(scaleX, scaleY));
                        SketchCanvas.Strokes.Draw(drawingContext);
                        drawingContext.Pop();
                    }
                }

                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(drawingVisual);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
            finally
            {
                SketchCanvas.Visibility = originalSketchVisibility;
            }
        }

        /// <summary>
        /// Exports the mask canvas as a grayscale Base64 PNG string.
        /// </summary>
        /// <returns>A Base64 encoded PNG string of the mask, or null if the mask is empty.</returns>
        public string GetMaskAsBase64()
        {
            if (!_maskEditingEnabled) return null;
            
            if (MaskCanvas.Strokes.Count == 0) return null;

            int width, height;
            
            if (SourceImage.Source is BitmapSource bmpSource)
            {
                width = bmpSource.PixelWidth;
                height = bmpSource.PixelHeight;
            }
            else
            {
                width = MaskWidthUpDown.Value ?? 512;
                height = MaskHeightUpDown.Value ?? 512;
            }

            var originalMaskVisibility = MaskCanvas.Visibility;
            try
            {
                MaskCanvas.Visibility = Visibility.Visible;
                MaskCanvas.Measure(new Size(ImageGrid.ActualWidth, ImageGrid.ActualHeight));
                MaskCanvas.Arrange(new Rect(0, 0, ImageGrid.ActualWidth, ImageGrid.ActualHeight));
                
                if (MaskCanvas.ActualWidth <= 0 || MaskCanvas.ActualHeight <= 0) return null;
                
                var tempCanvas = new InkCanvas { Background = Brushes.Black };
                var strokesCopy = MaskCanvas.Strokes.Clone();
                foreach (var stroke in strokesCopy)
                {
                    var originalAttrs = stroke.DrawingAttributes;
                    originalAttrs.Color = Color.FromArgb(originalAttrs.Color.A, 255, 255, 255);
                    stroke.DrawingAttributes = originalAttrs;
                }
                tempCanvas.Strokes = strokesCopy;

                double scaleX = width / MaskCanvas.ActualWidth;
                double scaleY = height / MaskCanvas.ActualHeight;
                tempCanvas.LayoutTransform = new ScaleTransform(scaleX, scaleY);
            
                tempCanvas.Effect = new BlurEffect { Radius = FeatherSlider.Value * scaleX };

                var size = new Size(width, height);
                tempCanvas.Measure(size);
                tempCanvas.Arrange(new Rect(size));
            
                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(tempCanvas);

                var grayBitmap = new FormatConvertedBitmap(rtb, PixelFormats.Gray8, null, 0);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(grayBitmap));
                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
            finally
            {
                MaskCanvas.Visibility = originalMaskVisibility;
            }
        }
    }
}