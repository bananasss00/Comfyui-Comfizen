using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Comfizen
{
    public partial class FilterableComboBox : UserControl
    {
        public ObservableCollection<object> FilteredItems { get; } = new ObservableCollection<object>();
        public event EventHandler<string> ItemSelected;
        public ICommand ClearSearchCommand { get; }

        public FilterableComboBox()
        {
            InitializeComponent();
            
            ClearSearchCommand = new RelayCommand(_ =>
            {
                SearchFilter = string.Empty;
                FilterTextBox.Focus(); // Возвращаем фокус в поле поиска
            });
        }

        #region Dependency Properties
        public static readonly DependencyProperty ItemSelectedCommandProperty = DependencyProperty.Register(
            "ItemSelectedCommand", typeof(ICommand), typeof(FilterableComboBox), new PropertyMetadata(null));

        public ICommand ItemSelectedCommand
        {
            get => (ICommand)GetValue(ItemSelectedCommandProperty);
            set => SetValue(ItemSelectedCommandProperty, value);
        }
        
        public static readonly DependencyProperty ItemSelectedCommandParameterProperty = DependencyProperty.Register(
            "ItemSelectedCommandParameter", typeof(object), typeof(FilterableComboBox), new PropertyMetadata(null));

        public object ItemSelectedCommandParameter
        {
            get => GetValue(ItemSelectedCommandParameterProperty);
            set => SetValue(ItemSelectedCommandParameterProperty, value);
        }
        
        public static readonly DependencyProperty ItemTemplateSelectorProperty = DependencyProperty.Register(
            "ItemTemplateSelector", typeof(DataTemplateSelector), typeof(FilterableComboBox), new PropertyMetadata(null));

        public DataTemplateSelector ItemTemplateSelector
        {
            get => (DataTemplateSelector)GetValue(ItemTemplateSelectorProperty);
            set => SetValue(ItemTemplateSelectorProperty, value);
        }
        
        public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
            "ItemTemplate", typeof(DataTemplate), typeof(FilterableComboBox), new PropertyMetadata(null));

        public DataTemplate ItemTemplate
        {
            get => (DataTemplate)GetValue(ItemTemplateProperty);
            set => SetValue(ItemTemplateProperty, value);
        }

        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            "ItemsSource", typeof(IEnumerable), typeof(FilterableComboBox), new PropertyMetadata(null, OnItemsSourceChanged));

        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
            "SelectedItem", typeof(string), typeof(FilterableComboBox), 
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

        public string SelectedItem
        {
            get => (string)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public static readonly DependencyProperty SearchFilterProperty = DependencyProperty.Register(
            "SearchFilter", typeof(string), typeof(FilterableComboBox), new PropertyMetadata(string.Empty, OnSearchFilterChanged));

        public string SearchFilter
        {
            get => (string)GetValue(SearchFilterProperty);
            set => SetValue(SearchFilterProperty, value);
        }

        public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
            "PlaceholderText", typeof(string), typeof(FilterableComboBox), new PropertyMetadata("Выберите значение..."));

        public string PlaceholderText
        {
            get => (string)GetValue(PlaceholderTextProperty);
            set => SetValue(PlaceholderTextProperty, value);
        }

        #endregion
        
        #region Validation Dependency Property

        // Используем ReadOnly-свойство, чтобы его нельзя было установить извне.
        // Контрол сам будет управлять своим состоянием валидности.
        private static readonly DependencyPropertyKey IsValueValidPropertyKey = DependencyProperty.RegisterReadOnly(
            "IsValueValid", typeof(bool), typeof(FilterableComboBox), new PropertyMetadata(true));

        public static readonly DependencyProperty IsValueValidProperty = IsValueValidPropertyKey.DependencyProperty;

        public bool IsValueValid
        {
            get => (bool)GetValue(IsValueValidProperty);
            private set => SetValue(IsValueValidPropertyKey, value);
        }

        #endregion
        
        #region Property Changed Callbacks

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FilterableComboBox control) return;
            
            // 1. Отписываемся от событий старой коллекции, чтобы избежать утечек памяти
            if (e.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= control.ItemsSource_CollectionChanged;
            }

            // 2. Подписываемся на события новой коллекции
            if (e.NewValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += control.ItemsSource_CollectionChanged;
            }

            // 3. Вызываем фильтрацию для первоначального заполнения
            control.FilterItems();
    
            control.ValidateSelectedItem();
        }
        
        private void ItemsSource_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Когда источник данных изменяется (кто-то добавил/удалил элемент),
            // мы просто заново фильтруем наш внутренний список.
            FilterItems();
        }

        private static void OnSearchFilterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FilterableComboBox control)
            {
                control.FilterItems();
            }
        }
        
        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FilterableComboBox control)
            {
                control.ValidateSelectedItem();
            }
        }

        #endregion
        
        private void ValidateSelectedItem()
        {
            // Считаем значение валидным, если список пуст или значение не установлено.
            if (ItemsSource == null || string.IsNullOrEmpty(SelectedItem))
            {
                IsValueValid = true;
                return;
            }

            // --- START OF MODIFICATION ---
            // The ItemsSource can contain any object, not just strings.
            // We need to cast to object and compare using ToString(), which is what the user sees.
            var source = ItemsSource.Cast<object>();
            IsValueValid = source.Any(s => s.ToString().Equals(SelectedItem, StringComparison.OrdinalIgnoreCase));
            // --- END OF MODIFICATION ---
        }
        
        private void FilterItems()
        {
            FilteredItems.Clear();
            if (ItemsSource == null) return;

            var source = ItemsSource.Cast<object>();

            // Если фильтр пуст, показываем всё как есть (с заголовками)
            if (string.IsNullOrWhiteSpace(SearchFilter))
            {
                foreach (var item in source)
                {
                    FilteredItems.Add(item);
                }
            }
            else
            {
                // --- MODIFIED: Split search filter by spaces for multi-word search ---
                string[] searchTerms = SearchFilter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                
                var filtered = source
                    .Where(item => searchTerms.All(term => 
                        item.ToString().IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));

                foreach (var item in filtered)
                {
                    FilteredItems.Add(item);
                }
            }
        }

        private void ItemListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                // This makes the control work with any object type, not just strings.
                CommitSelection(e.AddedItems[0]);
            }
        }
        
        // Этот обработчик устанавливает фокус на поле поиска, когда Popup открывается
        private void ItemPopup_Opened(object sender, EventArgs e)
        {
            FilterTextBox.Focus();
            ItemListBox.SelectedIndex = -1;
        }
        
        /// <summary>
        /// Helper method to find a visual ancestor of a specific type.
        /// </summary>
        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);
            return null;
        }
        
        /// <summary>
        /// Handles mouse clicks to select an item, but ignores clicks on buttons within the item.
        /// </summary>
        private void ItemListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Check if the click originated from within a Button.
            // If it did, we do nothing and let the Button handle its own click/command.
            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
            {
                return;
            }
            
            // This allows selection of any object type bound to the ListBox.
            if (e.OriginalSource is FrameworkElement element &&
                element.DataContext != null)
            {
                CommitSelection(element.DataContext);
            }
        }
        
        private void FilterTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!ItemPopup.IsOpen) return;

            if (e.Key == Key.Down)
            {
                e.Handled = true;
                ChangeSelection(1);
            }
            else if (e.Key == Key.Up)
            {
                e.Handled = true;
                ChangeSelection(-1);
            }
            // --- START OF MODIFICATION ---
            else if (e.Key == Key.Escape)
            {
                // If there is text in the search filter, clear it.
                if (!string.IsNullOrEmpty(SearchFilter))
                {
                    SearchFilter = string.Empty;
                    // Mark the event as handled to prevent the popup from closing.
                    e.Handled = true;
                }
                // If the search filter is already empty, this 'if' is skipped.
                // The event will not be handled, allowing it to bubble up to the
                // ItemListBox_PreviewKeyDown handler, which will then close the popup.
            }
            // --- END OF MODIFICATION ---
        }
        
        private void ItemListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!ItemPopup.IsOpen) return;

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (ItemListBox.SelectedItem != null)
                {
                    CommitSelection(ItemListBox.SelectedItem);
                }
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                ItemPopup.IsOpen = false;
            }
        }
        
        /// <summary>
        /// Вспомогательный метод для навигации по списку.
        /// </summary>
        /// <param name="delta">1 для движения вниз, -1 для движения вверх.</param>
        private void ChangeSelection(int delta)
        {
            if (ItemListBox.Items.Count == 0) return;
            
            int newIndex = ItemListBox.SelectedIndex + delta;

            // Пропускаем заголовки
            while (newIndex >= 0 && newIndex < ItemListBox.Items.Count && ItemListBox.Items[newIndex] is WorkflowListHeader)
            {
                newIndex += delta;
            }

            // Проверяем, что не вышли за границы
            if (newIndex >= 0 && newIndex < ItemListBox.Items.Count)
            {
                ItemListBox.SelectedIndex = newIndex;
                ItemListBox.ScrollIntoView(ItemListBox.SelectedItem);

                // Получаем контейнер (ListBoxItem) и фокусируемся на нем
                var lbi = ItemListBox.ItemContainerGenerator.ContainerFromItem(ItemListBox.SelectedItem) as ListBoxItem;
                lbi?.Focus();
            }
        }
        
        /// <summary>
        /// Вспомогательный метод для завершения выбора.
        /// </summary>
        private void CommitSelection(object selectedItem)
        {
            if (selectedItem == null) return;
            
            var selectedItemString = selectedItem.ToString();
            
            SetValue(SelectedItemProperty, selectedItemString);
            ItemPopup.IsOpen = false;
            ItemSelected?.Invoke(this, selectedItemString);

            // --- MODIFIED: Package the selected item and the command parameter into a Tuple ---
            if (ItemSelectedCommand != null)
            {
                // We create a tuple to send both the selected item and the context parameter to the command.
                var commandParameterTuple = Tuple.Create(selectedItem, ItemSelectedCommandParameter);
                if (ItemSelectedCommand.CanExecute(commandParameterTuple))
                {
                    ItemSelectedCommand.Execute(commandParameterTuple);
                    
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        SearchFilter = string.Empty;
                        SetValue(SelectedItemProperty, string.Empty);
                    }));
                }
            }
        }
    }
}