using RealtimeAvatarController.Core;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Doubles
{
    /// <summary>
    /// <see cref="RegistryLocator"/> 静的状態をテスト時に差し替えるためのヘルパ（Requirement 11.2）。
    /// SetUp で <see cref="RegistryLocator.ResetForTest"/> を呼び、
    /// <see cref="InMemoryProviderRegistry"/> / <see cref="InMemoryMoCapSourceRegistry"/> を Override する。
    /// </summary>
    public sealed class RacRegistryFixture
    {
        public InMemoryProviderRegistry ProviderRegistry { get; private set; }
        public InMemoryMoCapSourceRegistry MoCapSourceRegistry { get; private set; }
        public ISlotErrorChannel ErrorChannel { get; private set; }

        /// <summary>RAC グローバルレジストリを差し替える。</summary>
        public void SetUp(string providerTypeId = "Builtin", string moCapTypeId = "Stub")
        {
            RegistryLocator.ResetForTest();
            ProviderRegistry = new InMemoryProviderRegistry();
            ProviderRegistry.RegisterStub(providerTypeId);
            MoCapSourceRegistry = new InMemoryMoCapSourceRegistry();
            MoCapSourceRegistry.RegisterStub(moCapTypeId);
            RegistryLocator.OverrideProviderRegistry(ProviderRegistry);
            RegistryLocator.OverrideMoCapSourceRegistry(MoCapSourceRegistry);
            ErrorChannel = RegistryLocator.ErrorChannel; // 既定の DefaultSlotErrorChannel
        }

        /// <summary>テスト後にレジストリをリセットする。</summary>
        public void TearDown()
        {
            RegistryLocator.ResetForTest();
            ProviderRegistry = null;
            MoCapSourceRegistry = null;
            ErrorChannel = null;
        }
    }
}
