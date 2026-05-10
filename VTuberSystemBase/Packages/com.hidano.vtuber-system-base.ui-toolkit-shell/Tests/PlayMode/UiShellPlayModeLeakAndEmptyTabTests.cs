#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
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
    /// Task 12.8 (E2E / PlayMode): PlayMode 反復起動リーク試験 と 空枠 UXML 単独表示試験
    /// （Requirements 8.3, 8.4, 8.5, 8.6, 8.7, 10.1, 10.2; design.md §UiShellLifecycleDriver,
    /// §RootUiDocumentBuilder, §TabPanelRegistry §Risks）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>反復起動リーク試験。</b> <see cref="UiShellLifecycleDriver"/> 経由で StartShell ↔
    /// StopShell を 5 回繰り返し、以下のリーク兆候が一切出ないことを観測する:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>UIDocument 重複生成防止 (Requirement 8.4):</b>
    ///         <see cref="FakeRootUiDocumentFactory.CreateInvocationCount"/> ↔
    ///         <see cref="FakeRootUiDocumentFactory.DisposeInvocationCount"/> が
    ///         イテレーション毎に同一値で進行し、ルート UIDocument の二重実体化が起きないこと。</item>
    ///   <item><b>購読残存ゼロ (Requirement 5.7, 8.3, task 10.4):</b> イテレーション毎に
    ///         <see cref="ITabLifecycleHandle.Track"/> 経由で登録した
    ///         <see cref="UiSubscriptionClient"/> 由来トークンが、StopShell 後の
    ///         backstop sweep ですべて <see cref="ISubscriptionToken.IsActive"/>=<c>false</c>
    ///         に落ちること。</item>
    ///   <item><b>Addressables ハンドル残存ゼロ (Requirement 4.8, 8.3):</b> イテレーション毎に
    ///         <see cref="AddressablesAssetLoader.GetSnapshot"/> が StopShell 後に
    ///         <c>PendingCount == 0</c> かつ <c>PendingByScope</c> が空であること。</item>
    /// </list>
    /// <para>
    /// <b>空枠 UXML 単独表示試験 (Requirement 10.1, 10.2)。</b> タブ spec (#4〜#6) が一切無い
    /// 状態でも、<c>EmptyTabShell.uxml</c> をそのまま 3 タブ全てに割り当てれば、シェルは
    /// SkinValidator を含む全ブートストラップ手順を成功させ、3 タブの style.display 切替も
    /// 成立する。Wave 2 (ui-toolkit-shell) の完了判定が Wave 3 (タブ spec 群) の実装と独立に
    /// 下せる、という task 12.8 の最終ゴールをコードで固定する。
    /// </para>
    /// <para>
    /// <b>テスト分類について。</b> 本 fixture は Tests/PlayMode 境界に置かれ
    /// <c>[UnityTest]</c> でフレームを跨いで検証するが、参照する asmdef
    /// （<c>UiToolkitShell.Tests</c>）が <c>includePlatforms: [Editor]</c> のため
    /// 実体としては Editor の coroutine ランナー上で動く。実 GameObject ライフサイクル
    /// (Destroy 遅延) には依存せず、<see cref="FakeRootUiDocumentFactory"/> の
    /// CreateInvocationCount / DisposeInvocationCount で UIDocument 重複生成不在を観測する
    /// 構成にしてあるため、Editor / PlayMode どちらのランナーでも結果は等価となる。
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class UiShellPlayModeLeakAndEmptyTabTests
    {
        private const string EmptyTabShellUxmlPath =
            "Packages/com.hidano.vtuber-system-base.ui-toolkit-shell/Runtime.UxmlUss/EmptyTabShell.uxml";

        private RecordingDiagnosticsLogger _logger = null!;
        private FakeIpcClient _bus = null!;
        private FakeAddressablesInitializer _addressables = null!;
        private FakeRootUiDocumentFactory _rootFactory = null!;
        private UiToolkitShellSkinProfile _skin = null!;
        private VisualTreeAsset _emptyTabShellVta = null!;
        private List<UnityEngine.Object> _disposables = null!;

        [SetUp]
        public void SetUp()
        {
            // Static lifecycle driver state must start clean — a previous test in the same
            // domain reload could have left an active provider behind (Requirement 8.6).
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

            // Load the real EmptyTabShell.uxml from disk so the empty-tab test exercises the
            // production placeholder UXML rather than a synthesised stub. The asmdef is
            // editor-only so AssetDatabase is available.
            _emptyTabShellVta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(EmptyTabShellUxmlPath);
            Assert.That(_emptyTabShellVta, Is.Not.Null,
                $"EmptyTabShell.uxml must be loadable at '{EmptyTabShellUxmlPath}' (Requirement 10.2).");

            _skin = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            // RootVisualTreeAsset is not the focus of these tests; a synthetic VTA satisfies
            // UiToolkitShellSkinProfile.Validate. EmptyTabShellVta is reused for all 3 tabs
            // so the empty-tab test exercises the actual placeholder UXML across every slot.
            _skin.RootVisualTreeAsset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            _skin.CharacterTabVisualTreeAsset = _emptyTabShellVta;
            _skin.StageLightingTabVisualTreeAsset = _emptyTabShellVta;
            _skin.CameraSwitcherTabVisualTreeAsset = _emptyTabShellVta;

            _disposables = new List<UnityEngine.Object>
            {
                _skin.RootVisualTreeAsset,
                _skin,
            };
        }

        [TearDown]
        public void TearDown()
        {
            UiShellLifecycleDriver.ResetForTests();
            for (var i = _disposables.Count - 1; i >= 0; i--)
            {
                if (_disposables[i] != null) UnityEngine.Object.DestroyImmediate(_disposables[i]);
            }
            _disposables.Clear();
            MainThreadAffinity.Reset();
        }

        // ----- Test 1: 5x repeat start/stop must not leak ----------------

        [UnityTest]
        public IEnumerator PlayMode_StartStop_FiveTimes_NoUiDocumentSubscriptionOrAddressablesLeak()
        {
            const int iterations = 5;

            UiShellLifecycleDriver.Configure(
                configProvider: BuildConfig,
                bootstrapperFactory: () => new UiShellBootstrapper(_rootFactory),
                diagnosticsLoggerFactory: () => _logger);

            // Cumulative residue tracker — every token from every iteration must remain
            // inactive at the very end so a late re-fire after StopShell can't slip through.
            var allTokens = new List<ISubscriptionToken>(iterations * 3);
            AddressablesAssetLoader? capturedLoader = null;

            for (var i = 0; i < iterations; i++)
            {
                UiShellLifecycleDriver.StartShell();
                Assert.That(UiShellLifecycleDriver.IsRunning, Is.True,
                    $"Iteration {i}: StartShell must transition the driver to running.");

                var bootstrapper = (UiShellBootstrapper)UiShellLifecycleDriver.Current!;
                var registry = bootstrapper.TabPanelRegistry!;
                var subscriptionClient = bootstrapper.SubscriptionClient!;
                var loader = bootstrapper.AssetLoader!;
                capturedLoader = loader;

                Assert.That(_rootFactory.CreateInvocationCount, Is.EqualTo(i + 1),
                    $"Iteration {i}: root UIDocument factory must Create exactly once per StartShell (no duplicate UIDocument).");
                Assert.That(_rootFactory.DisposeInvocationCount, Is.EqualTo(i),
                    $"Iteration {i}: factory dispose count should still be at the previous total — Stop has not run yet.");

                // Register a handle and Track 3 subscription tokens through it. The
                // bootstrapper's StopShell backstop (TabPanelRegistry.DisposeAllHandles)
                // must dispose every tracked resource even if the test mock never calls
                // handle.Dispose itself (Requirement 5.7, task 10.4).
                var handle = registry.RegisterTab(TabId.Character, new TabMetadata("character"));
                var iterTokens = new List<ISubscriptionToken>(3);
                for (var k = 0; k < 3; k++)
                {
                    var token = subscriptionClient.Subscribe<int>(
                        topic: $"ui/leak-test/iter-{i}/state-{k}",
                        kind: MessageKind.State,
                        callback: _ => { });
                    handle.Track(token);
                    iterTokens.Add(token);
                    allTokens.Add(token);
                }
                Assert.That(handle.TrackedResourceCount, Is.EqualTo(3),
                    $"Iteration {i}: Track must record all 3 subscriptions on the handle.");

                UiShellLifecycleDriver.StopShell();
                Assert.That(UiShellLifecycleDriver.IsRunning, Is.False,
                    $"Iteration {i}: StopShell must transition the driver back to dormant.");

                // Defer one frame so any late dispatch from the stopped subsystems
                // would have a chance to surface. The leak test still passes if nothing
                // happens — the assertions below are the actual contract.
                yield return null;

                Assert.That(_rootFactory.DisposeInvocationCount, Is.EqualTo(i + 1),
                    $"Iteration {i}: root UIDocument factory dispose count must equal create count after StopShell (no GameObject / PanelSettings leak).");
                Assert.That(_rootFactory.CreateInvocationCount, Is.EqualTo(_rootFactory.DisposeInvocationCount),
                    $"Iteration {i}: Create / Dispose pairing must be balanced (no UIDocument 重複生成).");

                foreach (var token in iterTokens)
                {
                    Assert.That(token.IsActive, Is.False,
                        $"Iteration {i}: subscription on '{token.Topic}' must be released by the StopShell backstop (Requirement 5.7).");
                }

                var snapshot = loader.GetSnapshot();
                Assert.That(snapshot.PendingCount, Is.EqualTo(0),
                    $"Iteration {i}: AddressablesAssetLoader must report 0 pending loads after StopShell (Requirement 4.8).");
                Assert.That(snapshot.PendingByScope, Is.Empty,
                    $"Iteration {i}: AddressablesAssetLoader.PendingByScope must be empty after StopShell (no scope-level handle residue).");
            }

            // Cumulative checks across all iterations — anything still active after 5 cycles
            // would mean the backstop missed at least one resource lifetime.
            Assert.That(allTokens.Count, Is.EqualTo(iterations * 3));
            foreach (var token in allTokens)
            {
                Assert.That(token.IsActive, Is.False,
                    $"Cumulative: subscription on '{token.Topic}' must remain inactive after all {iterations} iterations.");
            }

            Assert.That(_rootFactory.CreateInvocationCount, Is.EqualTo(iterations),
                "5 iterations must result in exactly 5 root-document creations (no duplicate UIDocument).");
            Assert.That(_rootFactory.DisposeInvocationCount, Is.EqualTo(iterations),
                "5 iterations must result in exactly 5 root-document disposals (no resource leak).");
            Assert.That(UiShellLifecycleDriver.StartInvocationCount, Is.EqualTo(iterations));
            Assert.That(UiShellLifecycleDriver.StopInvocationCount, Is.GreaterThanOrEqualTo(iterations),
                "Each iteration must increment StopInvocationCount; ResetForTests in TearDown can add one more.");

            // Lifecycle category must record one "shell stopped." entry per StopShell, so the
            // operator-facing log directly evidences the leak-free 5-iteration contract.
            var stoppedLogCount = _logger.Entries
                .Count(e => e.Category == LogCategory.Lifecycle && e.Message.Contains("shell stopped"));
            Assert.That(stoppedLogCount, Is.EqualTo(iterations),
                $"Diagnostics log must record exactly {iterations} 'shell stopped.' lifecycle entries.");

            // Capture a final snapshot through the loader reference held from the last
            // iteration to confirm the disposed loader is also drained (defence-in-depth).
            Assert.That(capturedLoader, Is.Not.Null);
            var finalSnapshot = capturedLoader!.GetSnapshot();
            Assert.That(finalSnapshot.PendingCount, Is.EqualTo(0));
            Assert.That(finalSnapshot.PendingByScope, Is.Empty);

            // Wave 2 completion marker — emitted via the diagnostics logger so the test
            // output preserves a single "Wave 2 leak-free" signal that operators can grep
            // for without consulting tab-spec artefacts (task 12.8 観測可能な完了状態).
            _logger.Log(LogLevel.Info, LogCategory.Lifecycle,
                $"Wave 2 leak-test passed: {iterations} StartShell/StopShell iterations completed " +
                $"with no UIDocument / subscription / Addressables-handle residue " +
                "(verified independently of Wave 3 tab specs).");
        }

        // ----- Test 2: Empty UXML keeps the shell standalone-bootable -----

        [UnityTest]
        public IEnumerator EmptyTabShell_RendersAllThreeTabsAndAllowsTabSwitching_WithoutTabSpec()
        {
            using var bootstrapper = new UiShellBootstrapper(_rootFactory);
            var emptyTabMount = new EmptyTabShellMountStrategy();

            var result = bootstrapper.StartShell(new UiShellConfig
            {
                SkinProfile = _skin,
                IpcBus = _bus,
                TabMountStrategy = emptyTabMount,
                AddressablesInitializer = _addressables,
                DiagnosticsLogger = _logger,
                InitialTab = TabId.Character,
            });

            Assert.That(result.Success, Is.True,
                $"StartShell must succeed with EmptyTabShell.uxml-only skin (Requirement 10.2). Error: {result.Error} {result.Detail}");

            // One frame to let UI Toolkit settle any deferred attach work for the cloned
            // tab subtrees. The contract assertions below do not depend on the frame
            // boundary, but yielding keeps the test honest about coroutine-style flow.
            yield return null;

            var registry = bootstrapper.TabPanelRegistry!;
            Assert.That(registry.IsPreloadComplete, Is.True,
                "All 3 tabs must reach Mounted state when each is bound to EmptyTabShell.uxml (Requirement 3.1).");
            var preload = registry.GetPreloadProgress();
            Assert.That(preload.LoadedCount, Is.EqualTo(3));
            Assert.That(preload.FailedTabs, Is.Empty,
                "EmptyTabShell.uxml combined with the per-tab modifier class must satisfy SkinValidator for every tab — Wave 2 must boot independently of Wave 3 (task 12.8).");

            var snapshot = registry.SnapshotTabRoots();
            foreach (var tabId in new[] { TabId.Character, TabId.StageLighting, TabId.CameraSwitcher })
            {
                Assert.That(snapshot.ContainsKey(tabId), Is.True,
                    $"Tab '{tabId}' must be bound to a VisualElement after MountTabs.");
                var tabRoot = snapshot[tabId];
                Assert.That(tabRoot.ClassListContains(SkinValidationRules.CharacterTab.TabRoot), Is.True,
                    $"Tab '{tabId}' root must carry the canonical 'vsb-tab-root' class authored in EmptyTabShell.uxml.");
                Assert.That(tabRoot.ClassListContains("vsb-tab-root--empty"), Is.True,
                    $"Tab '{tabId}' root must carry the 'vsb-tab-root--empty' modifier authored in EmptyTabShell.uxml — proves the placeholder UXML was actually instantiated.");
                Assert.That(tabRoot.ClassListContains(SkinValidationRules.RequiredTabClassesFor(tabId)[1]), Is.True,
                    $"Tab '{tabId}' root must carry the per-tab modifier class added by the mount strategy.");
                Assert.That(tabRoot.Q<Label>(className: "vsb-tab-root__placeholder"), Is.Not.Null,
                    $"Tab '{tabId}' must contain the EmptyTabShell.uxml placeholder Label so operators see the (タブ未統合) marker.");
            }

            // Initial tab is Character — verify SwitchTo cycles through all 3 tabs without
            // failure, evidencing that Wave 2 tab switching is functional with empty UXML.
            Assert.That(registry.ActiveTab, Is.EqualTo(TabId.Character),
                "Initial active tab must be Character (Requirement 3.3).");

            var switchToStage = registry.SwitchTo(TabId.StageLighting);
            Assert.That(switchToStage.Success, Is.True,
                $"SwitchTo StageLighting must succeed with empty UXML. Error: {switchToStage.Error}");
            Assert.That(registry.ActiveTab, Is.EqualTo(TabId.StageLighting));

            var switchToCamera = registry.SwitchTo(TabId.CameraSwitcher);
            Assert.That(switchToCamera.Success, Is.True,
                $"SwitchTo CameraSwitcher must succeed with empty UXML. Error: {switchToCamera.Error}");
            Assert.That(registry.ActiveTab, Is.EqualTo(TabId.CameraSwitcher));

            var switchBack = registry.SwitchTo(TabId.Character);
            Assert.That(switchBack.Success, Is.True);
            Assert.That(registry.ActiveTab, Is.EqualTo(TabId.Character));

            bootstrapper.StopShell();
            Assert.That(bootstrapper.IsRunning, Is.False);

            // Wave 2 independence marker — emitted via diagnostics so the operator-facing
            // log captures the standalone empty-tab boot in a single line (task 12.8).
            _logger.Log(LogLevel.Info, LogCategory.Lifecycle,
                "Wave 2 empty-UXML test passed: shell booted, validated and tab-switched " +
                "with EmptyTabShell.uxml only — no Wave 3 tab spec implementation required.");
        }

        // ----- helpers ---------------------------------------------------

        private UiShellConfig BuildConfig()
        {
            // Each iteration gets a fresh mount strategy so per-iteration CreatedRoots
            // does not leak across runs; the rest of the dependencies are reused on
            // purpose — they model long-lived process-wide singletons (the IPC bus, the
            // diagnostics logger, the Addressables initializer) that survive shell
            // restarts in real PlayMode (Requirement 8.4).
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

        /// <summary>
        /// Mount strategy that instantiates the skin profile's per-tab
        /// <see cref="VisualTreeAsset"/> (always <c>EmptyTabShell.uxml</c> in this fixture)
        /// for each of the three canonical tabs and adds the per-tab modifier class so
        /// <see cref="SkinValidator"/> can pass without a tab spec authoring extra UXML.
        /// </summary>
        private sealed class EmptyTabShellMountStrategy : ITabMountStrategy
        {
            public bool MountTabs(TabMountContext context)
            {
                foreach (var tabId in new[] { TabId.Character, TabId.StageLighting, TabId.CameraSwitcher })
                {
                    var vta = ResolveTabVta(context.SkinProfile, tabId);
                    if (vta == null)
                    {
                        context.Registry.MarkTabFailed(tabId, "tab VisualTreeAsset is null");
                        continue;
                    }

                    // Instantiate the tab UXML and reach into it for the authored
                    // 'vsb-tab-root' element. The cloned tree wraps the UXML content in an
                    // outer container so the placeholder element is one level down — Q
                    // by class returns null if not found, in which case we use the cloned
                    // root directly as a defensive fallback.
                    var clone = vta.Instantiate();
                    var tabRoot = clone.Q<VisualElement>(className: SkinValidationRules.CharacterTab.TabRoot)
                                  ?? clone;
                    tabRoot.AddToClassList(ModifierFor(tabId));

                    context.RootVisualElement.Add(clone);
                    context.Registry.NotifyTabMounted(tabId, tabRoot);
                }
                return true;
            }

            private static VisualTreeAsset? ResolveTabVta(UiToolkitShellSkinProfile skin, TabId tabId) => tabId switch
            {
                TabId.Character => skin.CharacterTabVisualTreeAsset,
                TabId.StageLighting => skin.StageLightingTabVisualTreeAsset,
                TabId.CameraSwitcher => skin.CameraSwitcherTabVisualTreeAsset,
                _ => throw new ArgumentOutOfRangeException(nameof(tabId), tabId, null),
            };

            private static string ModifierFor(TabId tabId) => tabId switch
            {
                TabId.Character => SkinValidationRules.CharacterTab.TabRootModifier,
                TabId.StageLighting => SkinValidationRules.StageLightingTab.TabRootModifier,
                TabId.CameraSwitcher => SkinValidationRules.CameraSwitcherTab.TabRootModifier,
                _ => throw new ArgumentOutOfRangeException(nameof(tabId), tabId, null),
            };
        }
    }
}
