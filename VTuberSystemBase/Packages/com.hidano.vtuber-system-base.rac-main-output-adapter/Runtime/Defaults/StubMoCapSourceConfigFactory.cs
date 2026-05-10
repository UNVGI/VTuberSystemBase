using RealtimeAvatarController.Core;
using UnityEngine;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;

namespace VTuberSystemBase.RacMainOutputAdapter.Defaults
{
    /// <summary>
    /// <see cref="IMoCapSourceConfigFactory"/> の既定実装。
    /// Stub MoCap Source 用 <see cref="MoCapSourceDescriptor"/> を Slot 単位で構築する（Requirement 8.4）。
    /// </summary>
    public sealed class StubMoCapSourceConfigFactory : IMoCapSourceConfigFactory
    {
        /// <summary>
        /// Stub Source の typeId 文字列。利用者プロジェクトは <c>RegistryLocator.MoCapSourceRegistry.Register</c>
        /// でこの typeId に対応する Stub IMoCapSourceFactory を登録する必要がある。
        /// 本 spec パッケージは Factory 自体を登録しない（Registry グローバル副作用を避けるため、
        /// 利用者プロジェクト or Tests/Doubles の責務）。
        /// </summary>
        public const string StubTypeId = "Stub";

        /// <inheritdoc/>
        public MoCapSourceDescriptor Build(string slotId)
        {
            var config = ScriptableObject.CreateInstance<StubMoCapSourceConfig>();
            config.name = $"StubMoCapSourceConfig_{slotId}";
            return new MoCapSourceDescriptor
            {
                SourceTypeId = StubTypeId,
                Config = config,
            };
        }
    }
}
