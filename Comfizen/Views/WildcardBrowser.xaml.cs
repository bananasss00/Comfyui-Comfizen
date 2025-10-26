using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Comfizen.Views;

public partial class WildcardBrowser : Window
{
    public WildcardBrowser()
    {
        InitializeComponent();
        Loaded += WildcardBrowser_Loaded;
    }
    
    private void WildcardBrowser_Loaded(object sender, RoutedEventArgs e)
    {
        // The SearchTextBox is now directly part of this window's XAML, so we can access it.
        // This is much simpler than finding it in a template.
        SearchTextBox.Focus();
    }

    // Handles double-clicking on a list item to insert it
    private void ListBoxItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is WildcardBrowserViewModel vm)
        {
            if (vm.InsertCommand.CanExecute(null))
            {
                vm.InsertCommand.Execute(null);
            }
        }
    }
}
