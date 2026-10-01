using System.Collections.Specialized;
using System.ComponentModel;
using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;

namespace Preferis.Settings.MacOS.Controls;

/// <summary>
/// The list on the left of the profiles and rules pages: a rounded box with the rows, reorderable by dragging, and a
/// bar of small buttons below it, as in System Settings and Mail's rules.
/// </summary>
internal sealed class ListPane<T> : IDisposable
    where T : class, INotifyPropertyChanged
{
    private const string RowType = "net.mayuki.preferis.row";

    private readonly IList<T> _items;
    private readonly Func<T, NSView> _row;
    private readonly Action<T?> _select;
    private readonly string[] _rowProperties;
    private readonly List<(T Item, PropertyChangedEventHandler Handler)> _rowSubscriptions = [];
    private bool _updatingSelection;

    /// <param name="move">Called when a row is dragged to a new index; null disables dragging.</param>
    /// <param name="rowProperties">Item properties shown in the row; the row is redrawn when they change.</param>
    public ListPane(IList<T> items, Func<T, NSView> row, Action<T?> select, Action<T, int>? move, double rowHeight, params string[] rowProperties)
    {
        _items = items;
        _row = row;
        _select = select;
        _rowProperties = rowProperties;

        Table = new NSTableView
        {
            HeaderView = null,
            Style = NSTableViewStyle.Inset,
            RowHeight = (nfloat)rowHeight,
            BackgroundColor = NSColor.Clear,
            AllowsEmptySelection = true,
            FocusRingType = NSFocusRingType.None,
        };
        Table.AddColumn(new NSTableColumn("row") { ResizingMask = NSTableColumnResizing.Autoresizing });
        if (move is not null)
        {
            Table.RegisterForDraggedTypes([RowType]);
        }
        Table.Source = new Source(this, move);

        var scroll = new NSScrollView { DocumentView = Table, DrawsBackground = false, HasVerticalScroller = true, AutohidesScrollers = true, BorderType = NSBorderType.NoBorder };
        Box = new NSBox
        {
            BoxType = NSBoxType.NSBoxCustom,
            BorderWidth = 0,
            CornerRadius = 12,
            FillColor = Ui.GroupFill,
            ContentViewMargins = CGSize.Empty,
            ContentView = scroll,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };

        Bar = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Spacing = 6, Alignment = NSLayoutAttribute.CenterY };
        View = Ui.VStack(8, Box, Bar);
        ((NSStackView)View).Distribution = NSStackViewDistribution.Fill;
        Ui.FillWidth(Box, View);
        Box.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Vertical);

        if (items is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += OnCollectionChanged;
        }
        Subscribe();
    }

    public NSView View { get; }

    public NSTableView Table { get; }

    /// <summary>The rounded box around the rows.</summary>
    public NSBox Box { get; }

    /// <summary>Buttons below the list (+, −, ⋯).</summary>
    public NSStackView Bar { get; }

    /// <summary>Adds <paramref name="view"/> between the rows and the button bar (the catch-all rule below the rules).</summary>
    public void AddBelowRows(NSView view)
    {
        ((NSStackView)View).InsertArrangedSubview(view, 1);
        Ui.FillWidth(view, View);
    }

    public void Select(T? item)
    {
        var index = item is null ? -1 : _items.IndexOf(item);
        if (Table.SelectedRow == index)
        {
            return;
        }
        _updatingSelection = true;
        try
        {
            if (index < 0)
            {
                Table.DeselectAll(null);
            }
            else
            {
                Table.SelectRow(index, false);
                Table.ScrollRowToVisible(index);
            }
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    public void Dispose()
    {
        if (_items is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged -= OnCollectionChanged;
        }
        Unsubscribe();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Unsubscribe();
        Subscribe();
        _updatingSelection = true;
        try
        {
            Table.ReloadData();
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    // A row shows a few properties of its item (name, summary, badges); redraw it when they change.
    private void Subscribe()
    {
        foreach (var item in _items)
        {
            var current = item;
            PropertyChangedEventHandler handler = (_, e) =>
            {
                if (e.PropertyName is null || _rowProperties.Contains(e.PropertyName))
                {
                    var index = _items.IndexOf(current);
                    if (index >= 0)
                    {
                        Table.ReloadData(NSIndexSet.FromIndex(index), NSIndexSet.FromIndex(0));
                    }
                }
            };
            item.PropertyChanged += handler;
            _rowSubscriptions.Add((item, handler));
        }
    }

    private void Unsubscribe()
    {
        foreach (var (item, handler) in _rowSubscriptions)
        {
            item.PropertyChanged -= handler;
        }
        _rowSubscriptions.Clear();
    }

    private sealed class Source : NSTableViewSource
    {
        private readonly ListPane<T> _owner;
        private readonly Action<T, int>? _move;

        public Source(ListPane<T> owner, Action<T, int>? move)
        {
            _owner = owner;
            _move = move;
        }

        public Source(NativeHandle handle)
            : base(handle)
        {
            _owner = null!;
        }

        public override nint GetRowCount(NSTableView tableView) => _owner._items.Count;

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row) => _owner._row(_owner._items[(int)row]);

        // The table comes from the notification: the first one can arrive while the pane is still being built.
        public override void SelectionDidChange(NSNotification notification)
        {
            if (_owner._updatingSelection || notification.Object is not NSTableView table)
            {
                return;
            }
            var row = (int)table.SelectedRow;
            _owner._select(row >= 0 && row < _owner._items.Count ? _owner._items[row] : null);
        }

        public override INSPasteboardWriting GetPasteboardWriterForRow(NSTableView table, nint row)
        {
            var item = new NSPasteboardItem();
            item.SetStringForType(row.ToString(System.Globalization.CultureInfo.InvariantCulture), RowType);
            return item;
        }

        public override NSDragOperation ValidateDrop(NSTableView tableView, INSDraggingInfo info, nint row, NSTableViewDropOperation dropOperation)
        {
            if (_move is null)
            {
                return NSDragOperation.None;
            }
            // Between rows only: dropping onto a row has no meaning in an ordered list.
            if (dropOperation == NSTableViewDropOperation.On)
            {
                tableView.SetDropRowDropOperation(row, NSTableViewDropOperation.Above);
            }
            return NSDragOperation.Move;
        }

        public override bool AcceptDrop(NSTableView tableView, INSDraggingInfo info, nint row, NSTableViewDropOperation dropOperation)
        {
            var text = info.DraggingPasteboard.PasteboardItems.FirstOrDefault()?.GetStringForType(RowType);
            if (_move is null || !int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var from) || from >= _owner._items.Count)
            {
                return false;
            }
            var item = _owner._items[from];
            // Dropping above row N moves the item to N, or to N - 1 once it is no longer counted above.
            var to = (int)row > from ? (int)row - 1 : (int)row;
            _move(item, to);
            _owner.Select(item);
            _owner._select(item);
            return true;
        }
    }
}
