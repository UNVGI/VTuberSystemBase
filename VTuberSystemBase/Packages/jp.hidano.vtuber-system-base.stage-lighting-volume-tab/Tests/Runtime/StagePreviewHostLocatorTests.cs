#nullable enable
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Lifecycle tests for the static <see cref="StagePreviewHostLocator"/> singleton
    /// (Task 2.4, Requirements 2.1, 2.5). The locator is a process-wide static, so
    /// every test is responsible for cleaning up its own registration in TearDown.
    /// </summary>
    [TestFixture]
    public sealed class StagePreviewHostLocatorTests
    {
        private FakePreviewHostService? _registered;

        [TearDown]
        public void TearDown()
        {
            // Defensive cleanup in case a test forgot to unregister.
            if (_registered is not null && ReferenceEquals(StagePreviewHostLocator.Current, _registered))
            {
                StagePreviewHostLocator.Unregister(_registered);
            }
            _registered = null;
        }

        [Test]
        public void Current_IsNull_BeforeAnyRegistration()
        {
            // Sanity: the locator starts (or has been cleaned up to) null.
            // A previous test may have leaked a registration, so explicitly null it
            // through Unregister of whatever is currently there to keep this assertion
            // deterministic.
            var leaked = StagePreviewHostLocator.Current;
            if (leaked is not null) StagePreviewHostLocator.Unregister(leaked);

            Assert.That(StagePreviewHostLocator.Current, Is.Null);
        }

        [Test]
        public void Register_PublishesService_AsCurrent()
        {
            var leaked = StagePreviewHostLocator.Current;
            if (leaked is not null) StagePreviewHostLocator.Unregister(leaked);

            var svc = new FakePreviewHostService();
            StagePreviewHostLocator.Register(svc);
            _registered = svc;

            Assert.That(StagePreviewHostLocator.Current, Is.SameAs(svc));
        }

        [Test]
        public void Unregister_ResetsCurrentToNull()
        {
            var leaked = StagePreviewHostLocator.Current;
            if (leaked is not null) StagePreviewHostLocator.Unregister(leaked);

            var svc = new FakePreviewHostService();
            StagePreviewHostLocator.Register(svc);
            StagePreviewHostLocator.Unregister(svc);

            Assert.That(StagePreviewHostLocator.Current, Is.Null);
        }

        [Test]
        public void Register_DuplicateRegistration_AdoptsLatestService()
        {
            var leaked = StagePreviewHostLocator.Current;
            if (leaked is not null) StagePreviewHostLocator.Unregister(leaked);

            var first = new FakePreviewHostService();
            var second = new FakePreviewHostService();
            StagePreviewHostLocator.Register(first);
            StagePreviewHostLocator.Register(second);
            _registered = second;

            // Latest registration wins (per design.md §StagePreviewHostLocator Validation).
            Assert.That(StagePreviewHostLocator.Current, Is.SameAs(second));
        }

        [Test]
        public void Unregister_OfStaleService_IsNoOp()
        {
            var leaked = StagePreviewHostLocator.Current;
            if (leaked is not null) StagePreviewHostLocator.Unregister(leaked);

            var first = new FakePreviewHostService();
            var second = new FakePreviewHostService();
            StagePreviewHostLocator.Register(first);
            StagePreviewHostLocator.Register(second);
            _registered = second;

            // Unregistering the now-stale "first" must NOT clear the locator,
            // because "second" is currently registered.
            StagePreviewHostLocator.Unregister(first);

            Assert.That(StagePreviewHostLocator.Current, Is.SameAs(second));
        }
    }
}
