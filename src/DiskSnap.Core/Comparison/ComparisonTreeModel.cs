using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using DiskSnap.Core.Formatting;

namespace DiskSnap.Core.Comparison;

public sealed class ComparisonTreeRow : INotifyPropertyChanged
{
    private bool _isExpanded;

    internal ComparisonTreeRow(ComparisonNode node, int depth, bool hasChildren, bool isExpanded, long maxDeltaMagnitude)
    {
        Node = node;
        Depth = depth;
        HasChildren = hasChildren;
        _isExpanded = isExpanded;
        MaxDeltaMagnitude = maxDeltaMagnitude;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ComparisonNode Node { get; }
    public int Depth { get; }
    public bool HasChildren { get; }
    public long MaxDeltaMagnitude { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        internal set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public string DisplayName
    {
        get
        {
            var name = Node.Presence == NodePresence.Aggregate
                ? "[small files]"
                : Node.Path.Length == 0 ? "(drive root)" : Node.Path[(Node.Path.LastIndexOf('\\') + 1)..];
            var status = Node.Presence switch
            {
                NodePresence.New => "new",
                NodePresence.Removed => "removed",
                _ => Node.Observation switch
                {
                    FileObservation.NewlyObserved => "newly observed",
                    FileObservation.NoLongerObserved => "no longer observed",
                    _ => null
                }
            };
            return status is null ? name : $"{name} ({status})";
        }
    }

    public string OlderDisplay => SizeFormatter.Format(Node.OlderSize);
    public string NewerDisplay => SizeFormatter.Format(Node.NewerSize);
    public string OlderTooltip => SizeFormatter.FormatExactBytes(Node.OlderSize);
    public string NewerTooltip => SizeFormatter.FormatExactBytes(Node.NewerSize);

    public string DeltaDisplay
    {
        get
        {
            if (Node.Delta is long delta)
            {
                return FormatSigned(delta);
            }

            if (Node.FoldedFileChangeLowerBound is long bound)
            {
                var signedBound = Node.Observation == FileObservation.NoLongerObserved ? -bound : bound;
                return $"at least {FormatSigned(signedBound)} (assuming ordinary-file folding)";
            }

            return "-";
        }
    }

    public string DeltaTooltip
    {
        get
        {
            if (Node.Delta is long delta)
            {
                return SizeFormatter.FormatExactBytes(delta);
            }

            if (Node.FoldedFileChangeLowerBound is long bound)
            {
                var signedBound = Node.Observation == FileObservation.NoLongerObserved ? -bound : bound;
                return $"At least {SizeFormatter.FormatExactBytes(signedBound)} if the file was folded; exclusions or skipped paths may also explain the missing observation.";
            }

            return "Delta is unknown.";
        }
    }

    public string PercentDisplay => Node.PercentChange is double percent
        ? $"{percent.ToString("+0.##;-0.##;+0", CultureInfo.CurrentCulture)}%"
        : "-";

    public bool IsShrinkage => Node.Delta is long delta ? delta < 0 :
        Node.FoldedFileChangeLowerBound is long bound &&
        (Node.Observation == FileObservation.NoLongerObserved ? -bound : bound) < 0;

    public double DeltaBarWidth
    {
        get
        {
            var magnitude = Node.Delta is long delta
                ? Math.Abs(delta)
                : Node.FoldedFileChangeLowerBound is long bound ? Math.Abs(bound) : 0;
            return MaxDeltaMagnitude == 0 ? 0 : (double)magnitude / MaxDeltaMagnitude * 100;
        }
    }

    public string PathTooltip => Node.Path.Length == 0 ? "Drive root" : Node.Path;

    public bool HasGuidance => Node.OlderGuidance.Count > 0 || Node.NewerGuidance.Count > 0;

    public string GuidanceTooltip => string.Join(Environment.NewLine + Environment.NewLine,
        Node.OlderGuidance.Concat(Node.NewerGuidance)
            .DistinctBy(rule => (rule.Id, rule.Title, rule.Guidance))
            .Select(rule => $"{rule.Title}{Environment.NewLine}{rule.Guidance}"));

    private static string FormatSigned(long value) =>
        (value >= 0 ? "+" : "-") + SizeFormatter.Format(Math.Abs(value));
}

public sealed class ComparisonTreeModel
{
    private readonly SnapshotComparison _comparison;
    private readonly HashSet<ComparisonNode> _growthRelevant = [];
    private readonly HashSet<ComparisonNode> _expanded = [];
    private readonly long _maxDeltaMagnitude;
    private bool _growthOnly;

    public ComparisonTreeModel(SnapshotComparison comparison, bool initiallyExpanded = true)
    {
        _comparison = comparison ?? throw new ArgumentNullException(nameof(comparison));
        _maxDeltaMagnitude = comparison.Nodes
            .Select(node => Math.Max(
                node.Delta.HasValue ? Math.Abs(node.Delta.Value) : 0,
                node.FoldedFileChangeLowerBound is long bound ? Math.Abs(bound) : 0))
            .DefaultIfEmpty(0)
            .Max();
        PrecomputeGrowthRelevantNodes();
        if (initiallyExpanded)
        {
            _expanded.Add(comparison.Root);
        }

        VisibleRows = new ComparisonTreeRows();
        RebuildVisibleRows();
    }

    public ComparisonTreeRows VisibleRows { get; }

    public bool GrowthOnly
    {
        get => _growthOnly;
        set
        {
            if (_growthOnly == value)
            {
                return;
            }

            _growthOnly = value;
            RebuildVisibleRows();
        }
    }

    public bool ToggleExpanded(ComparisonTreeRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.HasChildren)
        {
            return false;
        }

        var rowIndex = VisibleRows.IndexOf(row);
        if (rowIndex < 0)
        {
            return false;
        }

        if (!_expanded.Add(row.Node))
        {
            _expanded.Remove(row.Node);
            row.IsExpanded = false;
            var end = rowIndex + 1;
            while (end < VisibleRows.Count && VisibleRows[end].Depth > row.Depth)
            {
                end++;
            }

            if (end > rowIndex + 1)
            {
                VisibleRows.RemoveRange(rowIndex + 1, end - rowIndex - 1);
            }

            return true;
        }

        row.IsExpanded = true;
        var inserted = CreateExpandedDescendants(row.Node, row.Depth + 1);
        if (inserted.Count > 0)
        {
            VisibleRows.InsertRange(rowIndex + 1, inserted);
        }

        return true;
    }

    public ComparisonTreeRow? ExpandToPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalizedPath = path.Replace('/', '\\');
        var target = _comparison.Nodes.FirstOrDefault(node =>
            string.Equals(node.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return null;
        }

        if (GrowthOnly && !_growthRelevant.Contains(target))
        {
            return null;
        }

        var changed = false;
        for (var ancestor = target.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            changed |= _expanded.Add(ancestor);
        }

        if (changed)
        {
            RebuildVisibleRows();
        }

        return VisibleRows.FirstOrDefault(row => ReferenceEquals(row.Node, target));
    }

    public ComparisonTreeRow? JumpToBiggestGrower()
    {
        var biggest = _comparison.TopGrowers().FirstOrDefault();
        return biggest is null ? null : ExpandToPath(biggest.Path);
    }

    private void PrecomputeGrowthRelevantNodes()
    {
        foreach (var node in _comparison.Nodes)
        {
            if (node.Delta > 0 || node.Observation == FileObservation.NewlyObserved)
            {
                for (var current = node; current is not null && _growthRelevant.Add(current); current = current.Parent)
                {
                }
            }
        }
    }

    private bool ShouldInclude(ComparisonNode node) => !GrowthOnly || _growthRelevant.Contains(node);

    private void RebuildVisibleRows()
    {
        var rows = new List<ComparisonTreeRow>();
        if (ShouldInclude(_comparison.Root))
        {
            var root = CreateRow(_comparison.Root, 0);
            rows.Add(root);
            if (_expanded.Contains(_comparison.Root))
            {
                AppendExpandedDescendants(_comparison.Root, 1, rows);
            }
        }

        VisibleRows.ReplaceRange(rows);
    }

    private List<ComparisonTreeRow> CreateExpandedDescendants(ComparisonNode parent, int depth)
    {
        var rows = new List<ComparisonTreeRow>();
        AppendExpandedDescendants(parent, depth, rows);
        return rows;
    }

    private void AppendExpandedDescendants(ComparisonNode parent, int depth, List<ComparisonTreeRow> rows)
    {
        var stack = new Stack<(ComparisonNode Node, int Depth)>();
        PushChildrenInDisplayOrder(parent, depth, stack);
        while (stack.Count > 0)
        {
            var (node, nodeDepth) = stack.Pop();
            if (!ShouldInclude(node))
            {
                continue;
            }

            rows.Add(CreateRow(node, nodeDepth));
            if (_expanded.Contains(node))
            {
                PushChildrenInDisplayOrder(node, nodeDepth + 1, stack);
            }
        }
    }

    private void PushChildrenInDisplayOrder(ComparisonNode parent, int depth, Stack<(ComparisonNode Node, int Depth)> stack)
    {
        for (var index = parent.Children.Count - 1; index >= 0; index--)
        {
            var child = parent.Children[index];
            if (ShouldInclude(child))
            {
                stack.Push((child, depth));
            }
        }
    }

    private ComparisonTreeRow CreateRow(ComparisonNode node, int depth)
    {
        var hasChildren = node.Children.Any(ShouldInclude);
        return new ComparisonTreeRow(node, depth, hasChildren, _expanded.Contains(node), _maxDeltaMagnitude);
    }
}

public sealed class ComparisonTreeRows : IList<ComparisonTreeRow>, IReadOnlyList<ComparisonTreeRow>, IList, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly Dictionary<ComparisonTreeRow, Entry> _entries = [];
    private Entry? _root;
    private uint _randomState = 0x9E3779B9;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public int Count => SizeOf(_root);
    public bool IsReadOnly => true;
    bool IList.IsReadOnly => true;
    bool IList.IsFixedSize => true;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    public ComparisonTreeRow this[int index]
    {
        get => EntryAt(index).Row;
        set => throw new NotSupportedException();
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    public int IndexOf(ComparisonTreeRow item)
    {
        if (!_entries.TryGetValue(item, out var entry))
        {
            return -1;
        }

        var index = SizeOf(entry.Left);
        while (entry.Parent is { } parent)
        {
            if (ReferenceEquals(entry, parent.Right))
            {
                index += SizeOf(parent.Left) + 1;
            }
            entry = parent;
        }

        return index;
    }

    public bool Contains(ComparisonTreeRow item) => _entries.ContainsKey(item);

    public void CopyTo(ComparisonTreeRow[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (arrayIndex < 0 || array.Length - arrayIndex < Count)
        {
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        }

        foreach (var row in this)
        {
            array[arrayIndex++] = row;
        }
    }

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (var rowIndex = 0; rowIndex < Count; rowIndex++)
        {
            array.SetValue(this[rowIndex], index + rowIndex);
        }
    }

    public IEnumerator<ComparisonTreeRow> GetEnumerator()
    {
        var stack = new Stack<Entry>();
        var current = _root;
        while (current is not null || stack.Count > 0)
        {
            while (current is not null)
            {
                stack.Push(current);
                current = current.Left;
            }

            current = stack.Pop();
            yield return current.Row;
            current = current.Right;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Add(ComparisonTreeRow item) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, ComparisonTreeRow item) => throw new NotSupportedException();
    public bool Remove(ComparisonTreeRow item) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    int IList.Add(object? value) => throw new NotSupportedException();
    void IList.Clear() => throw new NotSupportedException();
    bool IList.Contains(object? value) => value is ComparisonTreeRow row && Contains(row);
    int IList.IndexOf(object? value) => value is ComparisonTreeRow row ? IndexOf(row) : -1;
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();

    internal void InsertRange(int index, IReadOnlyList<ComparisonTreeRow> rows)
    {
        if (index < 0 || index > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        if (rows.Count == 0)
        {
            return;
        }

        if (rows.Count is > 1 and <= 200)
        {
            for (var i = 0; i < rows.Count; i++)
                InsertRange(index + i, [rows[i]]);
            return;
        }

        var inserted = BuildTree(rows);
        var (left, right) = Split(_root, index);
        _root = Merge(Merge(left, inserted), right);
        if (rows.Count == 1)
            NotifyItem(NotifyCollectionChangedAction.Add, rows[0], index);
        else
            NotifyReset();
    }

    internal void RemoveRange(int index, int count)
    {
        if (index < 0 || count < 0 || index > Count - count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        if (count == 0)
        {
            return;
        }

        if (count is > 1 and <= 200)
        {
            for (var i = 0; i < count; i++)
                RemoveRange(index, 1);
            return;
        }

        var singleRow = count == 1 ? this[index] : null;
        var (left, remainder) = Split(_root, index);
        var (removed, right) = Split(remainder, count);
        if (removed is not null)
        {
            var pending = new Stack<Entry>();
            pending.Push(removed);
            while (pending.Count > 0)
            {
                var entry = pending.Pop();
                _entries.Remove(entry.Row);
                if (entry.Left is not null) pending.Push(entry.Left);
                if (entry.Right is not null) pending.Push(entry.Right);
            }
        }

        _root = Merge(left, right);
        if (singleRow is not null)
            NotifyItem(NotifyCollectionChangedAction.Remove, singleRow, index);
        else
            NotifyReset();
    }

    internal void ReplaceRange(IReadOnlyList<ComparisonTreeRow> rows)
    {
        _entries.Clear();
        _root = BuildTree(rows);
        NotifyReset();
    }

    private Entry EntryAt(int index)
    {
        if (index < 0 || index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var current = _root!;
        while (true)
        {
            var leftSize = SizeOf(current.Left);
            if (index < leftSize)
            {
                current = current.Left!;
            }
            else if (index == leftSize)
            {
                return current;
            }
            else
            {
                index -= leftSize + 1;
                current = current.Right!;
            }
        }
    }

    private Entry? BuildTree(IReadOnlyList<ComparisonTreeRow> rows)
    {
        var pending = new Stack<Entry>();
        for (var index = 0; index < rows.Count; index++)
        {
            var entry = new Entry(rows[index], NextPriority());
            _entries.Add(entry.Row, entry);
            Entry? previous = null;
            while (pending.Count > 0 && pending.Peek().Priority < entry.Priority)
            {
                previous = pending.Pop();
            }

            SetLeft(entry, previous);
            if (pending.Count > 0)
            {
                SetRight(pending.Peek(), entry);
            }
            pending.Push(entry);
        }

        if (pending.Count == 0)
        {
            return null;
        }

        var root = pending.Peek();
        while (root.Parent is not null)
        {
            root = root.Parent;
        }
        root.Parent = null;

        var traversal = new Stack<Entry>();
        var postorder = new Stack<Entry>();
        traversal.Push(root);
        while (traversal.Count > 0)
        {
            var entry = traversal.Pop();
            postorder.Push(entry);
            if (entry.Left is not null) traversal.Push(entry.Left);
            if (entry.Right is not null) traversal.Push(entry.Right);
        }
        while (postorder.Count > 0)
        {
            UpdateSize(postorder.Pop());
        }

        return root;
    }

    private uint NextPriority()
    {
        _randomState ^= _randomState << 13;
        _randomState ^= _randomState >> 17;
        _randomState ^= _randomState << 5;
        return _randomState;
    }

    private static (Entry? Left, Entry? Right) Split(Entry? root, int count)
    {
        var path = new Stack<(Entry Node, bool OnRight)>();
        var current = root;
        while (current is not null)
        {
            var leftSize = SizeOf(current.Left);
            if (count <= leftSize)
            {
                path.Push((current, true));
                current = current.Left;
            }
            else
            {
                path.Push((current, false));
                count -= leftSize + 1;
                current = current.Right;
            }
        }

        Entry? left = null;
        Entry? right = null;
        while (path.Count > 0)
        {
            var (node, onRight) = path.Pop();
            if (onRight)
            {
                SetLeft(node, right);
                UpdateSize(node);
                node.Parent = null;
                right = node;
            }
            else
            {
                SetRight(node, left);
                UpdateSize(node);
                node.Parent = null;
                left = node;
            }
        }

        if (left is not null) left.Parent = null;
        if (right is not null) right.Parent = null;
        return (left, right);
    }

    private static Entry? Merge(Entry? left, Entry? right)
    {
        if (left is null)
        {
            if (right is not null) right.Parent = null;
            return right;
        }
        if (right is null)
        {
            left.Parent = null;
            return left;
        }

        var path = new Stack<(Entry Node, bool AttachRight)>();
        while (left is not null && right is not null)
        {
            if (left.Priority >= right.Priority)
            {
                var next = left.Right;
                SetRight(left, null);
                path.Push((left, true));
                left = next;
                if (left is not null) left.Parent = null;
            }
            else
            {
                var next = right.Left;
                SetLeft(right, null);
                path.Push((right, false));
                right = next;
                if (right is not null) right.Parent = null;
            }
        }

        var result = left ?? right;
        if (result is not null) result.Parent = null;
        while (path.Count > 0)
        {
            var (node, attachRight) = path.Pop();
            if (attachRight) SetRight(node, result);
            else SetLeft(node, result);
            UpdateSize(node);
            node.Parent = null;
            result = node;
        }

        return result;
    }

    private static void SetLeft(Entry node, Entry? child)
    {
        if (node.Left is { } oldChild && !ReferenceEquals(oldChild, child) && ReferenceEquals(oldChild.Parent, node))
        {
            oldChild.Parent = null;
        }
        node.Left = child;
        if (child is not null) child.Parent = node;
    }

    private static void SetRight(Entry node, Entry? child)
    {
        if (node.Right is { } oldChild && !ReferenceEquals(oldChild, child) && ReferenceEquals(oldChild.Parent, node))
        {
            oldChild.Parent = null;
        }
        node.Right = child;
        if (child is not null) child.Parent = node;
    }

    private static int SizeOf(Entry? entry) => entry?.SubtreeSize ?? 0;

    private static void UpdateSize(Entry entry) =>
        entry.SubtreeSize = 1 + SizeOf(entry.Left) + SizeOf(entry.Right);

    private void NotifyItem(NotifyCollectionChangedAction action, ComparisonTreeRow row, int index)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(action, row, index));
    }

    private void NotifyReset()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private sealed class Entry(ComparisonTreeRow row, uint priority)
    {
        public ComparisonTreeRow Row { get; } = row;
        public uint Priority { get; } = priority;
        public int SubtreeSize { get; set; } = 1;
        public Entry? Parent { get; set; }
        public Entry? Left { get; set; }
        public Entry? Right { get; set; }
    }
}
