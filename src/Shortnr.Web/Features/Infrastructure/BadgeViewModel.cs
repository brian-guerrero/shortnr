namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Model for the shared <c>Shared/_Badge</c> partial — the inline <c>&lt;mark&gt;</c> stamp used for
/// short states (verified, active, pending, archived) and for tag/scope chips.
/// </summary>
public class BadgeViewModel
{
    public required StatusKind Kind { get; init; }

    public required string Text { get; init; }

    /// <summary>Opts out of the stamp's uppercase treatment — for user-supplied names and tags.</summary>
    public bool PreserveCase { get; init; }

    /// <summary>Extra CSS class, e.g. <c>badge-gap</c> when the badge trails other content in a cell.</summary>
    public string? CssClass { get; init; }

    public string CssClasses => string.Join(
        ' ',
        new[] { Kind.ToCssClass(), PreserveCase ? "preserve-case" : null, CssClass }
            .Where(c => !string.IsNullOrEmpty(c)));
}
