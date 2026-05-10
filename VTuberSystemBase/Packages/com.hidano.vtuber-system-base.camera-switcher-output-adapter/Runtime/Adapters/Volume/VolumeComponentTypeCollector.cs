#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Rendering;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Volume
{
    internal static class VolumeComponentTypeCollector
    {
        /// <summary>
        /// 既知の <see cref="VolumeComponent"/> 派生型を列挙する。URP 17 では
        /// <see cref="VolumeManager.baseComponentTypeArray"/> が <c>isInitialized=false</c>
        /// の状態でアクセスすると <see cref="InvalidOperationException"/> を投げるため、
        /// RenderPipeline 構築前 (=テスト初期化フェーズ) は同 API に依存できない。
        /// 初期化済みなら VolumeManager に従い、それ以外はロード済みアセンブリから
        /// 自前で列挙する。
        /// </summary>
        public static Type[] Collect()
        {
            try
            {
                var vm = VolumeManager.instance;
                if (vm != null && vm.isInitialized)
                {
                    var arr = vm.baseComponentTypeArray;
                    if (arr != null && arr.Length > 0) return arr;
                }
            }
            catch { /* fall through to assembly scan */ }

            var list = new List<Type>(64);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] asmTypes;
                try
                {
                    asmTypes = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    asmTypes = ex.Types == null ? Array.Empty<Type>() : Array.FindAll(ex.Types, t => t != null)!;
                }
                catch
                {
                    continue;
                }

                foreach (var t in asmTypes)
                {
                    if (t == null || t.IsAbstract) continue;
                    if (!typeof(VolumeComponent).IsAssignableFrom(t)) continue;
                    list.Add(t);
                }
            }
            return list.ToArray();
        }
    }
}
