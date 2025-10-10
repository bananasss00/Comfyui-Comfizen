using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        
            // Проверяем, содержится ли выбранный элемент в списке,
            // учитывая, что SelectedItem может быть без расширения .json
            var source = ItemsSource.OfType<string>();
            IsValueValid = source.Any(s =>
            {
                // TODO: better fix. processes displayed without extensions 
                var sWithoutExtension = s.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                                        ? s.Substring(0, s.Length - 5)
                                        : s;
                return sWithoutExtension.Equals(SelectedItem, StringComparison.OrdinalIgnoreCase);
            });
        }
        
        private void FilterItems()
        {
            FilteredItems.Clear();
            if (ItemsSource == null) return;

            // Преобразуем к object, так как коллекция теперь гетерогенная
            var source = ItemsSource.Cast<object>();
    
            var filtered = string.IsNullOrWhiteSpace(SearchFilter)
                ? source // Если фильтр пуст, показываем всё как есть (с заголовками)
                : source.OfType<string>() // Иначе, берем ТОЛЬКО строки
                    .Where(m => m.IndexOf(SearchFilter, StringComparison.OrdinalIgnoreCase) >= 0);

            foreach (var item in filtered)
            {
                FilteredItems.Add(item);
            }
        }

        private void ItemListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is string selectedItem)
            {
                // Устанавливаем SelectedItem, который привязан к TextBox
                SetValue(SelectedItemProperty, selectedItem); 
                ItemPopup.IsOpen = false; // Закрываем Popup после выбора
                ItemSelected?.Invoke(this, selectedItem);
            }
        }
        
        // Этот обработчик устанавливает фокус на поле поиска, когда Popup открывается
        private void ItemPopup_Opened(object sender, EventArgs e)
        {
            FilterTextBox.Focus();
            ItemListBox.SelectedIndex = -1;
        }
        
        /// <summary>
        /// Обрабатывает клик мыши для выбора элемента.
        /// </summary>
        private void ItemListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement element &&
                element.DataContext is string selectedItem)
            {
                CommitSelection(selectedItem);
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
        }
        
        private void ItemListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!ItemPopup.IsOpen) return;

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (ItemListBox.SelectedItem is string selectedItem)
                {
                    CommitSelection(selectedItem);
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
        private void CommitSelection(string selectedItem)
        {
            SelectedItem = selectedItem;
            ItemPopup.IsOpen = false;
            ItemSelected?.Invoke(this, selectedItem);
        }
    }
}