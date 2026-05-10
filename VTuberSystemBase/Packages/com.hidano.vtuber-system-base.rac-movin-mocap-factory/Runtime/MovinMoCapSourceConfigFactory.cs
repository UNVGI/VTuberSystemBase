using RealtimeAvatarController.Core;
using RealtimeAvatarController.MoCap.Movin;
using UnityEngine;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;

namespace VTuberSystemBase.RacMovinMoCapFactory
{
    /// <summary>
    /// Builds MOVIN MoCap source descriptors for RAC slots.
    /// </summary>
    public sealed class MovinMoCapSourceConfigFactory : IMoCapSourceConfigFactory
    {
        public const int DefaultPort = 11235;

        public MovinMoCapSourceConfigFactory(
            int port = DefaultPort,
            string rootBoneName = "",
            string boneClass = "")
        {
            Port = port;
            RootBoneName = rootBoneName ?? string.Empty;
            BoneClass = boneClass ?? string.Empty;
        }

        public int Port { get; }

        public string RootBoneName { get; }

        public string BoneClass { get; }

        public MoCapSourceDescriptor Build(string slotId)
        {
            var config = ScriptableObject.CreateInstance<MovinMoCapSourceConfig>();
            config.name = $"MovinMoCapSourceConfig_{slotId}";
            config.port = Port;
            config.rootBoneName = RootBoneName;
            config.boneClass = BoneClass;

            return new MoCapSourceDescriptor
            {
                SourceTypeId = MovinMoCapSourceFactory.MovinSourceTypeId,
                Config = config,
            };
        }
    }
}
