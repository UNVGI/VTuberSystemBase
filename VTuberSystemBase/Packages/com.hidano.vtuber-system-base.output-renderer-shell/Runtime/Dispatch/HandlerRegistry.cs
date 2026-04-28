#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.OutputRendererShell.Abstractions;

namespace VTuberSystemBase.OutputRendererShell.Dispatch
{
    /// <summary>
    /// <c>(topic, kind)</c> をキーとしてハンドラ <see cref="Delegate"/> を登録／ルックアップ／解除する内部レジストリ
    /// （Req 3.3 / 4.5 / 4.6）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fail-Fast 方針：同一 <c>(topic, kind)</c> への重複登録は <see cref="InvalidOperationException"/> を送出する。
    /// 登録解除は <see cref="Register"/> が返す <see cref="OutputCommandHandlerRegistration"/> の Dispose で行う。
    /// </para>
    /// <para>
    /// 本レジストリは内部状態を <see cref="Dictionary{TKey, TValue}"/> で保持するためスレッドセーフではない。
    /// 上位の <c>OutputCommandDispatcher</c>（Task 4.2）が Unity メインスレッド前提で利用する想定。
    /// </para>
    /// </remarks>
    public sealed class HandlerRegistry
    {
        private readonly Dictionary<(string topic, OutputCommandKind kind), Delegate> _entries = new();

        /// <summary>現在登録済みのハンドラ件数。</summary>
        public int Count => _entries.Count;

        /// <summary>
        /// <paramref name="topic"/> / <paramref name="kind"/> へ <paramref name="handler"/> を登録し、
        /// 解除トークンを返す。
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="topic"/> が null/空。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> が null。</exception>
        /// <exception cref="InvalidOperationException">同一キーで既登録（重複登録は Fail-Fast）。</exception>
        public OutputCommandHandlerRegistration Register(string topic, OutputCommandKind kind, Delegate handler)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("topic must not be null/empty.", nameof(topic));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var key = (topic, kind);
            if (_entries.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"Handler for (topic='{topic}', kind={kind}) is already registered. Duplicate registration is not allowed.");
            }
            _entries.Add(key, handler);

            return new OutputCommandHandlerRegistration(() =>
            {
                _entries.Remove(key);
            });
        }

        /// <summary>
        /// <paramref name="topic"/> / <paramref name="kind"/> に対応するハンドラを取得する。未登録の場合 <c>false</c>。
        /// </summary>
        public bool TryGet(string topic, OutputCommandKind kind, out Delegate handler)
        {
            if (_entries.TryGetValue((topic, kind), out var found))
            {
                handler = found;
                return true;
            }
            handler = null!;
            return false;
        }

        /// <summary>登録済みエントリ全件をクリアする（Dispatcher.Dispose で利用）。</summary>
        public void Clear() => _entries.Clear();

        /// <summary>
        /// <paramref name="topic"/> に対して任意の <see cref="OutputCommandKind"/> でハンドラが
        /// 1 件以上登録されているかを返す。
        /// kind 不整合（Req 4.6）と未登録（Req 3.5）を区別するために <c>OutputCommandDispatcher</c> が利用する。
        /// </summary>
        public bool HasAnyForTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic)) return false;
            foreach (var key in _entries.Keys)
            {
                if (key.topic == topic) return true;
            }
            return false;
        }
    }
}
