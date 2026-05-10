#nullable enable
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Presenters;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 5.5 acceptance: duplicate-name rejection surfaces as a UI error,
    /// active preset cannot be deleted, activate triggers the orchestrator.
    /// </summary>
    [TestFixture]
    public sealed class PresetManagerPresenterTests
    {
        private sealed class FakeOrchestrator : IPresetRestoreOrchestrator
        {
            public int ReplayCount { get; private set; }

            public event Action<RestoreProgressEvent>? OnProgress;

            public Task ReplayActivePresetAsync(CancellationToken cancellationToken)
            {
                ReplayCount++;
                OnProgress?.Invoke(new RestoreProgressEvent());
                return Task.CompletedTask;
            }

            public void Dispose() { }
        }

        [Test]
        public async Task CreatePreset_DuplicateName_SurfacesError()
        {
            var clock = new ManualClock();
            var storage = new InMemoryPresetStorage();
            var logic = new PresetStoreLogic(storage, clock, TimeSpan.FromMilliseconds(500));
            await logic.InitializeAsync(CancellationToken.None);
            var store = new CharacterTabStateStore();
            var orch = new FakeOrchestrator();
            var container = new VisualElement();
            using var presenter = new PresetManagerPresenter(logic, store, orch, container, null);

            var first = await presenter.CreatePresetAsync("Morning");
            Assert.IsTrue(first.Success);
            var second = await presenter.CreatePresetAsync("Morning");

            Assert.IsFalse(second.Success);
            Assert.AreEqual(PresetOperationErrorCode.DuplicateName, second.Error);
            Assert.IsNotNull(presenter.LastErrorMessage);
        }

        [Test]
        public async Task DeleteActive_IsRejected()
        {
            var clock = new ManualClock();
            var storage = new InMemoryPresetStorage();
            var logic = new PresetStoreLogic(storage, clock, TimeSpan.FromMilliseconds(500));
            await logic.InitializeAsync(CancellationToken.None);
            var store = new CharacterTabStateStore();
            var orch = new FakeOrchestrator();
            var container = new VisualElement();
            using var presenter = new PresetManagerPresenter(logic, store, orch, container, null);
            var created = await presenter.CreatePresetAsync("A");
            Assert.IsTrue(created.Success);
            var activated = await presenter.ActivatePresetAsync(created.PresetId!);
            Assert.IsTrue(activated.Success);

            var del = await presenter.DeletePresetAsync(created.PresetId!);

            Assert.IsFalse(del.Success);
            Assert.AreEqual(PresetOperationErrorCode.CannotDeleteActive, del.Error);
        }

        [Test]
        public async Task Activate_TriggersOrchestratorReplay()
        {
            var clock = new ManualClock();
            var storage = new InMemoryPresetStorage();
            var logic = new PresetStoreLogic(storage, clock, TimeSpan.FromMilliseconds(500));
            await logic.InitializeAsync(CancellationToken.None);
            var store = new CharacterTabStateStore();
            var orch = new FakeOrchestrator();
            var container = new VisualElement();
            using var presenter = new PresetManagerPresenter(logic, store, orch, container, null);
            var created = await presenter.CreatePresetAsync("A");

            var r = await presenter.ActivatePresetAsync(created.PresetId!);

            Assert.IsTrue(r.Success);
            Assert.AreEqual(1, orch.ReplayCount);
            Assert.AreEqual(created.PresetId, store.ActivePresetId);
        }
    }
}
