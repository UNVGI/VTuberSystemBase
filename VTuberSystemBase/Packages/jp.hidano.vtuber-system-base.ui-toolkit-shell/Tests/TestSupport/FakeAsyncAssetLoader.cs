#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.UiToolkitShell.AssetLoading;

namespace VTuberSystemBase.UiToolkitShell.Tests.TestSupport
{
    /// <summary>
    /// Test double for <see cref="IAsyncAssetLoader"/>. Lets tests control the timing and outcome
    /// of every <c>LoadAsync</c> call without exercising Unity Addressables. Behaviour is
    /// intentionally programmable along four axes (Requirement 10.7):
    /// <list type="bullet">
    ///   <item>Immediate vs. deferred completion (see <see cref="Mode"/>).</item>
    ///   <item>Per-key registration of success values (<see cref="RegisterAsset"/>) or
    ///         failures (<see cref="RegisterFailure"/>).</item>
    ///   <item>Manual completion of pending handles via
    ///         <see cref="CompleteWith{T}"/> / <see cref="FailWith"/>.</item>
    ///   <item>Cooperative cancellation via <see cref="IAssetLoadHandle.Cancel"/>.</item>
    /// </list>
    /// The diagnostics snapshot returned from <see cref="GetSnapshot"/> tracks live counters,
    /// but tests can override the value entirely via <see cref="SnapshotOverride"/>.
    /// </summary>
    public sealed class FakeAsyncAssetLoader : IAsyncAssetLoader
    {
        public enum CompletionMode
        {
            /// <summary>LoadAsync resolves before returning, using registered assets/failures.</summary>
            Immediate,
            /// <summary>LoadAsync stores the handle as pending until a test resolves it explicitly.</summary>
            Deferred,
        }

        private readonly object syncRoot = new();
        private readonly List<IFakeHandle> liveHandles = new();
        private readonly Dictionary<string, UnityEngine.Object> registeredAssets = new();
        private readonly Dictionary<string, LoadError> registeredFailures = new();
        private int completedCount;
        private int failedCount;
        private int cancelledCount;
        private int releasedCount;

        /// <summary>Default completion mode applied to subsequent <c>LoadAsync</c> calls.</summary>
        public CompletionMode Mode { get; set; } = CompletionMode.Deferred;

        /// <summary>When set, <see cref="GetSnapshot"/> returns this value instead of the live one.</summary>
        public AssetLoaderSnapshot? SnapshotOverride { get; set; }

        public int CompletedCount => completedCount;
        public int FailedCount => failedCount;
        public int CancelledCount => cancelledCount;
        public int ReleasedCount => releasedCount;

        /// <summary>Snapshot (copy) of the handles that are currently in <see cref="AssetLoadState.Pending"/>.</summary>
        public IReadOnlyList<IAssetLoadHandle> PendingHandles
        {
            get
            {
                lock (syncRoot)
                {
                    var pending = new List<IAssetLoadHandle>();
                    foreach (var h in liveHandles)
                    {
                        if (h.State == AssetLoadState.Pending) pending.Add(h);
                    }
                    return pending;
                }
            }
        }

        /// <summary>Pre-stage an asset to be returned for <paramref name="addressableKey"/> in immediate mode,
        /// or via <see cref="ResolvePending(string)"/> in deferred mode.</summary>
        public void RegisterAsset(string addressableKey, UnityEngine.Object asset)
        {
            if (string.IsNullOrEmpty(addressableKey)) throw new ArgumentException("addressableKey must not be null or empty", nameof(addressableKey));
            if (asset is null) throw new ArgumentNullException(nameof(asset));
            lock (syncRoot)
            {
                registeredAssets[addressableKey] = asset;
                registeredFailures.Remove(addressableKey);
            }
        }

        /// <summary>Pre-stage a failure for <paramref name="addressableKey"/>.</summary>
        public void RegisterFailure(string addressableKey, LoadError error)
        {
            if (string.IsNullOrEmpty(addressableKey)) throw new ArgumentException("addressableKey must not be null or empty", nameof(addressableKey));
            lock (syncRoot)
            {
                registeredFailures[addressableKey] = error;
                registeredAssets.Remove(addressableKey);
            }
        }

        /// <summary>Clears all <see cref="RegisterAsset"/> / <see cref="RegisterFailure"/> entries.</summary>
        public void ClearRegistrations()
        {
            lock (syncRoot)
            {
                registeredAssets.Clear();
                registeredFailures.Clear();
            }
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

            var handle = new FakeHandle<T>(this, addressableKey, scopeId, onCompleted);
            lock (syncRoot)
            {
                liveHandles.Add(handle);
            }

            if (Mode == CompletionMode.Immediate)
            {
                ResolveFromRegistrations(handle);
            }

            return handle;
        }

        public void Release(IAssetLoadHandle handle)
        {
            if (handle is null) throw new ArgumentNullException(nameof(handle));
            if (handle is not IFakeHandle fake)
            {
                throw new ArgumentException("Handle was not produced by this FakeAsyncAssetLoader", nameof(handle));
            }
            ReleaseInternal(fake);
        }

        public void ReleaseAll(string scopeId)
        {
            if (string.IsNullOrEmpty(scopeId)) throw new ArgumentException("scopeId must not be null or empty", nameof(scopeId));
            IFakeHandle[] toRelease;
            lock (syncRoot)
            {
                var list = new List<IFakeHandle>();
                foreach (var h in liveHandles)
                {
                    if (h.ScopeId == scopeId) list.Add(h);
                }
                toRelease = list.ToArray();
            }
            foreach (var h in toRelease) ReleaseInternal(h);
        }

        public AssetLoaderSnapshot GetSnapshot()
        {
            if (SnapshotOverride.HasValue) return SnapshotOverride.Value;
            lock (syncRoot)
            {
                var pendingByScope = new Dictionary<string, int>();
                int pending = 0;
                foreach (var h in liveHandles)
                {
                    if (h.State != AssetLoadState.Pending) continue;
                    pending++;
                    pendingByScope.TryGetValue(h.ScopeId, out var current);
                    pendingByScope[h.ScopeId] = current + 1;
                }
                return new AssetLoaderSnapshot(
                    pendingCount: pending,
                    completedCount: completedCount,
                    failedCount: failedCount,
                    pendingByScope: pendingByScope);
            }
        }

        /// <summary>Completes a specific pending handle with <paramref name="asset"/>.
        /// Returns false if the handle is not pending or the type does not match.</summary>
        public bool CompleteWith<T>(IAssetLoadHandle handle, T asset)
            where T : UnityEngine.Object
        {
            if (handle is null) throw new ArgumentNullException(nameof(handle));
            if (asset is null) throw new ArgumentNullException(nameof(asset));
            if (handle is FakeHandle<T> typed)
            {
                return typed.DispatchSuccess(asset);
            }
            return false;
        }

        /// <summary>Fails a specific pending handle with <paramref name="error"/>.</summary>
        public bool FailWith(IAssetLoadHandle handle, LoadError error)
        {
            if (handle is null) throw new ArgumentNullException(nameof(handle));
            if (handle is IFakeHandle fake)
            {
                return fake.DispatchFailure(error);
            }
            return false;
        }

        /// <summary>Resolves every currently pending handle whose key has a matching
        /// <see cref="RegisterAsset"/> or <see cref="RegisterFailure"/> entry. Useful for
        /// flush-style assertions in deferred-mode tests.</summary>
        public void ResolveAllPending()
        {
            IFakeHandle[] snapshot;
            lock (syncRoot)
            {
                var list = new List<IFakeHandle>();
                foreach (var h in liveHandles)
                {
                    if (h.State == AssetLoadState.Pending) list.Add(h);
                }
                snapshot = list.ToArray();
            }
            foreach (var h in snapshot) h.ResolveFromRegistrations();
        }

        /// <summary>Resolves pending handles whose key matches <paramref name="addressableKey"/>.</summary>
        public void ResolvePending(string addressableKey)
        {
            IFakeHandle[] snapshot;
            lock (syncRoot)
            {
                var list = new List<IFakeHandle>();
                foreach (var h in liveHandles)
                {
                    if (h.State == AssetLoadState.Pending && h.AddressableKey == addressableKey) list.Add(h);
                }
                snapshot = list.ToArray();
            }
            foreach (var h in snapshot) h.ResolveFromRegistrations();
        }

        private void ResolveFromRegistrations(IFakeHandle handle)
        {
            handle.ResolveFromRegistrations();
        }

        private bool TryGetRegisteredFailure(string key, out LoadError error)
        {
            lock (syncRoot)
            {
                return registeredFailures.TryGetValue(key, out error);
            }
        }

        private bool TryGetRegisteredAsset(string key, out UnityEngine.Object? asset)
        {
            lock (syncRoot)
            {
                if (registeredAssets.TryGetValue(key, out var obj))
                {
                    asset = obj;
                    return true;
                }
                asset = null;
                return false;
            }
        }

        private void ReleaseInternal(IFakeHandle handle)
        {
            handle.Release();
        }

        private void RemoveLive(IFakeHandle handle)
        {
            lock (syncRoot)
            {
                liveHandles.Remove(handle);
            }
        }

        private void IncrementCompleted() => System.Threading.Interlocked.Increment(ref completedCount);
        private void IncrementFailed() => System.Threading.Interlocked.Increment(ref failedCount);
        private void IncrementCancelled() => System.Threading.Interlocked.Increment(ref cancelledCount);
        private void IncrementReleased() => System.Threading.Interlocked.Increment(ref releasedCount);

        private interface IFakeHandle : IAssetLoadHandle
        {
            void ResolveFromRegistrations();
            bool DispatchFailure(LoadError error);
            void Release();
        }

        private sealed class FakeHandle<T> : IFakeHandle
            where T : UnityEngine.Object
        {
            private readonly FakeAsyncAssetLoader owner;
            private readonly Action<AssetLoadResult<T>> callback;
            private AssetLoadState state = AssetLoadState.Pending;

            public FakeHandle(FakeAsyncAssetLoader owner, string key, string scopeId, Action<AssetLoadResult<T>> callback)
            {
                this.owner = owner;
                AddressableKey = key;
                ScopeId = scopeId;
                this.callback = callback;
            }

            public string AddressableKey { get; }
            public string ScopeId { get; }
            public AssetLoadState State => state;

            public void Cancel()
            {
                if (state != AssetLoadState.Pending) return;
                state = AssetLoadState.Cancelled;
                owner.IncrementCancelled();
                owner.RemoveLive(this);
                callback(AssetLoadResult<T>.Fail(new LoadError(
                    LoadErrorCode.Cancelled,
                    AddressableKey,
                    "Cancelled by handle.Cancel()")));
            }

            public bool DispatchSuccess(T asset)
            {
                if (state != AssetLoadState.Pending) return false;
                state = AssetLoadState.Completed;
                owner.IncrementCompleted();
                owner.RemoveLive(this);
                callback(AssetLoadResult<T>.Ok(asset));
                return true;
            }

            public bool DispatchFailure(LoadError error)
            {
                if (state != AssetLoadState.Pending) return false;
                state = AssetLoadState.Failed;
                owner.IncrementFailed();
                owner.RemoveLive(this);
                callback(AssetLoadResult<T>.Fail(error));
                return true;
            }

            public void Release()
            {
                if (state == AssetLoadState.Pending)
                {
                    state = AssetLoadState.Released;
                    owner.IncrementReleased();
                    owner.RemoveLive(this);
                    callback(AssetLoadResult<T>.Fail(new LoadError(
                        LoadErrorCode.Cancelled,
                        AddressableKey,
                        "Released while pending")));
                    return;
                }
                if (state == AssetLoadState.Completed)
                {
                    state = AssetLoadState.Released;
                    owner.IncrementReleased();
                    owner.RemoveLive(this);
                }
            }

            public void ResolveFromRegistrations()
            {
                if (state != AssetLoadState.Pending) return;
                if (owner.TryGetRegisteredFailure(AddressableKey, out var err))
                {
                    DispatchFailure(err);
                    return;
                }
                if (owner.TryGetRegisteredAsset(AddressableKey, out var obj))
                {
                    if (obj is T typed)
                    {
                        DispatchSuccess(typed);
                        return;
                    }
                    DispatchFailure(new LoadError(
                        LoadErrorCode.AssetTypeMismatch,
                        AddressableKey,
                        $"Registered asset is {obj?.GetType().FullName} but caller requested {typeof(T).FullName}"));
                    return;
                }
                DispatchFailure(new LoadError(
                    LoadErrorCode.KeyNotFound,
                    AddressableKey,
                    "No asset or failure registered for this key in immediate mode"));
            }
        }
    }
}
