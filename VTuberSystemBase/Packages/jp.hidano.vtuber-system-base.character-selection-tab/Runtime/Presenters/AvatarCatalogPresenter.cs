#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CharacterSelectionTab.Presenters
{
    /// <summary>
    /// Renders the avatar candidate grid and resolves thumbnails through
    /// <see cref="IAvatarThumbnailResolver"/>. (task 5.2.) Keeps a per-key
    /// VisualElement so subsequent catalog updates only diff. Empty catalog
    /// shows a "no avatars available" placeholder and disables the assign
    /// flow until candidates arrive.
    /// </summary>
    public sealed class AvatarCatalogPresenter : IDisposable
    {
        public const string ItemSelectedClass = "vsb-avatar-item--selected";
        public const string ItemLoadingClass = "vsb-avatar-item--loading";
        public const string ItemFallbackClass = "vsb-avatar-item--fallback";
        public const string EmptyMessageName = "vsb-avatar-catalog__empty";
        public const string ErrorMessageName = "vsb-avatar-catalog__error";

        private readonly ICharacterTabStateStore _store;
        private readonly IAvatarThumbnailResolver _thumbnails;
        private readonly VisualElement _container;
        private readonly VisualTreeAsset? _itemTemplate;
        private readonly string _scopeId;
        private readonly IDiagnosticsLogger? _log;
        private readonly Dictionary<string, VisualElement> _items =
            new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        private VisualElement? _emptyMessage;
        private VisualElement? _errorMessage;
        private bool _disposed;

        public Action<string>? OnAvatarClicked { get; set; }
        public Action? OnReloadRequested { get; set; }

        public AvatarCatalogPresenter(
            ICharacterTabStateStore store,
            IAvatarThumbnailResolver thumbnails,
            VisualElement container,
            VisualTreeAsset? itemTemplate,
            string scopeId,
            IDiagnosticsLogger? logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _itemTemplate = itemTemplate;
            if (string.IsNullOrEmpty(scopeId)) throw new ArgumentException("scopeId required", nameof(scopeId));
            _scopeId = scopeId;
            _log = logger;
            _store.OnChanged += OnStoreChanged;
            Render();
        }

        public IReadOnlyDictionary<string, VisualElement> ItemsForTesting => _items;
        public bool IsAssignmentEnabled { get; private set; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _store.OnChanged -= OnStoreChanged;
            _thumbnails.ReleaseAll(_scopeId);
            _container.Clear();
            _items.Clear();
        }

        private void OnStoreChanged(StateChangeScope scope)
        {
            if ((scope & (StateChangeScope.AvatarCatalog | StateChangeScope.Assignment)) == 0) return;
            Render();
        }

        public void Render()
        {
            if (_disposed) return;
            var catalog = _store.AvatarCatalog;
            // Detect duplicates as a defensive check (Store has already deduplicated).
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var dedup = new List<AvatarCatalogEntry>(catalog.Count);
            for (int i = 0; i < catalog.Count; i++)
            {
                var entry = catalog[i];
                if (string.IsNullOrEmpty(entry.AvatarKey)) continue;
                if (!seen.Add(entry.AvatarKey))
                {
                    _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                        $"AvatarCatalog: duplicate key '{entry.AvatarKey}' ignored.");
                    continue;
                }
                dedup.Add(entry);
            }

            if (dedup.Count == 0)
            {
                ShowEmptyMessage();
                IsAssignmentEnabled = false;
                return;
            }
            ClearMessages();
            IsAssignmentEnabled = true;

            // Add new items / update labels.
            var keep = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < dedup.Count; i++)
            {
                var entry = dedup[i];
                keep.Add(entry.AvatarKey);
                if (!_items.TryGetValue(entry.AvatarKey, out var item))
                {
                    item = CloneOrBuildItem();
                    _items[entry.AvatarKey] = item;
                    _container.Add(item);
                    WireClick(item, entry.AvatarKey);
                    BeginLoadThumbnail(entry.AvatarKey, item);
                }
                UpdateLabel(item, entry);
                ApplySelection(item, entry.AvatarKey);
            }

            // Remove vanished entries.
            var toRemove = new List<string>();
            foreach (var k in _items.Keys)
            {
                if (!keep.Contains(k)) toRemove.Add(k);
            }
            foreach (var k in toRemove)
            {
                _container.Remove(_items[k]);
                _items.Remove(k);
            }
        }

        public void ShowError(string message)
        {
            ClearMessages();
            _errorMessage = new VisualElement { name = ErrorMessageName };
            var label = new Label(message);
            _errorMessage.Add(label);
            var retry = new Button(() => OnReloadRequested?.Invoke()) { text = "Retry" };
            _errorMessage.Add(retry);
            _container.Add(_errorMessage);
        }

        // ---------- private ----------

        private VisualElement CloneOrBuildItem()
        {
            if (_itemTemplate is not null)
            {
                var root = _itemTemplate.CloneTree();
                var inner = root.Q<VisualElement>("vsb-avatar-item");
                return inner ?? root;
            }
            var item = new VisualElement { name = "vsb-avatar-item" };
            item.AddToClassList("vsb-avatar-item");
            item.AddToClassList(ItemLoadingClass);
            item.Add(new VisualElement { name = "vsb-avatar-item__thumbnail" });
            item.Add(new Label { name = "vsb-avatar-item__loading", text = "..." });
            item.Add(new Label { name = "vsb-avatar-item__label" });
            return item;
        }

        private void WireClick(VisualElement item, string avatarKey)
        {
            item.RegisterCallback<ClickEvent>(_ => OnAvatarClicked?.Invoke(avatarKey));
        }

        private void UpdateLabel(VisualElement item, AvatarCatalogEntry entry)
        {
            var label = item.Q<Label>("vsb-avatar-item__label");
            if (label is not null) label.text = entry.DisplayName;
        }

        private void ApplySelection(VisualElement item, string avatarKey)
        {
            // Highlight when the active slot's assignment matches this key.
            var selectedSlot = _store.SelectedSlotId;
            if (string.IsNullOrEmpty(selectedSlot))
            {
                item.RemoveFromClassList(ItemSelectedClass);
                return;
            }
            var slot = _store.GetSlot(selectedSlot!);
            bool selected = slot is not null
                && string.Equals(slot.AssignedAvatarKey, avatarKey, StringComparison.Ordinal);
            if (selected) item.AddToClassList(ItemSelectedClass);
            else item.RemoveFromClassList(ItemSelectedClass);
        }

        private void BeginLoadThumbnail(string avatarKey, VisualElement item)
        {
            item.AddToClassList(ItemLoadingClass);
            _thumbnails.LoadThumbnail(avatarKey, _scopeId, result =>
            {
                if (_disposed) return;
                if (!_items.ContainsKey(avatarKey)) return; // item retired before completion
                item.RemoveFromClassList(ItemLoadingClass);
                if (result.Success && result.Sprite != null)
                {
                    var thumb = item.Q<VisualElement>("vsb-avatar-item__thumbnail");
                    if (thumb is not null)
                    {
                        thumb.style.backgroundImage = new StyleBackground(result.Sprite);
                    }
                    if (result.IsFallback)
                    {
                        item.AddToClassList(ItemFallbackClass);
                    }
                    else
                    {
                        item.RemoveFromClassList(ItemFallbackClass);
                    }
                }
                else
                {
                    _log?.Log(LogLevel.Warning, LogCategory.AssetLoad,
                        $"AvatarCatalog.Thumbnail load failed avatarKey={avatarKey} error={result.Error?.Code}");
                    item.AddToClassList(ItemFallbackClass);
                }
            });
            // Hide the loading placeholder once any state is set; visual
            // hiding is delegated to USS modifier classes.
            var loadingLabel = item.Q<Label>("vsb-avatar-item__loading");
            if (loadingLabel is not null) loadingLabel.style.display = DisplayStyle.None;
        }

        private void ShowEmptyMessage()
        {
            ClearItems();
            ClearMessages();
            _emptyMessage = new VisualElement { name = EmptyMessageName };
            _emptyMessage.Add(new Label("No avatar candidates available."));
            _container.Add(_emptyMessage);
        }

        private void ClearItems()
        {
            foreach (var item in _items.Values) _container.Remove(item);
            _items.Clear();
        }

        private void ClearMessages()
        {
            if (_emptyMessage is not null)
            {
                _container.Remove(_emptyMessage);
                _emptyMessage = null;
            }
            if (_errorMessage is not null)
            {
                _container.Remove(_errorMessage);
                _errorMessage = null;
            }
        }
    }
}
