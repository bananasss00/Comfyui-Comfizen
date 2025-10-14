namespace Comfizen
{
    public enum FieldType
    {
        Any,
        Seed,
        WildcardSupportPrompt,
        Sampler,
        Scheduler,
        ImageInput, // Поле для исходного изображения (Base64)
        MaskInput,   // Поле для маски (Base64)
        SliderInt,
        SliderFloat,
        ComboBox,
        Model,
        Markdown
    }
}
