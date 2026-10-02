using System.Diagnostics;
using Il2CppInterop.Runtime.Attributes;
using Nivalis;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.Locale.UI;
using Nivalis.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NivalisMods.HudOverhaul;

public sealed class VirtualReviews : MonoBehaviour
{
    private sealed class Entry
    {
        internal readonly Review Review;
        internal readonly int Time, Score, Original;
        internal readonly bool Positive;
        internal float Height = 110;
        internal Entry(Review review, int original)
        {
            Review = review; Original = original;
            Time = review.ReviewTime.TotalGameSeconds; Score = review.Score; Positive = review.Positive;
        }
    }
    private sealed class Row
    {
        internal ReviewItemDisplayUi Ui = null!;
        internal RectTransform Rect = null!;
        internal Entry? Entry;
    }
    private LocaleReviewOverviewTab _tab = null!;
    private RectTransform _content = null!;
    private readonly List<Row> _pool = new();
    private List<Entry> _entries = new();
    private readonly ReviewWindow _window = new();
    private float _spacing, _left, _right, _top, _bottom;
    private float _lastOffset = float.NaN, _lastWidth, _lastHeight;
    private bool _ready, _failed;
    private int _heightSamples;
    private VerticalLayoutGroup? _layout;
    private ContentSizeFitter? _fitter;
    private bool _layoutEnabled, _fitterEnabled;
    private readonly List<(CanvasBehaviourManager Manager, Behaviour Component)> _ownedLayouts = new();
    private bool _wasVisible;
    private readonly List<Action> _restoreGeometry = new();
    private readonly HashSet<int> _measured = new();

    public void OnEnable() => _lastOffset = float.NaN;
    public void OnDisable()
    {
        _wasVisible = false;
        _lastOffset = float.NaN;
    }

    public VirtualReviews(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp]
    internal static bool Refresh(LocaleReviewOverviewTab tab)
    {
        var view = tab.GetComponent<VirtualReviews>() ?? tab.gameObject.AddComponent<VirtualReviews>();
        if (!ModOptions.FastReviews.Value)
        {
            if (view._ready)
            {
                view.Restore();
                foreach (var restore in view._restoreGeometry) restore();
                view._restoreGeometry.Clear(); view._measured.Clear(); view._pool.Clear();
                view._ready = view._failed = false;
            }
            return false;
        }
        if (view._failed) return false;
        try
        {
            view._tab = tab;
            view.Prepare();
            view.Reload();
            return true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"Virtual review loading failed; returning to native list: {e}");
            view.Restore();
            return false;
        }
    }

    [HideFromIl2Cpp]
    private void Prepare()
    {
        if (_ready) return;
        var list = _tab.reviewList;
        var scroll = _tab.scrollRect;
        if (list == null || scroll?.content == null || scroll.viewport == null || list.useGridDisplay)
            throw new InvalidOperationException("Unsupported review list layout.");
        _content = scroll.content;
        if (list._itemDisplayParent != _content)
            throw new InvalidOperationException("Review rows are not direct children of the scrolling content.");
        _layout = _content.GetComponent<VerticalLayoutGroup>();
        if (_layout == null) throw new InvalidOperationException("Review content has no vertical layout.");
        _fitter = _content.GetComponent<ContentSizeFitter>();
        _layoutEnabled = _layout.enabled;
        _fitterEnabled = _fitter != null && _fitter.enabled;
        _spacing = _layout.spacing;
        _left = _layout.padding.left; _right = _layout.padding.right;
        _top = _layout.padding.top; _bottom = _layout.padding.bottom;
        // Clear only once. From here on, retain and rebind the small row pool.
        list.Clear();
        ClaimLayout();
        _content.pivot = new Vector2(_content.pivot.x, 1);
        var sorter = ListSorter.Get(list);
        sorter.ExternalOrder = true;
        SortDropdown.Attach(list, scroll, ReviewModes.All, SortChanged);
        sorter.Dropdown!.onValueChanged = new TMPro.TMP_Dropdown.DropdownEvent();
        sorter.Dropdown.onValueChanged.AddListener(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<int>>(new Action<int>(_ => SortChanged())));
        _ready = true;
    }

    [HideFromIl2Cpp]
    private void ClaimLayout()
    {
        // CanvasBehaviourManager enables every registered Behaviour when its
        // canvas reopens, regardless of our earlier enabled=false. Only remove
        // the two content controllers owned by virtualization; row components
        // remain managed normally by the game.
        var previousOwned = _ownedLayouts.Count;
        foreach (var manager in Resources.FindObjectsOfTypeAll<CanvasBehaviourManager>())
        {
            var behaviours = manager.canvasBehaviours;
            if (behaviours == null) continue;
            foreach (var component in new Behaviour?[] { _layout, _fitter })
            {
                if (component == null) continue;
                var removed = false;
                while (behaviours.Remove(component)) removed = true;
                if (removed && !_ownedLayouts.Any(x => x.Manager == manager && x.Component == component))
                    _ownedLayouts.Add((manager, component));
            }
        }
        if (_layout != null) _layout.enabled = false;
        if (_fitter != null) _fitter.enabled = false;
        if (_ownedLayouts.Count != previousOwned)
            if (Plugin.IsVerbose) Plugin.Verbose($"Virtual reviews own {_ownedLayouts.Count} content layout registrations; native canvas toggles retained for row contents.");
    }

    [HideFromIl2Cpp]
    private void Reload()
    {
        var started = Stopwatch.GetTimestamp();
        ClaimLayout();
        _heightSamples = 0;
        var now = TimeOfDayManager.CurrentTime;
        var start = 0;
        var end = now.GameplayGameDay;
        // Match the base game's calendar periods, including its 08:00 day boundary.
        if (_tab.toggleToday.isOn) start = end;
        else if (_tab.toggleThisWeek.isOn)
        {
            start = now.GetWeekStartTime().GameplayGameDay;
            end = now.GetWeekEndTime().GameplayGameDay;
        }
        else if (_tab.toggleThisMonth.isOn)
        {
            var month = now.GetMonth();
            var year = now.GameplayGameDay / 365;
            start = month.GetStartTime(year).GameplayGameDay;
            end = month.GetEndTime(year).GameplayGameDay;
        }
        var entries = new List<Entry>();
        var reviews = _tab._venue?.RuntimeData?.Reviews;
        if (reviews != null)
        {
            var count = reviews.Count;
            for (var i = 0; i < count; i++)
            {
                var review = reviews[i];
                var day = review.ReviewTime.GameplayGameDay;
                if (day >= start && day <= end) entries.Add(new Entry(review, i));
            }
        }
        _entries = entries;
        SortChanged();
        if (Plugin.IsVerbose) Plugin.Verbose($"PERF Virtual reviews: {_entries.Count} matching, {_pool.Count} pooled rows, {PerformanceTrace.Ms(Stopwatch.GetTimestamp() - started):F1} ms refresh.");
    }

    [HideFromIl2Cpp]
    private void SortChanged()
    {
        var mode = ListSorter.Get(_tab.reviewList).Mode();
        double Value(Entry e) => mode.Key switch
        {
            SortKey.ReviewTime => e.Time,
            SortKey.Stars => e.Score,
            SortKey.Sentiment => e.Positive ? 1 : 0,
            _ => e.Original
        };
        _entries = _entries.OrderBy(e => mode.Descending ? -Value(e) : Value(e))
            .ThenBy(e => e.Original).ToList();
        _tab.scrollRect.StopMovement();
        _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, 0);
        RebuildOffsets();
        _lastOffset = float.NaN;
        Render();
    }

    [HideFromIl2Cpp]
    private void RebuildOffsets()
    {
        _window.Reset(_entries.Select(e => e.Height), _spacing);
        _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _window.Height + _top + _bottom);
    }

    public void LateUpdate()
    {
        if (!_ready || _failed || !ModOptions.FastReviews.Value) return;
        try { Render(); }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"Virtual review scrolling failed; returning to native list: {e}");
            Restore();
            _tab.RefreshReviewList();
        }
    }

    [HideFromIl2Cpp]
    private void Render()
    {
        var manager = _tab.reviewList.canvasBehaviourManager;
        var panelVisible = _tab.scrollRect.isActiveAndEnabled && (manager == null || manager.GetCanvasIsEnabled());
        if (!panelVisible)
        {
            _wasVisible = false;
            return;
        }
        if (!_wasVisible)
        {
            _wasVisible = true;
            ClaimLayout();
            foreach (var row in _pool) row.Entry = null;
            _lastOffset = float.NaN;
            RebuildOffsets();
        }
        // Recover if another native refresh registered a content controller again.
        if ((_layout != null && _layout.enabled) || (_fitter != null && _fitter.enabled))
        {
            ClaimLayout();
            _lastOffset = float.NaN;
            RebuildOffsets();
        }
        var expectedHeight = _window.Height + _top + _bottom;
        if (Math.Abs(_content.rect.height - expectedHeight) > 0.5f)
        {
            RebuildOffsets();
            _lastOffset = float.NaN;
        }
        var viewport = _tab.scrollRect.viewport.rect;
        if (viewport.height <= 0 || _content.rect.width <= 0) return;
        var offset = Math.Max(0, _content.anchoredPosition.y - _top);
        var width = _content.rect.width - _left - _right;
        var resized = Math.Abs(_lastWidth - width) > 0.5f;
        var hiddenRow = _pool.Any(row => row.Entry != null && !row.Rect.gameObject.activeSelf);
        if (!hiddenRow && !resized && Math.Abs(_lastHeight - viewport.height) < 0.5f && Math.Abs(_lastOffset - offset) < 0.1f) return;
        _lastWidth = width; _lastHeight = viewport.height;
        if (resized)
        {
            foreach (var entry in _entries) entry.Height = 110;
            foreach (var row in _pool) row.Entry = null;
            RebuildOffsets();
        }
        // Measuring newly visible rows can change the range. Re-evaluate a few
        // times to fill the viewport, while keeping work bounded to visible rows.
        var settled = false;
        for (var pass = 0; pass < 3; pass++)
        {
            var (first, last) = _window.Visible(offset, viewport.height);
            var needed = last - first + 1;
            while (_pool.Count < needed)
            {
                // Native panel cleanup can reset its cursor while our pool lives.
                // Request the next allocated slot, never an already pooled row.
                _tab.reviewList._displayedInstanceCount = _tab.reviewList._itemDisplayInstances.Count;
                var display = _tab.reviewList.AddItem(true, true);
                var ui = display.GameObject.GetComponentInChildren<ReviewItemDisplayUi>(true);
                if (ui == null) throw new InvalidOperationException("Review prefab has no review UI.");
                var rect = display.GameObject.GetComponent<RectTransform>();
                var anchorMin = rect.anchorMin; var anchorMax = rect.anchorMax; var pivot = rect.pivot; var size = rect.sizeDelta;
                _restoreGeometry.Add(() => { if (rect != null) { rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.pivot = pivot; rect.sizeDelta = size; } });
                _pool.Add(new Row { Ui = ui, Rect = rect });
            }
            var changed = false;
            var wanted = _entries.Skip(first).Take(needed).ToHashSet();
            var spare = new Queue<Row>(_pool.Where(row => row.Entry == null || !wanted.Contains(row.Entry)));
            var visible = new List<Row>();
            for (var i = 0; i < needed; i++)
            {
                var index = first + i;
                var entry = _entries[index];
                // Keep already visible entries on the same objects so controller
                // selection and text are not rebound whenever one row scrolls in.
                var row = _pool.FirstOrDefault(row => row.Entry == entry) ?? spare.Dequeue();
                visible.Add(row);
                row.Rect.anchorMin = new Vector2(0, 1);
                row.Rect.anchorMax = new Vector2(1, 1);
                row.Rect.pivot = new Vector2(0, 1);
                row.Rect.sizeDelta = new Vector2(-_left - _right, entry.Height);
                row.Rect.anchoredPosition = new Vector2(_left, -_top - _window.Top(index));
                if (!row.Rect.gameObject.activeSelf) row.Rect.gameObject.SetActive(true);
                if (row.Entry != entry)
                {
                    row.Ui.Show(entry.Review);
                    row.Entry = entry;
                    var height = MeasureRow(row);
                    if (Math.Abs(height - entry.Height) > 0.5f) { entry.Height = height; changed = true; }
                    row.Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(row.Rect);
                }
            }
            foreach (var row in _pool.Where(row => !visible.Contains(row)))
            {
                if (row.Rect.gameObject.activeSelf) row.Rect.gameObject.SetActive(false);
                row.Entry = null;
            }
            var dropdown = ListSorter.Get(_tab.reviewList).Dropdown;
            var selects = visible.Select(row => row.Rect.GetComponent<Selectable>()).Where(s => s != null).ToArray();
            for (var i = 0; i < selects.Length; i++)
            {
                var nav = selects[i].navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = i == 0 ? dropdown : selects[i - 1];
                nav.selectOnDown = i + 1 < selects.Length ? selects[i + 1] : null;
                selects[i].navigation = nav;
            }
            if (dropdown != null)
            {
                var nav = dropdown.navigation;
                nav.selectOnDown = selects.FirstOrDefault();
                dropdown.navigation = nav;
            }
            if (!changed) { settled = true; break; }
            // Preserve the first visible review when measurements above it change.
            var anchor = _window.At(offset);
            var within = offset - _window.Top(anchor);
            RebuildOffsets();
            if (_entries.Count > 0)
            {
                offset = _window.Top(anchor) + within;
                _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, offset + _top);
            }
        }
        // If the third measurement changes offsets, finish placement next frame.
        _lastOffset = settled ? offset : float.NaN;
    }

    [HideFromIl2Cpp]
    private float MeasureRow(Row row)
    {
        if (_measured.Add(row.Rect.GetInstanceID()))
        {
            foreach (var target in new[] { row.Rect.gameObject, row.Ui.nameText.gameObject, row.Ui.contentText.gameObject })
            {
                var existing = target.GetComponent<LayoutElement>();
                if (existing == null) _restoreGeometry.Add(() => { if (target != null) UnityEngine.Object.DestroyImmediate(target.GetComponent<LayoutElement>()); });
                else
                {
                    var min = existing.minHeight; var preferred = existing.preferredHeight; var flexible = existing.flexibleHeight; var priority = existing.layoutPriority;
                    _restoreGeometry.Add(() => { if (existing != null) { existing.minHeight = min; existing.preferredHeight = preferred; existing.flexibleHeight = flexible; existing.layoutPriority = priority; } });
                }
            }
        }
        var horizontal = row.Rect.GetComponent<HorizontalLayoutGroup>();
        var details = row.Ui.contentText.transform.parent.GetComponent<VerticalLayoutGroup>();
        if (horizontal == null || details == null)
            throw new InvalidOperationException("Unsupported review text layout.");

        if (_measured.Add(-row.Rect.GetInstanceID()))
        {
            var horizontalExpand = horizontal.childForceExpandHeight; var verticalExpand = details.childForceExpandHeight;
            _restoreGeometry.Add(() => { if (horizontal != null) horizontal.childForceExpandHeight = horizontalExpand; if (details != null) details.childForceExpandHeight = verticalExpand; });
        }
        // Resolve width first. A recycled TMP element's cached preferredHeight
        // can refer to its previous/narrow prefab width. The root also includes
        // an Image layout element, so its preferred size is not a text metric.
        horizontal.childForceExpandHeight = false;
        details.childForceExpandHeight = false;
        horizontal.CalculateLayoutInputHorizontal();
        horizontal.SetLayoutHorizontal();
        details.CalculateLayoutInputHorizontal();
        details.SetLayoutHorizontal();

        float TextHeight(TMPro.TextMeshProUGUI text)
        {
            var width = text.rectTransform.rect.width;
            if (width <= 1) throw new InvalidOperationException("Review text width is not ready.");
            var height = text.GetPreferredValues(text.text, width, float.PositiveInfinity).y;
            if (!float.IsFinite(height) || height < 0)
                throw new InvalidOperationException("Invalid measured review text height.");
            // Give the row layout the same fresh measurement used by the virtual
            // offsets; do not let stale TMP layout caches stretch its children.
            var element = text.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
            element.layoutPriority = 2;
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0;
            return height;
        }
        var nameHeight = TextHeight(row.Ui.nameText);
        var bodyHeight = TextHeight(row.Ui.contentText);
        var date = row.Ui.dateText;
        var dateHeight = date.GetPreferredValues(date.text, Math.Max(1, date.rectTransform.rect.width), float.PositiveInfinity).y;
        var headerHeight = Math.Max(nameHeight, dateHeight);
        var height = Math.Max(92.8f, horizontal.padding.vertical + details.padding.vertical
            + headerHeight + details.spacing + bodyHeight);
        var sizing = row.Rect.GetComponent<LayoutElement>() ?? row.Rect.gameObject.AddComponent<LayoutElement>();
        sizing.layoutPriority = 2;
        sizing.minHeight = height;
        sizing.preferredHeight = height;
        sizing.flexibleHeight = 0;
        if (_heightSamples++ < 5)
            if (Plugin.IsVerbose) Plugin.Verbose($"Review geometry: width {row.Ui.contentText.rectTransform.rect.width:F1}, body {bodyHeight:F1}, row {height:F1}.");
        return height;
    }

    [HideFromIl2Cpp]
    private void Restore()
    {
        _failed = true;
        foreach (var (manager, component) in _ownedLayouts)
            if (manager != null && component != null && manager.canvasBehaviours != null && !manager.canvasBehaviours.Contains(component))
                manager.canvasBehaviours.Add(component);
        _ownedLayouts.Clear();
        if (_layout != null) _layout.enabled = _layoutEnabled;
        if (_fitter != null) _fitter.enabled = _fitterEnabled;
        if (_tab?.reviewList != null) ListSorter.Get(_tab.reviewList).ExternalOrder = false;
        // The fallback must not retain a callback into the failed virtual view.
        var dropdown = _tab?.reviewList == null ? null : ListSorter.Get(_tab.reviewList).Dropdown;
        if (dropdown != null)
        {
            dropdown.onValueChanged = new TMPro.TMP_Dropdown.DropdownEvent();
            dropdown.onValueChanged.AddListener(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<int>>(
                new Action<int>(_ => _tab!.RefreshReviewList())));
        }
    }
}

internal static class ReviewModes
{
    internal static readonly SortMode[] All =
    [
        new(Labels.Get("sort.original"), SortKey.Native),
        new(Labels.Get("reviews.time.desc"), SortKey.ReviewTime, true),
        new(Labels.Get("reviews.time.asc"), SortKey.ReviewTime),
        new(Labels.Get("reviews.stars.desc"), SortKey.Stars, true),
        new(Labels.Get("reviews.stars.asc"), SortKey.Stars),
        new(Labels.Get("reviews.positive"), SortKey.Sentiment, true),
        new(Labels.Get("reviews.negative"), SortKey.Sentiment)
    ];
}
