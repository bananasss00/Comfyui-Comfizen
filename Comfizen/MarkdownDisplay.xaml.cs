using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace Comfizen
{
    public partial class MarkdownDisplay : UserControl
    {
        public MarkdownDisplay()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty MarkdownTextProperty =
            DependencyProperty.Register("MarkdownText", typeof(string), typeof(MarkdownDisplay),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnMarkdownTextChanged));

        public string MarkdownText
        {
            get { return (string)GetValue(MarkdownTextProperty); }
            set { SetValue(MarkdownTextProperty, value); }
        }

        private static void OnMarkdownTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarkdownDisplay control)
            {
                var newDoc = MarkdownConverter.ToFlowDocument(e.NewValue as string);
                
                if (control.Viewer.Document == null)
                {
                    control.Viewer.Document = new FlowDocument();
                }
                
                control.Viewer.Document.Blocks.Clear();
                foreach (var block in newDoc.Blocks.ToList())
                {
                    control.Viewer.Document.Blocks.Add(block);
                }
            }
        }

        // --- START OF FIX 2: Correctly handle double click ---
        private void Viewer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Use the correct helper for logical elements
            if (FindLogicalAncestor<Hyperlink>(e.OriginalSource as DependencyObject) != null)
            {
                e.Handled = true;
                return;
            }
            
            Viewer.Visibility = Visibility.Collapsed;
            Editor.Visibility = Visibility.Visible;
            Editor.Focus();
            Editor.SelectAll();
        }
        // --- END OF FIX 2 ---

        private void Editor_LostFocus(object sender, RoutedEventArgs e)
        {
            Viewer.Visibility = Visibility.Visible;
            Editor.Visibility = Visibility.Collapsed;
        }

        // --- START OF FIX 1: Handle link clicks reliably with a Preview event ---
        private void Viewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            if (source == null) return;

            var link = FindLogicalAncestor<Hyperlink>(source);

            if (link != null && link.NavigateUri != null)
            {
                // --- НАЧАЛО ИЗМЕНЕНИЯ ---
                // Проверяем, является ли ссылка кастомной ссылкой на воркфлоу
                if (link.NavigateUri.Scheme.Equals("wf", StringComparison.OrdinalIgnoreCase))
                {
                    // Собираем относительный путь из URI (например, из "wf://folder/my_workflow.json" получаем "folder/my_workflow.json")
                    var relativePath = link.NavigateUri.OriginalString.Substring("wf://".Length);

                    // Получаем доступ к MainViewModel главного окна
                    if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm)
                    {
                        // Вызываем метод для открытия/переключения на вкладку с воркфлоу
                        mainVm.OpenOrSwitchToWorkflow(relativePath);
                    }
                    e.Handled = true; // Помечаем событие как обработанное, чтобы ссылка не открывалась стандартным образом
                }
                // --- КОНЕЦ ИЗМЕНЕНИЯ ---
                else // Стандартная обработка для обычных http/https ссылок
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(link.NavigateUri.AbsoluteUri) { UseShellExecute = true });
                        e.Handled = true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(ex, $"Could not open link: {link.NavigateUri.AbsoluteUri}");
                    }
                }
            }
        }
        // --- END OF FIX 1 ---
        
        /// <summary>
        /// Finds an ancestor of a given type in the logical tree.
        /// </summary>
        private static T FindLogicalAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T)
                {
                    return (T)current;
                }
                // Use LogicalTreeHelper for document elements
                current = LogicalTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}