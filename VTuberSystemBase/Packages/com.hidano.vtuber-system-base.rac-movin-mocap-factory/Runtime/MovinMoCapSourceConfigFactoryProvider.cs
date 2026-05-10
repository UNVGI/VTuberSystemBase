using UnityEngine;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;

namespace VTuberSystemBase.RacMovinMoCapFactory
{
    /// <summary>
    /// Inspector-configurable provider for MOVIN MoCap source config factories.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("VTuberSystemBase/RAC MOVIN MoCap Factory Provider")]
    public sealed class MovinMoCapSourceConfigFactoryProvider : MonoBehaviour, IMoCapSourceConfigFactoryProvider
    {
        [Header("MOVIN Source Settings")]
        [SerializeField]
        [Range(1, 65535)]
        [Tooltip("MOVIN OSC listen port copied to MovinMoCapSourceConfig.port.")]
        private int port = MovinMoCapSourceConfigFactory.DefaultPort;

        [SerializeField]
        [Tooltip("Optional root bone name override copied to MovinMoCapSourceConfig.rootBoneName.")]
        private string rootBoneName = "";

        [SerializeField]
        [Tooltip("Optional bone class override copied to MovinMoCapSourceConfig.boneClass.")]
        private string boneClass = "";

        /// <inheritdoc />
        public IMoCapSourceConfigFactory Factory =>
            new MovinMoCapSourceConfigFactory(port, rootBoneName, boneClass);
    }
}
