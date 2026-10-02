using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Preferis.Settings.Windows.Controls;

/// <summary>
/// Pushes view model changes into controls and control edits into the view model, without {Binding} (reflection, which
/// NativeAOT trims) and without x:Bind (the pages are built in code, like the AppKit ones). One instance per view;
/// disposing it unsubscribes from the view models so a replaced view stops updating.
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

    /// <summary>Two-way text. The box is only rewritten when the view model holds something else, so typing is not disturbed.</summary>
    public void Text(TextBox box, INotifyPropertyChanged source, string property, Func<string> get, Action<string> set)
    {
        Observe(source, () =>
        {
            var value = get();
            if (Normalize(box.Text) != Normalize(value))
            {
                box.Text = value;
            }
        }, property);
        box.TextChanged += (_, _) => set(Normalize(box.Text));
    }

    public void Check(CheckBox check, INotifyPropertyChanged source, string property, Func<bool> get, Action<bool> set)
    {
        Observe(source, () => check.IsChecked = get(), property);
        check.Click += (_, _) => set(check.IsChecked == true);
    }

    public void Switch(ToggleSwitch toggle, INotifyPropertyChanged source, string property, Func<bool> get, Action<bool> set)
    {
        var updating = false;
        Observe(source, () =>
        {
            updating = true;
            toggle.IsOn = get();
            updating = false;
        }, property);
        toggle.Toggled += (_, _) =>
        {
            if (!updating && toggle.IsOn != get())
            {
                set(toggle.IsOn);
            }
        };
    }

    /// <summary>
    /// A drop-down over a list of items. The list is rebuilt when <paramref name="itemsProperty"/> changes (or the
    /// collection does), the selection follows <paramref name="selectedProperty"/>.
    /// </summary>
    public void Popup<T>(
        ComboBox combo,
        INotifyPropertyChanged source,
        string selectedProperty,
        Func<IReadOnlyList<T>> items,
        Func<T?> selected,
        Action<T> set,
        Func<T, string> title,
        Func<T, byte[]?>? icon = null,
        string? itemsProperty = null)
        where T : class
    {
        IReadOnlyList<T> current = [];
        var updating = false;
        void Rebuild()
        {
            updating = true;
            current = items();
            combo.Items.Clear();
            foreach (var item in current)
            {
                combo.Items.Add(Ui.MenuContent(title(item), icon?.Invoke(item)));
            }
            updating = false;
            Select();
        }
        void Select()
        {
            updating = true;
            var value = selected();
            combo.SelectedIndex = value is null ? -1 : IndexOf(current, value);
            updating = false;
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
        combo.SelectionChanged += (_, _) =>
        {
            var index = combo.SelectedIndex;
            if (!updating && index >= 0 && index < current.Count && !ReferenceEquals(current[index], selected()))
            {
                set(current[index]);
            }
        };
    }

    /// <summary>A drop-down over a fixed list of value/title pairs (enums, strings).</summary>
    public void Choice<T>(ComboBox combo, INotifyPropertyChanged source, string property, IReadOnlyList<(T Value, string Title)> choices, Func<T> get, Action<T> set)
    {
        var updating = false;
        combo.Items.Clear();
        foreach (var (_, title) in choices)
        {
            combo.Items.Add(title);
        }
        Observe(source, () =>
        {
            updating = true;
            var value = get();
            for (var i = 0; i < choices.Count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(choices[i].Value, value))
                {
                    combo.SelectedIndex = i;
                }
            }
            updating = false;
        }, property);
        combo.SelectionChanged += (_, _) =>
        {
            var index = combo.SelectedIndex;
            if (!updating && index >= 0 && index < choices.Count && !EqualityComparer<T>.Default.Equals(choices[index].Value, get()))
            {
                set(choices[index].Value);
            }
        };
    }

    /// <summary>A number box, two-way. Typing an invalid number leaves the view model as it is.</summary>
    public void Number(NumberBox box, INotifyPropertyChanged source, string property, Func<double> get, Action<double> set)
    {
        var updating = false;
        Observe(source, () =>
        {
            updating = true;
            box.Value = get();
            updating = false;
        }, property);
        box.ValueChanged += (_, e) =>
        {
            if (!updating && !double.IsNaN(e.NewValue) && e.NewValue != get())
            {
                set(e.NewValue);
            }
        };
    }

    /// <summary>Runs the command when the button is used, and enables the button as the command allows.</summary>
    public void Command(ButtonBase button, ICommand command, Func<object?>? parameter = null)
    {
        void Update() => button.IsEnabled = command.CanExecute(parameter?.Invoke());
        Update();
        EventHandler handler = (_, _) => Update();
        command.CanExecuteChanged += handler;
        _unsubscribe.Add(() => command.CanExecuteChanged -= handler);
        button.Click += (_, _) => Execute(command, parameter?.Invoke());
    }

    /// <summary>A menu item for a command; enabled as the command allows when the menu opens.</summary>
    public static MenuFlyoutItem MenuItem(string text, ICommand command, object? parameter = null)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => Execute(command, parameter);
        item.Loaded += (_, _) => item.IsEnabled = command.CanExecute(parameter);
        return item;
    }

    public void Visible(UIElement element, INotifyPropertyChanged source, Func<bool> visible, params string[] properties)
        => Observe(source, () => element.Visibility = visible() ? Visibility.Visible : Visibility.Collapsed, properties);

    public void Add(Action unsubscribe) => _unsubscribe.Add(unsubscribe);

    public void Dispose()
    {
        foreach (var unsubscribe in _unsubscribe)
        {
            unsubscribe();
        }
        _unsubscribe.Clear();
    }

    private static void Execute(ICommand command, object? argument)
    {
        if (command.CanExecute(argument))
        {
            command.Execute(argument);
        }
    }

    // The text box reports Return as \r; the view models split lines on \n.
    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

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
