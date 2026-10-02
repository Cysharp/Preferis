using Preferis.Core.Localization;

namespace Preferis.Core.Tests.Localization;

/// <summary>
/// Tests that change <see cref="UiLanguages.Current"/>, which the whole process shares: they run alone, so that no other
/// test sees Japanese texts meanwhile.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LanguageCollection
{
    public const string Name = "UI language";
}

/// <summary>Base of the tests in <see cref="LanguageCollection"/>: puts the language back to English afterwards.</summary>
[Collection(LanguageCollection.Name)]
public abstract class LanguageTest : IDisposable
{
    public void Dispose()
    {
        UiLanguages.Current = UiLanguage.English;
        GC.SuppressFinalize(this);
    }
}
