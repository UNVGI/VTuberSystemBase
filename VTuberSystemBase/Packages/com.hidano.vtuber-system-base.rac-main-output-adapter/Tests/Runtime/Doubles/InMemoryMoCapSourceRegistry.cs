using System.Collections.Generic;
using RealtimeAvatarController.Core;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Doubles
{
    /// <summary>
    /// テスト用 <see cref="IMoCapSourceRegistry"/>。typeId に <see cref="StubMoCapSource"/> を返す Factory を登録できる。
    /// 参照カウントは <see cref="DefaultMoCapSourceRegistry"/> 同等の扱い。
    /// </summary>
    public sealed class InMemoryMoCapSourceRegistry : IMoCapSourceRegistry
    {
        private readonly Dictionary<string, IMoCapSourceFactory> _factories = new();
        private readonly Dictionary<MoCapSourceDescriptor, Entry> _instances = new();

        /// <inheritdoc/>
        public void Register(string sourceTypeId, IMoCapSourceFactory factory)
        {
            if (_factories.ContainsKey(sourceTypeId))
                throw new RegistryConflictException(sourceTypeId, "InMemoryMoCapSourceRegistry");
            _factories[sourceTypeId] = factory;
        }

        /// <summary>任意 typeId に「<see cref="StubMoCapSource"/> を返す Factory」を登録するヘルパ。</summary>
        public void RegisterStub(string sourceTypeId)
        {
            Register(sourceTypeId, new StubFactory());
        }

        /// <inheritdoc/>
        public IMoCapSource Resolve(MoCapSourceDescriptor descriptor)
        {
            if (_instances.TryGetValue(descriptor, out var entry))
            {
                entry.RefCount++;
                _instances[descriptor] = entry;
                return entry.Source;
            }
            if (!_factories.TryGetValue(descriptor.SourceTypeId, out var factory))
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"sourceTypeId '{descriptor.SourceTypeId}' is not registered.");

            var source = factory.Create(descriptor.Config);
            _instances[descriptor] = new Entry { Source = source, RefCount = 1 };
            return source;
        }

        /// <inheritdoc/>
        public void Release(IMoCapSource source)
        {
            if (source == null) return;
            MoCapSourceDescriptor key = null;
            foreach (var kv in _instances)
            {
                if (ReferenceEquals(kv.Value.Source, source)) { key = kv.Key; break; }
            }
            if (key == null) return;
            var entry = _instances[key];
            entry.RefCount--;
            if (entry.RefCount <= 0)
            {
                _instances.Remove(key);
                entry.Source.Dispose();
            }
            else
            {
                _instances[key] = entry;
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> GetRegisteredTypeIds()
        {
            return new List<string>(_factories.Keys);
        }

        private struct Entry { public IMoCapSource Source; public int RefCount; }

        private sealed class StubFactory : IMoCapSourceFactory
        {
            public IMoCapSource Create(MoCapSourceConfigBase config) => new StubMoCapSource();
        }
    }
}
