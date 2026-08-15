namespace Shortnr.Web.Features.Infrastructure;

/// <summary>Button treatment for a <see cref="ActionFormViewModel"/>; maps to Pico/site button classes.</summary>
public enum ActionButtonStyle
{
    Primary,
    Secondary,
    Danger,
}

/// <summary>
/// Model for the shared <c>Shared/_ActionForm</c> partial — the one-button HTMX form used all over
/// the settings, bio and dashboard surfaces (revoke, delete, toggle, reorder, …).
/// </summary>
/// <remarks>
/// A <see cref="ActionButtonStyle.Danger"/> button always gets an <c>hx-confirm</c>: supply
/// <see cref="ConfirmMessage"/> for specific wording, otherwise <see cref="DefaultDangerConfirm"/>
/// applies. This is why destructive actions can't silently ship without a prompt.
/// </remarks>
public class ActionFormViewModel
{
    public const string DefaultDangerConfirm = "Are you sure? This cannot be undone.";

    public required string PostUrl { get; init; }

    /// <summary>The <c>hx-target</c> selector the response swaps into.</summary>
    public required string Target { get; init; }

    public required string ButtonLabel { get; init; }

    public Dictionary<string, string> HiddenFields { get; init; } = [];

    public ActionButtonStyle ButtonStyle { get; init; } = ActionButtonStyle.Secondary;

    /// <summary>Applies <c>btn-sm</c>; on by default because nearly every one of these sits in a table cell.</summary>
    public bool Small { get; init; } = true;

    /// <summary>Extra selectors whose values ride along with the post (filters, sort state).</summary>
    public string? HxInclude { get; init; }

    /// <summary>Overrides the confirm prompt. Ignored for non-danger buttons unless explicitly set.</summary>
    public string? ConfirmMessage { get; init; }

    public string? ResolvedConfirm => ConfirmMessage
        ?? (ButtonStyle == ActionButtonStyle.Danger ? DefaultDangerConfirm : null);

    public string ButtonClasses => string.Join(
        ' ',
        new[]
        {
            ButtonStyle switch
            {
                ActionButtonStyle.Secondary => "secondary",
                ActionButtonStyle.Danger => "danger",
                _ => null,
            },
            Small ? "btn-sm" : null,
        }.Where(c => !string.IsNullOrEmpty(c)));
}
