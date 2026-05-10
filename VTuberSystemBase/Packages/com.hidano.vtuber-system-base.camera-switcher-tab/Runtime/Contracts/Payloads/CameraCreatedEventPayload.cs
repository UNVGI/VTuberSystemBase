namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Event payload for <see cref="CameraIpcTopics.CameraCreated"/>
    /// (<c>camera/created</c>, design.md L1268 / L1331-L1336). Sent by the
    /// main-output side once a UI <c>add</c> request has been honoured. The UI
    /// correlates this back to the original request via <see cref="ClientRequestId"/>.
    /// </summary>
    public readonly struct CameraCreatedEventPayload
    {
        /// <summary>GUID copied from the originating <see cref="CameraCommandPayload.ClientRequestId"/>.</summary>
        public string ClientRequestId { get; init; }

        /// <summary>The cameraId allocated by the main-output side.</summary>
        public string CameraId { get; init; }

        /// <summary>
        /// Full metadata for the newly-created camera (display name / type / default
        /// transform), redundantly carried so the UI can render the new card without
        /// waiting for a fresh <see cref="CamerasListPayload"/>.
        /// </summary>
        public CameraListEntry Metadata { get; init; }
    }
}
