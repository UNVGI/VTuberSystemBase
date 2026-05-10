#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles
{
    /// <summary>
    /// Test double for <see cref="IPreviewHandleResolver"/>. Tests can seed
    /// <see cref="Handles"/> with stub objects keyed by textureKey; resolves
    /// return them as <see cref="PreviewHandleResolution.Hit"/>. Unknown keys
    /// return <see cref="PreviewHandleResolution.Miss"/>.
    /// </summary>
    public sealed class FakePreviewHandleResolver : IPreviewHandleResolver
    {
        public Dictionary<string, object> Handles { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

        public List<string> Released { get; } = new List<string>();
        public List<string> Resolved { get; } = new List<string>();

        public Task<PreviewHandleResolution> ResolveAsync(string textureKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Resolved.Add(textureKey);
            return Handles.TryGetValue(textureKey, out var handle)
                ? Task.FromResult(PreviewHandleResolution.Hit(handle))
                : Task.FromResult(PreviewHandleResolution.Miss("not seeded"));
        }

        public void Release(string textureKey)
        {
            Released.Add(textureKey);
        }
    }
}
