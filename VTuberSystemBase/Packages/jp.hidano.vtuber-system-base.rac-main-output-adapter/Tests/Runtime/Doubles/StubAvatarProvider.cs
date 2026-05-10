using System.Threading;
using Cysharp.Threading.Tasks;
using RealtimeAvatarController.Core;
using UnityEngine;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Doubles
{
    /// <summary>
    /// テスト用 <see cref="IAvatarProvider"/>。<see cref="GameObject"/> を内部生成して返し、
    /// <see cref="ReleaseAvatar"/> で破棄する。テストで RAC <see cref="SlotManager"/> の
    /// <c>Active</c>/<c>Disposed</c> 遷移を再現する目的（Requirement 11.2）。
    /// </summary>
    public sealed class StubAvatarProvider : IAvatarProvider
    {
        private readonly string _slotId;
        private bool _disposed;

        /// <summary>テスト用 Provider を生成する。<paramref name="slotId"/> は GameObject 名に使う。</summary>
        public StubAvatarProvider(string slotId = "stub")
        {
            _slotId = slotId;
        }

        /// <inheritdoc/>
        public string ProviderType => "Stub";

        /// <inheritdoc/>
        public GameObject RequestAvatar(ProviderConfigBase config)
        {
            return new GameObject($"StubAvatar-{_slotId}");
        }

        /// <inheritdoc/>
        public UniTask<GameObject> RequestAvatarAsync(ProviderConfigBase config, CancellationToken cancellationToken = default)
        {
            return UniTask.FromResult(RequestAvatar(config));
        }

        /// <inheritdoc/>
        public void ReleaseAvatar(GameObject avatar)
        {
            if (avatar == null) return;
            // EditMode では DestroyImmediate、PlayMode では Destroy が必要。
            // テストフィクスチャは Tests.Runtime（PlayMode）を想定するため Destroy で問題ない。
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(avatar);
            else
                UnityEngine.Object.DestroyImmediate(avatar);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _disposed = true;
        }
    }
}
