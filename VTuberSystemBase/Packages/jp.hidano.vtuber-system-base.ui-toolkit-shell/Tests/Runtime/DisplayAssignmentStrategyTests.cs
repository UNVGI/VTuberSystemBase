#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Bootstrap;
using VTuberSystemBase.UiToolkitShell.Skin;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 10.3: <see cref="IDisplayAssignmentStrategy"/> をフック点として
    /// <see cref="UiShellBootstrapper"/> に組み込んだことを固定する
    /// （Requirement 1.6, design.md §UiShellBootstrapper.DisplayAssignmentHook）。
    /// 既定の <see cref="FixedDisplayZeroStrategy"/> は <c>targetDisplay = 0</c> を維持し、
    /// 差替え可能な戦略を渡したときに限り別 display へ割当できることを契約として確認する。
    /// </summary>
    [TestFixture]
    public sealed class DisplayAssignmentStrategyTests
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
            _skin.RootVisualTreeAsset = ScriptableObject.CreateInstance<VisualTreeAsset>();
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

        // ---- FixedDisplayZeroStrategy ----------------------------------

        [Test]
        [Description("FixedDisplayZeroStrategy は要求値に関係なく 0 を返す（既定の Display 1 限定保証）")]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(7)]
        [TestCase(-1)]
        public void FixedDisplayZeroStrategy_ResolveTargetDisplay_AlwaysReturnsZero(int requested)
        {
            var strategy = FixedDisplayZeroStrategy.Instance;

            Assert.That(strategy.ResolveTargetDisplay(requested), Is.EqualTo(0));
        }

        [Test]
        [Description("FixedDisplayZeroStrategy.Instance はシングルトン参照を提供する")]
        public void FixedDisplayZeroStrategy_Instance_IsSingleton()
        {
            Assert.That(FixedDisplayZeroStrategy.Instance, Is.SameAs(FixedDisplayZeroStrategy.Instance));
        }

        // ---- Default behaviour: targetDisplay pinned to 0 ---------------

        [Test]
        [Description("UiShellConfig.DisplayAssignmentStrategy 未指定時は FixedDisplayZeroStrategy が適用される")]
        public void StartShell_NoStrategyConfigured_UsesFixedDisplayZeroStrategy()
        {
            var bootstrapper = new UiShellBootstrapper(_rootFactory);
            var config = MakeConfig();
            // RequestedTargetDisplay defaults to 0.

            bootstrapper.StartShell(config);

            Assert.That(bootstrapper.DisplayAssignmentStrategy,
                Is.SameAs(FixedDisplayZeroStrategy.Instance),
                "Default fallback must be the singleton FixedDisplayZeroStrategy");
            Assert.That(bootstrapper.EffectiveTargetDisplay, Is.EqualTo(0));
            Assert.That(bootstrapper.PanelSettings!.targetDisplay, Is.EqualTo(0));
            bootstrapper.StopShell();
        }

        [Test]
        [Description("既定戦略では RequestedTargetDisplay が非 0 でも targetDisplay は 0 に固定される")]
        public void StartShell_DefaultStrategy_PinsTargetDisplayToZero_EvenWhenRequestedNonZero()
        {
            var bootstrapper = new UiShellBootstrapper(_rootFactory);
            var config = MakeConfig();
            config.RequestedTargetDisplay = 2;

            bootstrapper.StartShell(config);

            Assert.That(bootstrapper.EffectiveTargetDisplay, Is.EqualTo(0),
                "FixedDisplayZeroStrategy must collapse any requested value back to 0 (Requirement 1.7)");
            Assert.That(_rootFactory.LastRequestedTargetDisplay, Is.EqualTo(0),
                "The factory must be handed the strategy-resolved value, not the raw request");
            Assert.That(bootstrapper.PanelSettings!.targetDisplay, Is.EqualTo(0));
            bootstrapper.StopShell();
        }

        // ---- Replacement behaviour: targetDisplay can change ------------

        [Test]
        [Description("差替え Strategy を渡した場合に PanelSettings.targetDisplay がその戦略の返り値に変化する（Requirement 1.6）")]
        public void StartShell_CustomStrategyReturnsNonZero_PanelSettingsTargetDisplayMatches()
        {
            var bootstrapper = new UiShellBootstrapper(_rootFactory);
            var config = MakeConfig();
            config.DisplayAssignmentStrategy = new ConstantDisplayStrategy(2);

            bootstrapper.StartShell(config);

            Assert.That(bootstrapper.EffectiveTargetDisplay, Is.EqualTo(2));
            Assert.That(_rootFactory.LastRequestedTargetDisplay, Is.EqualTo(2),
                "The strategy-resolved value must be propagated to the root factory");
            Assert.That(bootstrapper.PanelSettings!.targetDisplay, Is.EqualTo(2),
                "Requirement 1.6: a swapped-in IDisplayAssignmentStrategy must control the actual targetDisplay");
            bootstrapper.StopShell();
        }

        [Test]
        [Description("差替え Strategy が複数の値を返しても PanelSettings.targetDisplay に反映される（網羅）")]
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(7)]
        public void StartShell_CustomStrategy_VariousValues_PanelSettingsTargetDisplayMatches(int resolved)
        {
            var bootstrapper = new UiShellBootstrapper(_rootFactory);
            var config = MakeConfig();
            config.DisplayAssignmentStrategy = new ConstantDisplayStrategy(resolved);

            bootstrapper.StartShell(config);

            Assert.That(bootstrapper.EffectiveTargetDisplay, Is.EqualTo(resolved));
            Assert.That(bootstrapper.PanelSettings!.targetDisplay, Is.EqualTo(resolved));
            bootstrapper.StopShell();
        }

        [Test]
        [Description("差替え Strategy は config.RequestedTargetDisplay を引数として受け取る")]
        public void StartShell_CustomStrategy_ReceivesRequestedTargetDisplay()
        {
            var bootstrapper = new UiShellBootstrapper(_rootFactory);
            var capturing = new CapturingDisplayStrategy(resolveTo: 5);
            var config = MakeConfig();
            config.RequestedTargetDisplay = 9;
            config.DisplayAssignmentStrategy = capturing;

            bootstrapper.StartShell(config);

            Assert.That(capturing.InvocationCount, Is.EqualTo(1));
            Assert.That(capturing.LastRequested, Is.EqualTo(9),
                "The bootstrapper must pass UiShellConfig.RequestedTargetDisplay verbatim to the strategy");
            Assert.That(bootstrapper.EffectiveTargetDisplay, Is.EqualTo(5));
            Assert.That(bootstrapper.PanelSettings!.targetDisplay, Is.EqualTo(5));
            bootstrapper.StopShell();
        }

        // ---- StopShell clears the hook state ----------------------------

        [Test]
        [Description("StopShell 後は DisplayAssignmentStrategy / EffectiveTargetDisplay がリセットされる")]
        public void StopShell_ClearsDisplayAssignmentState()
        {
            var bootstrapper = new UiShellBootstrapper(_rootFactory);
            var config = MakeConfig();
            config.DisplayAssignmentStrategy = new ConstantDisplayStrategy(1);

            bootstrapper.StartShell(config);
            bootstrapper.StopShell();

            Assert.That(bootstrapper.DisplayAssignmentStrategy, Is.Null);
            Assert.That(bootstrapper.EffectiveTargetDisplay, Is.Null);
        }

        // ---- Defensive: strategy throwing falls back to Display 0 -------

        [Test]
        [Description("Strategy が例外を投げた場合は Display 0 にフォールバックし、起動は完遂する")]
        public void StartShell_StrategyThrows_FallsBackToDisplayZero_AndStartupSucceeds()
        {
            var bootstrapper = new UiShellBootstrapper(_rootFactory);
            var config = MakeConfig();
            config.DisplayAssignmentStrategy = new ThrowingDisplayStrategy();

            var result = bootstrapper.StartShell(config);

            Assert.That(result.Success, Is.True,
                "Strategy failure must not block shell startup (Requirement 1.6 hook is non-fatal)");
            Assert.That(bootstrapper.EffectiveTargetDisplay, Is.EqualTo(0));
            Assert.That(bootstrapper.PanelSettings!.targetDisplay, Is.EqualTo(0));
            bootstrapper.StopShell();
        }

        // ---- helpers ----------------------------------------------------

        private sealed class ConstantDisplayStrategy : IDisplayAssignmentStrategy
        {
            private readonly int _value;
            public ConstantDisplayStrategy(int value) { _value = value; }
            public int ResolveTargetDisplay(int requested) => _value;
        }

        private sealed class CapturingDisplayStrategy : IDisplayAssignmentStrategy
        {
            private readonly int _resolveTo;
            public int InvocationCount { get; private set; }
            public int LastRequested { get; private set; }

            public CapturingDisplayStrategy(int resolveTo) { _resolveTo = resolveTo; }

            public int ResolveTargetDisplay(int requested)
            {
                InvocationCount++;
                LastRequested = requested;
                return _resolveTo;
            }
        }

        private sealed class ThrowingDisplayStrategy : IDisplayAssignmentStrategy
        {
            public int ResolveTargetDisplay(int requested)
                => throw new System.InvalidOperationException("strategy boom");
        }
    }
}
