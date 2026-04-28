#nullable enable
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Bootstrap;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Skin;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 12.6 (Integration): 起動 → プリロード → 初期タブ表示の End-to-End 結合テスト。
    /// <para>
    /// <see cref="UiShellBootstrapper.StartShell"/> を IPC 未接続・Addressables 初期化成功の
    /// 条件下で走らせ、(1) <see cref="ShellDiagnosticsSnapshot.Preload"/> が
    /// <c>LoadedCount == 3</c> かつ FailedTabs 空、(2) <see cref="ShellDiagnosticsSnapshot.ActiveTab"/>
    /// が <see cref="TabId.Character"/> に解決、(3) IPC 未接続でも UI が操作可能（タブ切替成功・
    /// PublishState は NotConnected を即時返却）であることを 1 シナリオで固定する
    /// （Requirements 1.4, 3.1, 3.3, 10.1; design.md §UiShellBootstrapper initialisation 順序）。
    /// </para>
    /// <para>
    /// 観測可能な完了条件 (task 12.6) として、<see cref="BootstrapStep.ShellRunning"/> 到達時の
    /// "shell running." ログが <see cref="LogCategory.Lifecycle"/> で記録されていることを併せて
    /// 確認し、Wave 2 完了条件（後続 spec #4〜#6 の実装に依存せずシェル単独で起動完遂）が
    /// この結合シナリオで満たされていることをログ出力で示す（Requirement 10.1 / design.md §10）。
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class ShellStartupEndToEndIntegrationTests
    {
        private RecordingDiagnosticsLogger _logger = null!;
        private FakeIpcClient _bus = null!;
        private FakeRootUiDocumentFactory _rootFactory = null!;
        private FakeTabMountStrategy _tabMount = null!;
        private FakeAddressablesInitializer _addressables = null!;
        private UiToolkitShellSkinProfile _skin = null!;
        private VisualTreeAsset _skinRoot = null!;

        [SetUp]
        public void SetUp()
        {
            MainThreadAffinity.Capture();

            _logger = new RecordingDiagnosticsLogger();
            // Bus stays in its default Disconnected state — Requirement 9.1 says the
            // shell must come up without waiting for IPC.
            _bus = new FakeIpcClient();
            _rootFactory = new FakeRootUiDocumentFactory();
            _tabMount = new FakeTabMountStrategy();
            _addressables = new FakeAddressablesInitializer
            {
                // Default to Immediate / Ok — Addressables initialisation succeeds
                // synchronously so the bootstrap path advances past
                // BootstrapStep.AddressablesInitialized.
                Mode = FakeAddressablesInitializer.CompletionMode.Immediate,
                StagedResult = AddressablesInitResult.Ok(),
            };

            _skin = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            _skinRoot = ScriptableObject.CreateInstance<VisualTreeAsset>();
            _skin.RootVisualTreeAsset = _skinRoot;
        }

        [TearDown]
        public void TearDown()
        {
            if (_skinRoot != null) Object.DestroyImmediate(_skinRoot);
            if (_skin != null) Object.DestroyImmediate(_skin);
            MainThreadAffinity.Reset();
        }

        private UiShellConfig MakeConfig()
        {
            return new UiShellConfig
            {
                SkinProfile = _skin,
                IpcBus = _bus,
                TabMountStrategy = _tabMount,
                AddressablesInitializer = _addressables,
                DiagnosticsLogger = _logger,
                InitialTab = TabId.Character,
            };
        }

        // ---- E2E: 起動 → プリロード → 初期タブ表示 -----------------------

        [Test]
        [Description("StartShell 後、診断スナップショットで Preload.LoadedCount==3 / ActiveTab==Character / 失敗タブなし が成立する (Req 1.4, 3.1, 3.3, 10.1)")]
        public void StartShell_DiagnosticsSnapshot_ReportsThreeTabsLoadedAndCharacterActive()
        {
            using var bootstrapper = new UiShellBootstrapper(_rootFactory);

            var result = bootstrapper.StartShell(MakeConfig());

            Assert.That(result.Success, Is.True,
                $"StartShell must complete in the IPC-disconnected / Addressables-OK scenario: {result.Error} {result.Detail}");
            Assert.That(bootstrapper.IsRunning, Is.True);
            Assert.That(bootstrapper.TabPanelRegistry, Is.Not.Null);
            Assert.That(bootstrapper.AssetLoader, Is.Not.Null);
            Assert.That(bootstrapper.ConnectionStatus, Is.Not.Null);

            // Compose a ShellDiagnosticsSnapshotProvider over the live subsystems and
            // capture once. This is the contract task 12.6 names directly: the
            // aggregated snapshot must report LoadedCount == 3 and ActiveTab == Character.
            var registry = bootstrapper.TabPanelRegistry!;
            var assetLoader = bootstrapper.AssetLoader!;
            var connection = bootstrapper.ConnectionStatus!;

            var provider = new ShellDiagnosticsSnapshotProvider(
                preload: () => registry.GetPreloadProgress(),
                assetLoad: () => assetLoader.GetSnapshot(),
                connectionStatus: () => connection.CurrentStatus,
                activeSubscriptionCount: () => 0,
                activeTab: () => registry.ActiveTab ?? TabId.Character);

            var snapshot = provider.Capture();

            Assert.That(snapshot.Preload.LoadedCount, Is.EqualTo(3),
                "All three tab UIDocuments should be preloaded (Requirement 3.1).");
            Assert.That(snapshot.Preload.TotalCount, Is.EqualTo(3));
            Assert.That(snapshot.Preload.FailedTabs, Is.Empty,
                "No tab should fail in the happy-path E2E scenario.");
            Assert.That(snapshot.ActiveTab, Is.EqualTo(TabId.Character),
                "Initial active tab must be Character (Requirement 3.3).");
            Assert.That(registry.ActiveTab, Is.EqualTo(TabId.Character),
                "Registry's ActiveTab must agree with the snapshot value (defence-in-depth).");
            Assert.That(snapshot.AssetLoad.PendingCount, Is.EqualTo(0),
                "No outstanding async loads should be pending right after preload.");
            Assert.That(snapshot.AssetLoad.FailedCount, Is.EqualTo(0));

            bootstrapper.StopShell();
        }

        [Test]
        [Description("IPC 未接続でも UI は操作可能: タブ切替が成功し、PublishState は NotConnected を即時返却する (Req 9.1, 9.4, 10.1)")]
        public void StartShell_IpcDisconnected_UiRemainsOperable()
        {
            using var bootstrapper = new UiShellBootstrapper(_rootFactory);

            var result = bootstrapper.StartShell(MakeConfig());
            Assert.That(result.Success, Is.True);

            var registry = bootstrapper.TabPanelRegistry!;
            var connection = bootstrapper.ConnectionStatus!;

            // Connection stays in its Initializing/Disconnected state — the bus was
            // never transitioned to Connected. The UI must still be operable.
            Assert.That(connection.IsConnected, Is.False,
                "IPC must remain unconnected for this E2E scenario to exercise Requirement 9.1.");

            // Tab switch path: Character is already active, so switch to a different
            // tab and back to confirm the UI is interactive without IPC.
            var switchToStage = registry.SwitchTo(TabId.StageLighting);
            Assert.That(switchToStage.Success, Is.True,
                "Switching tabs must succeed even with IPC disconnected (Requirement 9.1, 9.2).");
            Assert.That(registry.ActiveTab, Is.EqualTo(TabId.StageLighting));

            var switchToCamera = registry.SwitchTo(TabId.CameraSwitcher);
            Assert.That(switchToCamera.Success, Is.True);
            Assert.That(registry.ActiveTab, Is.EqualTo(TabId.CameraSwitcher));

            // PublishState path: must short-circuit with NotConnected (Requirement 9.4).
            var sendResult = bootstrapper.CommandClient!.PublishState("ui/e2e/test", new { value = 42 });
            Assert.That(sendResult.Success, Is.False);
            Assert.That(sendResult.Error.HasValue, Is.True);
            Assert.That(sendResult.Error!.Value.Code, Is.EqualTo(SendErrorCode.NotConnected));

            bootstrapper.StopShell();
        }

        [Test]
        [Description("Bootstrap 完了時に ShellRunning ステップ到達 + Lifecycle カテゴリの 'shell running.' ログが残る (Wave 2 完了条件; Req 10.1, 11.1)")]
        public void StartShell_EmitsShellRunningCompletionLog()
        {
            using var bootstrapper = new UiShellBootstrapper(_rootFactory);

            var result = bootstrapper.StartShell(MakeConfig());
            Assert.That(result.Success, Is.True);

            // The ordered initialisation sequence must end with ShellRunning so that
            // the lifecycle driver / external monitoring can observe the Wave 2
            // completion condition without consulting any tab-spec artefact.
            Assert.That(bootstrapper.InitializationSteps,
                Does.Contain(BootstrapStep.AddressablesInitialized));
            Assert.That(bootstrapper.InitializationSteps,
                Does.Contain(BootstrapStep.IpcConnectionAttempted));
            Assert.That(bootstrapper.InitializationSteps[bootstrapper.InitializationSteps.Count - 1],
                Is.EqualTo(BootstrapStep.ShellRunning));

            // The lifecycle log emitted at the end of StartShell is the operator-
            // facing signal that the shell completed startup (Wave 2 completion).
            var hasShellRunningLog = _logger.Entries.Any(e =>
                e.Category == LogCategory.Lifecycle &&
                e.Message.Contains("shell running"));
            Assert.That(hasShellRunningLog, Is.True,
                "A LogCategory.Lifecycle 'shell running.' entry must be emitted to mark Wave 2 completion.");

            bootstrapper.StopShell();
        }
    }
}
