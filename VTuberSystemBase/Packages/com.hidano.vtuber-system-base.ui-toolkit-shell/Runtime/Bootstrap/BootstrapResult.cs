#nullable enable

namespace VTuberSystemBase.UiToolkitShell.Bootstrap
{
    /// <summary>
    /// Discriminated-union-style result for a bootstrap step. design.md §Bootstrap defines
    /// <c>UiShellBootstrapper.StartShell</c> as returning this struct so that failures
    /// surface as data rather than exceptions, letting <c>UiShellLifecycleDriver</c> abort
    /// startup safely without bringing the host process down (Requirement 9.1, 9.7).
    /// </summary>
    /// <remarks>
    /// On <see cref="Success"/> the <see cref="Error"/> is null. On failure the
    /// <see cref="Error"/> is populated with the originating <see cref="BootstrapErrorCode"/>
    /// and <see cref="Detail"/> carries an optional human-readable explanation that may be
    /// logged or surfaced through the diagnostics panel.
    /// </remarks>
    public readonly struct BootstrapResult
    {
        private BootstrapResult(bool success, BootstrapErrorCode? error, string? detail)
        {
            Success = success;
            Error = error;
            Detail = detail;
        }

        public bool Success { get; }

        public BootstrapErrorCode? Error { get; }

        public string? Detail { get; }

        public static BootstrapResult Ok() => new BootstrapResult(true, null, null);

        public static BootstrapResult Fail(BootstrapErrorCode code, string? detail = null)
            => new BootstrapResult(false, code, detail);
    }
}
