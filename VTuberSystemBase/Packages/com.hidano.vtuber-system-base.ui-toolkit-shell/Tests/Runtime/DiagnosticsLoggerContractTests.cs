#nullable enable
using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 3.1 (Red): <c>IDiagnosticsLogger</c> 契約と <c>LogLevel</c> / <c>LogCategory</c> 列挙の
    /// 振る舞いを定義する。 3.2 で <c>IDiagnosticsLogger</c> / <c>LogLevel</c> / <c>LogCategory</c> /
    /// <c>DiagnosticsLogger</c> を実装するまでは「型未定義」（CS0246）で失敗する。
    /// design.md §Diagnostics の Service Interface セクション
    /// （<c>IDiagnosticsLogger.Log(LogLevel, LogCategory, string, object?)</c>,
    /// <c>MinimumLevel { get; set; }</c>, <c>LogLevel = Trace/Debug/Info/Warning/Error</c>,
    /// <c>LogCategory = Preload/TabSwitch/AssetLoad/Ipc/Connection/Skin/Lifecycle/TabSpec</c>）
    /// に対応する。
    /// </summary>
    [TestFixture]
    public sealed class DiagnosticsLoggerContractTests
    {
        [Test]
        [Description("LogLevel が Trace < Debug < Info < Warning < Error の昇順で宣言されること（Req 11.8）")]
        public void LogLevel_OrderingIsAscendingBySeverity()
        {
            Assert.That((int)LogLevel.Trace, Is.LessThan((int)LogLevel.Debug));
            Assert.That((int)LogLevel.Debug, Is.LessThan((int)LogLevel.Info));
            Assert.That((int)LogLevel.Info, Is.LessThan((int)LogLevel.Warning));
            Assert.That((int)LogLevel.Warning, Is.LessThan((int)LogLevel.Error));
        }

        [Test]
        [Description("LogCategory が design.md §Diagnostics で定義する 8 カテゴリを宣言すること（Req 11.1〜11.6）")]
        public void LogCategory_DeclaresAllRequiredCategories()
        {
            var declared = Enum.GetNames(typeof(LogCategory));
            Assert.That(declared, Is.SupersetOf(new[]
            {
                nameof(LogCategory.Preload),
                nameof(LogCategory.TabSwitch),
                nameof(LogCategory.AssetLoad),
                nameof(LogCategory.Ipc),
                nameof(LogCategory.Connection),
                nameof(LogCategory.Skin),
                nameof(LogCategory.Lifecycle),
                nameof(LogCategory.TabSpec),
            }));
        }

        [Test]
        [Description("MinimumLevel を下回るレベルのログは Unity Console に一切出力されないこと（Req 11.8）")]
        public void Log_BelowMinimumLevel_IsNotEmittedToUnityConsole()
        {
            IDiagnosticsLogger logger = new DiagnosticsLogger { MinimumLevel = LogLevel.Warning };

            // Trace / Debug / Info はいずれも Warning 未満なので、Unity Console に出力されてはならない。
            // LogAssert.Expect を呼ばないため、出力が発生すれば NoUnexpectedReceived で失敗する。
            logger.Log(LogLevel.Trace, LogCategory.AssetLoad, "trace-suppressed");
            logger.Log(LogLevel.Debug, LogCategory.Ipc, "debug-suppressed");
            logger.Log(LogLevel.Info, LogCategory.Preload, "info-suppressed");

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        [Description("MinimumLevel 以上のログは Unity Console にレベル種別とカテゴリトークンを保ったまま出力されること（Req 11.4 / 11.8）")]
        public void Log_AtMinimumLevelOrAbove_EmitsToUnityConsole_WithCategoryInFormat()
        {
            IDiagnosticsLogger logger = new DiagnosticsLogger { MinimumLevel = LogLevel.Info };

            // Warning は LogType.Warning として Console へ。カテゴリ名 "Connection" がフォーマットに含まれる。
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Warning\]\[Connection\] reconnecting"));
            logger.Log(LogLevel.Warning, LogCategory.Connection, "reconnecting");

            // Error は LogType.Error として Console へ。カテゴリ名 "Ipc" がフォーマットに含まれる。
            LogAssert.Expect(LogType.Error, new Regex(@"\[Error\]\[Ipc\] send-failed"));
            logger.Log(LogLevel.Error, LogCategory.Ipc, "send-failed");
        }

        [Test]
        [Description("カテゴリ別のメッセージがそれぞれ正しいカテゴリトークンで配信されること（Req 11.1〜11.6）")]
        public void Log_DifferentCategories_AllEmitWithCorrespondingCategoryToken()
        {
            IDiagnosticsLogger logger = new DiagnosticsLogger { MinimumLevel = LogLevel.Trace };
            var categories = new[]
            {
                LogCategory.Preload,
                LogCategory.TabSwitch,
                LogCategory.AssetLoad,
                LogCategory.Ipc,
                LogCategory.Connection,
                LogCategory.Skin,
                LogCategory.Lifecycle,
                LogCategory.TabSpec,
            };

            foreach (var category in categories)
            {
                LogAssert.Expect(LogType.Log, new Regex(@"\[Info\]\[" + category + @"\] msg-" + category));
                logger.Log(LogLevel.Info, category, "msg-" + category);
            }
        }

        [Test]
        [Description("MinimumLevel は実行時に変更可能で、変更後のログから新ポリシーが適用されること（Req 11.8）")]
        public void MinimumLevel_RuntimeMutable_AffectsSubsequentDispatch()
        {
            IDiagnosticsLogger logger = new DiagnosticsLogger { MinimumLevel = LogLevel.Error };

            // 初期状態（MinimumLevel = Error）では Warning は抑制される。
            logger.Log(LogLevel.Warning, LogCategory.Lifecycle, "suppressed-warn");
            LogAssert.NoUnexpectedReceived();

            // 実行時に Trace に下げると、以降の Warning は出力されるようになる。
            logger.MinimumLevel = LogLevel.Trace;
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Warning\]\[Lifecycle\] now-allowed"));
            logger.Log(LogLevel.Warning, LogCategory.Lifecycle, "now-allowed");
        }
    }
}
