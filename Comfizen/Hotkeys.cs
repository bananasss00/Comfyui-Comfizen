namespace Comfizen
{
    /// <summary>
    /// Provides static properties for hotkey combinations and their localized tooltips.
    /// </summary>
    public static class Hotkeys
    {
        public static string MoveNext => "NumPad 6";
        public static string MovePrevious => "NumPad 4";
        public static string SaveImage => "NumPad 5";
        public static string CloseFullscreen => "Escape";
            
        public static string MoveNextTooltip => string.Format("{0} ({1})", LocalizationService.Instance["Fullscreen_Next"], MoveNext);
        public static string MovePreviousTooltip => string.Format("{0} ({1})", LocalizationService.Instance["Fullscreen_Previous"], MovePrevious);
        public static string SaveImageTooltip => string.Format("{0} ({1})", LocalizationService.Instance["Fullscreen_SaveImage"], SaveImage);
        public static string SaveImageResaveTooltip => string.Format("{0} ({1})", LocalizationService.Instance["Fullscreen_SaveImage_ResaveTooltip"], SaveImage);
        public static string CloseFullscreenTooltip => string.Format("{0} ({1})", LocalizationService.Instance["Fullscreen_Close"], CloseFullscreen);
    }
}