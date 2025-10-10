using System.Windows;
using System.Windows.Controls;

namespace Comfizen
{
    public class FieldTemplateSelector : DataTemplateSelector
    {
        public DataTemplate TextTemplate { get; set; }
        public DataTemplate SeedTemplate { get; set; }
        public DataTemplate SliderTemplate { get; set; }
        public DataTemplate ComboBoxTemplate { get; set; }
        public DataTemplate CheckBoxTemplate { get; set; }
        public DataTemplate WildcardTemplate { get; set; }
        public DataTemplate AnyTypeTemplate { get; set; }
        public DataTemplate InpaintTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            return item switch
            {
                InpaintFieldViewModel => InpaintTemplate,
                SeedFieldViewModel => SeedTemplate,
                SliderFieldViewModel => SliderTemplate,
                ComboBoxFieldViewModel => ComboBoxTemplate,
                CheckBoxFieldViewModel => CheckBoxTemplate,
                TextFieldViewModel vm when vm.Type == FieldType.WildcardSupportPrompt => WildcardTemplate,
                // ========================================================== //
                //     НАЧАЛО ИЗМЕНЕНИЯ                                       //
                // ========================================================== //
                TextFieldViewModel vm when vm.Type == FieldType.Any => AnyTypeTemplate,
                // ========================================================== //
                //     КОНЕЦ ИЗМЕНЕНИЯ                                        //
                // ========================================================== //
                TextFieldViewModel => TextTemplate,
                _ => base.SelectTemplate(item, container)
            };
        }
    }
}