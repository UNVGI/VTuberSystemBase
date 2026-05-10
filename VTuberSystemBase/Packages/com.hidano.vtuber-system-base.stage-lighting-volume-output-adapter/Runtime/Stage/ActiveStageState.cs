#nullable enable
using UnityEngine;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Stage
{
    /// <summary>
    /// Mutable state for the currently mounted stage instance. Single-threaded (main
    /// thread) access is assumed; the <see cref="StageHandler"/> guards transitions.
    /// </summary>
    internal sealed class ActiveStageState
    {
        public GameObject? CurrentStage { get; private set; }
        public string? CurrentAddressableKey { get; private set; }
        public bool IsLoading { get; private set; }

        public void SetLoading(bool loading) => IsLoading = loading;

        public void SetActive(GameObject stage, string key)
        {
            CurrentStage = stage;
            CurrentAddressableKey = key;
            IsLoading = false;
        }

        public void Clear()
        {
            CurrentStage = null;
            CurrentAddressableKey = null;
            IsLoading = false;
        }
    }
}
