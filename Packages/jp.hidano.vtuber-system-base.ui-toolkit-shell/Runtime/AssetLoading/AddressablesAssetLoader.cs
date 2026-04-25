#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.UiToolkitShell.AssetLoading
{
    /// <summary>
    /// Production <see cref="IAsyncAssetLoader"/> backed by Unity Addressables. Wraps
    /// <see cref="Addressables.LoadAssetAsync{TObject}(object)"/> + the
    /// <see cref="AsyncOperationHandle{TObject}.Completed"/> event so completion callbacks
    /// land on the Unity main thread without any synchronous wait — the type never calls
    /// <c>WaitForCompletion</c>, which is required to keep the main output frame from
    /// stalling while the UI side issues loads (Requirement 4.6, 11.3; design.md
    /// §AssetLoading §AddressablesAssetLoader).
    /// </summary>
    /// <remarks>
    /// Dedupe: a per-(key, type) shared entry caches the underlying
    /// <see cref="AsyncOperationHandle{TObject}"/>. A second <see cref="LoadAsync{T}"/>
    /// for the same key while the first is still in flight does not start a new load —
    /// it joins the existing entry and receives the same completion fan-out (Req 4.7).
    /// Scope tracking: every wrapper handle is indexed by its <c>scopeId</c> so
    /// <see cref="ReleaseAll(string)"/> can release every handle a tab spec acquired in
    /// one call (Req 4.8). The underlying Addressables handle is released only after
    /// every wrapper that joined it has been released, keeping ref-count semantics intact.
    /// </remarks>
    public sealed class AddressablesAssetLoader : IAsyncAssetLoader, IDisposable
    {
        private readonly IDiagnosticsLogger _logger;
        private readonly object _gate = new object();
        private readonly Dictionary<EntryKey, ISharedEntry> _shared = new Dictionary<EntryKey, ISharedEntry>();
        private readonly Dictionary<string, HashSet<IInternalHandle>> _byScope = new Dictionary<string, HashSet<IInternalHandle>>();
        private int _completedCount;
        private int _failedCount;
        private bool _disposed;

        public AddressablesAssetLoader(IDiagnosticsLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IAssetLoadHandle LoadAsync<T>(
            string addressableKey,
            string scopeId,
            Action<AssetLoadResult<T>> onCompleted)
            where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(addressableKey)) throw new ArgumentException("addressableKey must not be null or empty", nameof(addressableKey));
            if (string.IsNullOrEmpty(scopeId)) throw new ArgumentException("scopeId must not be null or empty", nameof(scopeId));
            if (onCompleted is null) throw new ArgumentNullException(nameof(onCompleted));

            var wrapper = new InternalHandle<T>(this, addressableKey, scopeId, onCompleted);
            SharedEntry<T> entry;
            bool startNewLoad;
            AssetLoadResult<T>? alreadySettled = null;

            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(AddressablesAssetLoader));

                var key = new EntryKey(addressableKey, typeof(T));
                if (_shared.TryGetValue(key, out var existing))
                {
                    entry = (SharedEntry<T>)existing;
                    startNewLoad = false;
                    if (entry.IsSettled)
                    {
                        alreadySettled = entry.SettledResult;
                    }
                }
                else
                {
                    entry = new SharedEntry<T>(addressableKey, typeof(T));
                    _shared[key] = entry;
                    startNewLoad = true;
                }

                wrapper.Entry = entry;
                entry.Subscribers.Add(wrapper);
                AddToScopeIndex(scopeId, wrapper);
            }

            _logger.Log(LogLevel.Info, LogCategory.AssetLoad,
                $"AssetLoadStarted key={addressableKey} scope={scopeId} type={typeof(T).Name}");

            if (startNewLoad)
            {
                try
                {
                    entry.Underlying = Addressables.LoadAssetAsync<T>(addressableKey);
                    entry.Underlying.Completed += op => OnUnderlyingCompleted(entry, op);
                }
                catch (Exception ex)
                {
                    var error = new LoadError(LoadErrorCode.AddressablesNotInitialized, addressableKey,
                        $"Addressables.LoadAssetAsync threw before scheduling: {ex.GetType().Name}", ex);
                    SettleEntry(entry, AssetLoadResult<T>.Fail(error));
                }
            }
            else if (alreadySettled.HasValue)
            {
                wrapper.DeliverSettled(alreadySettled.Value);
            }

            return wrapper;
        }

        public void Release(IAssetLoadHandle handle)
        {
            if (handle is null) throw new ArgumentNullException(nameof(handle));
            if (handle is not IInternalHandle internalHandle || internalHandle.Owner != this)
            {
                throw new ArgumentException("Handle was not produced by this AddressablesAssetLoader", nameof(handle));
            }
            internalHandle.ExecuteRelease();
        }

        public void ReleaseAll(string scopeId)
        {
            if (string.IsNullOrEmpty(scopeId)) throw new ArgumentException("scopeId must not be null or empty", nameof(scopeId));

            IInternalHandle[] toRelease;
            lock (_gate)
            {
                if (!_byScope.TryGetValue(scopeId, out var bucket) || bucket.Count == 0)
                {
                    return;
                }
                toRelease = new IInternalHandle[bucket.Count];
                bucket.CopyTo(toRelease);
            }
            foreach (var h in toRelease) h.ExecuteRelease();
        }

        public AssetLoaderSnapshot GetSnapshot()
        {
            lock (_gate)
            {
                var pendingByScope = new Dictionary<string, int>();
                int pending = 0;
                foreach (var kv in _byScope)
                {
                    int scopePending = 0;
                    foreach (var h in kv.Value)
                    {
                        if (h.State == AssetLoadState.Pending) scopePending++;
                    }
                    if (scopePending > 0)
                    {
                        pendingByScope[kv.Key] = scopePending;
                        pending += scopePending;
                    }
                }
                return new AssetLoaderSnapshot(
                    pendingCount: pending,
                    completedCount: _completedCount,
                    failedCount: _failedCount,
                    pendingByScope: pendingByScope);
            }
        }

        public void Dispose()
        {
            ISharedEntry[] entries;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                entries = new ISharedEntry[_shared.Count];
                _shared.Values.CopyTo(entries, 0);
                _shared.Clear();
                _byScope.Clear();
            }
            foreach (var entry in entries) entry.ReleaseUnderlying();
        }

        private void OnUnderlyingCompleted<T>(SharedEntry<T> entry, AsyncOperationHandle<T> op)
            where T : UnityEngine.Object
        {
            AssetLoadResult<T> result;
            if (op.Status == AsyncOperationStatus.Succeeded && op.Result != null)
            {
                result = AssetLoadResult<T>.Ok(op.Result);
            }
            else
            {
                var ex = op.OperationException;
                var code = ex is null ? LoadErrorCode.KeyNotFound : LoadErrorCode.IoFailure;
                result = AssetLoadResult<T>.Fail(new LoadError(
                    code,
                    entry.AddressableKey,
                    ex?.Message ?? "Addressables operation reported failure",
                    ex));
            }
            SettleEntry(entry, result);
        }

        private void SettleEntry<T>(SharedEntry<T> entry, AssetLoadResult<T> result)
            where T : UnityEngine.Object
        {
            InternalHandle<T>[] subscribers;
            lock (_gate)
            {
                if (entry.IsSettled) return;
                entry.IsSettled = true;
                entry.SettledResult = result;
                subscribers = entry.Subscribers.ToArray();
                if (result.Success) _completedCount++;
                else _failedCount++;
            }
            foreach (var sub in subscribers) sub.DeliverSettled(result);

            _logger.Log(
                result.Success ? LogLevel.Info : LogLevel.Warning,
                LogCategory.AssetLoad,
                result.Success
                    ? $"AssetLoadCompleted key={entry.AddressableKey} type={entry.AssetType.Name}"
                    : $"AssetLoadFailed key={entry.AddressableKey} type={entry.AssetType.Name} code={result.Error?.Code}");
        }

        private void AddToScopeIndex(string scopeId, IInternalHandle handle)
        {
            if (!_byScope.TryGetValue(scopeId, out var bucket))
            {
                bucket = new HashSet<IInternalHandle>();
                _byScope[scopeId] = bucket;
            }
            bucket.Add(handle);
        }

        private void RemoveFromScopeIndex(string scopeId, IInternalHandle handle)
        {
            if (_byScope.TryGetValue(scopeId, out var bucket))
            {
                bucket.Remove(handle);
                if (bucket.Count == 0) _byScope.Remove(scopeId);
            }
        }

        private void DropSubscriber<T>(SharedEntry<T> entry, InternalHandle<T> handle, bool releaseUnderlyingIfEmpty)
            where T : UnityEngine.Object
        {
            bool releaseUnderlying = false;
            lock (_gate)
            {
                entry.Subscribers.Remove(handle);
                RemoveFromScopeIndex(handle.ScopeId, handle);
                if (releaseUnderlyingIfEmpty && entry.Subscribers.Count == 0)
                {
                    _shared.Remove(new EntryKey(entry.AddressableKey, entry.AssetType));
                    releaseUnderlying = true;
                }
            }
            if (releaseUnderlying) entry.ReleaseUnderlying();
        }

        private readonly struct EntryKey : IEquatable<EntryKey>
        {
            public EntryKey(string addressableKey, Type assetType)
            {
                AddressableKey = addressableKey;
                AssetType = assetType;
            }

            public string AddressableKey { get; }
            public Type AssetType { get; }

            public bool Equals(EntryKey other) =>
                AddressableKey == other.AddressableKey && AssetType == other.AssetType;

            public override bool Equals(object? obj) => obj is EntryKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(AddressableKey, AssetType);
        }

        private interface ISharedEntry
        {
            string AddressableKey { get; }
            Type AssetType { get; }
            void ReleaseUnderlying();
        }

        private sealed class SharedEntry<T> : ISharedEntry where T : UnityEngine.Object
        {
            public SharedEntry(string key, Type assetType)
            {
                AddressableKey = key;
                AssetType = assetType;
            }

            public string AddressableKey { get; }
            public Type AssetType { get; }
            public AsyncOperationHandle<T> Underlying;
            public readonly List<InternalHandle<T>> Subscribers = new List<InternalHandle<T>>();
            public bool IsSettled;
            public AssetLoadResult<T> SettledResult;
            private bool _underlyingReleased;

            public void ReleaseUnderlying()
            {
                if (_underlyingReleased) return;
                if (!Underlying.IsValid()) return;
                _underlyingReleased = true;
                Addressables.Release(Underlying);
            }
        }

        private interface IInternalHandle : IAssetLoadHandle
        {
            object Owner { get; }
            string ScopeId { get; }
            void ExecuteRelease();
        }

        private sealed class InternalHandle<T> : IInternalHandle where T : UnityEngine.Object
        {
            private readonly AddressablesAssetLoader _owner;
            private readonly Action<AssetLoadResult<T>> _callback;
            private AssetLoadState _state = AssetLoadState.Pending;
            public SharedEntry<T> Entry = null!;

            public InternalHandle(AddressablesAssetLoader owner, string key, string scopeId, Action<AssetLoadResult<T>> callback)
            {
                _owner = owner;
                _callback = callback;
                AddressableKey = key;
                ScopeId = scopeId;
            }

            public string AddressableKey { get; }
            public string ScopeId { get; }
            public AssetLoadState State => _state;
            public object Owner => _owner;

            public void Cancel()
            {
                if (_state != AssetLoadState.Pending) return;
                _state = AssetLoadState.Cancelled;
                _owner.DropSubscriber(Entry, this, releaseUnderlyingIfEmpty: true);
                _callback(AssetLoadResult<T>.Fail(new LoadError(
                    LoadErrorCode.Cancelled,
                    AddressableKey,
                    "Cancelled via IAssetLoadHandle.Cancel")));
            }

            public void ExecuteRelease()
            {
                var prior = _state;
                if (prior == AssetLoadState.Released || prior == AssetLoadState.Cancelled) return;
                _state = AssetLoadState.Released;
                _owner.DropSubscriber(Entry, this, releaseUnderlyingIfEmpty: true);
                if (prior == AssetLoadState.Pending)
                {
                    _callback(AssetLoadResult<T>.Fail(new LoadError(
                        LoadErrorCode.Cancelled,
                        AddressableKey,
                        "Released while pending")));
                }
            }

            public void DeliverSettled(AssetLoadResult<T> result)
            {
                if (_state != AssetLoadState.Pending) return;
                _state = result.Success ? AssetLoadState.Completed : AssetLoadState.Failed;
                _callback(result);
            }
        }
    }
}
