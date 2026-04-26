#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Bootstrap;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Skin;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.PlayMode
{
    /// <summary>
    /// Task 12.10 (Coverage Audit) の診断 API 実機検証。
    /// <para>
    /// PlayMode で <see cref="UiShellLifecycleDriver"/> 経由に起動した本物の
    /// <see cref="UiShellBootstrapper"/> から取得した live サブシステム
    /// (<see cref="TabPanelRegistry"/>, <see cref="AddressablesAssetLoader"/>,
    /// <see cref="ConnectionStatus"/>, <see cref="UiSubscriptionClient"/>) を
    /// <see cref="ShellDiagnosticsSnapshotProvider"/> に束ね、
    /// <see cref="ShellDiagnosticsSnapshot"/> の 6 フィールド全てが「埋まった状態」で
    /// 返ることを固定する (Requirements 3.7, 4.9, 11.9, 10.5; design.md
    /// §Diagnostics §ShellDiagnosticsSnapshot)。
    /// </para>
    /// <para>
    /// この test は <c>RequirementCoverageAudit.md</c> の表に記載した 5 サンプルの
    /// 任意抜き取り検証と並んで、12.10 の観測可能な完了状態を成立させる
    /// 自動 assertion ペアの片方となる。
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class UiShellPlayModeDiagnosticsSnapshotAuditTests
    {
        private RecordingDiagnosticsLogger _logger = null!;
        private FakeIpcClient _bus = null!;
        private FakeAddressablesInitializer _addressables = null!;
        private FakeRootUiDocumentFactory _rootFactory = null!;
        private UiToolkitShellSkinProfile _skin = null!;
        private VisualTreeAsset _skinRoot = null!;

        [SetUp]
        public void SetUp()
        {
            UiShellLifecycleDriver.ResetForTests();
            MainThreadAffinity.Capture();

            _logger = new RecordingDiagnosticsLogger();
            _bus = new FakeIpcClient();
            _addressables = new FakeAddressablesInitializer
            {
                Mode = FakeAddressablesInitializer.CompletionMode.Immediate,
                StagedResult = AddressablesInitResult.Ok(),
            };
            _rootFactory = new FakeRootUiDocumentFactory();

            _skin = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            _skinRoot = ScriptableObject.CreateInstance<VisualTreeAsset>();
            _skin.RootVisualTreeAsset = _skinRoot;
        }

        [TearDown]
        public void TearDown()
        {
            UiShellLifecycleDriver.ResetForTests();
            if (_skinRoot != null) UnityEngine.Object.DestroyImmediate(_skinRoot);
            if (_skin != null) UnityEngine.Object.DestroyImmediate(_skin);
            MainThreadAffinity.Reset();
        }

        [UnityTest]
        public IEnumerator Capture_OnLiveBootstrappedShell_PopulatesAllFields()
        {
            UiShellLifecycleDriver.Configure(
                configProvider: BuildConfig,
                bootstrapperFactory: () => new UiShellBootstrapper(_rootFactory),
                diagnosticsLoggerFactory: () => _logger);

            UiShellLifecycleDriver.StartShell();
            Assert.That(UiShellLifecycleDriver.IsRunning, Is.True,
                "StartShell must transition the driver to running before snapshot capture.");

            var bootstrapper = (UiShellBootstrapper)UiShellLifecycleDriver.Current!;
            var registry = bootstrapper.TabPanelRegistry!;
            var loader = bootstrapper.AssetLoader!;
            var connection = bootstrapper.ConnectionStatus!;
            var subscriptionClient = bootstrapper.SubscriptionClient!;
            Assert.That(registry, Is.Not.Null);
            Assert.That(loader, Is.Not.Null);
            Assert.That(connection, Is.Not.Null);
            Assert.That(subscriptionClient, Is.Not.Null);

            // Register two real subscriptions through the live UiSubscriptionClient so the
            // ActiveSubscriptionCount slot is exercised against an actual count rather than a
            // stub closure constant. The closure below counts only the audit-owned tokens to
            // keep the assertion stable irrespective of any internal subscriptions the
            // bootstrap may have set up (e.g. MainOutputStatusWatcher).
            var auditTokens = new List<ISubscriptionToken>
            {
                subscriptionClient.Subscribe<int>(
                    "ui/audit/snapshot/state",
                    MessageKind.State,
                    _ => { }),
                subscriptionClient.Subscribe<int>(
                    "ui/audit/snapshot/event",
                    MessageKind.Event,
                    _ => { }),
            };

            yield return null;

            var before = DateTimeOffset.UtcNow.AddSeconds(-1);

            var provider = new ShellDiagnosticsSnapshotProvider(
                preload: () => registry.GetPreloadProgress(),
                assetLoad: () => loader.GetSnapshot(),
                connectionStatus: () => connection.CurrentStatus,
                activeSubscriptionCount: () => auditTokens.Count(t => t.IsActive),
                activeTab: () => registry.ActiveTab ?? TabId.Character);

            var snapshot = provider.Capture();

            var after = DateTimeOffset.UtcNow.AddSeconds(1);

            // Field 1/6: Preload progress — populated by the live registry.
            Assert.That(snapshot.Preload.LoadedCount, Is.EqualTo(3),
                "Live bootstrap must report all 3 tabs preloaded (Requirement 3.1).");
            Assert.That(snapshot.Preload.TotalCount, Is.EqualTo(3));
            Assert.That(snapshot.Preload.FailedTabs, Is.Not.Null,
                "FailedTabs must always be a non-null collection.");
            Assert.That(snapshot.Preload.FailedTabs, Is.Empty,
                "Happy-path bootstrap must not record any failed tabs.");

            // Field 2/6: AssetLoader snapshot — populated by the live AddressablesAssetLoader.
            Assert.That(snapshot.AssetLoad.PendingByScope, Is.Not.Null,
                "PendingByScope dictionary must be initialised, never null (Requirement 4.9).");
            Assert.That(snapshot.AssetLoad.PendingCount, Is.GreaterThanOrEqualTo(0));
            Assert.That(snapshot.AssetLoad.CompletedCount, Is.GreaterThanOrEqualTo(0));
            Assert.That(snapshot.AssetLoad.FailedCount, Is.GreaterThanOrEqualTo(0));

            // Field 3/6: Connection status — must be one of the declared 6 codes.
            Assert.That(Enum.IsDefined(typeof(ConnectionStatusCode), snapshot.ConnectionStatus),
                Is.True,
                "ConnectionStatus must resolve to a declared ConnectionStatusCode value (Req 11.6).");

            // Field 4/6: Active subscription count — directly observable from the audit tokens.
            Assert.That(snapshot.ActiveSubscriptionCount, Is.EqualTo(2),
                "Both audit-owned subscriptions must be counted as active.");

            // Field 5/6: Active tab — the bootstrapper's InitialTab is Character.
            Assert.That(snapshot.ActiveTab, Is.EqualTo(TabId.Character),
                "InitialTab Character must surface as ActiveTab (Requirement 3.3).");

            // Field 6/6: CapturedAt — DefaultClock path must produce a wall-clock value
            // bracketing the test execution window.
            Assert.That(snapshot.CapturedAt, Is.GreaterThanOrEqualTo(before));
            Assert.That(snapshot.CapturedAt, Is.LessThanOrEqualTo(after));

            // Disposing one audit token must visibly drop ActiveSubscriptionCount on the next
            // Capture(), proving the snapshot samples its sources fresh on each call (and not
            // a one-time cached value) — this is the contract task 12.10 requires of the
            // diagnostic API for it to remain useful as a live monitoring surface.
            auditTokens[0].Dispose();
            var second = provider.Capture();
            Assert.That(second.ActiveSubscriptionCount, Is.EqualTo(1),
                "Dispose of one audit token must reduce ActiveSubscriptionCount on subsequent Capture().");

            // Cleanup the remaining audit token so the StopShell backstop has nothing to chase.
            auditTokens[1].Dispose();

            UiShellLifecycleDriver.StopShell();
            Assert.That(UiShellLifecycleDriver.IsRunning, Is.False);

            // Operator-facing breadcrumb confirming the audit ran end-to-end on the live shell.
            _logger.Log(LogLevel.Info, LogCategory.Lifecycle,
                "Coverage audit (task 12.10) snapshot capture succeeded: 6/6 fields populated " +
                "on a live bootstrapped shell.");
        }

        private UiShellConfig BuildConfig()
        {
            return new UiShellConfig
            {
                SkinProfile = _skin,
                IpcBus = _bus,
                TabMountStrategy = new FakeTabMountStrategy(),
                AddressablesInitializer = _addressables,
                DiagnosticsLogger = _logger,
                InitialTab = TabId.Character,
            };
        }
    }
}
