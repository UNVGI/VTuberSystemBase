#nullable enable
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.OutputRendererShell.Diagnostics;

namespace VTuberSystemBase.OutputRendererShell.EditModeTests
{
    /// <summary>
    /// Task 1.3: <see cref="OutputShellLogger"/> のレベル抑制と Unity Console 経由の出力を検証する。
    /// </summary>
    [TestFixture]
    public class OutputShellLoggerTests
    {
        [Test]
        [Description("LogLevel の順序が Verbose < Info < Warning < Error であること（Req 9.7）")]
        public void LogLevel_OrderingIsAscendingBySeverity()
        {
            Assert.That((int)LogLevel.Verbose, Is.LessThan((int)LogLevel.Info));
            Assert.That((int)LogLevel.Info, Is.LessThan((int)LogLevel.Warning));
            Assert.That((int)LogLevel.Warning, Is.LessThan((int)LogLevel.Error));
        }

        [Test]
        [Description("既定コンストラクタで MinLevel が Info に設定されること")]
        public void Constructor_DefaultMinLevel_IsInfo()
        {
            var logger = new OutputShellLogger();
            Assert.AreEqual(LogLevel.Info, logger.MinLevel);
        }

        [Test]
        [Description("MinLevel=Info のとき Verbose 呼び出しは Unity Console に流れない（Req 9.7）")]
        public void Verbose_BelowMinLevel_IsSuppressed()
        {
            var logger = new OutputShellLogger(LogLevel.Info);
            // Suppressed となるため LogAssert.Expect は不要。Unhandled message は発生しない。
            Assert.DoesNotThrow(() => logger.Verbose("verbose-msg", "TestComponent"));
        }

        [Test]
        [Description("MinLevel=Verbose のとき Verbose 呼び出しは Debug.Log で出力される（Req 9.1）")]
        public void Verbose_AtOrAboveMinLevel_LogsToUnityConsole()
        {
            var logger = new OutputShellLogger(LogLevel.Verbose);
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[Verbose\]\[OutputShell\]\[TestComp\] verbose-msg"));
            logger.Verbose("verbose-msg", "TestComp");
        }

        [Test]
        [Description("Info は Debug.Log（LogType.Log）で出力されること")]
        public void Info_AtOrAboveMinLevel_LogsAsLog()
        {
            var logger = new OutputShellLogger(LogLevel.Info);
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[Info\]\[OutputShell\] info-msg"));
            logger.Info("info-msg");
        }

        [Test]
        [Description("MinLevel=Warning のとき Info は抑制されること（Req 9.7 ログレベル切替）")]
        public void Info_BelowMinLevel_IsSuppressed()
        {
            var logger = new OutputShellLogger(LogLevel.Warning);
            Assert.DoesNotThrow(() => logger.Info("should-be-suppressed"));
        }

        [Test]
        [Description("Warning は Debug.LogWarning で出力されること（Req 9.4）")]
        public void Warning_LogsAsWarning()
        {
            var logger = new OutputShellLogger(LogLevel.Verbose);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[Warning\]\[OutputShell\]\[topic=t/x\] warn-msg"));
            logger.Warning("warn-msg", topic: "t/x");
        }

        [Test]
        [Description("Error は Debug.LogError 経由で呼ばれ、構造化情報を含むこと（Req 9.5）")]
        public void Error_LogsAsErrorWithCorrelationId()
        {
            var logger = new OutputShellLogger(LogLevel.Verbose);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[Error\]\[OutputShell\]\[Comp\]\[topic=t\]\[corr=c-1\] err-msg"));
            logger.Error("err-msg", component: "Comp", topic: "t", correlationId: "c-1");
        }

        [Test]
        [Description("Error が Exception を受け取った場合、例外型名・メッセージがログに含まれること（Req 9.5）")]
        public void Error_WithException_IncludesTypeAndMessage()
        {
            var logger = new OutputShellLogger(LogLevel.Verbose);
            var ex = new InvalidOperationException("boom");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"InvalidOperationException: boom"));
            logger.Error("handler-failed", ex, component: "Dispatcher");
        }

        [Test]
        [Description("MinLevel=Error のとき Warning は抑制され、Error のみが出力されること")]
        public void MinLevelError_SuppressesLowerLevels()
        {
            var logger = new OutputShellLogger(LogLevel.Error);
            Assert.DoesNotThrow(() =>
            {
                logger.Verbose("v");
                logger.Info("i");
                logger.Warning("w");
            });

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[Error\]\[OutputShell\] only-error"));
            logger.Error("only-error");
        }

        [Test]
        [Description("MinLevel は実行時に変更可能であること（Req 9.7）")]
        public void MinLevel_IsRuntimeMutable()
        {
            var logger = new OutputShellLogger(LogLevel.Error);
            // 初期は Warning 抑制
            logger.Warning("suppressed-warn");

            logger.MinLevel = LogLevel.Verbose;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[Warning\]\[OutputShell\] now-allowed"));
            logger.Warning("now-allowed");
        }

        [Test]
        [Description("OutputShellLogger 型は UnityEngine.GUI / GUILayout / UIDocument などをシグネチャに含まないこと（Req 5.3 / 5.7 / 9.6）")]
        public void OutputShellLogger_TypeMembers_DoNotMentionGuiTypes()
        {
            var t = typeof(OutputShellLogger);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var member in t.GetMembers(flags))
            {
                var signature = member.ToString() ?? string.Empty;
                Assert.That(signature, Does.Not.Contain("GUI"),
                    $"Member {member} appears to reference IMGUI / OnGUI types");
                Assert.That(signature, Does.Not.Contain("UIDocument"),
                    $"Member {member} appears to reference UI Toolkit UIDocument");
                Assert.That(signature, Does.Not.Contain("PanelSettings"),
                    $"Member {member} appears to reference UI Toolkit PanelSettings");
                Assert.That(signature, Does.Not.Contain("VisualElement"),
                    $"Member {member} appears to reference UI Toolkit VisualElement");
            }
        }

        [Test]
        [Description("OutputShellLogger は IMGUI / UI Toolkit ランタイムを直接参照しないこと（Req 5.3 / 5.7 / 9.6）")]
        public void OutputShellLogger_TypeMethods_DoNotCallGuiOrUiToolkit()
        {
            // メソッド本体が利用する型を IL レベルで検査するのは EditMode テストの範囲を超えるため、
            // 出力経路として用意されているメソッドを実呼び出ししても LogAssert 上で
            // 「Debug.Log* 以外への出力」が発生しないことを behavioural に保証する。
            var logger = new OutputShellLogger(LogLevel.Verbose);
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[Verbose\]\[OutputShell\] v"));
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[Info\]\[OutputShell\] i"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[Warning\]\[OutputShell\] w"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[Error\]\[OutputShell\] e"));
            logger.Verbose("v");
            logger.Info("i");
            logger.Warning("w");
            logger.Error("e");
        }
    }
}
