using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Preferis.Settings.Windows.Controls;

/// <summary>
/// The list on the left of the profiles and rules pages: a card with the rows, reorderable by dragging, and a bar of
/// small buttons below it. Each row binds itself to its item; the rows are rebuilt only when the collection changes.
/// </summary>
internal sealed class ListPane<T> : IDisposable
    where T : class, INotifyPropertyChanged
{
    private readonly IList<T> _items;
    private readonly Func<T, Bindings, UIElement> _row;
    private readonly Action<T?> _select;
    private readonly Action<T, int>? _move;
    private readonly List<Bindings> _rowBindings = [];
    private readonly ListView _list;
    private List<T> _shown = [];
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
                _select(_list.SelectedIndex >= 0 && _list.SelectedIndex < _items.Count ? _items[_list.SelectedIndex] : null);
            }
        };
        _list.DragItemsStarting += (_, e) => _dragged = e.Items.Count == 1 && e.Items[0] is ListViewItem { Tag: int index } && index < _items.Count ? _items[index] : null;
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
        _list.SelectedIndex = item is null ? -1 : _items.IndexOf(item);
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

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        // The rows are new objects: keep the selection on the same item (moving an item keeps the view model's selection).
        var selected = _list.SelectedItem is ListViewItem { Tag: int previous } ? _shown.ElementAtOrDefault(previous) : null;
        _updating = true;
        DisposeRows();
        _list.Items.Clear();
        _shown = [.. _items];
        for (var i = 0; i < _items.Count; i++)
        {
            var bindings = new Bindings();
            _rowBindings.Add(bindings);
            _list.Items.Add(new ListViewItem
            {
                Content = _row(_items[i], bindings),
                Tag = i,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 8, 12, 8),
            });
        }
        _list.SelectedIndex = selected is null ? -1 : _items.IndexOf(selected);
        _updating = false;
    }

    // The list reordered its own rows; tell the view model, whose collection change then rebuilds the rows.
    private void OnDragged()
    {
        if (_dragged is not { } item || _move is null)
        {
            return;
        }
        _dragged = null;
        for (var i = 0; i < _list.Items.Count; i++)
        {
            if (_list.Items[i] is ListViewItem { Tag: int index } && index < _items.Count && ReferenceEquals(_items[index], item))
            {
                _move(item, i);
                Select(item);
                return;
            }
        }
    }

    private void DisposeRows()
    {
        foreach (var bindings in _rowBindings)
        {
            bindings.Dispose();
        }
        _rowBindings.Clear();
    }
}
