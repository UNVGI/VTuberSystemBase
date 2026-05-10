namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// State payload for <see cref="CameraIpcTopics.PreviewHandle(string)"/>
    /// (<c>camera/{cameraId}/preview/handle</c>, design.md L1281). Published by
    /// the main-output side after an <c>attach</c> command. The UI resolves the
    /// concrete <c>RenderTexture</c> via a Service Locator keyed by
    /// <see cref="TextureKey"/> — the actual texture object never travels over IPC.
    /// </summary>
    public readonly struct PreviewHandleStatePayload
    {
        /// <summary>Service-Locator key the UI uses to resolve the RenderTexture.</summary>
        public string TextureKey { get; init; }

        /// <summary>Allocated frame size <c>[width, height]</c>.</summary>
        public int[] Size { get; init; }

        /// <summary>Allocated frame rate cap.</summary>
        public int Fps { get; init; }
    }
}
