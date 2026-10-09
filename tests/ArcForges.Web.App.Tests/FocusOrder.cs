// SPDX-License-Identifier: AGPL-3.0-only
// Focus-order and keyboard assertions of the interactive pages (the CI accessibility gate, brief section 6 decision 3).
// bUnit renders the markup but does not move focus, so the keyboard sequence is read from the rendered document: the
// controls a keyboard user can reach, in document order, skipping disabled controls and tabindex -1. Native controls
// (button, a, input) give Enter and Space activation for free, so the tests also pin the element kind and the form's
// default button, which decides what Enter does in a text field.
using System.Globalization;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Components;
using Bunit;
using Xunit;

namespace ArcForges.Web.App.Tests;

internal static class FocusOrder
{
    private const string Reachable = "a[href], button, input, select, textarea, [tabindex]";

    /// <summary>The keyboard tab sequence: each reachable control as "tag:name", in document order.</summary>
    internal static IReadOnlyList<string> TabSequence<TComponent>(IRenderedComponent<TComponent> rendered)
        where TComponent : IComponent =>
        rendered.FindAll(Reachable)
            .Where(element => element.GetAttribute("tabindex") != "-1" && !element.HasAttribute("disabled"))
            .Select(Describe)
            .ToList();

    /// <summary>The document order of the live regions and the controls, for reading-order checks.</summary>
    internal static IReadOnlyList<string> ReadingOrder<TComponent>(IRenderedComponent<TComponent> rendered)
        where TComponent : IComponent =>
        rendered.FindAll("[role=status], [role=alert], button, input")
            .Select(element => element.GetAttribute("role") ?? element.LocalName)
            .ToList();

    /// <summary>No element may take a positive tabindex, which would reorder the page away from document order.</summary>
    internal static void AssertNoPositiveTabIndex<TComponent>(IRenderedComponent<TComponent> rendered)
        where TComponent : IComponent
    {
        foreach (var element in rendered.FindAll("[tabindex]"))
        {
            var value = element.GetAttribute("tabindex")!;
            Assert.True(
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index <= 0,
                $"A positive tabindex reorders the keyboard sequence: {element.OuterHtml}");
        }
    }

    /// <summary>The first submit button of a form is its default button: Enter in a text field activates it.</summary>
    internal static string DefaultButtonOf(IElement form)
    {
        var button = form.QuerySelectorAll("button")
            .First(candidate => (candidate.GetAttribute("type") ?? "submit") == "submit");
        return NameOf(button);
    }

    private static string Describe(IElement element) => $"{element.LocalName}:{NameOf(element)}";

    private static string NameOf(IElement element)
    {
        if (element.LocalName == "input")
        {
            var label = element.Closest("label")?.TextContent.Trim();
            return element.GetAttribute("aria-label") ?? (string.IsNullOrEmpty(label) ? "(unnamed)" : label);
        }

        return element.TextContent.Trim();
    }
}
