// AdvancedPromptEditor.xaml.cs
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Comfizen
{
    // A ViewModel for a single token button
    public class TokenViewModel : INotifyPropertyChanged
    {
        public string FullText { get; set; }
        public string DisplayText { get; set; }
        public bool IsDisabled { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class AdvancedPromptEditor : UserControl
    {
        private bool _isUpdatingInternally = false;

        private Point? _dragStartPoint;
        private bool _isDragging = false;
        
        public ObservableCollection<TokenViewModel> Tokens { get; } = new ObservableCollection<TokenViewModel>();

        public AdvancedPromptEditor()
        {
            InitializeComponent();
        }

        #region DependencyProperty for PromptText

        public static readonly DependencyProperty PromptTextProperty =
            DependencyProperty.Register("PromptText", typeof(string), typeof(AdvancedPromptEditor),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPromptTextChanged));

        public string PromptText
        {
            get { return (string)GetValue(PromptTextProperty); }
            set { SetValue(PromptTextProperty, value); }
        }

        private static void OnPromptTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (AdvancedPromptEditor)d;
            if (!control._isUpdatingInternally)
            {
                control.ParseAndRefreshTokens();
            }
        }

        #endregion

        #region Token Parsing and Joining

        private void ParseAndRefreshTokens()
        {
            Tokens.Clear();
            var rawTokens = PromptUtils.Tokenize(PromptText);

            foreach (var token in rawTokens)
            {
                bool isDisabled = token.StartsWith(PromptUtils.DISABLED_TOKEN_PREFIX);
                Tokens.Add(new TokenViewModel
                {
                    FullText = token,
                    DisplayText = isDisabled ? token.Substring(PromptUtils.DISABLED_TOKEN_PREFIX.Length) : token,
                    IsDisabled = isDisabled
                });
            }
        }
        
        private void UpdatePromptFromTokens()
        {
            _isUpdatingInternally = true;
            PromptText = string.Join(", ", Tokens.Select(t => t.FullText));
            _isUpdatingInternally = false;
        }

        #endregion

        #region Event Handlers

        private void PromptTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ParseAndRefreshTokens();
        }
        
        private void ToggleToken(TokenViewModel tokenVm)
        {
            if (tokenVm == null) return;

            tokenVm.IsDisabled = !tokenVm.IsDisabled;
            tokenVm.FullText = tokenVm.IsDisabled ? $"{PromptUtils.DISABLED_TOKEN_PREFIX}{tokenVm.DisplayText}" : tokenVm.DisplayText;
            
            UpdatePromptFromTokens();
        }
        
        private void WildcardButton_Click(object sender, RoutedEventArgs e)
        {
            int savedCaretIndex = PromptTextBox.CaretIndex;
            Action<string> insertAction = (textToInsert) =>
            {
                PromptTextBox.Text = PromptTextBox.Text.Insert(savedCaretIndex, textToInsert);
                PromptTextBox.CaretIndex = savedCaretIndex + textToInsert.Length;
                PromptTextBox.Focus();
            };
    
            var hostWindow = new Comfizen.Views.WildcardBrowser { Owner = Window.GetWindow(this) };
            var viewModel = new WildcardBrowserViewModel(hostWindow, insertAction);
            hostWindow.DataContext = viewModel;
            hostWindow.ShowDialog();
        }
        
        private void Token_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // FIX: Cast to FrameworkElement to work with Label.
            if ((sender as FrameworkElement)?.DataContext is not TokenViewModel tokenVm) return;
            Tokens.Remove(tokenVm);
            UpdatePromptFromTokens();
        }
        
        private void Token_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true; 
            
            // FIX: Cast to FrameworkElement to work with Label.
            if ((sender as FrameworkElement)?.DataContext is not TokenViewModel tokenVm || tokenVm.IsDisabled) return;
            
            var loraRegex = new Regex(@"^<lora:(.*?):([0-9.-]+)>$");
            string currentToken = tokenVm.FullText;
            string newToken = currentToken;
            double weightChange = e.Delta > 0 ? 0.05 : -0.05;

            var loraMatch = loraRegex.Match(currentToken);
            if (loraMatch.Success)
            {
                var content = loraMatch.Groups[1].Value;
                double.TryParse(loraMatch.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var weight);
                double newWeight = Math.Max(-2.0, Math.Min(3.0, weight + weightChange));
                newToken = $"<lora:{content}:{newWeight.ToString("0.0#", CultureInfo.InvariantCulture)}>";
            }
            else if (currentToken.StartsWith("(") && currentToken.EndsWith(")"))
            {
                int lastColon = currentToken.LastIndexOf(':');
                if (lastColon > 0 && lastColon < currentToken.Length - 1)
                {
                    string potentialWeight = currentToken.Substring(lastColon + 1, currentToken.Length - lastColon - 2);
                    if (double.TryParse(potentialWeight, NumberStyles.Any, CultureInfo.InvariantCulture, out var weight))
                    {
                        string content = currentToken.Substring(1, lastColon - 1);
                        double newWeight = Math.Max(0.05, weight + weightChange);
                        newToken = Math.Abs(newWeight - 1.0) < 0.01 ? content : $"({content}:{newWeight.ToString("0.0#", CultureInfo.InvariantCulture)})";
                    }
                    else
                    {
                        double newWeight = weightChange > 0 ? 1.05 : 0.95;
                        newToken = $"({currentToken}:{newWeight.ToString("0.0#", CultureInfo.InvariantCulture)})";
                    }
                }
            }
            else
            {
                double newWeight = weightChange > 0 ? 1.05 : 0.95;
                newToken = $"({currentToken}:{newWeight.ToString("0.0#", CultureInfo.InvariantCulture)})";
            }
            
            tokenVm.FullText = newToken;
            tokenVm.DisplayText = newToken;
            UpdatePromptFromTokens();
            ParseAndRefreshTokens(); 
        }

        #endregion
        
        #region Drag and Drop Handlers
        
        // FIX: Changed type checks from 'Button' to 'FrameworkElement' to support the new 'Label' implementation.
        private void Token_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                _dragStartPoint = e.GetPosition(element);
                _isDragging = false;
                element.CaptureMouse();
            }
        }

        private void Token_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element && element.IsMouseCaptured)
            {
                if (_dragStartPoint.HasValue && e.LeftButton == MouseButtonState.Pressed)
                {
                    Point position = e.GetPosition(element);
                    if (Math.Abs(position.X - _dragStartPoint.Value.X) > SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(position.Y - _dragStartPoint.Value.Y) > SystemParameters.MinimumVerticalDragDistance)
                    {
                        _isDragging = true;
                        element.ReleaseMouseCapture();
                        DragDrop.DoDragDrop(element, element.DataContext, DragDropEffects.Move);
                        _dragStartPoint = null;
                    }
                }
            }
        }

        private void Token_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.IsMouseCaptured)
            {
                element.ReleaseMouseCapture();
            }

            if (!_isDragging)
            {
                if(sender is FrameworkElement element2)
                {
                    ToggleToken(element2.DataContext as TokenViewModel);
                }
            }
            _dragStartPoint = null;
            _isDragging = false;
        }
        
        private void Token_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(TokenViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void Token_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(TokenViewModel)) is TokenViewModel draggedToken &&
                (sender as FrameworkElement)?.DataContext is TokenViewModel targetToken)
            {
                int draggedIndex = Tokens.IndexOf(draggedToken);
                int targetIndex = Tokens.IndexOf(targetToken);

                if (draggedIndex != -1 && targetIndex != -1)
                {
                    Tokens.Move(draggedIndex, targetIndex);
                    UpdatePromptFromTokens();
                }
            }
        }

        #endregion
    }
}