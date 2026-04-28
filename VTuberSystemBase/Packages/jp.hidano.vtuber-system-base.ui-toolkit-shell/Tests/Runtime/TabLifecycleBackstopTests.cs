#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
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
    /// Task 10.4: タブ ライフサイクルと購読解除のバックストップ。
    /// <see cref="ITabLifecycleHandle.Dispose"/> および
    /// <see cref="UiShellBootstrapper.StopShell"/> 時に、
    /// <see cref="UiSubscriptionClient"/> 経由の購読・<see cref="IAsyncAssetLoader"/> の
    /// scope が一括解除される結合テスト。タブ spec 相当のモックが
    /// <c>Dispose</c> を忘れた場合でもシェル停止時に全解除されることを固定する
    /// （Requirement 2.8, 5.7; design.md §TabPanelRegistry §Risks）。
    /// </summary>
    [TestFixture]
    public sealed class TabLifecycleBackstopTests
    {
        private RecordingDiagnosticsLogger _logger = null!;
        private FakeIpcClient _bus = null!;
        private FakeRootUiDocumentFactory _rootFactory = null!;
        private FakeTabMountStrategy _tabMount = null!;
        private FakeAddressablesInitializer _addressables = null!;
        private UiToolkitShellSkinProfile _skin = null!;
        private List<UnityEngine.Object> _disposables = null!;

        [SetUp]
        public void SetUp()
        {
            _logger = new RecordingDiagnosticsLogger();
            _bus = new FakeIpcClient();
            _rootFactory = new FakeRootUiDocumentFactory();
            _tabMount = new FakeTabMountStrategy();
            _addressables = new FakeAddressablesInitializer();
            _skin = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            _skin.RootVisualTreeAsset = ScriptableObject.CreateInstance<UnityEngine.UIElements.VisualTreeAsset>();
            _disposables = new List<UnityEngine.Object>
            {
                _skin,
                _skin.RootVisualTreeAsset,
            };
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = _disposables.Count - 1; i >= 0; i--)
            {
                if (_disposables[i] != null) UnityEngine.Object.DestroyImmediate(_disposables[i]);
            }
            _disposables.Clear();
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
            };
        }

        // ---- Integration: subscriptions through the shell ---------------

        [Test]
        [Description("タブ spec モックが購読 10 件を Track したまま Dispose を忘れても、StopShell 後に全件 IsActive=false になる（Requirement 5.7, task 10.4 観測可能完了状態）")]
        public void StopShell_BackstopsTrackedSubscriptions_WhenTabSpecForgetsToDispose()
        {
            using var bootstrapper = new UiShellBootstrapper(_rootFactory);
            var startResult = bootstrapper.StartShell(MakeConfig());
            Assert.That(startResult.Success, Is.True, $"Bootstrap failed: {startResult.Error} {startResult.Detail}");

            var registry = bootstrapper.TabPanelRegistry!;
            var subscriptionClient = bootstrapper.SubscriptionClient!;

            // Tab specs register their handle and subscribe N tokens through it.
            // Distribute 10 subscriptions across the three canonical tabs (4/3/3)
            // so the backstop must walk every per-handle resource list.
            var characterHandle = registry.RegisterTab(TabId.Character, new TabMetadata("Character"));
            var stageHandle = registry.RegisterTab(TabId.StageLighting, new TabMetadata("StageLighting"));
            var cameraHandle = registry.RegisterTab(TabId.CameraSwitcher, new TabMetadata("CameraSwitcher"));

            var allTokens = new List<ISubscriptionToken>(10);
            for (var i = 0; i < 4; i++)
            {
                var token = subscriptionClient.Subscribe<int>($"ui/character/state-{i}", MessageKind.State, _ => { });
                characterHandle.Track(token);
                allTokens.Add(token);
            }
            for (var i = 0; i < 3; i++)
            {
                var token = subscriptionClient.Subscribe<int>($"ui/stage/state-{i}", MessageKind.State, _ => { });
                stageHandle.Track(token);
                allTokens.Add(token);
            }
            for (var i = 0; i < 3; i++)
            {
                var token = subscriptionClient.Subscribe<int>($"ui/camera/state-{i}", MessageKind.State, _ => { });
                cameraHandle.Track(token);
                allTokens.Add(token);
            }

            Assert.That(allTokens.Count, Is.EqualTo(10), "Test setup must seed exactly 10 subscriptions.");
            foreach (var token in allTokens)
            {
                Assert.That(token.IsActive, Is.True,
                    $"Subscription on '{token.Topic}' must be active before StopShell.");
            }
            Assert.That(characterHandle.TrackedResourceCount, Is.EqualTo(4));
            Assert.That(stageHandle.TrackedResourceCount, Is.EqualTo(3));
            Assert.That(cameraHandle.TrackedResourceCount, Is.EqualTo(3));

            // Tab spec deliberately forgets to dispose its handles.
            bootstrapper.StopShell();

            var stillActive = 0;
            foreach (var token in allTokens)
            {
                if (token.IsActive) stillActive++;
            }
            Assert.That(stillActive, Is.EqualTo(0),
                $"All 10 subscriptions must be released by the StopShell backstop; {stillActive} still active.");
            Assert.That(characterHandle.IsDisposed, Is.True);
            Assert.That(stageHandle.IsDisposed, Is.True);
            Assert.That(cameraHandle.IsDisposed, Is.True);
            Assert.That(characterHandle.TrackedResourceCount, Is.EqualTo(0));
            Assert.That(stageHandle.TrackedResourceCount, Is.EqualTo(0));
            Assert.That(cameraHandle.TrackedResourceCount, Is.EqualTo(0));
        }

        [Test]
        [Description("StopShell の二重呼び出しでも backstop が安全に no-op になる")]
        public void StopShell_BackstopIsIdempotent()
        {
            using var bootstrapper = new UiShellBootstrapper(_rootFactory);
            bootstrapper.StartShell(MakeConfig());

            var registry = bootstrapper.TabPanelRegistry!;
            var subscriptionClient = bootstrapper.SubscriptionClient!;
            var handle = registry.RegisterTab(TabId.Character, new TabMetadata("Character"));
            var token = subscriptionClient.Subscribe<int>("ui/test/state", MessageKind.State, _ => { });
            handle.Track(token);

            bootstrapper.StopShell();
            Assert.That(token.IsActive, Is.False);

            Assert.DoesNotThrow(() => bootstrapper.StopShell());
        }

        // ---- Unit: ITabLifecycleHandle backstop on a registry directly ---

        [Test]
        [Description("ITabLifecycleHandle.Dispose 単体でも Track した購読は即時解除される（Requirement 5.7）")]
        public void HandleDispose_ReleasesTrackedSubscriptionsImmediately()
        {
            var registry = new TabPanelRegistry(_logger);
            var handle = registry.RegisterTab(TabId.Character, new TabMetadata("Character"));
            var subscriptionClient = new UiSubscriptionClient(_bus, _logger);
            var tokens = new List<ISubscriptionToken>();
            for (var i = 0; i < 5; i++)
            {
                var token = subscriptionClient.Subscribe<int>($"ui/topic-{i}", MessageKind.Event, _ => { });
                handle.Track(token);
                tokens.Add(token);
            }

            handle.Dispose();

            foreach (var token in tokens)
            {
                Assert.That(token.IsActive, Is.False,
                    $"Token on '{token.Topic}' must be disposed when its owning handle is disposed.");
            }
        }

        [Test]
        [Description("Dispose 後に Track された購読は即座に解除される（リーク防止）")]
        public void HandleDispose_LateTrackDisposesImmediately()
        {
            var registry = new TabPanelRegistry(_logger);
            var handle = registry.RegisterTab(TabId.Character, new TabMetadata("Character"));
            handle.Dispose();
            var subscriptionClient = new UiSubscriptionClient(_bus, _logger);
            var lateToken = subscriptionClient.Subscribe<int>("ui/late", MessageKind.State, _ => { });

            handle.Track(lateToken);

            Assert.That(lateToken.IsActive, Is.False,
                "Late Track after Dispose must dispose the resource immediately so no subscription survives the handle.");
        }

        // ---- Unit: TrackAssetScope releases via the loader on Dispose ----

        [Test]
        [Description("TrackAssetScope した IAsyncAssetLoader は handle.Dispose 時に scope 単位で ReleaseAll される（Requirement 4.8 と 5.7 の結合）")]
        public void HandleDispose_TrackAssetScope_ReleasesAllPendingLoads()
        {
            var registry = new TabPanelRegistry(_logger);
            var handle = registry.RegisterTab(TabId.Character, new TabMetadata("Character"));
            var loader = new FakeAsyncAssetLoader { Mode = FakeAsyncAssetLoader.CompletionMode.Deferred };
            handle.TrackAssetScope(loader);

            for (var i = 0; i < 4; i++)
            {
                loader.LoadAsync<UnityEngine.Object>(
                    addressableKey: $"asset-{i}",
                    scopeId: handle.ScopeId,
                    onCompleted: _ => { });
            }
            // A second tab worth of pending loads on a different scope must NOT
            // be released by this handle.
            loader.LoadAsync<UnityEngine.Object>(
                addressableKey: "other-scope-asset",
                scopeId: "tab/other",
                onCompleted: _ => { });

            var beforeSnapshot = loader.GetSnapshot();
            Assert.That(beforeSnapshot.PendingCount, Is.EqualTo(5));

            handle.Dispose();

            var afterSnapshot = loader.GetSnapshot();
            Assert.That(afterSnapshot.PendingCount, Is.EqualTo(1),
                "Only the other-scope load must remain pending; the handle's scope must be fully released.");
            Assert.That(afterSnapshot.PendingByScope.ContainsKey(handle.ScopeId), Is.False,
                $"Scope '{handle.ScopeId}' must not appear in pendingByScope after the handle disposed.");
        }

        // ---- Unit: registry-level backstop sweep --------------------------

        [Test]
        [Description("TabPanelRegistry.DisposeAllHandles はあらゆるタブの handle を強制 Dispose する（バックストップ実装の単体検証）")]
        public void RegistryDisposeAllHandles_DisposesEveryHandle()
        {
            var registry = new TabPanelRegistry(_logger);
            var handles = new List<ITabLifecycleHandle>
            {
                registry.RegisterTab(TabId.Character, new TabMetadata("Character")),
                registry.RegisterTab(TabId.StageLighting, new TabMetadata("StageLighting")),
                registry.RegisterTab(TabId.CameraSwitcher, new TabMetadata("CameraSwitcher")),
            };
            var subscriptionClient = new UiSubscriptionClient(_bus, _logger);
            var tokens = new List<ISubscriptionToken>();
            foreach (var h in handles)
            {
                var token = subscriptionClient.Subscribe<int>($"ui/{h.TabId}/seed", MessageKind.State, _ => { });
                h.Track(token);
                tokens.Add(token);
            }

            registry.DisposeAllHandles();

            foreach (var h in handles)
            {
                Assert.That(h.IsDisposed, Is.True, $"{h.TabId} handle must be disposed by the backstop sweep.");
            }
            foreach (var token in tokens)
            {
                Assert.That(token.IsActive, Is.False,
                    $"Token on '{token.Topic}' must be disposed by the backstop sweep.");
            }
            // After the sweep, every TabId slot is freed and re-registration must
            // succeed (mirrors the post-Stop / next-Start contract).
            Assert.DoesNotThrow(() =>
                registry.RegisterTab(TabId.Character, new TabMetadata("Character")));
        }

        [Test]
        [Description("空の registry に対して DisposeAllHandles を呼んでも例外にならない")]
        public void RegistryDisposeAllHandles_OnEmpty_IsNoOp()
        {
            var registry = new TabPanelRegistry(_logger);
            Assert.DoesNotThrow(() => registry.DisposeAllHandles());
        }
    }
}
