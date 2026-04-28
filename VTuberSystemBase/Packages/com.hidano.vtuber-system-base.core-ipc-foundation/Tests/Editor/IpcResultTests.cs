#nullable enable
using System;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class IpcResultTests
    {
        [Test]
        public void Ok_NonGeneric_HasSuccessTrueAndNullError()
        {
            var result = IpcResult.Ok();

            Assert.IsTrue(result.Success);
            Assert.IsNull(result.Error);
        }

        [Test]
        public void Fail_NonGeneric_HasSuccessFalseAndError()
        {
            var error = new CoreIpcError.NotConnected();

            var result = IpcResult.Fail(error);

            Assert.IsFalse(result.Success);
            Assert.AreSame(error, result.Error);
        }

        [Test]
        public void Fail_NonGeneric_NullError_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => IpcResult.Fail(null!));
        }

        [Test]
        public void Ok_Generic_CarriesValue()
        {
            var result = IpcResult<int>.Ok(42);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(42, result.Value);
            Assert.IsNull(result.Error);
        }

        [Test]
        public void Fail_Generic_HasDefaultValueAndError()
        {
            var error = new CoreIpcError.RequestTimeout(TimeSpan.FromSeconds(5));

            var result = IpcResult<string>.Fail(error);

            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Value);
            Assert.AreSame(error, result.Error);
        }
    }
}
