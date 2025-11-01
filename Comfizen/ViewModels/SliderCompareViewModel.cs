using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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
            });
            ChangeImageLeftCommand = new RelayCommand(_ => ChangeImage(img => ImageLeft = img));
            ChangeImageRightCommand = new RelayCommand(_ => ChangeImage(img => ImageRight = img));
        }

        public void Open(ImageOutput left, ImageOutput right)
        {
            ImageLeft = left;
            ImageRight = right;
            SliderPosition = 50; // Reset slider position
            IsViewOpen = true;
        }
        
        private void ChangeImage(Action<ImageOutput> setImageAction)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All Files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var bytes = File.ReadAllBytes(dialog.FileName);
                    var newImage = new ImageOutput
                    {
                        ImageBytes = bytes,
                        FileName = Path.GetFileName(dialog.FileName),
                        // We can't know the prompt, but we can compute hashes
                        VisualHash = Utils.ComputePixelHash(bytes)
                    };
                    
                    setImageAction(newImage);
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Failed to load image for comparison.");
                }
            }
        }
        
        public void HandleDrop(DragEventArgs e, string target)
        {
            ImageOutput droppedImage = null;

            if (e.Data.GetData(typeof(ImageOutput)) is ImageOutput imageOutput)
            {
                droppedImage = imageOutput;
            }
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                try
                {
                    var bytes = File.ReadAllBytes(files[0]);
                    droppedImage = new ImageOutput
                    {
                        ImageBytes = bytes,
                        FileName = Path.GetFileName(files[0]),
                        VisualHash = Utils.ComputePixelHash(bytes)
                    };
                }
                catch (Exception ex)
                {
                     Logger.Log(ex, "Failed to load dropped image for comparison.");
                }
            }
            
            if (droppedImage != null)
            {
                if (target == "Left")
                {
                    ImageLeft = droppedImage;
                }
                else if (target == "Right")
                {
                    ImageRight = droppedImage;
                }
            }
        }
    }
}