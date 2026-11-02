---
title: Condition Operators
---

When a rule's condition compares the host or URL passed to Preferis, you can use the following operators.

:::tip
Whether the comparison is case-sensitive depends on the input. **Host**, **URL**, **App ID**, and **App path** are never case-sensitive, while **URL (case-sensitive)** is.
:::

## is

Checks whether the input exactly matches the specified value.

For example, `app.example` matches the input `app.example`.

## starts with

Checks whether the input starts with the specified value.

For example, `app.example` matches the inputs `app.example` and `app.example.com`.

## ends with

Checks whether the input ends with the specified value.

For example, `app.example` matches the inputs `app.example` and `myapp.example`.

## contains

Checks whether the input contains the specified value.

For example, `app.example` matches the inputs `app.example`, `myapp.example`, and `myapp.example.com`.

## matches

Checks whether the input matches the specified pattern. In a pattern, `*` matches any string and `?` matches any single character.

For example, `*.example` matches the input `sub.example`, and `app?.example` matches the inputs `app1.example` and `app9.example`.

## matches regex

Checks whether the input matches the specified regular expression. Regular expressions use the .NET regular expression engine.
