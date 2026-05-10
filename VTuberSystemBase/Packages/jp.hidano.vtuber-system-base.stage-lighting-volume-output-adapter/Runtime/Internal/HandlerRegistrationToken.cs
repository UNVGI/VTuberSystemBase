#nullable enable
using System;
using System.Collections.Generic;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal
{
    /// <summary>
    /// Composite <see cref="IDisposable"/> that owns multiple child tokens and disposes them
    /// in LIFO order on <see cref="Dispose"/>. Double-dispose is a no-op. Individual child
    /// dispose exceptions are swallowed so a single faulty token cannot prevent the rest from
    /// being released; the first encountered exception is rethrown after all children are
    /// processed.
    /// </summary>
    internal sealed class HandlerRegistrationToken : IDisposable
    {
        private readonly List<IDisposable> _children = new List<IDisposable>();
        private readonly object _lock = new object();
        private bool _disposed;

        public HandlerRegistrationToken() { }

        public HandlerRegistrationToken(params IDisposable[] children)
        {
            if (children == null) return;
            foreach (var c in children)
            {
                if (c != null) _children.Add(c);
            }
        }

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _children.Count;
                }
            }
        }

        public bool IsDisposed
        {
            get
            {
                lock (_lock)
                {
                    return _disposed;
                }
            }
        }

        /// <summary>
        /// Adds a child to be disposed by this token. Returns the same token for chaining.
        /// If this token has already been disposed, <paramref name="child"/> is disposed
        /// immediately.
        /// </summary>
        public HandlerRegistrationToken Add(IDisposable? child)
        {
            if (child == null) return this;
            lock (_lock)
            {
                if (_disposed)
                {
                    // dispose immediately so we don't leak
                }
                else
                {
                    _children.Add(child);
                    return this;
                }
            }
            try { child.Dispose(); } catch { /* swallow */ }
            return this;
        }

        public void Dispose()
        {
            List<IDisposable> snapshot;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                snapshot = new List<IDisposable>(_children);
                _children.Clear();
            }

            Exception? firstException = null;
            for (int i = snapshot.Count - 1; i >= 0; i--)
            {
                try
                {
                    snapshot[i].Dispose();
                }
                catch (Exception ex)
                {
                    firstException ??= ex;
                }
            }

            if (firstException != null)
            {
                throw firstException;
            }
        }
    }
}
