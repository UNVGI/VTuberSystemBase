using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditorInternal;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Editor
{
    /// <summary>
    /// 本 spec の Runtime asmdef が「禁止される依存」（character-selection-tab Runtime / 他タブ Runtime /
    /// 他出力アダプタ Runtime / core-ipc-foundation 具体実装 / ui-toolkit-shell）を参照していないことを検証する
    /// （Requirement 1.2）。
    /// </summary>
    [TestFixture]
    public sealed class PackageBoundaryTests
    {
        private const string RuntimeAsmdefAssetPath =
            "Packages/com.hidano.vtuber-system-base.rac-main-output-adapter/Runtime/VTuberSystemBase.RacMainOutputAdapter.Runtime.asmdef";

        private static readonly string[] ForbiddenAssemblyNames =
        {
            "VTuberSystemBase.CharacterSelectionTab.Runtime",
            "VTuberSystemBase.StageLightingVolumeTab.Runtime",
            "VTuberSystemBase.CameraSwitcherTab.Runtime",
            "VTuberSystemBase.UiToolkitShell.Runtime",
            "VTuberSystemBase.CoreIpc.Core",
            "VTuberSystemBase.OutputRendererShell.Internal",
        };

        [Test]
        public void RuntimeAsmdef_DoesNotReferenceForbiddenAssemblies()
        {
            var asset = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(RuntimeAsmdefAssetPath);
            Assert.That(asset, Is.Not.Null, $"Runtime asmdef not found at: {RuntimeAsmdefAssetPath}");
            var json = asset.text;
            foreach (var name in ForbiddenAssemblyNames)
            {
                Assert.That(json, Does.Not.Contain(name),
                    $"Runtime asmdef must NOT reference forbidden assembly '{name}'.");
            }
        }
    }
}
