using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
                // Assign the generated document to the FlowDocument inside the viewer
                if (control.Viewer.Document == null)
                {
                    control.Viewer.Document = new System.Windows.Documents.FlowDocument();
                }
                var newDoc = MarkdownConverter.ToFlowDocument(e.NewValue as string);
                control.Viewer.Document.Blocks.Clear();
                foreach (var block in newDoc.Blocks.ToList())
                {
                    control.Viewer.Document.Blocks.Add(block);
                }
            }
        }

        private void Viewer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Don't switch to edit mode if a hyperlink was clicked
            if (e.OriginalSource is System.Windows.Documents.Hyperlink)
            {
                return;
            }
            
            // Switch to edit mode on double click
            Viewer.Visibility = Visibility.Collapsed;
            Editor.Visibility = Visibility.Visible;
            Editor.Focus();
            Editor.SelectAll();
        }


        private void Editor_LostFocus(object sender, RoutedEventArgs e)
        {
            // Switch back to view mode when focus is lost
            Viewer.Visibility = Visibility.Visible;
            Editor.Visibility = Visibility.Collapsed;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                // Log the error or show a message to the user
                Logger.Log(ex, $"Could not open link: {e.Uri.AbsoluteUri}");
            }
        }
    }
}