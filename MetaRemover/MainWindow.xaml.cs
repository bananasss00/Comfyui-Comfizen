// File: MainWindow.xaml.cs
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace MetaRemover
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Handles the DragOver event to show the correct cursor icon.
        /// </summary>
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            // Allow drop only if the dragged data contains files.
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) 
                ? DragDropEffects.Copy 
                : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>
        /// Handles the main Drop event when files are released onto the window.
        /// </summary>
        private void Window_Drop(object sender, DragEventArgs e)
        {
            // 1. Get the list of dropped file paths.
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files) || !files.Any())
            {
                return;
            }

            // 2. Ask the user for an output directory using the built-in WinForms dialog.
            var dialog = new FolderBrowserDialog
            {
                Description = "Select a folder to save the cleaned files"
            };

            // Show the dialog and check if the user clicked "OK".
            // The return type is a DialogResult enum, not a bool?.
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return; // User cancelled.
            }
            string outputDirectory = dialog.SelectedPath;

            // 3. Process each file (this part remains unchanged).
            int processedCount = 0;
            int skippedCount = 0;
            var detailsLog = new StringBuilder();

            foreach (var filePath in files)
            {
                try
                {
                    // Read the file content.
                    byte[] originalBytes = File.ReadAllBytes(filePath);

                    // Attempt to remove metadata.
                    byte[]? cleanedBytes = MetadataRemover.RemoveComfizenMetadata(originalBytes);
                    
                    string fileName = Path.GetFileName(filePath);

                    // If metadata was found and removed...
                    if (cleanedBytes != null)
                    {
                        // Save the new, cleaned file.
                        string outputPath = Path.Combine(outputDirectory, fileName);
                        File.WriteAllBytes(outputPath, cleanedBytes);
                        processedCount++;
                        detailsLog.AppendLine($"Cleaned: {fileName}");
                    }
                    else // No metadata was found.
                    {
                        skippedCount++;
                        detailsLog.AppendLine($"Skipped (metadata not found): {fileName}");
                    }
                }
                catch (Exception ex)
                {
                    skippedCount++;
                    detailsLog.AppendLine($"Error processing {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            // 4. Show a summary to the user.
            var summary = new StringBuilder();
            summary.AppendLine($"Processing complete!");
            summary.AppendLine($"Files cleaned: {processedCount}");
            summary.AppendLine($"Files skipped: {skippedCount}");
            summary.AppendLine();
            summary.AppendLine("Details:");
            summary.Append(detailsLog.ToString());

            MessageBox.Show(this, summary.ToString(), "Result", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}