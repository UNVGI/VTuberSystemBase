#nullable enable
using NUnit.Framework;
using System;
using System.Threading.Tasks;
using UnityEngine;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using ConnectionStatusCode = VTuberSystemBase.UiToolkitShell.Commands.ConnectionStatusCode;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 1.4 acceptance test: every test double behaves correctly under its own
    /// public API. Failures here would make every later integration test ambiguous.
    /// </summary>
    [TestFixture]
    public sealed class TestDoublesSelfTests
    {
        [Test]
        public void ManualClock_AdvanceFiresOnTick()
        {
            var clock = new ManualClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            DateTimeOffset? observed = null;
            clock.OnTick += t => observed = t;
            clock.Advance(TimeSpan.FromMilliseconds(250));
            Assert.IsTrue(observed.HasValue);
            Assert.AreEqual(TimeSpan.FromMilliseconds(250), clock.UtcNow - new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        }

        [Test]
        public void FakeUiCommandClient_RecordsAndCanFail()
        {
            var c = new FakeUiCommandClient();
            var ok = c.PublishState("topic/x", new { v = 1 });
            Assert.IsTrue(ok.Success);
            c.ForceFail = true;
            c.FailWith = new SendError(SendErrorCode.NotConnected);
            var fail = c.PublishEvent("topic/x", new { v = 2 });
            Assert.IsFalse(fail.Success);
            Assert.AreEqual(SendErrorCode.NotConnected, fail.Error?.Code);
            Assert.AreEqual(2, c.Sent.Count);
            Assert.AreEqual(MessageKind.State, c.Sent[0].Kind);
            Assert.AreEqual(MessageKind.Event, c.Sent[1].Kind);
        }

        [Test]
        public async Task FakeUiCommandClient_RequestUsesResponder()
        {
            var c = new FakeUiCommandClient
            {
                RequestResponder = _ => "hello",
            };
            var resp = await c.RequestAsync<string, string>("topic/y", "ping");
            Assert.IsTrue(resp.Success);
            Assert.AreEqual("hello", resp.Response);
            Assert.AreEqual(1, c.Requests.Count);
        }

        [Test]
        public void FakeUiSubscriptionClient_DeliversToTopicAndKind()
        {
            var s = new FakeUiSubscriptionClient();
            int hits = 0;
            using var token = s.Subscribe<int>("a", MessageKind.State, env => { hits += env.Payload; });
            s.Emit("a", 1);
            s.Emit("a", 5);
            s.Emit("b", 100); // different topic, no hit
            s.Emit("a", 7, MessageKind.Event); // wrong kind, no hit
            Assert.AreEqual(6, hits);
        }

        [Test]
        public void FakeUiSubscriptionClient_DisposeStops()
        {
            var s = new FakeUiSubscriptionClient();
            int hits = 0;
            var token = s.Subscribe<int>("a", MessageKind.State, env => hits++);
            s.Emit("a", 1);
            token.Dispose();
            s.Emit("a", 2);
            Assert.AreEqual(1, hits);
        }

        [Test]
        public void FakeAsyncAssetLoader_ResolvesRegisteredAndKeyNotFound()
        {
            var loader = new FakeAsyncAssetLoader();
            var sprite = ScriptableObject.CreateInstance<UnityEngine.ScriptableObject>(); // any UnityEngine.Object
            try
            {
                loader.RegisterAsset("hit", sprite);
                AssetLoadResult<UnityEngine.Object> got = default;
                loader.LoadAsync<UnityEngine.Object>("hit", "scope:t", r => got = r);
                Assert.IsTrue(got.Success);

                AssetLoadResult<UnityEngine.Object> miss = default;
                loader.LoadAsync<UnityEngine.Object>("miss", "scope:t", r => miss = r);
                Assert.IsFalse(miss.Success);
                Assert.AreEqual(LoadErrorCode.KeyNotFound, miss.Error?.Code);

                loader.ReleaseAll("scope:t");
                Assert.Contains("scope:t", loader.ScopeReleases);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        [Test]
        public void FakeConnectionStatus_RaisesEventOnTransition()
        {
            var s = new FakeConnectionStatus(ConnectionStatusCode.Disconnected);
            ConnectionStatusEvent? captured = null;
            s.OnStatusChanged += e => captured = e;
            s.SetStatus(ConnectionStatusCode.Connected);
            Assert.IsTrue(captured.HasValue);
            Assert.AreEqual(ConnectionStatusCode.Disconnected, captured!.Value.From);
            Assert.AreEqual(ConnectionStatusCode.Connected, captured!.Value.To);
            Assert.IsTrue(s.IsConnected);
        }

        [Test]
        public void FakeDiagnosticsLogger_RespectsMinimumLevel()
        {
            var l = new FakeDiagnosticsLogger { MinimumLevel = LogLevel.Warning };
            l.Log(LogLevel.Debug, LogCategory.TabSpec, "should be dropped");
            l.Log(LogLevel.Warning, LogCategory.TabSpec, "kept");
            Assert.AreEqual(1, l.Entries.Count);
            Assert.AreEqual("kept", l.Entries[0].Message);
        }

        [Test]
        public async Task InMemoryPresetStorage_SaveLoadDelete()
        {
            var s = new InMemoryPresetStorage();
            var rec = new PresetRecord
            {
                Header = new PresetHeader { PresetId = "p1", Name = "Morning" },
            };
            await s.SaveAsync(rec, default);
            var all = await s.LoadAllAsync(default);
            Assert.AreEqual(1, all.Count);
            await s.SetActiveAsync("p1", default);
            Assert.AreEqual("p1", await s.LoadActivePresetIdAsync(default));
            await s.DeleteAsync("p1", default);
            Assert.AreEqual(0, (await s.LoadAllAsync(default)).Count);
        }
    }
}
