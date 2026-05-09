using System.Collections.Generic;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Event payload for <see cref="CameraIpcTopics.PreviewCommand"/>
    /// (<c>camera/preview/command</c>, design.md L1280). UI requests the
    /// main-output side to attach or detach preview render textures for a set
    /// of cameras.
    /// </summary>
    public readonly struct PreviewCommandPayload
    {
        /// <summary>One of <c>attach</c> / <c>detach</c>.</summary>
        public string Op { get; init; }

        /// <summary>Cameras affected by this attach / detach request.</summary>
        public IReadOnlyList<string> CameraIds { get; init; }

        /// <summary>Optional preview frame size <c>[width, height]</c> (attach only).</summary>
        public int[]? Size { get; init; }

        /// <summary>Optional preview frame rate cap (attach only).</summary>
        public int? Fps { get; init; }
    }

    /// <summary>String constants for <see cref="PreviewCommandPayload.Op"/>.</summary>
    public static class PreviewCommandOps
    {
        public const string Attach = "attach";
        public const string Detach = "detach";
    }
}
