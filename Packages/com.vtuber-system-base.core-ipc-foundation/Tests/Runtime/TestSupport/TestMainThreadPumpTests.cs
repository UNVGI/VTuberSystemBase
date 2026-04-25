#nullable enable
using System;
using NUnit.Framework;

namespace VTuberSystemBase.CoreIpc.Tests.TestSupport
{
    [TestFixture]
    public sealed class TestMainThreadPumpTests
    {
        [Test]
        public void Pump_InvokesAllRegisteredCallbacks()
        {
            var pump = new TestMainThreadPump();
            int countA = 0;
            int countB = 0;
            pump.Register(() => countA++);
            pump.Register(() => countB++);

            pump.Pump();

            Assert.AreEqual(1, countA);
            Assert.AreEqual(1, countB);
        }

        [Test]
        public void Pump_WithFrames_InvokesEachCallbackPerFrame()
        {
            var pump = new TestMainThreadPump();
            int count = 0;
            pump.Register(() => count++);

            pump.Pump(5);

            Assert.AreEqual(5, count);
        }

        [Test]
        public void Register_DisposeRemovesCallback()
        {
            var pump = new TestMainThreadPump();
            int count = 0;
            var token = pump.Register(() => count++);

            pump.Pump();
            Assert.AreEqual(1, count);

            token.Dispose();
            pump.Pump();

            Assert.AreEqual(1, count);
            Assert.AreEqual(0, pump.RegisteredCount);
        }

        [Test]
        public void Register_DisposeIsIdempotent()
        {
            var pump = new TestMainThreadPump();
            var token = pump.Register(() => { });
            token.Dispose();
            Assert.DoesNotThrow(() => token.Dispose());
        }

        [Test]
        public void Register_NullCallback_Throws()
        {
            var pump = new TestMainThreadPump();
            Assert.Throws<ArgumentNullException>(() => pump.Register(null!));
        }

        [Test]
        public void Pump_NegativeFrames_Throws()
        {
            var pump = new TestMainThreadPump();
            Assert.Throws<ArgumentOutOfRangeException>(() => pump.Pump(-1));
        }
    }
}
