#nullable enable
using System;
using System.Collections;
using UnityEngine;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.OutputRendererShell.Scene;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Bootstrap
{
    /// <summary>
    /// Helper that polls <see cref="OutputSceneBootstrapper"/> until its diagnostics report
    /// <see cref="OutputSceneInitPhase.Complete"/>, then invokes
    /// <see cref="StageLightingVolumeOutputAdapterBootstrapper.TryStart"/>. Used when the
    /// output shell does not (yet) expose an explicit <c>OnInitComplete</c> event hook.
    /// </summary>
    public static class AdapterStartupRegistration
    {
        public const int DefaultMaxFrames = 60;

        public static IEnumerator WaitForOutputSceneAndStart(
            StageLightingVolumeOutputAdapterBootstrapper adapter,
            OutputSceneBootstrapper outputSceneBootstrapper,
            int maxFrames = DefaultMaxFrames)
        {
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            if (outputSceneBootstrapper == null) throw new ArgumentNullException(nameof(outputSceneBootstrapper));

            int frames = 0;
            while (frames++ < maxFrames)
            {
                var diag = outputSceneBootstrapper.Diagnostics;
                if (diag != null && diag.CurrentPhase == OutputSceneInitPhase.Complete)
                {
                    adapter.TryStart();
                    yield break;
                }
                yield return null;
            }
            // Best-effort fallback: try anyway so any partial dependency wires up; the
            // adapter's TryStart logs a warning when prerequisites are missing.
            adapter.TryStart();
        }
    }
}
