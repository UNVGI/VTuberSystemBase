#nullable enable
using System;
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    /// <summary>
    /// Task 1.3 acceptance tests: <see cref="CameraId"/> guard rails,
    /// <see cref="UcapiFlatRecord"/> empty-vs-bytes semantics, and
    /// <see cref="SerializeResult"/> / <see cref="OscEmitResult"/> /
    /// <see cref="PresetIoResult"/> mutually-exclusive states.
    /// </summary>
    [TestFixture]
    public sealed class ValueTypeGuardsTests
    {
        // ---- CameraId ----

        [Test]
        public void CameraId_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new CameraId(null!));
        }

        [Test]
        public void CameraId_RejectsEmpty()
        {
            Assert.Throws<ArgumentException>(() => new CameraId(""));
        }

        [Test]
        public void CameraId_RejectsDisallowedCharacters()
        {
            Assert.Throws<ArgumentException>(() => new CameraId("cam/01"));
            Assert.Throws<ArgumentException>(() => new CameraId("cam 01"));
            Assert.Throws<ArgumentException>(() => new CameraId("cam.01"));
        }

        [Test]
        public void CameraId_AcceptsAllowedCharacters()
        {
            var id = new CameraId("cam_42-x");
            Assert.IsTrue(id.HasValue);
            Assert.AreEqual("cam_42-x", id.Value);
        }

        [Test]
        public void CameraId_DefaultIsUnset()
        {
            var id = default(CameraId);
            Assert.IsFalse(id.HasValue);
        }

        [Test]
        public void CameraId_TryCreate_FailsForInvalid()
        {
            Assert.IsFalse(CameraId.TryCreate(null, out _));
            Assert.IsFalse(CameraId.TryCreate("", out _));
            Assert.IsFalse(CameraId.TryCreate("a/b", out _));
        }

        // ---- UcapiFlatRecord ----

        [Test]
        public void UcapiFlatRecord_EmptyReportsHasValueFalse()
        {
            var rec = UcapiFlatRecord.Empty;
            Assert.IsFalse(rec.HasValue);
            Assert.AreEqual(0, rec.Length);
            Assert.AreEqual(0, rec.AsBytes().Length);
        }

        [Test]
        public void UcapiFlatRecord_FromBytesPreservesLength()
        {
            var bytes = new byte[UcapiFlatRecord.ExpectedSize];
            for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)i;
            var rec = UcapiFlatRecord.FromBytes(bytes);
            Assert.IsTrue(rec.HasValue);
            Assert.AreEqual(UcapiFlatRecord.ExpectedSize, rec.Length);
            Assert.AreSame(bytes, rec.AsBytes());
        }

        [Test]
        public void UcapiFlatRecord_FromBytesRejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => UcapiFlatRecord.FromBytes(null!));
        }

        // ---- SerializeResult ----

        [Test]
        public void SerializeResult_OkRequiresNonEmptyRecord()
        {
            Assert.Throws<ArgumentException>(() => SerializeResult.Ok(UcapiFlatRecord.Empty));
        }

        [Test]
        public void SerializeResult_InvalidRejectsNoneReason()
        {
            Assert.Throws<ArgumentException>(() => SerializeResult.Invalid(SerializeFailureReason.None));
        }

        [Test]
        public void SerializeResult_OkAndInvalidAreMutuallyExclusive()
        {
            var ok = SerializeResult.Ok(UcapiFlatRecord.FromBytes(new byte[1]));
            Assert.IsTrue(ok.Success);
            Assert.IsTrue(ok.Record.HasValue);
            Assert.AreEqual(SerializeFailureReason.None, ok.FailureReason);

            var bad = SerializeResult.Invalid(SerializeFailureReason.InvalidRotation, "NaN");
            Assert.IsFalse(bad.Success);
            Assert.IsFalse(bad.Record.HasValue);
            Assert.AreEqual(SerializeFailureReason.InvalidRotation, bad.FailureReason);
            Assert.AreEqual("NaN", bad.FailureDetail);
        }

        // ---- OscEmitResult ----

        [Test]
        public void OscEmitResult_OkHasNoFailure()
        {
            var ok = OscEmitResult.Ok();
            Assert.IsTrue(ok.Success);
            Assert.IsNull(ok.Failure);
        }

        [Test]
        public void OscEmitResult_FailCarriesKind()
        {
            var fail = OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.PortInUse, "57300"));
            Assert.IsFalse(fail.Success);
            Assert.IsNotNull(fail.Failure);
            Assert.AreEqual(OscFailureKind.PortInUse, fail.Failure!.Value.Kind);
            Assert.AreEqual("57300", fail.Failure!.Value.Detail);
        }

        // ---- PresetIoResult ----

        [Test]
        public void PresetIoResult_OkHasNoFailureKind()
        {
            var ok = PresetIoResult.Ok();
            Assert.IsTrue(ok.Success);
            Assert.AreEqual(PresetIoFailureKind.None, ok.FailureKind);
            Assert.IsNull(ok.FailureDetail);
        }

        [Test]
        public void PresetIoResult_FailRejectsNone()
        {
            Assert.Throws<ArgumentException>(() => PresetIoResult.Fail(PresetIoFailureKind.None));
        }

        [Test]
        public void PresetIoResult_FailCarriesKindAndDetail()
        {
            var ex = new System.IO.IOException("boom");
            var bad = PresetIoResult.Fail(PresetIoFailureKind.WriteFailed, "disk full", ex);
            Assert.IsFalse(bad.Success);
            Assert.AreEqual(PresetIoFailureKind.WriteFailed, bad.FailureKind);
            Assert.AreEqual("disk full", bad.FailureDetail);
            Assert.AreSame(ex, bad.Inner);
        }
    }
}
