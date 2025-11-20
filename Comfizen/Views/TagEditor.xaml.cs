using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Comfizen
{
    public partial class TagEditor : UserControl
    {
        public TagEditor()
        {
            InitializeComponent();
            AddTagCommand = new RelayCommand(AddTag);
            RemoveTagCommand = new RelayCommand(RemoveTag);
        }
        
        public static readonly DependencyProperty TagsProperty =
            DependencyProperty.Register("Tags", typeof(ObservableCollection<string>), typeof(TagEditor), new PropertyMetadata(null));

        public ObservableCollection<string> Tags
        {
            get => (ObservableCollection<string>)GetValue(TagsProperty);
            set => SetValue(TagsProperty, value);
        }
        
        public static readonly DependencyProperty AvailableTagsProperty =
            DependencyProperty.Register("AvailableTags", typeof(ObservableCollection<string>), typeof(TagEditor), new PropertyMetadata(null));

        public ObservableCollection<string> AvailableTags
        {
            get => (ObservableCollection<string>)GetValue(AvailableTagsProperty);
            set => SetValue(AvailableTagsProperty, value);
        }

        public ICommand AddTagCommand { get; }
        public ICommand RemoveTagCommand { get; }

        private void AddTag(object parameter)
        {
            var tag = parameter as string;
            if (!string.IsNullOrWhiteSpace(tag) && Tags != null && !Tags.Contains(tag.Trim()))
            {
                Tags.Add(tag.Trim());
                TagInput.Text = ""; // Clear input
            }
        }

        private void RemoveTag(object parameter)
        {
            var tag = parameter as string;
            if (tag != null && Tags != null)
            {
                Tags.Remove(tag);
            }
        }

        private void TagInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddTag(TagInput.Text);
                e.Handled = true;
            }
        }
    }
}