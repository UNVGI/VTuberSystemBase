#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Diagnostics;
using VTuberSystemBase.StageLightingVolumeTab.Services;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;
using VTuberSystemBase.StageLightingVolumeTab.ViewModel;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>Helper factory shared across ViewModel test fixtures.</summary>
    internal static class ViewModelTestFactory
    {
        public static (StageLightingVolumeTabViewModel vm,
                       FakeIpcClient ipc,
                       FakePresetStorage storage,
                       FakeClock clock,
                       FakeConnectionStatus conn,
                       FakeDiagnosticsLogger logger,
                       LightListState lightList,
                       StageCatalogState stageCatalog,
                       VolumeSchemaCache volumeCache,
                       DebounceFlusher debounce,
                       StageTabDiagnostics diagnostics) Build(
            bool startConnected = true)
        {
            var ipc = new FakeIpcClient();
            var storage = new FakePresetStorage();
            var clock = new FakeClock();
            var conn = new FakeConnectionStatus(startConnected
                ? VTuberSystemBase.UiToolkitShell.Commands.ConnectionStatusCode.Connected
                : VTuberSystemBase.UiToolkitShell.Commands.ConnectionStatusCode.Disconnected);
            var logger = new FakeDiagnosticsLogger();
            var lightList = new LightListState(ipc, logger);
            var stageCatalog = new StageCatalogState(ipc, logger);
            var volumeCache = new VolumeSchemaCache(ipc, logger);
            var debounce = new DebounceFlusher(StageLightingVolumeTabViewModel.DefaultDebounceInterval, clock);
            var diagnostics = new StageTabDiagnostics(logger);
            var vm = new StageLightingVolumeTabViewModel(
                ipc, ipc, conn, storage, lightList, stageCatalog, volumeCache, debounce, clock,
                diagnostics, logger);
            return (vm, ipc, storage, clock, conn, logger, lightList, stageCatalog, volumeCache, debounce, diagnostics);
        }
    }

    /// <summary>
    /// Locks the lifecycle behaviour of <see cref="StageLightingVolumeTabViewModel"/>
    /// (Task 5.1, Requirements 1.5, 1.6, 3.1, 4.1, 6.1, 8.4, 8.5, 11.3).
    /// </summary>
    [TestFixture]
    public sealed class ViewModelLifecycleTests
    {
        [Test]
        public void OnActivated_WhenConnected_StartsListAndCatalogSubscriptions()
        {
            var ctx = ViewModelTestFactory.Build(startConnected: true);

            ctx.vm.OnActivated();

            // Lights/list + stage/catalog + stage/current + stage/loaded + stage/load-failed
            // + light/added + light/error → 7 subscriptions.
            Assert.That(ctx.ipc.Subscriptions, Has.Count.EqualTo(7));
        }

        [Test]
        public async Task OnActivated_WhenConnected_TriggersVolumeSchemaFetch_AndPresetLoad()
        {
            var ctx = ViewModelTestFactory.Build(startConnected: true);
            ctx.ipc.RequestResponder = _ => new VolumeOverrideSchemaDto(1, new List<VolumeOverrideTypeDto>());

            ctx.vm.OnActivated();

            // Allow the fire-and-forget initialize task to complete.
            await Task.Delay(25);

            Assert.That(ctx.ipc.Requests, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(ctx.storage.LoadCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void OnActivated_WhenDisconnected_DoesNotRunInitialization()
        {
            var ctx = ViewModelTestFactory.Build(startConnected: false);

            ctx.vm.OnActivated();

            Assert.That(ctx.ipc.Requests, Is.Empty);
            Assert.That(ctx.storage.LoadCount, Is.EqualTo(0));
        }

        [Test]
        public async Task OnConnect_AfterActivation_KicksOffInitialization()
        {
            var ctx = ViewModelTestFactory.Build(startConnected: false);
            ctx.ipc.RequestResponder = _ => new VolumeOverrideSchemaDto(1, new List<VolumeOverrideTypeDto>());
            ctx.vm.OnActivated();

            ctx.conn.SetStatus(VTuberSystemBase.UiToolkitShell.Commands.ConnectionStatusCode.Connected);
            await Task.Delay(25);

            Assert.That(ctx.ipc.Requests, Has.Count.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void OnDeactivated_DropsSubscriptions()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            Assert.That(ctx.ipc.Subscriptions, Is.Not.Empty);

            ctx.vm.OnDeactivated();

            Assert.That(ctx.ipc.Subscriptions, Is.Empty);
        }

        [Test]
        public void Dispose_IsIdempotent_AndDropsSubscriptions()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();

            ctx.vm.Dispose();
            ctx.vm.Dispose();

            Assert.That(ctx.ipc.Subscriptions, Is.Empty);
        }
    }
}
