using System.Windows;
using System.Windows.Input;

namespace Comfizen;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel();
    }
}