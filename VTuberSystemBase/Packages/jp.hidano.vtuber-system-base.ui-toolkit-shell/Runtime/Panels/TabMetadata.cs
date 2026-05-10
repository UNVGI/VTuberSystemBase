#nullable enable

namespace VTuberSystemBase.UiToolkitShell.Panels
{
    /// <summary>
    /// Tab-spec supplied descriptor handed to <c>ITabPanelRegistry.RegisterTab</c>.
    /// Currently carries only the human-readable display name; further fields will
    /// be added when localization and analytics hooks land. The struct is
    /// intentionally minimal so future additions stay source-compatible.
    /// </summary>
    public readonly struct TabMetadata
    {
        public TabMetadata(string displayName)
        {
            DisplayName = displayName;
        }

        public string DisplayName { get; }
    }
}
