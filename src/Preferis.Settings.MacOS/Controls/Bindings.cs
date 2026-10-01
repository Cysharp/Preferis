using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using AppKit;
using Foundation;

namespace Preferis.Settings.MacOS.Controls;

/// <summary>
/// The glue AppKit lacks for plain C# view models: pushes view model changes into controls and control edits into the
/// view model. One instance per view; disposing it unsubscribes from the view models so a replaced view stops updating.
/// </summary>
internal sealed class Bindings : IDisposable
{
    private readonly List<Action> _unsubscribe = [];

    /// <summary>Calls <paramref name="apply"/> now and whenever one of <paramref name="properties"/> changes (any property when empty).</summary>
    public void Observe(INotifyPropertyChanged source, Action apply, params string[] properties)
    {
        apply();
        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (properties.Length == 0 || e.PropertyName is null || properties.Contains(e.PropertyName))
            {
                apply();
            }
        };
        source.PropertyChanged += handler;
        _unsubscribe.Add(() => source.PropertyChanged -= handler);
    }

    public void ObserveCollection(INotifyCollectionChanged source, Action apply)
    {
        apply();
        NotifyCollectionChangedEventHandler handler = (_, _) => apply();
        source.CollectionChanged += handler;
        _unsubscribe.Add(() => source.CollectionChanged -= handler);
    }

    /// <summary>Two-way text. The field is only rewritten when the view model holds something else, so typing is not disturbed.</summary>
    public void Text(NSTextField field, INotifyPropertyChanged source, string property, Func<string> get, Action<string> set)
    {
        Observe(source, () =>
        {
            var value = get();
            if (field.StringValue != value)
            {
                field.StringValue = value;
            }
        }, property);
        field.Changed += (_, _) => set(field.StringValue);
    }

    /// <summary>Two-way text for a multi-line text view.</summary>
    public void Text(NSTextView view, INotifyPropertyChanged source, string property, Func<string> get, Action<string> set)
    {
        Observe(source, () =>
        {
            var value = get();
            if (view.Value != value)
            {
                view.Value = value;
            }
        }, property);
        view.TextDidChange += (_, _) => set(view.Value);
    }

    public void Check(NSButton checkbox, INotifyPropertyChanged source, string property, Func<bool> get, Action<bool> set)
    {
        Observe(source, () => checkbox.State = get() ? NSCellStateValue.On : NSCellStateValue.Off, property);
        checkbox.Activated += (_, _) => set(checkbox.State == NSCellStateValue.On);
    }

    public void Switch(NSSwitch control, INotifyPropertyChanged source, string property, Func<bool> get, Action<bool> set)
    {
        Observe(source, () => control.State = get() ? 1 : 0, property);
        control.Activated += (_, _) => set(control.State == 1);
    }

    /// <summary>
    /// A pop-up button over a list of items. The menu is rebuilt when <paramref name="itemsProperty"/> changes (or the
    /// collection does), the selection follows <paramref name="selectedProperty"/>.
    /// </summary>
    public void Popup<T>(
        NSPopUpButton popup,
        INotifyPropertyChanged source,
        string selectedProperty,
        Func<IReadOnlyList<T>> items,
        Func<T?> selected,
        Action<T> set,
        Func<T, string> title,
        Func<T, NSImage?>? image = null,
        string? itemsProperty = null)
        where T : class
    {
        IReadOnlyList<T> current = [];
        void Rebuild()
        {
            current = items();
            popup.RemoveAllItems();
            foreach (var item in current)
            {
                popup.AddItem(title(item));
                if (image?.Invoke(item) is { } icon)
                {
                    popup.LastItem!.Image = Images.Resized(icon, 16);
                }
            }
            Select();
        }
        void Select()
        {
            var value = selected();
            var index = value is null ? -1 : IndexOf(current, value);
            popup.SelectItem(index);
        }

        Rebuild();
        if (itemsProperty is not null)
        {
            Observe(source, Rebuild, itemsProperty);
        }
        if (items() is INotifyCollectionChanged collection)
        {
            ObserveCollection(collection, Rebuild);
        }
        Observe(source, Select, selectedProperty);
        popup.Activated += (_, _) =>
        {
            var index = (int)popup.IndexOfSelectedItem;
            if (index >= 0 && index < current.Count)
            {
                set(current[index]);
            }
        };
    }

    /// <summary>A pop-up button over a fixed list of value/title pairs (enums, strings).</summary>
    public void Choice<T>(NSPopUpButton popup, INotifyPropertyChanged source, string property, IReadOnlyList<(T Value, string Title)> choices, Func<T> get, Action<T> set)
    {
        popup.RemoveAllItems();
        foreach (var (_, title) in choices)
        {
            popup.AddItem(title);
        }
        Observe(source, () =>
        {
            var value = get();
            for (var i = 0; i < choices.Count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(choices[i].Value, value))
                {
                    popup.SelectItem(i);
                }
            }
        }, property);
        popup.Activated += (_, _) =>
        {
            var index = (int)popup.IndexOfSelectedItem;
            if (index >= 0 && index < choices.Count)
            {
                set(choices[index].Value);
            }
        };
    }

    /// <summary>Runs the command when the control is used, and enables the control as the command allows.</summary>
    public void Command(NSControl control, ICommand command, Func<object?>? parameter = null)
    {
        void Update() => control.Enabled = command.CanExecute(parameter?.Invoke());
        Update();
        EventHandler handler = (_, _) => Update();
        command.CanExecuteChanged += handler;
        _unsubscribe.Add(() => command.CanExecuteChanged -= handler);
        control.Activated += (_, _) =>
        {
            var argument = parameter?.Invoke();
            if (command.CanExecute(argument))
            {
                command.Execute(argument);
            }
        };
    }

    public void Visible(NSView view, INotifyPropertyChanged source, Func<bool> visible, params string[] properties)
        => Observe(source, () => view.Hidden = !visible(), properties);

    public void Add(Action unsubscribe) => _unsubscribe.Add(unsubscribe);

    public void Dispose()
    {
        foreach (var unsubscribe in _unsubscribe)
        {
            unsubscribe();
        }
        _unsubscribe.Clear();
    }

    private static int IndexOf<T>(IReadOnlyList<T> list, T value)
        where T : class
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], value) || Equals(list[i], value))
            {
                return i;
            }
        }
        return -1;
    }
}

/// <summary>Images for PNG bytes coming from the view models (browser icons, profile pictures) and SF Symbols.</summary>
internal static class Images
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], NSImage> Cache = new();

    public static NSImage? FromPng(byte[]? png) => png is { Length: > 0 } bytes ? Cache.GetValue(bytes, b => new NSImage(NSData.FromArray(b))) : null;

    public static NSImage? Symbol(string name, double pointSize, nfloat? weight = null)
        => NSImage.GetSystemSymbol(name, null)?.GetImage(NSImageSymbolConfiguration.Create((System.Runtime.InteropServices.NFloat)pointSize, weight ?? NSFontWeight.Regular));

    /// <summary>A copy drawn at <paramref name="size"/> points, for menus that would show the image at full size.</summary>
    public static NSImage Resized(NSImage image, double size)
    {
        var copy = (NSImage)image.Copy();
        copy.Size = new CoreGraphics.CGSize(size, size);
        return copy;
    }
}
