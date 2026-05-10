#nullable enable
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Stage;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    /// <summary>
    /// EditMode coverage that does not require a configured Addressables group. The
    /// happy-path (real Addressables instantiate / release) is exercised in PlayMode tests
    /// (Task 9.2 / SLOA-3).
    /// </summary>
    public sealed class AddressablesInstantiationProviderTests
    {
        [Test]
        public void InstantiateAsync_NullKey_FailsFastWithNotFound()
        {
            var p = new AddressablesInstantiationProvider();
            var task = p.InstantiateAsync(null!, parent: null!);
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.Result.Success, Is.False);
            Assert.That(task.Result.ErrorCode, Is.EqualTo("not_found"));
        }

        [Test]
        public void InstantiateAsync_EmptyKey_FailsFastWithNotFound()
        {
            var p = new AddressablesInstantiationProvider();
            var task = p.InstantiateAsync(string.Empty, parent: null!);
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.Result.Success, Is.False);
            Assert.That(task.Result.ErrorCode, Is.EqualTo("not_found"));
        }

        [Test]
        public void LoadResourceLocationsAsync_EmptyLabel_ReturnsEmpty()
        {
            var p = new AddressablesInstantiationProvider();
            var task = p.LoadResourceLocationsAsync(string.Empty);
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.Result, Is.Empty);
        }

        [Test]
        public void ReleaseInstance_Null_IsNoOp()
        {
            var p = new AddressablesInstantiationProvider();
            Assert.DoesNotThrow(() => p.ReleaseInstance(null!));
        }
    }
}
