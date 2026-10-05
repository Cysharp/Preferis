using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Preferis.Settings.Windows.Controls;

/// <summary>
/// The list on the left of the profiles and rules pages: a card with the rows, reorderable by dragging, and a bar of
/// small buttons below it. Collection changes insert, remove or move the existing rows; only a reset rebuilds them.
/// </summary>
internal sealed class ListPane<T> : IDisposable
    where T : class, INotifyPropertyChanged
{
    private readonly IList<T> _items;
    private readonly Func<T, Bindings, UIElement> _row;
    private readonly Action<T?> _select;
    private readonly Action<T, int>? _move;
    private readonly Dictionary<T, (ListViewItem View, Bindings Bindings)> _rows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, T> _rowItems = [];
    private readonly ListView _list;
    private int _nextRowId;
    private bool _updating;
    private T? _dragged;

    /// <param name="row">Builds the row of an item, binding it with the given <see cref="Bindings"/>.</param>
    /// <param name="move">Called when a row is dragged to a new index; null disables dragging.</param>
    public ListPane(IList<T> items, Func<T, Bindings, UIElement> row, Action<T?> select, Action<T, int>? move)
    {
        _items = items;
        _row = row;
        _select = select;
        _move = move;

        _list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            CanDragItems = move is not null,
            CanReorderItems = move is not null,
            AllowDrop = move is not null,
            Padding = new Thickness(0, 4, 0, 4),
        };
        _list.SelectionChanged += (_, _) =>
        {
            if (!_updating)
            {
                _select(ItemOf(_list.SelectedItem as ListViewItem));
            }
        };
        _list.DragItemsStarting += (_, e) => _dragged = e.Items.Count == 1 ? ItemOf(e.Items[0] as ListViewItem) : null;
        _list.DragItemsCompleted += (_, _) => OnDragged();

        var box = Form.Box(_list);
        box.Padding = new Thickness(0);
        Box = box;

        Bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var grid = new Grid { RowSpacing = 6 };
        grid.RowDefinitions.Add(new RowDefinition { Height = Ui.Star });
        grid.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
        grid.Children.Add(box);
        Grid.SetRow(Bar, 1);
        grid.Children.Add(Bar);
        View = grid;

        if (items is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += OnCollectionChanged;
        }
        Rebuild();
    }

    public Grid View { get; }

    /// <summary>The card around the rows.</summary>
    public Border Box { get; }

    /// <summary>The buttons below the list (add, remove, more).</summary>
    public StackPanel Bar { get; }

    public ListView List => _list;

    /// <summary>Selects the row of <paramref name="item"/> (none for null) without reporting it back.</summary>
    public void Select(T? item)
    {
        _updating = true;
        _list.SelectedItem = RowFor(item);
        _updating = false;
        if (_list.SelectedItem is not null)
        {
            _list.ScrollIntoView(_list.SelectedItem);
        }
    }

    public void Dispose()
    {
        if (_items is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged -= OnCollectionChanged;
        }
        DisposeRows();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            Rebuild();
            return;
        }

        var selected = ItemOf(_list.SelectedItem as ListViewItem);
        _updating = true;
        try
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    for (var i = 0; i < e.NewItems!.Count; i++)
                    {
                        _list.Items.Insert(e.NewStartingIndex + i, CreateRow((T)e.NewItems[i]!));
                    }
                    break;
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Replace:
                    for (var i = 0; i < e.OldItems!.Count; i++)
                    {
                        var row = (ListViewItem)_list.Items[e.OldStartingIndex];
                        var item = ItemOf(row)!;
                        _rows[item].Bindings.Dispose();
                        _rows.Remove(item);
                        _rowItems.Remove((int)row.Tag);
                        _list.Items.RemoveAt(e.OldStartingIndex);
                    }
                    if (e.Action == NotifyCollectionChangedAction.Replace)
                    {
                        for (var i = 0; i < e.NewItems!.Count; i++)
                        {
                            _list.Items.Insert(e.NewStartingIndex + i, CreateRow((T)e.NewItems[i]!));
                        }
                    }
                    break;
                case NotifyCollectionChangedAction.Move:
                    var moved = e.NewItems!.Cast<T>().Select(item => RowFor(item)!).ToArray();
                    // A drag already moved the native row; a toolbar command has not. In both cases keep the row
                    // and its bindings, and put it at the source collection's destination.
                    if (moved.Length == 1 && _list.Items.IndexOf(moved[0]) == e.NewStartingIndex)
                    {
                        break;
                    }
                    foreach (var row in moved)
                    {
                        _list.Items.Remove(row);
                    }
                    for (var i = 0; i < moved.Length; i++)
                    {
                        _list.Items.Insert(e.NewStartingIndex + i, moved[i]);
                    }
                    break;
            }
            _list.SelectedItem = RowFor(selected);
        }
        finally
        {
            _updating = false;
        }
    }

    private void Rebuild()
    {
        // The rows are new objects: keep the selection on the same item (moving an item keeps the view model's selection).
        var selected = ItemOf(_list.SelectedItem as ListViewItem);
        _updating = true;
        DisposeRows();
        _list.Items.Clear();
        foreach (var item in _items)
        {
            _list.Items.Add(CreateRow(item));
        }
        _list.SelectedItem = RowFor(selected);
        _updating = false;
    }

    private ListViewItem CreateRow(T item)
    {
        var bindings = new Bindings();
        var id = _nextRowId++;
        var row = new ListViewItem
        {
            Content = _row(item, bindings),
            // A stable integer crosses the WinRT boundary; the view model stays on the managed side.
            Tag = id,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 8, 12, 8),
        };
        _rows.Add(item, (row, bindings));
        _rowItems.Add(id, item);
        return row;
    }

    private T? ItemOf(ListViewItem? row)
        => row?.Tag is int id && _rowItems.TryGetValue(id, out var item) ? item : null;

    private ListViewItem? RowFor(T? item)
        => item is not null && _rows.TryGetValue(item, out var row) ? row.View : null;

    // The list reordered its own rows; tell the view model, retaining the row and its selection.
    private void OnDragged()
    {
        if (_dragged is not { } item || _move is null)
        {
            return;
        }
        _dragged = null;
        for (var i = 0; i < _list.Items.Count; i++)
        {
            if (_list.Items[i] is ListViewItem row && ReferenceEquals(ItemOf(row), item))
            {
                _move(item, i);
                Select(item);
                return;
            }
        }
    }

    private void DisposeRows()
    {
        foreach (var row in _rows.Values)
        {
            row.Bindings.Dispose();
        }
        _rows.Clear();
        _rowItems.Clear();
    }
}
