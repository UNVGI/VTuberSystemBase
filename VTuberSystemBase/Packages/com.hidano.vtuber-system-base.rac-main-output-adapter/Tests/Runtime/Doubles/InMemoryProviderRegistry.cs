using System;
using System.Collections.Generic;
using RealtimeAvatarController.Core;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Doubles
{
    /// <summary>
    /// テスト用 <see cref="IProviderRegistry"/>。任意の typeId に <see cref="StubAvatarProvider"/> を返す Factory を登録できる。
    /// <see cref="RegistryLocator.OverrideProviderRegistry"/> 経由で注入する想定（Requirement 11.2）。
    /// </summary>
    public sealed class InMemoryProviderRegistry : IProviderRegistry
    {
        private readonly Dictionary<string, IAvatarProviderFactory> _factories = new();

        /// <inheritdoc/>
        public void Register(string providerTypeId, IAvatarProviderFactory factory)
        {
            if (_factories.ContainsKey(providerTypeId))
                throw new RegistryConflictException(providerTypeId, "InMemoryProviderRegistry");
            _factories[providerTypeId] = factory;
        }

        /// <summary>
        /// 任意の typeId に「<see cref="StubAvatarProvider"/> を返す Factory」を登録する利便ヘルパ。
        /// </summary>
        public void RegisterStub(string providerTypeId)
        {
            Register(providerTypeId, new StubFactory(providerTypeId));
        }

        /// <inheritdoc/>
        public IAvatarProvider Resolve(AvatarProviderDescriptor descriptor)
        {
            if (!_factories.TryGetValue(descriptor.ProviderTypeId, out var factory))
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"providerTypeId '{descriptor.ProviderTypeId}' is not registered.");
            return factory.Create(descriptor.Config);
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> GetRegisteredTypeIds()
        {
            return new List<string>(_factories.Keys);
        }

        private sealed class StubFactory : IAvatarProviderFactory
        {
            private readonly string _typeId;

            public StubFactory(string typeId) { _typeId = typeId; }

            public IAvatarProvider Create(ProviderConfigBase config)
            {
                return new StubAvatarProvider(_typeId);
            }
        }
    }
}
