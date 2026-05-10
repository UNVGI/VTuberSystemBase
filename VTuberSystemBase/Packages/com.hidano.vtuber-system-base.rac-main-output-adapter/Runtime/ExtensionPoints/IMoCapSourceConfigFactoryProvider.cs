namespace VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints
{
    /// <summary>
    /// Provides a MoCap source config factory for host-side dependency injection.
    /// </summary>
    /// <remarks>
    /// Implementations may return null while uninitialized. Hosts must tolerate that
    /// and keep the default StubMoCapSourceConfigFactory fallback behavior.
    /// </remarks>
    public interface IMoCapSourceConfigFactoryProvider
    {
        /// <summary>
        /// Gets the MoCap source config factory to inject into the RAC main output adapter.
        /// </summary>
        IMoCapSourceConfigFactory Factory { get; }
    }
}
