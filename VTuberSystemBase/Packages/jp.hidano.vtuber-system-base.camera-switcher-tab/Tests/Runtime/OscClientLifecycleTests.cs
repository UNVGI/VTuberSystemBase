#nullable enable
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Adapters.Osc;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;
using VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    [TestFixture]
    public sealed class OscClientLifecycleTests
    {
        [Test]
        public void Defaults_AreLocalhostAnd57300()
        {
            var emitter = new FakeOscEmitter();
            using var sut = new OscClientLifecycle(emitter);
            Assert.AreEqual(OscClientLifecycle.DefaultHost, sut.Host);
            Assert.AreEqual(OscClientLifecycle.DefaultPort, sut.Port);
            Assert.AreEqual(57300, sut.Port);
            Assert.AreEqual("127.0.0.1", sut.Host);
        }

        [Test]
        public void Configure_AppliesOnNextStart()
        {
            var emitter = new FakeOscEmitter();
            using var sut = new OscClientLifecycle(emitter);
            sut.Configure("10.0.0.5", 9000);
            Assert.AreEqual("10.0.0.5", sut.Host);
            Assert.AreEqual(9000, sut.Port);
        }

        [Test]
        public void Configure_RejectsEmptyHost()
        {
            var emitter = new FakeOscEmitter();
            using var sut = new OscClientLifecycle(emitter);
            Assert.Throws<System.ArgumentException>(() => sut.Configure("", 1234));
        }

        [Test]
        public async System.Threading.Tasks.Task StartAsync_PassesHostPortToEmitter()
        {
            var emitter = new FakeOscEmitter();
            using var sut = new OscClientLifecycle(emitter, "192.168.1.1", 7000);
            var result = await sut.StartAsync();
            Assert.IsTrue(result.Success);
            Assert.AreEqual("192.168.1.1", emitter.LastHost);
            Assert.AreEqual(7000, emitter.LastPort);
            Assert.AreEqual(OscEmitterState.Running, sut.EmitterState);
        }

        [Test]
        public async System.Threading.Tasks.Task StartAsync_SurfacesEmitterFailure()
        {
            var emitter = new FakeOscEmitter { ForceStartFailure = true };
            using var sut = new OscClientLifecycle(emitter);
            var result = await sut.StartAsync();
            Assert.IsFalse(result.Success);
            Assert.AreEqual(OscFailureKind.InitializationFailed, result.Failure!.Value.Kind);
        }

        [Test]
        public async System.Threading.Tasks.Task StopAsync_TransitionsBackToStopped()
        {
            var emitter = new FakeOscEmitter();
            using var sut = new OscClientLifecycle(emitter);
            await sut.StartAsync();
            var stop = await sut.StopAsync();
            Assert.IsTrue(stop.Success);
            Assert.AreEqual(OscEmitterState.Stopped, sut.EmitterState);
        }

        [Test]
        public void Dispose_DisposesEmitter()
        {
            var emitter = new FakeOscEmitter();
            var sut = new OscClientLifecycle(emitter);
            sut.Dispose();
            Assert.AreEqual(OscEmitterState.Disposed, emitter.State);
        }

        [Test]
        public void OscAddressBuilder_BuildsExpectedFlatAddress()
        {
            var addr = OscAddressBuilder.BuildFlatAddress("cam-7");
            Assert.AreEqual("/ucapi/camera/cam-7/flat", addr);
        }
    }
}
