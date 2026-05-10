namespace VTuberSystemBase.CharacterSelectionTab.Contracts
{
    /// <summary>
    /// Tag describing the dynamic type carried in a setting value or schema entry.
    /// Forward-compatible: unknown numeric values seen on the wire MUST be treated as
    /// "skip + log" by the receiver, never as a hard error.
    /// </summary>
    public enum SettingType
    {
        Float = 0,
        Int = 1,
        Bool = 2,
        Color = 3,
        Enum = 4,
        Vector3 = 5
    }
}
