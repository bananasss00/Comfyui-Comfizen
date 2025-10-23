using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

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
                // Ensure the document exists. This is good practice, though in our XAML it always should.
                if (control.Viewer.Document == null)
                {
                    control.Viewer.Document = new FlowDocument();
                }
                
                // THE FIX: Call the new converter method, passing the existing, styled document
                // from our FlowDocumentScrollViewer. This preserves all styles.
                MarkdownConverter.ConvertTo(e.NewValue as string, control.Viewer.Document);
            }
        }

        private void Viewer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
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

        /// <summary>
        /// Normalizes wf:// links in the raw text when editing is finished.
        /// </summary>
        private void Editor_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                var originalText = Editor.Text;
                var modifiedText = NormalizeWorkflowLinks(originalText);

                if (originalText != modifiedText)
                {
                    MarkdownText = modifiedText;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Failed to normalize markdown links.");
            }

            Viewer.Visibility = Visibility.Visible;
            Editor.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// A robust click handler that reads the URL from the Tag property for wf:// links
        /// and safely handles external http/https links.
        /// </summary>
        private void Viewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var link = FindLogicalAncestor<Hyperlink>(e.OriginalSource as DependencyObject);
            if (link == null) return;

            // Priority 1: Check our custom "wf://" link, which is now reliably in the Tag property.
            if (link.Tag is string url && url.StartsWith("wf://", StringComparison.OrdinalIgnoreCase))
            {
                string path = url.Substring("wf://".Length);
                
                // Unescape the path, as it was encoded by NormalizeWorkflowLinks.
                path = Uri.UnescapeDataString(path); 

                if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm)
                {
                    mainVm.OpenOrSwitchToWorkflow(path);
                }
                e.Handled = true;
            }
            // Priority 2: Handle standard, absolute web links safely.
            else if (link.NavigateUri != null && link.NavigateUri.IsAbsoluteUri)
            {
                // This check is now safe because IsAbsoluteUri is true.
                if (link.NavigateUri.Scheme == Uri.UriSchemeHttp || link.NavigateUri.Scheme == Uri.UriSchemeHttps)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(link.NavigateUri.OriginalString) { UseShellExecute = true });
                        e.Handled = true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(ex, $"Could not open external link: {link.NavigateUri.OriginalString}");
                    }
                }
            }
        }

        /// <summary>
        /// Cleans up wf:// links in the raw text. Performs URI encoding on the path part.
        /// This method is now idempotent, meaning it's safe to run it multiple times on the same text.
        /// </summary>
        private string NormalizeWorkflowLinks(string markdown)
        {
            var workflowsDirFullPath = Path.GetFullPath(Workflow.WorkflowsDir);
            
            return Regex.Replace(markdown, @"\[([^\]]*)\]\(([^)]+)\)", match =>
            {
                var linkText = match.Groups[1].Value;
                var url = match.Groups[2].Value.Trim();

                if (!url.StartsWith("wf://", StringComparison.OrdinalIgnoreCase))
                {
                    return match.Value;
                }

                var path = url.Substring("wf://".Length);

                // FIX: First, unescape any existing encoding to prevent double-encoding.
                path = Uri.UnescapeDataString(path);
                
                // 1. Normalize slashes
                var normalizedPath = path.Replace('\\', '/');
                
                // 2. Handle absolute paths
                if (Path.IsPathRooted(normalizedPath))
                {
                    try
                    {
                        var fileFullPath = Path.GetFullPath(normalizedPath);
                        if (fileFullPath.StartsWith(workflowsDirFullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            normalizedPath = Path.GetRelativePath(workflowsDirFullPath, fileFullPath).Replace('\\', '/');
                        }
                        else
                        {
                             Logger.LogToConsole(
                                $"Markdown Link Warning: The absolute path '{fileFullPath}' is outside the workflows directory.",
                                LogLevel.Warning, System.Windows.Media.Colors.Orange);
                        }
                    }
                    catch { /* Ignore invalid paths during normalization */ }
                }
                
                // 3. Re-encode the cleaned path to ensure it's a valid URI for Markdig.
                var encodedPath = Uri.EscapeDataString(normalizedPath);
                
                // Rebuild the link with the cleaned and correctly encoded path.
                return $"[{linkText}](wf://{encodedPath})";
            });
        }
        
        private static T FindLogicalAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T typed)
                {
                    return typed;
                }
                current = LogicalTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}