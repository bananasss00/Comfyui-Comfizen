// --- START OF FILE SliderCompareViewModel.cs ---

using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PropertyChanged;

namespace Comfizen
{
    [AddINotifyPropertyChangedInterface]
    public class SliderCompareViewModel : INotifyPropertyChanged
    {
        public bool IsViewOpen { get; set; }
        public ImageOutput ImageLeft { get; set; }
        public ImageOutput ImageRight { get; set; }
        public double SliderPosition { get; set; } = 50; // Start in the middle

        // --- START OF NEW PROPERTIES FOR VIDEO ---
        public bool AreBothVideos => ImageLeft?.Type == FileType.Video && ImageRight?.Type == FileType.Video;
        public bool IsPlaying { get; set; }
        public double CurrentPositionSeconds { get; set; }
        public double MaxDurationSeconds { get; set; }
        public string CurrentPositionFormatted => FormatTimeSpan(TimeSpan.FromSeconds(CurrentPositionSeconds));
        public string MaxDurationFormatted => FormatTimeSpan(TimeSpan.FromSeconds(MaxDurationSeconds));
        public ICommand PlayPauseCommand { get; }
        // --- END OF NEW PROPERTIES FOR VIDEO ---

        public ICommand CloseCommand { get; }
        public ICommand SwapImagesCommand { get; }
        public ICommand ChangeImageLeftCommand { get; }
        public ICommand ChangeImageRightCommand { get; }
        
        public event PropertyChangedEventHandler PropertyChanged;

        public SliderCompareViewModel()
        {
            CloseCommand = new RelayCommand(_ => IsViewOpen = false);
            SwapImagesCommand = new RelayCommand(_ => {
                (ImageLeft, ImageRight) = (ImageRight, ImageLeft);
            }, _ => ImageLeft != null && ImageRight != null); // Can only swap if both images exist
            
            ChangeImageLeftCommand = new RelayCommand(_ => ChangeImage(img => ImageLeft = img));
            ChangeImageRightCommand = new RelayCommand(_ => ChangeImage(img => ImageRight = img));

            // --- ADDED: Initialize video command ---
            PlayPauseCommand = new RelayCommand(_ => IsPlaying = !IsPlaying, _ => AreBothVideos);
        }

        /// <summary>
        /// Opens the comparison view with two specified images.
        /// </summary>
        public void Open(ImageOutput left, ImageOutput right)
        {
            ImageLeft = left;
            ImageRight = right;
            SliderPosition = 50; // Reset slider position
            
            ResetVideoState();

            IsViewOpen = true;
            
            // --- ADDED: Autoplay if both are videos ---
            if (AreBothVideos)
            {
                IsPlaying = true;
            }
        }

        /// <summary>
        /// Opens the comparison view with a single image, leaving the other side empty.
        /// </summary>
        public void Open(ImageOutput singleImage)
        {
            ImageLeft = singleImage;
            ImageRight = null; // Clear the right image
            SliderPosition = 50;

            ResetVideoState();
            
            IsViewOpen = true;
            
            // --- ADDED: Autoplay if both are videos (won't trigger here, but good practice) ---
            if (AreBothVideos)
            {
                IsPlaying = true;
            }
        }

        private void ResetVideoState()
        {
            IsPlaying = false;
            CurrentPositionSeconds = 0;
            MaxDurationSeconds = 0;
        }
        
        /// <summary>
        /// Opens a file dialog to let the user choose a new image.
        /// </summary>
        private void ChangeImage(Action<ImageOutput> setImageAction)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image and Video Files|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.mp4;*.mov;*.webm;*.gif|All Files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var newImage = new ImageOutput(dialog.FileName);
                    setImageAction(newImage);
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Failed to load image for comparison.");
                    MessageBox.Show($"Error loading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        /// <summary>
        /// Handles drop operations from both the internal gallery and the file system.
        /// </summary>
        public void HandleDrop(DragEventArgs e, string target)
        {
            ImageOutput newImage = null;

            if (e.Data.GetDataPresent(typeof(ImageOutput)))
            {
                newImage = e.Data.GetData(typeof(ImageOutput)) as ImageOutput;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    string filePath = files[0];
                    try
                    {
                        newImage = new ImageOutput(filePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(ex, $"Failed to create ImageOutput from dropped file: {filePath}");
                        MessageBox.Show($"Error loading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            
            if (newImage == null) return;

            if (target == "Left")
            {
                ImageLeft = newImage;
            }
            else if (target == "Right")
            {
                ImageRight = newImage;
            }
        }

        private string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return ts.ToString(@"h\:mm\:ss");
            return ts.ToString(@"mm\:ss");
        }
    }
}