#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VTuberSystemBase.UiToolkitShell.Bootstrap;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Skin;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 10.2: <see cref="UiShellLifecycleDriver"/> の PlayMode / Standalone / Edit モード
    /// 分岐とライフサイクル契約を Edit モードで固定する
    /// （design.md §Bootstrap §UiShellLifecycleDriver; Requirements 8.1, 8.2, 8.3, 8.4, 8.5,
    /// 8.6, 8.7）。
    /// </summary>
    /// <remarks>
    /// 実 PlayMode 遷移を伴わない結合検証のため、driver の Configure / StartShell /
    /// SimulatePlayModeStateChangeForTests という公開シームを通じてフェイク bootstrapper を
    /// 注入し、起動・停止・反復起動・dormancy をブラックボックスで検証する。
    /// RuntimeInitializeOnLoadMethod / Application.quitting / EditorApplication.playModeStateChanged
    /// 自体は Unity が呼び出す静的フックであり、本テストは driver がそれらフックから
    /// 駆動された場合に取るべき分岐ロジックを再現してテストする。
    /// </remarks>
    [TestFixture]
    public sealed class UiShellLifecycleDriverTests
    {
        private List<UnityEngine.Object> _disposables = null!;
        private UiToolkitShellSkinProfile _skin = null!;
        private FakeIpcClient _bus = null!;
        private FakeRootUiDocumentFactory _rootFactory = null!;
        private FakeTabMountStrategy _tabMount = null!;
        private FakeAddressablesInitializer _addressables = null!;
        private RecordingDiagnosticsLogger _logger = null!;

        [SetUp]
        public void SetUp()
        {
            // Each test starts from a clean slate so leftover static state from the previous
            // test cannot leak (Requirement 8.6: no domain-spanning state).
            UiShellLifecycleDriver.ResetForTests();

            _disposables = new List<UnityEngine.Object>();
            _logger = new RecordingDiagnosticsLogger();
            _bus = new FakeIpcClient();
            _rootFactory = new FakeRootUiDocumentFactory();
            _tabMount = new FakeTabMountStrategy();
            _addressables = new FakeAddressablesInitializer();
            _skin = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            _skin.RootVisualTreeAsset = ScriptableObject.CreateInstance<UnityEngine.UIElements.VisualTreeAsset>();
            _disposables.Add(_skin);
            _disposables.Add(_skin.RootVisualTreeAsset);
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
        }

        // ----- helpers -------------------------------------------------

        private UiShellConfig BuildConfig()
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

        private UiShellBootstrapper BuildBootstrapper()
        {
            return new UiShellBootstrapper(_rootFactory);
        }

        private void ConfigureWithRealBootstrapper(int? initialConfigCount = null)
        {
            UiShellLifecycleDriver.Configure(
                configProvider: BuildConfig,
                bootstrapperFactory: BuildBootstrapper,
                diagnosticsLoggerFactory: () => _logger);
            _ = initialConfigCount;
        }

        // ----- Dormancy / Edit-mode (Requirement 8.5) ------------------

        [Test]
        [Description("Configure 未実施の StartShell は dormant (Requirement 8.5 / Edit モード相当)")]
        public void StartShell_WithoutConfigure_IsDormant()
        {
            UiShellLifecycleDriver.StartShell();

            Assert.That(UiShellLifecycleDriver.IsRunning, Is.False);
            Assert.That(UiShellLifecycleDriver.Current, Is.Null);
            Assert.That(UiShellLifecycleDriver.StartInvocationCount, Is.EqualTo(1));
        }

        [Test]
        [Description("config provider が null を返した場合は dormant のまま (フェイルセーフ)")]
        public void StartShell_ConfigProviderReturnsNull_StaysDormant()
        {
            UiShellLifecycleDriver.Configure(
                configProvider: () => null!,
                bootstrapperFactory: BuildBootstrapper);

            UiShellLifecycleDriver.StartShell();

            Assert.That(UiShellLifecycleDriver.IsRunning, Is.False);
            Assert.That(UiShellLifecycleDriver.Current, Is.Null);
        }

        [Test]
        [Description("config provider が例外を投げた場合は driver が握り潰し dormant のまま (UI クラッシュ禁止)")]
        public void StartShell_ConfigProviderThrows_StaysDormant()
        {
            UiShellLifecycleDriver.Configure(
                configProvider: () => throw new InvalidOperationException("expected"),
                bootstrapperFactory: BuildBootstrapper,
                diagnosticsLoggerFactory: () => _logger);

            Assert.DoesNotThrow(() => UiShellLifecycleDriver.StartShell());
            Assert.That(UiShellLifecycleDriver.IsRunning, Is.False);
        }

        [Test]
        [Description("bootstrapper factory が例外を投げた場合も driver が握り潰し dormant のまま")]
        public void StartShell_BootstrapperFactoryThrows_StaysDormant()
        {
            UiShellLifecycleDriver.Configure(
                configProvider: BuildConfig,
                bootstrapperFactory: () => throw new InvalidOperationException("expected"),
                diagnosticsLoggerFactory: () => _logger);

            Assert.DoesNotThrow(() => UiShellLifecycleDriver.StartShell());
            Assert.That(UiShellLifecycleDriver.IsRunning, Is.False);
        }

        [Test]
        [Description("BootstrapResult が失敗を返したら IsRunning は false に保たれる (Requirement 9.7 連携)")]
        public void StartShell_BootstrapResultFails_KeepsDriverDormant()
        {
            var fakeBootstrapper = new FailingFakeBootstrapper(BootstrapErrorCode.SkinProfileMissing);
            UiShellLifecycleDriver.Configure(
                configProvider: BuildConfig,
                bootstrapperFactory: () => fakeBootstrapper,
                diagnosticsLoggerFactory: () => _logger);

            UiShellLifecycleDriver.StartShell();

            Assert.That(UiShellLifecycleDriver.IsRunning, Is.False);
            Assert.That(UiShellLifecycleDriver.Current, Is.Null);
            Assert.That(fakeBootstrapper.StartCalls, Is.EqualTo(1));
            Assert.That(fakeBootstrapper.DisposeCalls, Is.EqualTo(1),
                "失敗 bootstrapper はリーク回避のため Dispose されなければならない");
        }

        // ----- Standalone / PlayMode 起動 (Requirements 8.1, 8.2, 8.7) -

        [Test]
        [Description("Configure 後の StartShell は bootstrapper.StartShell を呼び IsRunning を true にする")]
        public void StartShell_WithConfigure_StartsBootstrapper()
        {
            ConfigureWithRealBootstrapper();

            UiShellLifecycleDriver.StartShell();

            Assert.That(UiShellLifecycleDriver.IsRunning, Is.True);
            Assert.That(UiShellLifecycleDriver.Current, Is.Not.Null);
            Assert.That(UiShellLifecycleDriver.Current!.IsRunning, Is.True);
        }

        [Test]
        [Description("StartShell の二重呼び出しは bootstrapper を再生成しない (Requirement 8.4)")]
        public void StartShell_TwiceWhileRunning_IsNoOp()
        {
            var counter = new InvocationCountingBootstrapperFactory(BuildBootstrapper);
            UiShellLifecycleDriver.Configure(
                configProvider: BuildConfig,
                bootstrapperFactory: counter.Build);

            UiShellLifecycleDriver.StartShell();
            var firstInstance = UiShellLifecycleDriver.Current;
            UiShellLifecycleDriver.StartShell();
            var secondInstance = UiShellLifecycleDriver.Current;

            Assert.That(secondInstance, Is.SameAs(firstInstance),
                "二重 StartShell は同一 bootstrapper を保持し続けなければならない");
            Assert.That(counter.InvocationCount, Is.EqualTo(1),
                "Bootstrapper factory は IsRunning 中に再呼び出しされないこと");
        }

        // ----- StopShell / 反復起動 (Requirements 8.3, 8.4) -----------

        [Test]
        [Description("StopShell は IsRunning を false に戻し bootstrapper を Dispose する (Requirement 8.3)")]
        public void StopShell_AfterStart_TearsDownBootstrapper()
        {
            var bootstrapper = BuildBootstrapper();
            UiShellLifecycleDriver.Configure(
                configProvider: BuildConfig,
                bootstrapperFactory: () => bootstrapper);

            UiShellLifecycleDriver.StartShell();
            Assume.That(UiShellLifecycleDriver.IsRunning, Is.True);

            UiShellLifecycleDriver.StopShell();

            Assert.That(UiShellLifecycleDriver.IsRunning, Is.False);
            Assert.That(UiShellLifecycleDriver.Current, Is.Null);
            Assert.That(bootstrapper.IsRunning, Is.False);
        }

        [Test]
        [Description("StopShell の二重呼び出しは安全に no-op")]
        public void StopShell_CalledTwice_IsSafe()
        {
            ConfigureWithRealBootstrapper();
            UiShellLifecycleDriver.StartShell();

            UiShellLifecycleDriver.StopShell();
            Assert.DoesNotThrow(() => UiShellLifecycleDriver.StopShell());
            Assert.That(UiShellLifecycleDriver.IsRunning, Is.False);
        }

        [Test]
        [Description("PlayMode Start/Stop を 5 回繰り返してもリーク兆候なし (Requirement 8.4 / observable 完了状態)")]
        public void PlayMode_StartStop_FiveTimes_DoesNotLeak()
        {
            var counter = new InvocationCountingBootstrapperFactory(BuildBootstrapper);
            UiShellLifecycleDriver.Configure(
                configProvider: BuildConfig,
                bootstrapperFactory: counter.Build);

            for (var i = 0; i < 5; i++)
            {
                UiShellLifecycleDriver.StartShell();
                Assert.That(UiShellLifecycleDriver.IsRunning, Is.True, $"start iteration {i}");
                UiShellLifecycleDriver.StopShell();
                Assert.That(UiShellLifecycleDriver.IsRunning, Is.False, $"stop iteration {i}");
            }

            Assert.That(counter.InvocationCount, Is.EqualTo(5),
                "5 回の Start に対し 5 回 bootstrapper が新規生成されること");
            Assert.That(UiShellLifecycleDriver.StartInvocationCount, Is.EqualTo(5));
            Assert.That(UiShellLifecycleDriver.StopInvocationCount, Is.EqualTo(5));
            foreach (var built in counter.Built)
            {
                Assert.That(built.IsRunning, Is.False, "全 bootstrapper が Stop 済みであること");
            }
        }

        // ----- PlayModeStateChange / Application.quitting シミュレーション -

        [Test]
        [Description("PlayMode 終了 (ExitingPlayMode) で StopShell が走る (Requirement 8.3)")]
        public void SimulatedExitingPlayMode_StopsShell()
        {
            ConfigureWithRealBootstrapper();
            UiShellLifecycleDriver.StartShell();
            Assume.That(UiShellLifecycleDriver.IsRunning, Is.True);

            UiShellLifecycleDriver.SimulatePlayModeStateChangeForTests(PlayModeStateChange.ExitingPlayMode);

            Assert.That(UiShellLifecycleDriver.IsRunning, Is.False);
            Assert.That(UiShellLifecycleDriver.Current, Is.Null);
        }

        [Test]
        [Description("EnteredEditMode はディフェンシブな二重 Stop でも例外を出さない")]
        public void SimulatedEnteredEditMode_AfterStop_IsSafeNoOp()
        {
            ConfigureWithRealBootstrapper();
            UiShellLifecycleDriver.StartShell();

            UiShellLifecycleDriver.SimulatePlayModeStateChangeForTests(PlayModeStateChange.ExitingPlayMode);
            Assert.DoesNotThrow(() =>
                UiShellLifecycleDriver.SimulatePlayModeStateChangeForTests(PlayModeStateChange.EnteredEditMode));
            Assert.That(UiShellLifecycleDriver.IsRunning, Is.False);
        }

        [Test]
        [Description("EnteredPlayMode / ExitingEditMode は Start を引き起こさない (Requirement 8.5)")]
        public void SimulatedEditModeTransitions_DoNotStartShell()
        {
            ConfigureWithRealBootstrapper();
            // Driver は dormant のまま
            UiShellLifecycleDriver.SimulatePlayModeStateChangeForTests(PlayModeStateChange.ExitingEditMode);
            UiShellLifecycleDriver.SimulatePlayModeStateChangeForTests(PlayModeStateChange.EnteredPlayMode);

            Assert.That(UiShellLifecycleDriver.IsRunning, Is.False,
                "PlayMode 状態変化フックは Start のトリガにはならない (Requirement 8.5 を遵守)");
        }

        // ----- 検証用補助 ----------------------------------------------

        private sealed class InvocationCountingBootstrapperFactory
        {
            private readonly Func<IUiShellBootstrapper> _inner;
            public int InvocationCount { get; private set; }
            public List<IUiShellBootstrapper> Built { get; } = new List<IUiShellBootstrapper>();

            public InvocationCountingBootstrapperFactory(Func<IUiShellBootstrapper> inner)
            {
                _inner = inner;
            }

            public IUiShellBootstrapper Build()
            {
                InvocationCount++;
                var built = _inner();
                Built.Add(built);
                return built;
            }
        }

        private sealed class FailingFakeBootstrapper : IUiShellBootstrapper, IDisposable
        {
            private readonly BootstrapErrorCode _failureCode;
            public int StartCalls { get; private set; }
            public int StopCalls { get; private set; }
            public int DisposeCalls { get; private set; }
            public bool IsRunning => false;
            public IReadOnlyList<BootstrapStep> InitializationSteps => Array.Empty<BootstrapStep>();

            public FailingFakeBootstrapper(BootstrapErrorCode failureCode)
            {
                _failureCode = failureCode;
            }

            public BootstrapResult StartShell(UiShellConfig config)
            {
                StartCalls++;
                return BootstrapResult.Fail(_failureCode, "deliberate failure for tests");
            }

            public void StopShell() { StopCalls++; }
            public void Dispose() { DisposeCalls++; }
        }
    }
}
