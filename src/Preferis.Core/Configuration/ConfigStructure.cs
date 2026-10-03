using System.Text.Json;
using static Preferis.Core.Localization.CoreText;

namespace Preferis.Core.Configuration;

/// <summary>
/// What the serializer leaves to check after reading (see <see cref="PreferisJsonContext"/>): <c>null</c> items of lists and
/// values of dictionaries (<c>"actions": [null]</c>). The model has no meaning for them and routing and the settings would
/// dereference them, so they fail the read like malformed JSON. The content of conditions and actions of an unknown type
/// is not looked into: it is for the newer Preferis that wrote it to judge.
/// </summary>
internal static class ConfigStructure
{
    /// <exception cref="JsonException">An item is null. The message names it by its JSON path.</exception>
    public static PreferisConfig Check(PreferisConfig config)
    {
        Each(config.Profiles, "$.profiles", Profile);
        Each(config.Rules, "$.rules", Rule);
        Actions(config.CatchAll.Actions, "$.catchAll.actions");
        return config;
    }

    /// <exception cref="JsonException">An item is null. The message names it by its JSON path.</exception>
    public static LocalConfig Check(LocalConfig local)
    {
        Each(local.BrowserOverrides, "$.browserOverrides");
        Each(local.ProfileTargets, "$.profileTargets", static (target, path) =>
        {
            Each(target.ExtraArgs, path + ".extraArgs");
            Command(target.Command, path + ".command");
        });
        return local;
    }

    private static void Profile(Profile profile, string path)
    {
        Each(profile.PreviousIds, path + ".previousIds");
        Each(profile.ExtraArgs, path + ".extraArgs");
        Command(profile.Command?.Windows, path + ".command.windows");
        Command(profile.Command?.MacOS, path + ".command.macos");
    }

    private static void Command(CommandSpec? command, string path)
    {
        if (command is not null)
        {
            Each(command.Args, path + ".args");
        }
    }

    private static void Rule(Rule rule, string path)
    {
        // No condition is a rule that never matches (docs/Design.md §6.1), not a broken one.
        if (rule.Condition is { } condition)
        {
            Condition(condition, path + ".condition");
        }
        Actions(rule.Actions, path + ".actions");
    }

    private static void Condition(Condition condition, string path)
    {
        switch (condition)
        {
            case AndCondition and:
                Each(and.Conditions, path + ".conditions", Condition);
                break;
            case OrCondition or:
                Each(or.Conditions, path + ".conditions", Condition);
                break;
            case NotCondition { Condition: { } negated }:
                Condition(negated, path + ".condition");
                break;
        }
    }

    private static void Actions(List<RoutingAction> actions, string path) => Each(actions, path, static (action, at) =>
    {
        if (action is OpenAction open)
        {
            Each(open.Profiles, at + ".profiles");
        }
    });

    private static void Each<T>(List<T>? items, string path, Action<T, string>? check = null)
        where T : class
    {
        if (items is null)
        {
            return;
        }
        for (var i = 0; i < items.Count; i++)
        {
            Item(items[i], $"{path}[{i}]", check);
        }
    }

    private static void Each<T>(Dictionary<string, T>? items, string path, Action<T, string>? check = null)
        where T : class
    {
        if (items is null)
        {
            return;
        }
        foreach (var (key, item) in items)
        {
            Item(item, $"{path}['{key}']", check);
        }
    }

    // The declared type says the item is never null; the file can still say otherwise.
    private static void Item<T>(T? item, string path, Action<T, string>? check)
        where T : class
    {
        if (item is null)
        {
            throw new JsonException(L("{0} must not be null.", path), path, lineNumber: null, bytePositionInLine: null);
        }
        check?.Invoke(item, path);
    }
}
