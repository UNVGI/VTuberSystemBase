#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 3.3: <c>ShellDiagnosticsSnapshot</c> / <c>IShellDiagnosticsSnapshotProvider</c> の
    /// 契約と振る舞いを TDD で固定する。プリロード進捗・非同期ロード件数・IPC 接続状態・
    /// アクティブタブ・購読数・取得時刻 を集約 struct として返し、Capture() は副作用を
    /// 持たず即時に値を返す。各サブシステムは Func 経由で注入可能で、サブシステム差替えで
    /// 値が変化する（Requirements 3.7, 4.9, 11.9）。
    /// design.md §Diagnostics §ShellDiagnosticsSnapshot 参照。
    /// </summary>
    [TestFixture]
    public sealed class ShellDiagnosticsSnapshotProviderTests
    {
        [Test]
        [Description("Capture() は注入された各サブシステムソースの現在値で全フィールドを埋めた struct を返す")]
        public void Capture_PopulatesAllFields_FromInjectedSources()
        {
            var preload = new PreloadProgress(
                loadedCount: 2,
                totalCount: 3,
                failedTabs: new[] { TabId.StageLighting });
            var assetLoad = new AssetLoaderSnapshot(
                pendingCount: 5,
                completedCount: 12,
                failedCount: 1,
                pendingByScope: new Dictionary<string, int> { ["Character"] = 5 });
            var capturedAt = new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);

            IShellDiagnosticsSnapshotProvider provider = new ShellDiagnosticsSnapshotProvider(
                preload: () => preload,
                assetLoad: () => assetLoad,
                connectionStatus: () => ConnectionStatusCode.Connected,
                activeSubscriptionCount: () => 7,
                activeTab: () => TabId.Character,
                clock: () => capturedAt);

            var snapshot = provider.Capture();

            Assert.That(snapshot.Preload.LoadedCount, Is.EqualTo(2));
            Assert.That(snapshot.Preload.TotalCount, Is.EqualTo(3));
            Assert.That(snapshot.Preload.FailedTabs, Is.EquivalentTo(new[] { TabId.StageLighting }));
            Assert.That(snapshot.AssetLoad.PendingCount, Is.EqualTo(5));
            Assert.That(snapshot.AssetLoad.CompletedCount, Is.EqualTo(12));
            Assert.That(snapshot.AssetLoad.FailedCount, Is.EqualTo(1));
            Assert.That(snapshot.AssetLoad.PendingByScope["Character"], Is.EqualTo(5));
            Assert.That(snapshot.ConnectionStatus, Is.EqualTo(ConnectionStatusCode.Connected));
            Assert.That(snapshot.ActiveSubscriptionCount, Is.EqualTo(7));
            Assert.That(snapshot.ActiveTab, Is.EqualTo(TabId.Character));
            Assert.That(snapshot.CapturedAt, Is.EqualTo(capturedAt));
        }

        [Test]
        [Description("ソースの値が変化すると、その後の Capture() は新しい値を返す（参照透過 / 都度サンプリング）")]
        public void Capture_ReflectsLatestSourceValues_OnSubsequentCalls()
        {
            var connection = ConnectionStatusCode.Initializing;
            var subscriptions = 0;
            var activeTab = TabId.Character;
            var assetLoad = new AssetLoaderSnapshot(0, 0, 0, new Dictionary<string, int>());
            var preload = new PreloadProgress(0, 3, Array.Empty<TabId>());
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            IShellDiagnosticsSnapshotProvider provider = new ShellDiagnosticsSnapshotProvider(
                preload: () => preload,
                assetLoad: () => assetLoad,
                connectionStatus: () => connection,
                activeSubscriptionCount: () => subscriptions,
                activeTab: () => activeTab,
                clock: () => now);

            var first = provider.Capture();
            Assert.That(first.ConnectionStatus, Is.EqualTo(ConnectionStatusCode.Initializing));
            Assert.That(first.ActiveSubscriptionCount, Is.EqualTo(0));
            Assert.That(first.ActiveTab, Is.EqualTo(TabId.Character));
            Assert.That(first.Preload.LoadedCount, Is.EqualTo(0));

            // サブシステム側の状態を差し替える。
            connection = ConnectionStatusCode.Connected;
            subscriptions = 4;
            activeTab = TabId.CameraSwitcher;
            preload = new PreloadProgress(3, 3, Array.Empty<TabId>());
            assetLoad = new AssetLoaderSnapshot(2, 1, 0, new Dictionary<string, int> { ["StageLighting"] = 2 });
            now = new DateTimeOffset(2026, 1, 1, 0, 0, 5, TimeSpan.Zero);

            var second = provider.Capture();
            Assert.That(second.ConnectionStatus, Is.EqualTo(ConnectionStatusCode.Connected));
            Assert.That(second.ActiveSubscriptionCount, Is.EqualTo(4));
            Assert.That(second.ActiveTab, Is.EqualTo(TabId.CameraSwitcher));
            Assert.That(second.Preload.LoadedCount, Is.EqualTo(3));
            Assert.That(second.AssetLoad.PendingCount, Is.EqualTo(2));
            Assert.That(second.CapturedAt, Is.EqualTo(now));
        }

        [Test]
        [Description("Capture() は副作用を持たない（同一ソース状態に対して同一値を繰り返し返す）")]
        public void Capture_IsSideEffectFree_OnRepeatedCallsWithStableSources()
        {
            var preload = new PreloadProgress(1, 3, new[] { TabId.CameraSwitcher });
            var assetLoad = new AssetLoaderSnapshot(0, 1, 0, new Dictionary<string, int>());
            var fixedTime = new DateTimeOffset(2026, 4, 26, 9, 30, 0, TimeSpan.Zero);

            IShellDiagnosticsSnapshotProvider provider = new ShellDiagnosticsSnapshotProvider(
                preload: () => preload,
                assetLoad: () => assetLoad,
                connectionStatus: () => ConnectionStatusCode.Reconnecting,
                activeSubscriptionCount: () => 2,
                activeTab: () => TabId.StageLighting,
                clock: () => fixedTime);

            var s1 = provider.Capture();
            var s2 = provider.Capture();
            var s3 = provider.Capture();

            Assert.That(s1.Preload.LoadedCount, Is.EqualTo(s2.Preload.LoadedCount));
            Assert.That(s1.AssetLoad.PendingCount, Is.EqualTo(s2.AssetLoad.PendingCount));
            Assert.That(s1.ConnectionStatus, Is.EqualTo(s3.ConnectionStatus));
            Assert.That(s1.ActiveSubscriptionCount, Is.EqualTo(s3.ActiveSubscriptionCount));
            Assert.That(s1.ActiveTab, Is.EqualTo(s3.ActiveTab));
            Assert.That(s1.CapturedAt, Is.EqualTo(s3.CapturedAt));
        }

        [Test]
        [Description("clock を省略した場合は DateTimeOffset.UtcNow 近傍が CapturedAt に設定される")]
        public void Capture_WithoutInjectedClock_UsesUtcNowApproximately()
        {
            IShellDiagnosticsSnapshotProvider provider = new ShellDiagnosticsSnapshotProvider(
                preload: () => new PreloadProgress(0, 3, Array.Empty<TabId>()),
                assetLoad: () => AssetLoaderSnapshot.Empty,
                connectionStatus: () => ConnectionStatusCode.Disconnected,
                activeSubscriptionCount: () => 0,
                activeTab: () => TabId.Character);

            var before = DateTimeOffset.UtcNow.AddSeconds(-1);
            var snapshot = provider.Capture();
            var after = DateTimeOffset.UtcNow.AddSeconds(1);

            Assert.That(snapshot.CapturedAt, Is.GreaterThanOrEqualTo(before));
            Assert.That(snapshot.CapturedAt, Is.LessThanOrEqualTo(after));
        }

        [Test]
        [Description("いずれかのソースが null の場合は ArgumentNullException で構築失敗する")]
        public void Constructor_NullSource_Throws()
        {
            Func<PreloadProgress> preload = () => new PreloadProgress(0, 3, Array.Empty<TabId>());
            Func<AssetLoaderSnapshot> assetLoad = () => AssetLoaderSnapshot.Empty;
            Func<ConnectionStatusCode> connection = () => ConnectionStatusCode.Initializing;
            Func<int> subs = () => 0;
            Func<TabId> active = () => TabId.Character;

            Assert.Throws<ArgumentNullException>(() => new ShellDiagnosticsSnapshotProvider(
                preload: null!, assetLoad: assetLoad, connectionStatus: connection,
                activeSubscriptionCount: subs, activeTab: active));
            Assert.Throws<ArgumentNullException>(() => new ShellDiagnosticsSnapshotProvider(
                preload: preload, assetLoad: null!, connectionStatus: connection,
                activeSubscriptionCount: subs, activeTab: active));
            Assert.Throws<ArgumentNullException>(() => new ShellDiagnosticsSnapshotProvider(
                preload: preload, assetLoad: assetLoad, connectionStatus: null!,
                activeSubscriptionCount: subs, activeTab: active));
            Assert.Throws<ArgumentNullException>(() => new ShellDiagnosticsSnapshotProvider(
                preload: preload, assetLoad: assetLoad, connectionStatus: connection,
                activeSubscriptionCount: null!, activeTab: active));
            Assert.Throws<ArgumentNullException>(() => new ShellDiagnosticsSnapshotProvider(
                preload: preload, assetLoad: assetLoad, connectionStatus: connection,
                activeSubscriptionCount: subs, activeTab: null!));
        }

        [Test]
        [Description("PreloadProgress.FailedTabs は null を許容せず、コンストラクタで空コレクションに正規化される")]
        public void PreloadProgress_NullFailedTabs_NormalizedToEmpty()
        {
            var progress = new PreloadProgress(0, 3, null!);
            Assert.That(progress.FailedTabs, Is.Not.Null);
            Assert.That(progress.FailedTabs, Is.Empty);
        }

        [Test]
        [Description("ConnectionStatusCode は design.md §Connection の 6 状態を宣言する（Req 11.6）")]
        public void ConnectionStatusCode_DeclaresAllSixStates()
        {
            var declared = Enum.GetNames(typeof(ConnectionStatusCode));
            Assert.That(declared, Is.SupersetOf(new[]
            {
                nameof(ConnectionStatusCode.Initializing),
                nameof(ConnectionStatusCode.Connecting),
                nameof(ConnectionStatusCode.Connected),
                nameof(ConnectionStatusCode.Disconnected),
                nameof(ConnectionStatusCode.Reconnecting),
                nameof(ConnectionStatusCode.FailedPermanently),
            }));
        }

        [Test]
        [Description("TabId は 3 種類（Character / StageLighting / CameraSwitcher）を宣言する")]
        public void TabId_DeclaresAllThreeTabs()
        {
            var declared = Enum.GetNames(typeof(TabId));
            Assert.That(declared, Is.SupersetOf(new[]
            {
                nameof(TabId.Character),
                nameof(TabId.StageLighting),
                nameof(TabId.CameraSwitcher),
            }));
        }
    }
}
