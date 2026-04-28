#nullable enable
using System;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityDebug = UnityEngine.Debug;

namespace VTuberSystemBase.CoreIpc.Core.Dispatch
{
    public static class PlayerLoopInstaller
    {
        private static Action? s_flushAction;

        public static bool IsInstalled => s_flushAction is not null;

        public static void Install(Action flushAction)
        {
            Install(flushAction, logWarning: null);
        }

        public static void Install(Action flushAction, Action<string>? logWarning)
        {
            if (flushAction is null) throw new ArgumentNullException(nameof(flushAction));

            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (IsInstalled || ContainsIpcDispatchStep(loop))
            {
                var message = $"PlayerLoopInstaller.Install called while already installed; existing IpcDispatchStep is being replaced. {Environment.StackTrace}";
                if (logWarning is not null)
                {
                    logWarning(message);
                }
                else
                {
                    UnityDebug.LogWarning(message);
                }

                loop = RemoveIpcDispatchStep(loop);
            }

            s_flushAction = flushAction;
            loop = AddIpcDispatchStep(loop, InvokeFlushAction);
            PlayerLoop.SetPlayerLoop(loop);
        }

        public static void Uninstall()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (!ContainsIpcDispatchStep(loop) && !IsInstalled)
            {
                return;
            }

            loop = RemoveIpcDispatchStep(loop);
            PlayerLoop.SetPlayerLoop(loop);
            s_flushAction = null;
        }

        private static void InvokeFlushAction()
        {
            s_flushAction?.Invoke();
        }

        private static bool ContainsIpcDispatchStep(PlayerLoopSystem loop)
        {
            if (loop.subSystemList is null)
            {
                return false;
            }

            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type == typeof(PreUpdate))
                {
                    var children = loop.subSystemList[i].subSystemList;
                    if (children is null) return false;
                    for (int j = 0; j < children.Length; j++)
                    {
                        if (children[j].type == typeof(IpcDispatchStep))
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }

            return false;
        }

        private static PlayerLoopSystem AddIpcDispatchStep(PlayerLoopSystem loop, PlayerLoopSystem.UpdateFunction updateDelegate)
        {
            if (loop.subSystemList is null)
            {
                throw new InvalidOperationException("Current PlayerLoop has no subSystemList; cannot install IpcDispatchStep.");
            }

            var subSystems = loop.subSystemList;
            for (int i = 0; i < subSystems.Length; i++)
            {
                if (subSystems[i].type != typeof(PreUpdate))
                {
                    continue;
                }

                var preUpdate = subSystems[i];
                var existingChildren = preUpdate.subSystemList ?? Array.Empty<PlayerLoopSystem>();
                var newChildren = new PlayerLoopSystem[existingChildren.Length + 1];
                Array.Copy(existingChildren, newChildren, existingChildren.Length);
                newChildren[newChildren.Length - 1] = new PlayerLoopSystem
                {
                    type = typeof(IpcDispatchStep),
                    updateDelegate = updateDelegate
                };
                preUpdate.subSystemList = newChildren;
                subSystems[i] = preUpdate;
                loop.subSystemList = subSystems;
                return loop;
            }

            throw new InvalidOperationException("Current PlayerLoop does not contain a PreUpdate phase; cannot install IpcDispatchStep.");
        }

        private static PlayerLoopSystem RemoveIpcDispatchStep(PlayerLoopSystem loop)
        {
            if (loop.subSystemList is null)
            {
                return loop;
            }

            var subSystems = loop.subSystemList;
            for (int i = 0; i < subSystems.Length; i++)
            {
                if (subSystems[i].type != typeof(PreUpdate))
                {
                    continue;
                }

                var preUpdate = subSystems[i];
                var existingChildren = preUpdate.subSystemList;
                if (existingChildren is null || existingChildren.Length == 0)
                {
                    return loop;
                }

                int removalCount = 0;
                for (int j = 0; j < existingChildren.Length; j++)
                {
                    if (existingChildren[j].type == typeof(IpcDispatchStep))
                    {
                        removalCount++;
                    }
                }

                if (removalCount == 0)
                {
                    return loop;
                }

                var filtered = new PlayerLoopSystem[existingChildren.Length - removalCount];
                int writeIndex = 0;
                for (int j = 0; j < existingChildren.Length; j++)
                {
                    if (existingChildren[j].type == typeof(IpcDispatchStep))
                    {
                        continue;
                    }
                    filtered[writeIndex++] = existingChildren[j];
                }

                preUpdate.subSystemList = filtered;
                subSystems[i] = preUpdate;
                loop.subSystemList = subSystems;
                return loop;
            }

            return loop;
        }
    }
}
