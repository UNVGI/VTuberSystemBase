#nullable enable
using System;
using System.Collections.Generic;

namespace VTuberSystemBase.CharacterSelectionTab.Services
{
    /// <summary>
    /// Concrete <see cref="IInteractionGuard"/> using an injected <see cref="IClock"/>
    /// for idle detection. Marks (slot, key) as interacting on
    /// <see cref="MarkInteracting"/> and auto-ends after
    /// <see cref="IdleThreshold"/> of inactivity once <see cref="Tick"/> is
    /// called with a sufficiently advanced timestamp.
    /// (task 2.2.)
    /// </summary>
    public sealed class InteractionGuard : IInteractionGuard
    {
        private readonly TimeSpan _idleThreshold;
        private readonly Dictionary<(string slotId, string settingKey), DateTimeOffset> _lastSeenAt =
            new Dictionary<(string, string), DateTimeOffset>();
        private DateTimeOffset _now;

        public InteractionGuard(IClock clock, TimeSpan idleThreshold)
        {
            if (clock is null) throw new ArgumentNullException(nameof(clock));
            if (idleThreshold <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleThreshold));
            _idleThreshold = idleThreshold;
            _now = clock.UtcNow;
            clock.OnTick += t => Tick(t);
        }

        public TimeSpan IdleThreshold => _idleThreshold;

        public event Action<InteractingChangedEventArgs>? OnChanged;

        public bool IsInteracting(string slotId, string settingKey)
            => _lastSeenAt.ContainsKey((slotId, settingKey));

        public void MarkInteracting(string slotId, string settingKey)
        {
            var key = (slotId, settingKey);
            bool wasInteracting = _lastSeenAt.ContainsKey(key);
            _lastSeenAt[key] = _now;
            if (!wasInteracting)
            {
                OnChanged?.Invoke(new InteractingChangedEventArgs(slotId, settingKey, true));
            }
        }

        public void EndInteracting(string slotId, string settingKey)
        {
            if (_lastSeenAt.Remove((slotId, settingKey)))
            {
                OnChanged?.Invoke(new InteractingChangedEventArgs(slotId, settingKey, false));
            }
        }

        public void Tick(DateTimeOffset now)
        {
            if (now < _now) return; // Monotonic clock contract.
            _now = now;
            if (_lastSeenAt.Count == 0) return;
            // Snapshot keys to allow safe removal during iteration.
            var snapshot = new List<(string slotId, string settingKey)>(_lastSeenAt.Count);
            foreach (var k in _lastSeenAt.Keys) snapshot.Add(k);
            foreach (var k in snapshot)
            {
                if (now - _lastSeenAt[k] >= _idleThreshold)
                {
                    _lastSeenAt.Remove(k);
                    OnChanged?.Invoke(new InteractingChangedEventArgs(k.slotId, k.settingKey, false));
                }
            }
        }
    }
}
