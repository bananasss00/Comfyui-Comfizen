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
        public DataTemplate MarkdownTemplate { get; set; }
        public DataTemplate ScriptButtonTemplate { get; set; }
        public DataTemplate NodeBypassTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            return item switch
            {
                MarkdownFieldViewModel => MarkdownTemplate,
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
                ScriptButtonFieldViewModel => ScriptButtonTemplate,
                NodeBypassFieldViewModel => NodeBypassTemplate,
                TextFieldViewModel => TextTemplate,
                _ => base.SelectTemplate(item, container)
            };
        }
    }
}