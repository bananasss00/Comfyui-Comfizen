using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PropertyChanged;

namespace Comfizen;

[AddINotifyPropertyChangedInterface]
public class WildcardBrowserViewModel : INotifyPropertyChanged
{
    private readonly Window _hostWindow;
    private List<string> _allWildcards;

    public string SearchText { get; set; }
    public ObservableCollection<string> FilteredWildcards { get; } = new();
    public string SelectedWildcard { get; set; }
    public string SelectedWildcardTag { get; private set; }

    public ICommand InsertCommand { get; }
    public ICommand CancelCommand { get; }

    public event PropertyChangedEventHandler PropertyChanged;

    public WildcardBrowserViewModel(Window hostWindow)
    {
        _hostWindow = hostWindow;
        LoadWildcards();

        InsertCommand = new RelayCommand(p => InsertAndClose(), p => !string.IsNullOrEmpty(SelectedWildcard));
        CancelCommand = new RelayCommand(p => Close());
            
        this.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);
    }
        
    private void OnPropertyChanged(string propertyName)
    {
        if (propertyName == nameof(SearchText))
        {
            FilterWildcards();
        }
    }

    private void LoadWildcards()
    {
        _allWildcards = new List<string>();
        string wildcardsDir = Path.Combine(Directory.GetCurrentDirectory(), "wildcards");
        Directory.CreateDirectory(wildcardsDir);
            
        if (Directory.Exists(wildcardsDir))
        {
            var files = Directory.GetFiles(wildcardsDir, "*.txt", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(wildcardsDir, file);
                // Convert to format like "folder/file" (without .txt)
                var wildcardName = Path.ChangeExtension(relativePath, null).Replace(Path.DirectorySeparatorChar, '/');
                _allWildcards.Add(wildcardName);
            }
            _allWildcards.Sort();
        }
        FilterWildcards();
    }

    private void FilterWildcards()
    {
        FilteredWildcards.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchText) ? _allWildcards : _allWildcards.Where(w => w.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);
        foreach (var item in filtered) FilteredWildcards.Add(item);
    }

    public void InsertAndClose() 
    { 
        if (!string.IsNullOrEmpty(SelectedWildcard)) 
        { 
            // Change: Insert wildcard without curly braces.
            SelectedWildcardTag = $"__{SelectedWildcard}__"; 
            _hostWindow.DialogResult = true; 
            Close(); 
        } 
    }
    private void Close() { _hostWindow.Close(); }
}