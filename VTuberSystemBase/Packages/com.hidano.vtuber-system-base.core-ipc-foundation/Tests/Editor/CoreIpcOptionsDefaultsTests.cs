#nullable enable
using System;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class CoreIpcOptionsDefaultsTests
    {
        [Test]
        public void DefaultInstance_ExposesContractDefaults()
        {
            var options = new CoreIpcOptions();

            Assert.AreEqual("127.0.0.1", options.Host);
            Assert.AreEqual(61874, options.Port);
            Assert.AreEqual(TimeSpan.FromSeconds(5), options.DefaultRequestTimeout);
            Assert.AreEqual(1_048_576L, options.MaxMessageSizeBytes);
        }

        [Test]
        public void DefaultInstance_ExposesReconnectAndLoggingDefaults()
        {
            var options = new CoreIpcOptions();

            Assert.AreEqual(TimeSpan.FromMilliseconds(250), options.ReconnectInitialDelay);
            Assert.AreEqual(2.0, options.ReconnectMultiplier);
            Assert.AreEqual(TimeSpan.FromSeconds(5), options.ReconnectMaxDelay);
            Assert.AreEqual(20, options.ReconnectMaxAttempts);
            Assert.AreEqual(1000, options.EventQueueWarningThresholdPerTopic);
            Assert.AreEqual(LogLevel.Info, options.LogLevel);
        }

        [Test]
        public void With_PartialOverride_PreservesOtherDefaults()
        {
            var options = new CoreIpcOptions { Port = 50000, LogLevel = LogLevel.Debug };

            Assert.AreEqual(50000, options.Port);
            Assert.AreEqual(LogLevel.Debug, options.LogLevel);
            Assert.AreEqual("127.0.0.1", options.Host);
            Assert.AreEqual(TimeSpan.FromSeconds(5), options.DefaultRequestTimeout);
        }
    }
}
