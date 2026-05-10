namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Event payload for <see cref="CameraIpcTopics.PresetCommand"/>
    /// (<c>camera/preset/command</c>, design.md L1277). Published by the UI as a
    /// reference notification when preset CRUD / activation occurs; persistence
    /// itself lives in the UI side.
    /// </summary>
    /// <remarks>
    /// Field usage by op:
    /// <list type="bullet">
    /// <item><c>create</c> — <see cref="Name"/>.</item>
    /// <item><c>rename</c> — <see cref="Name"/> (old) + <see cref="NewName"/>.</item>
    /// <item><c>duplicate</c> — <see cref="SourceName"/> + <see cref="Name"/> (target).</item>
    /// <item><c>delete</c> — <see cref="Name"/>.</item>
    /// <item><c>activate</c> — <see cref="Name"/>.</item>
    /// </list>
    /// </remarks>
    public readonly struct PresetCommandPayload
    {
        /// <summary>One of <c>create</c> / <c>rename</c> / <c>duplicate</c> / <c>delete</c> / <c>activate</c>.</summary>
        public string Op { get; init; }

        /// <summary>Primary preset name (semantics depend on <see cref="Op"/>).</summary>
        public string Name { get; init; }

        /// <summary>New name for <c>rename</c>; null otherwise.</summary>
        public string? NewName { get; init; }

        /// <summary>Source preset name for <c>duplicate</c>; null otherwise.</summary>
        public string? SourceName { get; init; }
    }

    /// <summary>String constants for <see cref="PresetCommandPayload.Op"/>.</summary>
    public static class PresetCommandOps
    {
        public const string Create = "create";
        public const string Rename = "rename";
        public const string Duplicate = "duplicate";
        public const string Delete = "delete";
        public const string Activate = "activate";
    }
}
