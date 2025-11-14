using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;
using Serilog.Events;
using Serilog.Filters;

namespace Comfizen
{
    /// <summary>
    /// Contains helper methods for traversing the visual tree.
    /// </summary>
    public static class VisualTreeHelpers
    {
        /// <summary>
        /// Finds the first parent of a given type in the visual tree.
        /// </summary>
        /// <typeparam name="T">The type of the parent to find.</typeparam>
        /// <param name="child">The starting dependency object.</param>
        /// <returns>The found parent or null if not found.</returns>
        public static T TryFindParent2<T>(this DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = GetParent(child); // Use the new helper method
            if (parentObject == null) return null;
            T parent = parentObject as T;
            return parent ?? TryFindParent2<T>(parentObject);
        }

        // Add this new helper method inside the VisualTreeHelpers class
        private static DependencyObject GetParent(DependencyObject child)
        {
            if (child == null) return null;

            // ContentElement (like elements in FlowDocument) uses LogicalTreeHelper
            if (child is ContentElement contentElement)
            {
                DependencyObject parent = LogicalTreeHelper.GetParent(contentElement);
                if (parent != null) return parent;

                FrameworkContentElement fce = contentElement as FrameworkContentElement;
                return fce?.Parent;
            }

            // For Visual elements, use VisualTreeHelper
            return VisualTreeHelper.GetParent(child);
        }
        
        public static T GetVisualParent<T>(this DependencyObject child) where T : DependencyObject
        {
            var parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return GetVisualParent<T>(parentObject);
        }
    }
    
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Initializes a new instance of the App class.
        /// </summary>
        public App()
        {
            #if RELEASE
            SetupGlobalExceptionHandling();
            #endif
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Start the in-memory server for video playback.
            InMemoryHttpServer.Instance.Start();
            
            // 1. Load settings first.
            var settings = SettingsService.Instance.Settings;

            // 2. Set the application language based on saved settings.
            LocalizationService.Instance.SetLanguage(settings.Language);

            // 3. Now, create and show the main window.
            var mainWindow = new MainWindow();
            
            // Get the ViewModel instance from the window's DataContext.
            if (mainWindow.DataContext is MainViewModel mainViewModel)
            {
                var consoleLogService = mainViewModel.GetConsoleLogService();
                
                // --- START OF CHANGE: Advanced Serilog Configuration ---
                var logConfig = new LoggerConfiguration()
                        .MinimumLevel.Debug()
                        .Enrich.FromLogContext();
                    
                logConfig.WriteTo.Logger(lc => lc
                    .Filter.ByExcluding(e => e.Properties.ContainsKey("ConsoleOnly"))
                    .WriteTo.File(
                        Path.Combine("logs", "log-.txt"),
                        rollingInterval: RollingInterval.Day,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                    ));

                if (consoleLogService != null)
                {
                    logConfig.WriteTo.Sink(new ConsoleLogServiceSink(consoleLogService));
                }

                Log.Logger = logConfig.CreateLogger();
            }
            else
            {
                // Fallback configuration if ViewModel is not found (should not happen in normal operation).
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File(Path.Combine("logs", "log-.txt"), rollingInterval: RollingInterval.Day)
                    .CreateLogger();
                
                Log.Warning("MainViewModel not found during startup, UI console logging will be disabled.");
            }
            mainWindow.Show();
        }
        
        private void TextBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(typeof(ImageOutput))
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void TextBox_Drop(object sender, DragEventArgs e)
        {
            if (sender is not TextBox textBox || textBox.DataContext is not TextFieldViewModel viewModel) return;

            byte[] imageBytes = null;
            if (e.Data.GetData(typeof(ImageOutput)) is ImageOutput imageOutput)
            {
                imageBytes = imageOutput.ImageBytes;
            }
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                imageBytes = GetImageBytesFromFile(files[0]);
            }

            if (imageBytes != null)
            {
                viewModel.UpdateWithImageData(imageBytes);
                e.Handled = true;
            }
        }

        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox || textBox.DataContext is not TextFieldViewModel viewModel) return;

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
            {
                byte[] imageBytes = GetImageBytesFromClipboard();
                if (imageBytes != null)
                {
                    viewModel.UpdateWithImageData(imageBytes);
                    e.Handled = true;
                }
            }
        }

        private void WildcardTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox || textBox.DataContext is not TextFieldViewModel viewModel) return;

            // --- Weighting Hotkey Logic ---
            // Check if the hotkey is for weighting (Ctrl+Up/Down) and the field type is correct.
            if (viewModel.Type == FieldType.WildcardSupportPrompt && Keyboard.Modifiers == ModifierKeys.Control &&
                (e.Key == Key.Up || e.Key == Key.Down))
            {
                // Prevent the default cursor movement.
                e.Handled = true;

                var fullText = textBox.Text;
                var selectionStart = textBox.SelectionStart;
                var selectionLength = textBox.SelectionLength;
                var selectedText = textBox.SelectedText;

                var regex = new Regex(@"^\((.+):([\d\.]+)\)$", RegexOptions.Singleline);

                // --- SCENARIO 1: Text is selected ---
                if (selectionLength > 0)
                {
                    var match = regex.Match(selectedText);
                    if (match.Success)
                    {
                        // Case 1a: The selection is already a valid (text:weight) block. Modify it.
                        var baseText = match.Groups[1].Value;
                        double.TryParse(match.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture,
                            out var currentWeight);

                        var delta = e.Key == Key.Up ? 0.05 : -0.05;
                        var newWeight = Math.Round(currentWeight + delta, 2);

                        var replacementText = Math.Abs(newWeight - 1.0) < 0.01
                            ? baseText
                            : $"({baseText}:{newWeight.ToString("0.0#", CultureInfo.InvariantCulture)})";

                        textBox.SelectedText = replacementText;
                        textBox.Select(selectionStart, replacementText.Length); // Reselect the new block
                    }
                    else
                    {
                        // Case 1b: The selection is plain text. Create a new weight block.
                        if (selectedText.Contains('(') || selectedText.Contains(')')) return;

                        var initialWeight = e.Key == Key.Up ? 1.05 : 0.95;
                        var replacementText =
                            $"({selectedText}:{initialWeight.ToString("0.0#", CultureInfo.InvariantCulture)})";

                        textBox.SelectedText = replacementText;
                        textBox.Select(selectionStart, replacementText.Length); // Select the new block
                    }
                }
                // --- SCENARIO 2: No text is selected, just a cursor ---
                else
                {
                    var caretIndex = textBox.CaretIndex;
                    int startParenIndex = -1, endParenIndex = -1;
                    var balance = 0;

                    for (var i = caretIndex - 1; i >= 0; i--)
                        if (fullText[i] == ')') balance++;
                        else if (fullText[i] == '(')
                            if (--balance < 0)
                            {
                                startParenIndex = i;
                                break;
                            }

                    if (startParenIndex != -1)
                    {
                        balance = 0;
                        for (var i = startParenIndex; i < fullText.Length; i++)
                            if (fullText[i] == '(') balance++;
                            else if (fullText[i] == ')')
                                if (--balance == 0)
                                {
                                    endParenIndex = i;
                                    break;
                                }
                    }

                    if (startParenIndex != -1 && endParenIndex != -1)
                    {
                        var blockText = fullText.Substring(startParenIndex, endParenIndex - startParenIndex + 1);
                        var match = regex.Match(blockText);

                        if (match.Success)
                        {
                            // Case 2a: The cursor is inside a valid weight block. Modify it.
                            var baseText = match.Groups[1].Value;
                            double.TryParse(match.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture,
                                out var currentWeight);

                            var delta = e.Key == Key.Up ? 0.05 : -0.05;
                            var newWeight = Math.Round(currentWeight + delta, 2);

                            var replacementText = Math.Abs(newWeight - 1.0) < 0.01
                                ? baseText
                                : $"({baseText}:{newWeight.ToString("0.0#", CultureInfo.InvariantCulture)})";

                            textBox.Select(startParenIndex, blockText.Length);
                            textBox.SelectedText = replacementText;

                            var newCaretIndex = replacementText.StartsWith("(")
                                ? startParenIndex + replacementText.Length - 1
                                : startParenIndex + replacementText.Length;
                            textBox.CaretIndex = newCaretIndex;
                            return; // Done
                        }
                    }

                    // --- START: New Logic for Word Under Cursor ---
                    // Case 2b: No surrounding block found. Find the word at the cursor.
                    char[] separators = { ' ', '\n', '\r', '\t', ',', '(', ')' };

                    // Find start of the word
                    var wordStartIndex = caretIndex;
                    while (wordStartIndex > 0 && Array.IndexOf(separators, fullText[wordStartIndex - 1]) == -1)
                        wordStartIndex--;

                    // Find end of the word
                    var wordEndIndex = caretIndex;
                    while (wordEndIndex < fullText.Length && Array.IndexOf(separators, fullText[wordEndIndex]) == -1)
                        wordEndIndex++;

                    if (wordStartIndex < wordEndIndex)
                    {
                        var currentWord = fullText.Substring(wordStartIndex, wordEndIndex - wordStartIndex);
                        var initialWeight = e.Key == Key.Up ? 1.05 : 0.95;
                        var replacementText =
                            $"({currentWord}:{initialWeight.ToString("0.0#", CultureInfo.InvariantCulture)})";

                        textBox.Select(wordStartIndex, currentWord.Length);
                        textBox.SelectedText = replacementText;
                        textBox.Select(wordStartIndex, replacementText.Length); // Select the new block
                    }
                    // --- END: New Logic for Word Under Cursor ---
                }
            }
        }
        
        private void PresetPopup_Opened(object sender, EventArgs e)
        {
            if (sender is Popup popup && popup.Child is DependencyObject child)
            {
                var textBox = FindVisualChild<TextBox>(child, "PresetNameTextBox");
                if (textBox != null)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        textBox.Focus();
                        textBox.SelectAll();
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
            }
        }

        public static T FindVisualChild<T>(DependencyObject parent, string childName = null) where T : DependencyObject
        {
            if (parent == null) return null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T typedChild)
                {
                    if (string.IsNullOrEmpty(childName) || (child is FrameworkElement fe && fe.Name == childName))
                    {
                        return typedChild;
                    }
                }

                var result = FindVisualChild<T>(child, childName);
                if (result != null)
                    return result;
            }
            return null;
        }

        private byte[] GetImageBytesFromClipboard()
        {
            if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image != null)
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    using var ms = new MemoryStream();
                    encoder.Save(ms);
                    return ms.ToArray();
                }
            }
            else if (Clipboard.ContainsFileDropList())
            {
                var filePaths = Clipboard.GetFileDropList();
                if (filePaths != null && filePaths.Count > 0)
                {
                    return GetImageBytesFromFile(filePaths[0]);
                }
            }
            return null;
        }

        private byte[] GetImageBytesFromFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
            if (imageExtensions.Contains(extension))
            {
                try
                {
                    return File.ReadAllBytes(filePath);
                }
                catch { }
            }
            return null;
        }

        void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var window = sender as Window;
            if (window == null || window.Template == null) return;

            var titleBar = window.Template.FindName("TitleBar", window) as Border;
            var minimizeButton = window.Template.FindName("MinimizeButton", window) as Button;
            var maximizeButton = window.Template.FindName("MaximizeButton", window) as Button;
            var restoreButton = window.Template.FindName("RestoreButton", window) as Button;
            var closeButton = window.Template.FindName("CloseButton", window) as Button;
            
            if (titleBar != null)
            {
                titleBar.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;
            }
            if (minimizeButton != null)
            {
                minimizeButton.Click += MinimizeButton_Click;
            }
            if (maximizeButton != null)
            {
                maximizeButton.Click += MaximizeButton_Click;
            }
            if (restoreButton != null)
            {
                restoreButton.Click += RestoreButton_Click;
            }
            if (closeButton != null)
            {
                closeButton.Click += CloseButton_Click;
            }

            var helper = new WindowInteropHelper(window);
            helper.EnsureHandle();
            var source = HwndSource.FromHwnd(helper.Handle);
            if (source != null)
            {
                source.AddHook(HookProc);
            }
        }
        
        void ListBoxItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is string)
            {
                if (item.TryFindParent<Window>()?.DataContext is WildcardBrowserViewModel vm)
                {
                    vm.InsertAndClose();
                }
            }
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(sender as DependencyObject)?.Close();
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(sender as DependencyObject);
            if (window != null) window.WindowState = WindowState.Maximized;
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(sender as DependencyObject);
            if (window != null) window.WindowState = WindowState.Normal;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(sender as DependencyObject);
            if (window != null) window.WindowState = WindowState.Minimized;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var window = Window.GetWindow(sender as DependencyObject);
            if (window == null) return;
            
            if (e.ClickCount == 2)
            {
                 window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                window.DragMove();
            }
        }

        private void SetupGlobalExceptionHandling()
        {
            AppDomain.CurrentDomain.UnhandledException +=
                (sender, args) => HandleException(args.ExceptionObject as Exception, "AppDomain.CurrentDomain.UnhandledException", true);

            DispatcherUnhandledException +=
                (sender, args) =>
                {
                    HandleException(args.Exception, "Application.Current.DispatcherUnhandledException", false);
                    args.Handled = true;
                    Shutdown(-1);
                };

            TaskScheduler.UnobservedTaskException +=
                (sender, args) =>
                {
                    HandleException(args.Exception, "TaskScheduler.UnobservedTaskException", false);
                    args.SetObserved();
                };
        }

        private void HandleException(Exception ex, string context, bool isTerminating)
        {
            Logger.Log(ex, $"Unhandled exception in {context}");

            var message = LocalizationService.Instance["App_CriticalErrorMessage"];
            var title = LocalizationService.Instance["App_CriticalErrorTitle"];
    
            if (Dispatcher.CheckAccess())
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                Dispatcher.Invoke(() => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error));
            }
    
            if (isTerminating)
            {
                Environment.Exit(1);
            }
        }
        
        /// <summary>
        /// Global handler for the wildcard browser button click.
        /// Works for both main and undocked windows.
        /// </summary>
        public void WorkflowField_ButtonClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not Button clickedButton || clickedButton.Tag as string != "OpenWildcardBrowser")
            {
                return;
            }

            if (clickedButton.DataContext is not TextFieldViewModel fieldVm) return;
            
            var parentGrid = (clickedButton.Parent as FrameworkElement)?.Parent as Grid;
            var wildcardTextBox = parentGrid?.FindName("WildcardTextBox") as TextBox;

            if (wildcardTextBox == null) return;

            int savedCaretIndex = wildcardTextBox.CaretIndex;

            Action<string> insertAction = (textToInsert) =>
            {
                wildcardTextBox.Text = wildcardTextBox.Text.Insert(savedCaretIndex, textToInsert);
                wildcardTextBox.CaretIndex = savedCaretIndex + textToInsert.Length;
                wildcardTextBox.Focus();
            };

            var ownerWindow = Window.GetWindow(clickedButton);
            var hostWindow = new Views.WildcardBrowser { Owner = ownerWindow };
            var viewModel = new WildcardBrowserViewModel(hostWindow, insertAction);
            hostWindow.DataContext = viewModel;
            hostWindow.ShowDialog();
            
            e.Handled = true;
        }
        
        /// <summary>
        /// Handles the DragDelta event from a Thumb to resize a text field,
        /// supporting both simple TextBox and the AdvancedPromptEditor.
        /// </summary>
        public void OnResizeThumbDragDelta(object sender, DragDeltaEventArgs e)
        {
            if (e.OriginalSource is not Thumb thumb) return;
        
            // The thumb's parent is the Grid that contains both editors.
            // This is a much more reliable way to find the sibling controls.
            if (thumb.Parent is not Grid parentGrid) return;
        
            // Use FindName, which is scoped to the DataTemplate's namescope,
            // making it the correct tool for this job.
            var simpleEditorTextBox = parentGrid.FindName("WildcardTextBox") as TextBox;
            var advancedEditor = parentGrid.FindName("AdvancedEditor") as AdvancedPromptEditor;
    
            if (simpleEditorTextBox != null && simpleEditorTextBox.IsVisible)
            {
                // Resize the simple TextBox
                simpleEditorTextBox.Height = Math.Max(60, simpleEditorTextBox.ActualHeight + e.VerticalChange);
            }
            else if (advancedEditor != null && advancedEditor.IsVisible)
            {
                // Resize the AdvancedPromptEditor control itself.
                advancedEditor.Height = Math.Max(150, advancedEditor.ActualHeight + e.VerticalChange);
            }
        
            e.Handled = true;
        }
        
        #region Window Resizing and Maximizing Code
        
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        private const int WM_GETMINMAXINFO = 0x0024;
        
        private const int WM_NCHITTEST = 0x0084;
        private readonly IntPtr HTLEFT = new IntPtr(10);
        private readonly IntPtr HTRIGHT = new IntPtr(11);
        private readonly IntPtr HTTOP = new IntPtr(12);
        private readonly IntPtr HTTOPLEFT = new IntPtr(13);
        private readonly IntPtr HTTOPRIGHT = new IntPtr(14);
        private readonly IntPtr HTBOTTOM = new IntPtr(15);
        private readonly IntPtr HTBOTTOMLEFT = new IntPtr(16);
        private readonly IntPtr HTBOTTOMRIGHT = new IntPtr(17);

        private IntPtr HookProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    MONITORINFO monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                    GetMonitorInfo(monitor, ref monitorInfo);
                    RECT rcWorkArea = monitorInfo.rcWork;
                    RECT rcMonitorArea = monitorInfo.rcMonitor;
                    mmi.ptMaxPosition.X = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
                    mmi.ptMaxPosition.Y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);
                    mmi.ptMaxSize.X = Math.Abs(rcWorkArea.Right - rcWorkArea.Left);
                    mmi.ptMaxSize.Y = Math.Abs(rcWorkArea.Bottom - rcWorkArea.Top);
                }
                Marshal.StructureToPtr(mmi, lParam, true);
            }

            if (msg == WM_NCHITTEST)
            {
                var window = (Window)HwndSource.FromHwnd(hwnd)?.RootVisual;
                if (window == null) return IntPtr.Zero;

                if (window.WindowState == WindowState.Maximized) return IntPtr.Zero;
                
                Point mousePos = new Point(lParam.ToInt32() & 0xFFFF, lParam.ToInt32() >> 16);
                Point windowMousePos = window.PointFromScreen(mousePos);

                int resizeBorderThickness = 8;

                bool onLeft = windowMousePos.X <= resizeBorderThickness;
                bool onRight = windowMousePos.X >= window.ActualWidth - resizeBorderThickness;
                bool onTop = windowMousePos.Y <= resizeBorderThickness;
                bool onBottom = windowMousePos.Y >= window.ActualHeight - resizeBorderThickness;

                if (onTop && onLeft) { handled = true; return HTTOPLEFT; }
                if (onTop && onRight) { handled = true; return HTTOPRIGHT; }
                if (onBottom && onLeft) { handled = true; return HTBOTTOMLEFT; }
                if (onBottom && onRight) { handled = true; return HTBOTTOMRIGHT; }
                if (onLeft) { handled = true; return HTLEFT; }
                if (onRight) { handled = true; return HTRIGHT; }
                if (onTop) { handled = true; return HTTOP; }
                if (onBottom) { handled = true; return HTBOTTOM; }
            }
            return IntPtr.Zero;
        }
        
        #endregion
    }

    /// <summary>
    /// Contains extension methods for dependency objects.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Finds the first parent of a given type in the visual tree.
        /// </summary>
        /// <typeparam name="T">The type of the parent to find.</typeparam>
        /// <param name="child">The starting dependency object.</param>
        /// <returns>The found parent or null if not found.</returns>
        public static T TryFindParent<T>(this DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = GetParent(child); // Use the new helper method
            if (parentObject == null) return null;
            T parent = parentObject as T;
            return parent ?? TryFindParent<T>(parentObject);
        }

        // Add this new helper method inside the Extensions class
        private static DependencyObject GetParent(DependencyObject child)
        {
            if (child == null) return null;

            if (child is ContentElement contentElement)
            {
                DependencyObject parent = LogicalTreeHelper.GetParent(contentElement);
                if (parent != null) return parent;

                FrameworkContentElement fce = contentElement as FrameworkContentElement;
                return fce?.Parent;
            }

            return VisualTreeHelper.GetParent(child);
        }
    }
}