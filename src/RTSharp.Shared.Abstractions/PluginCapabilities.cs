namespace RTSharp.Shared.Abstractions
{
    public record PluginCapabilities(bool HasSettingsWindow)
    {
        /// <summary>
        /// Has capability to spawn its own plugin settings window. Plugins without this capability will have Settings button grayed out in plugins window.
        /// </summary>
        public bool HasSettingsWindow { get; init; } = HasSettingsWindow;
    }
}
