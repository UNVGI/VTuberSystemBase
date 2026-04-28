#nullable enable
using System;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class CoreIpcErrorVariantsTests
    {
        [Test]
        public void AllVariants_AreSubtypesOfCoreIpcError_AndCarryStableCodes()
        {
            CoreIpcError[] variants =
            {
                new CoreIpcError.NotConnected(),
                new CoreIpcError.SizeLimitExceeded(2_000_000, 1_048_576),
                new CoreIpcError.InvalidTopic(""),
                new CoreIpcError.InvalidEnvelope("missing kind"),
                new CoreIpcError.RequestTimeout(TimeSpan.FromSeconds(5)),
                new CoreIpcError.PortInUse(61874),
                new CoreIpcError.ProtocolVersionMismatch("2.0", "1.0"),
                new CoreIpcError.TransportFailure("socket reset"),
                new CoreIpcError.HandlerException("NRE in handler"),
            };

            string[] expectedCodes =
            {
                "NOT_CONNECTED",
                "SIZE_LIMIT",
                "INVALID_TOPIC",
                "INVALID_ENVELOPE",
                "TIMEOUT",
                "PORT_IN_USE",
                "VERSION_MISMATCH",
                "TRANSPORT",
                "HANDLER_EX",
            };

            for (var i = 0; i < variants.Length; i++)
            {
                Assert.IsInstanceOf<CoreIpcError>(variants[i]);
                Assert.AreEqual(expectedCodes[i], variants[i].Code);
                Assert.IsNotNull(variants[i].Message);
                Assert.IsNotEmpty(variants[i].Message);
            }
        }

        [Test]
        public void SizeLimitExceeded_PreservesActualAndLimit()
        {
            var error = new CoreIpcError.SizeLimitExceeded(2_000_000, 1_048_576);

            Assert.AreEqual(2_000_000L, error.ActualBytes);
            Assert.AreEqual(1_048_576L, error.LimitBytes);
        }

        [Test]
        public void RequestTimeout_PreservesElapsed()
        {
            var elapsed = TimeSpan.FromSeconds(7);
            var error = new CoreIpcError.RequestTimeout(elapsed);

            Assert.AreEqual(elapsed, error.Elapsed);
        }

        [Test]
        public void Records_WithSamePayload_AreEqual()
        {
            var a = new CoreIpcError.PortInUse(61874);
            var b = new CoreIpcError.PortInUse(61874);

            Assert.AreEqual(a, b);
        }
    }
}
