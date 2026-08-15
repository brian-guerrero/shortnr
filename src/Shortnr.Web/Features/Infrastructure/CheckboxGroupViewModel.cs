using Microsoft.AspNetCore.Html;

namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Model for the shared <c>Shared/_CheckboxGroup</c> partial — a legend plus one checkbox per
/// option, all posting under the same field name (API key scopes, webhook event types).
/// </summary>
public class CheckboxGroupViewModel
{
    public required string Legend { get; init; }

    /// <summary>The form field name every checkbox posts under.</summary>
    public required string InputName { get; init; }

    public required IReadOnlyList<string> Options { get; init; }

    /// <summary>Decides the initial checked state per option; defaults to all checked.</summary>
    public Func<string, bool> IsChecked { get; init; } = _ => true;

    /// <summary>
    /// Help copy under the checkboxes. A Razor templated delegate (<c>@&lt;text&gt;…&lt;/text&gt;</c>)
    /// rather than a string, because both call sites mark up identifiers with <c>&lt;code&gt;</c> —
    /// this keeps that markup in a view instead of building HTML in C#.
    /// </summary>
    public Func<object?, IHtmlContent>? Help { get; init; }
}
