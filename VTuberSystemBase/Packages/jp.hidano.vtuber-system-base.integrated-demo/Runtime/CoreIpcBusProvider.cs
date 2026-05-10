#nullable enable
using UnityEngine;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using StageProvider = VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal.ICoreIpcBusProvider;
using RacProvider = VTuberSystemBase.RacMainOutputAdapter.Bootstrapper.ICoreIpcBusProvider;

namespace VTuberSystemBase.IntegratedDemo
{
    /// <summary>
    /// シーンに 1 つだけ配置する MonoBehaviour。<see cref="CoreIpcRuntime.Current"/> から
    /// 取得した <see cref="ICoreIpcBus"/> を保持し、3 アダプタが要求する各 <c>ICoreIpcBusProvider</c>
    /// インタフェース（stage-lighting-volume-output-adapter / rac-main-output-adapter のもの）を
    /// 同時に実装する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>RuntimeBootstrap との関係</b>:
    /// <c>core-ipc-foundation</c> パッケージは <c>[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]</c>
    /// で <see cref="CoreIpcRuntime.Current"/> を自動起動するため、本コンポーネントの <see cref="Awake"/>
    /// 時点で <c>Bus</c> は通常 non-null になる。初期化が間に合わない場合（例: テスト時の
    /// <c>RuntimeBootstrap.DisableAutoBootstrap()</c>）は <see cref="Bus"/> が null を返し、
    /// 各アダプタが既定の警告ログを出してから無効化される（破壊的失敗にしない）。
    /// </para>
    /// <para>
    /// <b>camera-switcher-output-adapter について</b>:
    /// 当該アダプタは独自の <c>ICoreIpcBusProvider</c> を持たず、
    /// <c>CameraSwitcherOutputAdapterBootstrapper.InjectForTesting(bus, dispatcher, sceneRoots)</c>
    /// にコードから直接 bus を渡す経路を使う（<see cref="IntegratedDemoBootstrap"/> 内で実施）。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CoreIpcBusProvider : MonoBehaviour, StageProvider, RacProvider
    {
        private ICoreIpcBus? _resolvedBus;

        /// <summary>
        /// 解決済み <see cref="ICoreIpcBus"/>。初回アクセス時に
        /// <see cref="CoreIpcRuntime.Current"/> を読み出してキャッシュする。
        /// </summary>
        public ICoreIpcBus? Bus => ResolveBus();

        // RAC adapter expects a property named CoreIpcBus.
        ICoreIpcBus RacProvider.CoreIpcBus => ResolveBus()!;

        // Stage-lighting-volume adapter expects a property named Bus (already implemented).
        ICoreIpcBus? StageProvider.Bus => ResolveBus();

        private ICoreIpcBus? ResolveBus()
        {
            if (_resolvedBus != null) return _resolvedBus;
            var runtime = CoreIpcRuntime.Current;
            if (runtime == null)
            {
                Debug.LogWarning(
                    "[IntegratedDemo.CoreIpcBusProvider] CoreIpcRuntime.Current is null; " +
                    "RuntimeBootstrap may not have completed yet. " +
                    "Adapters will defer initialization until the bus becomes available.");
                return null;
            }
            _resolvedBus = runtime.Bus;
            return _resolvedBus;
        }
    }
}
