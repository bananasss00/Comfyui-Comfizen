using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            return parent ?? TryFindParent2<T>(parentObject);
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
            // 1. Load settings first.
            var settingsService = new SettingsService();
            var settings = settingsService.LoadSettings();

            // 2. Set the application language based on saved settings.
            LocalizationService.Instance.SetLanguage(settings.Language);

            // 3. Now, create and show the main window.
            var mainWindow = new MainWindow();
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

            var message = "A critical error has occurred. The application will now close. " +
                          "Details have been written to the log file in the 'logs' folder.";
            
            if (Dispatcher.CheckAccess())
            {
                MessageBox.Show(message, "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                Dispatcher.Invoke(() => MessageBox.Show(message, "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
            
            if (isTerminating)
            {
                Environment.Exit(1);
            }
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
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            return parent ?? TryFindParent<T>(parentObject);
        }
    }
}