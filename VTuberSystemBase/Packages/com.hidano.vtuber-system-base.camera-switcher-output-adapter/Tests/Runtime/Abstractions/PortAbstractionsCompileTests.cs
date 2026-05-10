#nullable enable
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Abstractions
{
    /// <summary>
    /// Compile-time skeleton verifying every Task 1.2 port symbol is visible from
    /// Tests.Runtime through the Abstractions asmdef alone (Boundary: Abstractions/Ports).
    /// </summary>
    [TestFixture]
    public sealed class PortAbstractionsCompileTests
    {
        [Test]
        public void OscReceiverStartResultOk_HasSuccessTrue()
        {
            var ok = OscReceiverStartResult.Ok();
            Assert.That(ok.Success, Is.True);
            Assert.That(ok.FailureDetail, Is.Null);
        }

        [Test]
        public void OscReceiverStartResultFailure_PreservesDetail()
        {
            var failure = OscReceiverStartResult.Failure("port in use");
            Assert.That(failure.Success, Is.False);
            Assert.That(failure.FailureDetail, Is.EqualTo("port in use"));
        }

        [Test]
        public void VolumeBindResultOk_HasSuccessTrue()
        {
            var ok = VolumeBindResult.Ok();
            Assert.That(ok.Success, Is.True);
            Assert.That(ok.Reason, Is.Null);
        }

        [Test]
        public void VolumeBindResultError_DefaultReasonIsUnknown()
        {
            var err = VolumeBindResult.Error(string.Empty);
            Assert.That(err.Success, Is.False);
            Assert.That(err.Reason, Is.EqualTo(VolumeBindFailureReasons.Unknown));
        }

        [Test]
        public void OscReceiverHostStatus_DefaultIsStopped()
        {
            // Sanity: enum order keeps Stopped at 0 so default(OscReceiverHostStatus) == Stopped.
            Assert.That(default(OscReceiverHostStatus), Is.EqualTo(OscReceiverHostStatus.Stopped));
        }
    }
}
