#nullable enable
using System;
using NUnit.Framework;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Bootstrap;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 5.3: <see cref="AddressablesBootstrap"/> contract tests. Verifies that
    /// <c>Addressables.InitializeAsync()</c> is wrapped, that failure injection produces
    /// <see cref="BootstrapErrorCode.AddressablesInitFailed"/>, and that the four lifecycle
    /// events (Started / Completed / Failed) the bootstrap is responsible for are emitted to
    /// <see cref="LogCategory.AssetLoad"/> (design.md §AssetLoading
    /// §AddressablesAssetLoader Implementation Notes; Requirement 4.1, 11.3, 9.1).
    /// </summary>
    [TestFixture]
    public sealed class AddressablesBootstrapTests
    {
        [Test]
        [Description("Initialize() の引数バリデーション: null callback は ArgumentNullException")]
        public void Initialize_NullCallback_Throws()
        {
            var initializer = new FakeAddressablesInitializer();
            var logger = new RecordingDiagnosticsLogger();
            var bootstrap = new AddressablesBootstrap(initializer, logger);

            Assert.Throws<ArgumentNullException>(() => bootstrap.Initialize(null!));
        }

        [Test]
        [Description("コンストラクタは null 依存を拒否する（Bootstrap is a Composition Root, not a static factory）")]
        public void Constructor_NullArguments_Throw()
        {
            var initializer = new FakeAddressablesInitializer();
            var logger = new RecordingDiagnosticsLogger();
            Assert.Throws<ArgumentNullException>(() => new AddressablesBootstrap(null!, logger));
            Assert.Throws<ArgumentNullException>(() => new AddressablesBootstrap(initializer, null!));
        }

        [Test]
        [Description("成功時: BootstrapResult.Success == true / Error == null / Detail == null（Requirement 4.1）")]
        public void Initialize_Success_ReturnsBootstrapResultOk()
        {
            var initializer = new FakeAddressablesInitializer
            {
                Mode = FakeAddressablesInitializer.CompletionMode.Immediate,
                StagedResult = AddressablesInitResult.Ok(),
            };
            var logger = new RecordingDiagnosticsLogger();
            var bootstrap = new AddressablesBootstrap(initializer, logger);
            BootstrapResult? observed = null;

            bootstrap.Initialize(result => observed = result);

            Assert.That(observed.HasValue, Is.True);
            Assert.That(observed!.Value.Success, Is.True);
            Assert.That(observed.Value.Error, Is.Null);
            Assert.That(observed.Value.Detail, Is.Null);
            Assert.That(initializer.InvocationCount, Is.EqualTo(1));
        }

        [Test]
        [Description("失敗注入時: Error == BootstrapErrorCode.AddressablesInitFailed が即時返り、シェルが安全に起動中断できる（task 5.3 観測可能な完了状態）")]
        public void Initialize_FailureInjection_ReturnsBootstrapErrorCode_AddressablesInitFailed()
        {
            var initializer = new FakeAddressablesInitializer
            {
                Mode = FakeAddressablesInitializer.CompletionMode.Immediate,
                StagedResult = AddressablesInitResult.Fail(
                    new InvalidOperationException("settings missing"),
                    "settings missing"),
            };
            var logger = new RecordingDiagnosticsLogger();
            var bootstrap = new AddressablesBootstrap(initializer, logger);
            BootstrapResult? observed = null;

            bootstrap.Initialize(result => observed = result);

            Assert.That(observed.HasValue, Is.True);
            Assert.That(observed!.Value.Success, Is.False);
            Assert.That(observed.Value.Error.HasValue, Is.True);
            Assert.That(observed.Value.Error!.Value, Is.EqualTo(BootstrapErrorCode.AddressablesInitFailed));
            Assert.That(observed.Value.Detail, Does.Contain("settings missing"));
        }

        [Test]
        [Description("失敗時に AddressablesInitResult.Detail が null でも、Exception.Message にフォールバックして Detail が埋まる（Requirement 4.4 alignment）")]
        public void Initialize_FailureWithoutDetail_FallsBackToExceptionMessage()
        {
            var initializer = new FakeAddressablesInitializer
            {
                Mode = FakeAddressablesInitializer.CompletionMode.Immediate,
                StagedResult = AddressablesInitResult.Fail(
                    new InvalidOperationException("addressables-disabled"),
                    detail: null),
            };
            var logger = new RecordingDiagnosticsLogger();
            var bootstrap = new AddressablesBootstrap(initializer, logger);
            BootstrapResult? observed = null;

            bootstrap.Initialize(r => observed = r);

            Assert.That(observed!.Value.Success, Is.False);
            Assert.That(observed.Value.Detail, Is.EqualTo("addressables-disabled"));
        }

        [Test]
        [Description("成功時の必須ログ: Initialize 呼出し前に Started、callback 配信時に Completed が AssetLoad カテゴリで Info 出力される（Req 11.3）")]
        public void Initialize_Success_EmitsStartedAndCompleted_ToAssetLoadCategory()
        {
            var initializer = new FakeAddressablesInitializer
            {
                Mode = FakeAddressablesInitializer.CompletionMode.Immediate,
                StagedResult = AddressablesInitResult.Ok(),
            };
            var logger = new RecordingDiagnosticsLogger();
            var bootstrap = new AddressablesBootstrap(initializer, logger);

            bootstrap.Initialize(_ => { });

            Assert.That(logger.Entries, Has.Count.EqualTo(2));

            var started = logger.Entries[0];
            Assert.That(started.Level, Is.EqualTo(LogLevel.Info));
            Assert.That(started.Category, Is.EqualTo(LogCategory.AssetLoad));
            Assert.That(started.Message, Does.Contain("AddressablesInitStarted"));

            var completed = logger.Entries[1];
            Assert.That(completed.Level, Is.EqualTo(LogLevel.Info));
            Assert.That(completed.Category, Is.EqualTo(LogCategory.AssetLoad));
            Assert.That(completed.Message, Does.Contain("AddressablesInitCompleted"));
        }

        [Test]
        [Description("失敗時の必須ログ: Started を Info、Failed を Error レベルで AssetLoad カテゴリに出力し、detail がメッセージに含まれる（Req 11.3）")]
        public void Initialize_Failure_EmitsStartedThenFailed_AtErrorLevel_WithDetail()
        {
            var initializer = new FakeAddressablesInitializer
            {
                Mode = FakeAddressablesInitializer.CompletionMode.Immediate,
                StagedResult = AddressablesInitResult.Fail(
                    new InvalidOperationException("locator missing"),
                    "locator missing"),
            };
            var logger = new RecordingDiagnosticsLogger();
            var bootstrap = new AddressablesBootstrap(initializer, logger);

            bootstrap.Initialize(_ => { });

            Assert.That(logger.Entries, Has.Count.EqualTo(2));

            var started = logger.Entries[0];
            Assert.That(started.Level, Is.EqualTo(LogLevel.Info));
            Assert.That(started.Category, Is.EqualTo(LogCategory.AssetLoad));
            Assert.That(started.Message, Does.Contain("AddressablesInitStarted"));

            var failed = logger.Entries[1];
            Assert.That(failed.Level, Is.EqualTo(LogLevel.Error));
            Assert.That(failed.Category, Is.EqualTo(LogCategory.AssetLoad));
            Assert.That(failed.Message, Does.Contain("AddressablesInitFailed"));
            Assert.That(failed.Message, Does.Contain("locator missing"));
        }

        [Test]
        [Description("Deferred モード: callback は Resolve() を呼ぶまで発火しない（Req 4.3 同期 API 禁止）")]
        public void Initialize_Deferred_DoesNotInvokeCallback_UntilResolve()
        {
            var initializer = new FakeAddressablesInitializer
            {
                Mode = FakeAddressablesInitializer.CompletionMode.Deferred,
            };
            var logger = new RecordingDiagnosticsLogger();
            var bootstrap = new AddressablesBootstrap(initializer, logger);
            int callCount = 0;
            BootstrapResult? observed = null;

            bootstrap.Initialize(r => { callCount++; observed = r; });

            Assert.That(callCount, Is.EqualTo(0));
            Assert.That(initializer.HasPendingCallback, Is.True);
            // Started を出した時点で Completed/Failed はまだ出ていないこと
            Assert.That(logger.Entries, Has.Count.EqualTo(1));
            Assert.That(logger.Entries[0].Message, Does.Contain("AddressablesInitStarted"));

            initializer.Resolve(AddressablesInitResult.Fail(null, "deferred-failure"));

            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(observed!.Value.Success, Is.False);
            Assert.That(observed.Value.Error!.Value, Is.EqualTo(BootstrapErrorCode.AddressablesInitFailed));
            Assert.That(observed.Value.Detail, Is.EqualTo("deferred-failure"));
            Assert.That(logger.Entries, Has.Count.EqualTo(2));
            Assert.That(logger.Entries[1].Level, Is.EqualTo(LogLevel.Error));
        }

        [Test]
        [Description("BootstrapErrorCode の列挙値に AddressablesInitFailed が含まれる（design.md §Bootstrap §UiShellBootstrapper）")]
        public void BootstrapErrorCode_DeclaresAddressablesInitFailed()
        {
            var declared = Enum.GetNames(typeof(BootstrapErrorCode));
            Assert.That(declared, Does.Contain(nameof(BootstrapErrorCode.AddressablesInitFailed)));
            Assert.That(declared, Is.SupersetOf(new[]
            {
                nameof(BootstrapErrorCode.SkinProfileMissing),
                nameof(BootstrapErrorCode.PanelSettingsAssignFailed),
                nameof(BootstrapErrorCode.TabUxmlAttachFailed),
                nameof(BootstrapErrorCode.AddressablesInitFailed),
                nameof(BootstrapErrorCode.IpcAbstractionUnavailable),
            }));
        }

        [Test]
        [Description("BootstrapResult.Ok / Fail の構築契約: Ok は Error/Detail が null、Fail は Error と Detail を保持する")]
        public void BootstrapResult_FactoryConstructors_PreserveFields()
        {
            var ok = BootstrapResult.Ok();
            Assert.That(ok.Success, Is.True);
            Assert.That(ok.Error, Is.Null);
            Assert.That(ok.Detail, Is.Null);

            var fail = BootstrapResult.Fail(BootstrapErrorCode.AddressablesInitFailed, "x");
            Assert.That(fail.Success, Is.False);
            Assert.That(fail.Error!.Value, Is.EqualTo(BootstrapErrorCode.AddressablesInitFailed));
            Assert.That(fail.Detail, Is.EqualTo("x"));
        }
    }
}
