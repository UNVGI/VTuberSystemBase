#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Bootstrap;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Skin;

// Tab packages.
using VTuberSystemBase.CharacterSelectionTab.Bootstrap;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.StageLightingVolumeTab.Bootstrap;
using VTuberSystemBase.StageLightingVolumeTab.Preview;
using VTuberSystemBase.StageLightingVolumeTab.Services;
using VTuberSystemBase.CameraSwitcherTab.Bootstrap;

namespace VTuberSystemBase.IntegratedDemo
{
    /// <summary>
    /// 3 タブの UXML を Tab Roots として root に attach する <see cref="ITabMountStrategy"/> 実装。
    /// </summary>
    /// <remarks>
    /// <para>
    /// UI shell の design.md (L676) では「各タブ spec は UiShellBootstrapper 起動後に
    /// RegisterTab を呼ぶ」流れになっており、ITabMountStrategy.MountTabs は CommandClient /
    /// SubscriptionClient 等のサブシステムが構築される **前** に呼ばれる。よってここでは
    /// UXML を root に attach し <see cref="ITabPanelRegistry.NotifyTabMounted(TabId, VisualElement)"/>
    /// を呼ぶだけに留める。タブ spec の Bootstrapper 構築は <see cref="IntegratedDemoBootstrap"/>
    /// が shell 起動完了 (UiShellLifecycleDriver.IsRunning == true) を見届けた後に
    /// <see cref="IntegratedTabBootstrapperLauncher.LaunchAll"/> で行う。
    /// </para>
    /// </remarks>
    public sealed class IntegratedTabMountStrategy : ITabMountStrategy
    {
        private readonly UiToolkitShellSkinProfile _skinProfile;
        private readonly Dictionary<TabId, VisualElement> _tabRoots = new Dictionary<TabId, VisualElement>();
        private VisualElement? _shellRoot;

        public IntegratedTabMountStrategy(UiToolkitShellSkinProfile skinProfile)
        {
            _skinProfile = skinProfile ?? throw new ArgumentNullException(nameof(skinProfile));
        }

        /// <summary>shell 起動完了後にタブ Bootstrapper 構築側が参照する Tab Root マップ。</summary>
        public IReadOnlyDictionary<TabId, VisualElement> TabRoots => _tabRoots;

        /// <summary>shell の root VisualElement（タブの親）。</summary>
        public VisualElement? ShellRoot => _shellRoot;

        public bool MountTabs(TabMountContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            _shellRoot = context.RootVisualElement;
            var registry = context.Registry;
            var logger = context.Logger;

            MountSingle(registry, _shellRoot, logger,
                tabId: TabId.Character,
                tabAsset: _skinProfile.CharacterTabVisualTreeAsset,
                placeholderRootName: "vsb-char-tab");
            MountSingle(registry, _shellRoot, logger,
                tabId: TabId.StageLighting,
                tabAsset: _skinProfile.StageLightingTabVisualTreeAsset,
                placeholderRootName: "vsb-stage-tab");
            MountSingle(registry, _shellRoot, logger,
                tabId: TabId.CameraSwitcher,
                tabAsset: _skinProfile.CameraSwitcherTabVisualTreeAsset,
                placeholderRootName: "vsb-cam-tab");

            return true;
        }

        private void MountSingle(
            ITabPanelRegistry registry,
            VisualElement parentRoot,
            IDiagnosticsLogger logger,
            TabId tabId,
            VisualTreeAsset? tabAsset,
            string placeholderRootName)
        {
            try
            {
                var tabRoot = BuildTabRoot(tabAsset, placeholderRootName);
                parentRoot.Add(tabRoot);
                tabRoot.style.display = DisplayStyle.None;
                _tabRoots[tabId] = tabRoot;
                registry.NotifyTabMounted(tabId, tabRoot);
            }
            catch (Exception ex)
            {
                logger.Log(LogLevel.Error, LogCategory.Lifecycle,
                    $"IntegratedTabMountStrategy: {tabId} mount failed: {ex.Message}", ex);
                registry.MarkTabFailed(tabId, $"mount failure: {ex.Message}");
            }
        }

        private static VisualElement BuildTabRoot(
            VisualTreeAsset? tabAsset,
            string placeholderRootName)
        {
            if (tabAsset != null)
            {
                var template = tabAsset.CloneTree();
                template.name = placeholderRootName;
                return template;
            }
            // Skin がタブ用 UXML を持たない場合の placeholder。
            // タブ Bootstrapper はおそらく region 名の Q を失敗するので MarkTabFailed で記録する経路に乗る。
            var placeholder = new VisualElement { name = placeholderRootName };
            placeholder.AddToClassList("vsb-integrated-demo-placeholder");
            return placeholder;
        }
    }

    /// <summary>
    /// shell 起動完了後にタブ Bootstrapper 群を立ち上げ、IDisposable を保持するラウンチャ。
    /// </summary>
    public sealed class IntegratedTabBootstrapperLauncher : IDisposable
    {
        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private bool _disposed;

        public IReadOnlyList<IDisposable> Disposables => _disposables;
        public bool IsLaunched { get; private set; }

        /// <summary>3 タブ Bootstrapper を全部構築する。複数回呼んでも初回のみ実行する。</summary>
        public void LaunchAll(
            IntegratedTabMountStrategy strategy,
            ITabPanelRegistry registry,
            UiShellAccess access,
            string? cameraOscHost,
            int cameraOscPort,
            string? cameraPresetPath)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(IntegratedTabBootstrapperLauncher));
            if (IsLaunched) return;
            IsLaunched = true;

            if (access.CommandClient == null
                || access.SubscriptionClient == null
                || access.ConnectionStatus == null
                || access.AssetLoader == null)
            {
                Debug.LogError(
                    "[IntegratedTabBootstrapperLauncher] UI shell subsystems are not ready; "
                    + "tab Bootstrappers cannot be launched.");
                return;
            }

            // Character tab.
            TryLaunch(TabId.Character, () =>
            {
                if (!strategy.TabRoots.TryGetValue(TabId.Character, out var root)) return null;
                // RegisterTab は character tab Bootstrapper の引数だが、ITabMountStrategy 内で
                // NotifyTabMounted のみ呼んだ状態（RegisterTab 未実施）なので、ここで呼ぶ必要がある。
                var handle = registry.RegisterTab(TabId.Character, new TabMetadata("Character"));
                var presetStorage = new VTuberSystemBase.CharacterSelectionTab.Services.JsonPresetStorage(
                    null, access.Logger);
                var clock = new VTuberSystemBase.CharacterSelectionTab.Services.SystemClock();
                return new CharacterTabBootstrapper(
                    handle,
                    access.CommandClient,
                    access.SubscriptionClient,
                    access.ConnectionStatus,
                    access.AssetLoader,
                    access.Logger,
                    presetStorage,
                    clock,
                    root);
            });

            // Stage / Lighting / Volume tab. その Bootstrapper は内部で RegisterTab + NotifyTabMounted
            // を再度呼ぶが、Mount Strategy で先に NotifyTabMounted を呼んでいるため、Stage Bootstrapper 内の
            // RegisterTab(StageLighting,...) が「同 ID 二重」例外を投げる可能性がある。
            // 設計回避策: stage tab だけは Mount Strategy で NotifyTabMounted を呼ばず、
            // Bootstrapper に任せる。先に Tab Root を attach 済みなので NotifyTabMounted の root binding
            // を Bootstrapper 側がやることになる。
            TryLaunch(TabId.StageLighting, () =>
            {
                if (!strategy.TabRoots.TryGetValue(TabId.StageLighting, out var root)) return null;
                var stageStoragePath = Path.Combine(
                    Application.persistentDataPath, "stage-lighting-volume-tab", "presets.json");
                Directory.CreateDirectory(Path.GetDirectoryName(stageStoragePath)!);
                var presetStorage = new VTuberSystemBase.StageLightingVolumeTab.Services.JsonPresetStorage(
                    stageStoragePath, access.Logger);
                var clock = new VTuberSystemBase.StageLightingVolumeTab.Services.SystemClock();
                var previewAccessor = new PreviewRenderTextureAccessor();
                var previewCamera = new SceneViewStylePreviewCameraAdapter();
                return new StageLightingVolumeTabBootstrapper(
                    registry,
                    root,
                    access.CommandClient,
                    access.SubscriptionClient,
                    access.AssetLoader,
                    access.ConnectionStatus,
                    access.Logger,
                    presetStorage,
                    previewAccessor,
                    previewCamera,
                    clock);
            });

            // Camera switcher tab.
            TryLaunch(TabId.CameraSwitcher, () =>
            {
                if (!strategy.TabRoots.TryGetValue(TabId.CameraSwitcher, out var root)) return null;
                var handle = registry.RegisterTab(TabId.CameraSwitcher, new TabMetadata("Camera Switcher"));
                return new CameraSwitcherTabBootstrapper(
                    handle,
                    access.CommandClient,
                    access.SubscriptionClient,
                    access.ConnectionStatus,
                    access.AssetLoader,
                    access.Logger,
                    root,
                    string.IsNullOrEmpty(cameraOscHost) ? null : cameraOscHost,
                    cameraOscPort > 0 ? cameraOscPort : (int?)null,
                    string.IsNullOrEmpty(cameraPresetPath) ? null : cameraPresetPath,
                    previewResolverOverride: null);
            });
        }

        private void TryLaunch(TabId tabId, Func<IDisposable?> factory)
        {
            try
            {
                var disposable = factory();
                if (disposable != null) _disposables.Add(disposable);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[IntegratedTabBootstrapperLauncher] {tabId} bootstrapper failed: {ex.Message}\n{ex}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = _disposables.Count - 1; i >= 0; i--)
            {
                try { _disposables[i].Dispose(); } catch { /* shutdown best-effort */ }
            }
            _disposables.Clear();
        }
    }

    /// <summary>
    /// <see cref="UiShellBootstrapper"/> から取り出した IPC ファサード一式。
    /// </summary>
    public sealed class UiShellAccess
    {
        public UiShellAccess(
            VTuberSystemBase.UiToolkitShell.Commands.IUiCommandClient? commandClient,
            VTuberSystemBase.UiToolkitShell.Commands.IUiSubscriptionClient? subscriptionClient,
            VTuberSystemBase.UiToolkitShell.Commands.IConnectionStatus? connectionStatus,
            VTuberSystemBase.UiToolkitShell.AssetLoading.IAsyncAssetLoader? assetLoader,
            IDiagnosticsLogger? logger)
        {
            CommandClient = commandClient;
            SubscriptionClient = subscriptionClient;
            ConnectionStatus = connectionStatus;
            AssetLoader = assetLoader;
            Logger = logger;
        }

        public VTuberSystemBase.UiToolkitShell.Commands.IUiCommandClient? CommandClient { get; }
        public VTuberSystemBase.UiToolkitShell.Commands.IUiSubscriptionClient? SubscriptionClient { get; }
        public VTuberSystemBase.UiToolkitShell.Commands.IConnectionStatus? ConnectionStatus { get; }
        public VTuberSystemBase.UiToolkitShell.AssetLoading.IAsyncAssetLoader? AssetLoader { get; }
        public IDiagnosticsLogger? Logger { get; }
    }
}
