using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms; // Requires reference to System.Windows.Forms.dll
using System.Windows.Input;
using Microsoft.Win32;
using PropertyChanged;

namespace Comfizen;

[AddINotifyPropertyChangedInterface]
public class WildcardBrowserViewModel : INotifyPropertyChanged
{
    private readonly Window _hostWindow;
    private readonly Action<string> _insertAction;
    private List<string> _allWildcards;

    public string SearchText { get; set; }
    public ObservableCollection<string> FilteredWildcards { get; } = new();
    public string SelectedWildcard { get; set; }

    public ICommand InsertCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ReloadListCommand { get; } // New command
    public ICommand ConvertWildcardsToYamlCommand { get; } // Moved command
    public ICommand ConvertYamlToWildcardsCommand { get; } // Moved command


    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>
    /// ViewModel for the Wildcard Browser.
    /// </summary>
    /// <param name="hostWindow">The window hosting this ViewModel.</param>
    /// <param name="insertAction">The action to call to insert the selected wildcard text.</param>
    public WildcardBrowserViewModel(Window hostWindow, Action<string> insertAction)
    {
        _hostWindow = hostWindow;
        _insertAction = insertAction ?? throw new ArgumentNullException(nameof(insertAction));
        
        // --- Command Initialization ---
        InsertCommand = new RelayCommand(p => InsertAndClose(), p => !string.IsNullOrEmpty(SelectedWildcard));
        CancelCommand = new RelayCommand(p => Close());
        ReloadListCommand = new RelayCommand(_ => LoadWildcards());
        ConvertWildcardsToYamlCommand = new RelayCommand(ExecuteConvertWildcardsToYaml);
        ConvertYamlToWildcardsCommand = new RelayCommand(ExecuteConvertYamlToWildcards);
            
        this.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);
        
        LoadWildcards(); // Initial load
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
        // FIX: Use the central WildcardFileHandler to get ALL wildcard names, including from YAML.
        _allWildcards = WildcardFileHandler.GetAllWildcardNames();
        _allWildcards.Sort();
        FilterWildcards();
    }

    private void FilterWildcards()
    {
        FilteredWildcards.Clear();

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            foreach (var item in _allWildcards) FilteredWildcards.Add(item);
            return;
        }

        // Split the search text into multiple terms
        var searchTerms = SearchText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        // Find wildcards that contain ALL of the search terms
        var filtered = _allWildcards.Where(wildcard => 
            searchTerms.All(term => 
                wildcard.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
            )
        );
        
        foreach (var item in filtered)
        {
            FilteredWildcards.Add(item);
        }
    }
    
    // --- Methods for Commands ---

    public void InsertAndClose() 
    { 
        if (!string.IsNullOrEmpty(SelectedWildcard)) 
        {
            string textToInsert = $"__{SelectedWildcard}__";
            _insertAction(textToInsert); 
            _hostWindow.DialogResult = true; 
            Close(); 
        } 
    }
    
    private void Close() { _hostWindow.Close(); }

    private void ExecuteConvertWildcardsToYaml(object obj)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "YAML File (*.yaml)|*.yaml",
            Title = "Save Wildcards as YAML",
            FileName = "wildcards.yaml"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var wildcardsDir = Path.Combine(Directory.GetCurrentDirectory(), "wildcards");
                WildcardConverter.ConvertDirectoryToYaml(wildcardsDir, dialog.FileName);
                System.Windows.MessageBox.Show("Conversion successful!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                // Optionally reload the list if the user saves inside the wildcards folder
                LoadWildcards();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Conversion failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ExecuteConvertYamlToWildcards(object obj)
    {
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "YAML Files (*.yaml;*.yml)|*.yaml;*.yml",
            Title = "Open Wildcard YAML File"
        };

        if (openDialog.ShowDialog() == true)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Select a directory to unpack wildcards into";
                folderDialog.ShowNewFolderButton = true;
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        WildcardConverter.ConvertYamlToDirectory(openDialog.FileName, folderDialog.SelectedPath);
                        System.Windows.MessageBox.Show("Unpacking successful!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        // Optionally reload the list if the user unpacks into the main wildcards folder
                        LoadWildcards();
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Unpacking failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
    }
}