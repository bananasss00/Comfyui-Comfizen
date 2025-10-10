using System;
using System.Windows.Markup;

namespace Comfizen;

public class EnumBindingSource : MarkupExtension
{
    public Type EnumType { get; private set; }

    public EnumBindingSource(Type enumType)
    {
        if (enumType == null || !enumType.IsEnum)
            throw new ArgumentException("EnumType must be an enum type");
        EnumType = enumType;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return Enum.GetValues(EnumType);
    }
}