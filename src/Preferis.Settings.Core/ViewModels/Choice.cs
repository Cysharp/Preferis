namespace Preferis.Settings.ViewModels;

/// <summary>An entry of a drop-down list. Non-generic so that the views can template every kind of choice by its title.</summary>
public record Choice(string Title);

public sealed record EnumChoice<T>(T Value, string Title) : Choice(Title);
