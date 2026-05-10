using System;
using System.Collections.Generic;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.RacMainOutputAdapter.Diagnostics;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;
using VTuberSystemBase.RacMainOutputAdapter.Internal;

namespace VTuberSystemBase.RacMainOutputAdapter.Senders
{
    /// <summary>
    /// <c>avatars/catalog</c> の発行と、avatar 増減を購読層に通知する責務を担う
    /// （Requirement 6.2 / 6.4 / 6.6 / 6.7）。
    /// </summary>
    internal sealed class AvatarCatalogPublisher : IDisposable
    {
        private readonly IAdapterMessageSink _sink;
        private readonly IAvatarKeyResolver _keyResolver;
        private readonly PendingPublishQueue _pendingQueue;
        private readonly IDiagnosticsLogger _logger;
        private readonly HashSet<string> _knownKeys = new();

        private bool _subscribed;

        /// <summary>新規 avatar 発見通知（次回 catalog で含まれる avatarKey）。</summary>
        public event Action<string> OnAvatarAdded;

        /// <summary>avatar 削除通知。</summary>
        public event Action<string> OnAvatarRemoved;

        /// <summary>本 publisher を生成する。</summary>
        public AvatarCatalogPublisher(
            IAdapterMessageSink sink,
            IAvatarKeyResolver keyResolver,
            PendingPublishQueue pendingQueue,
            IDiagnosticsLogger logger)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _keyResolver = keyResolver ?? throw new ArgumentNullException(nameof(keyResolver));
            _pendingQueue = pendingQueue ?? throw new ArgumentNullException(nameof(pendingQueue));
            _logger = logger ?? new UnityConsoleDiagnosticsLogger();
        }

        /// <summary><see cref="IAvatarKeyResolver.OnAvatarKeysChanged"/> 購読開始。</summary>
        public void StartObserving()
        {
            if (_subscribed) return;
            _keyResolver.OnAvatarKeysChanged += HandleAvatarKeysChanged;
            _subscribed = true;
            // 初回 publish を保留キューに入れる（IPC 受信開始後に flush）。
            _pendingQueue.EnqueueOrExecute(_sink, sink => PublishCatalog(sink));
            // 初回 known set を最新化（OnAvatarAdded/Removed の差分検出用）。
            UpdateKnownSetSilently();
        }

        /// <summary>明示的に avatars/catalog を再 publish する。</summary>
        public void PublishCatalog(IAdapterMessageSink sink = null)
        {
            sink ??= _sink;
            try
            {
                var avatars = _keyResolver.AvatarKeys;
                var entries = new List<AvatarCatalogEntry>(avatars.Count);
                for (int i = 0; i < avatars.Count; i++)
                {
                    var a = avatars[i];
                    var key = a.AvatarKey ?? string.Empty;
                    var displayName = !string.IsNullOrEmpty(a.DisplayName) ? a.DisplayName : key;
                    entries.Add(new AvatarCatalogEntry { AvatarKey = key, DisplayName = displayName });
                }
                sink.PublishState(CharacterTopics.AvatarsCatalog, new AvatarCatalogPayload { Avatars = entries });
                _logger.Log(AdapterLogLevel.Debug, AdapterLogCategories.Catalog,
                    $"avatars/catalog publish count={entries.Count}");
            }
            catch (Exception ex)
            {
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Catalog,
                    "avatars/catalog publish failed (will retry on next change).", ex);
            }
        }

        private void HandleAvatarKeysChanged()
        {
            try
            {
                // 差分通知（Add/Remove イベント）。
                DetectAndDispatchDelta();
                // catalog 再 publish。
                _pendingQueue.EnqueueOrExecute(_sink, sink => PublishCatalog(sink));
            }
            catch (Exception ex)
            {
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Catalog,
                    "HandleAvatarKeysChanged failed.", ex);
            }
        }

        private void DetectAndDispatchDelta()
        {
            var current = _keyResolver.AvatarKeys;
            var newKeys = new HashSet<string>();
            for (int i = 0; i < current.Count; i++)
            {
                var k = current[i].AvatarKey;
                if (!string.IsNullOrEmpty(k)) newKeys.Add(k);
            }
            // Removed
            foreach (var k in _knownKeys)
            {
                if (!newKeys.Contains(k)) OnAvatarRemoved?.Invoke(k);
            }
            // Added
            foreach (var k in newKeys)
            {
                if (!_knownKeys.Contains(k)) OnAvatarAdded?.Invoke(k);
            }
            _knownKeys.Clear();
            foreach (var k in newKeys) _knownKeys.Add(k);
        }

        private void UpdateKnownSetSilently()
        {
            _knownKeys.Clear();
            var current = _keyResolver.AvatarKeys;
            for (int i = 0; i < current.Count; i++)
            {
                var k = current[i].AvatarKey;
                if (!string.IsNullOrEmpty(k)) _knownKeys.Add(k);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_subscribed)
            {
                _keyResolver.OnAvatarKeysChanged -= HandleAvatarKeysChanged;
                _subscribed = false;
            }
        }
    }
}
