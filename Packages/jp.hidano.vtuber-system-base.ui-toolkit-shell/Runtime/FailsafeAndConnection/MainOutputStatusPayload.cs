#nullable enable

namespace VTuberSystemBase.UiToolkitShell.FailsafeAndConnection
{
    /// <summary>
    /// Payload schema for the <c>output/display/fallback</c> state topic published by
    /// <c>output-renderer-shell</c> (spec #2). Mirrors the Display 1 fallback condition that the
    /// main-output side reports (OR-1): when <see cref="IsFallback"/> is <c>true</c>, the main
    /// output is being rendered on Display 1 instead of an external display, which raises an
    /// erroneous-broadcast risk that the UI surfaces through
    /// <c>NotificationBarController.ShowDisplayFallback</c>. See design.md
    /// §FailsafeAndConnection §MainOutputStatusWatcher (Requirements 9.6, 11.6).
    /// </summary>
    public sealed class MainOutputStatusPayload
    {
        public bool IsFallback { get; set; }

        public string? Reason { get; set; }
    }
}
