namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Model for the shared <c>Shared/_Alert</c> partial — the single place banner markup lives.
/// </summary>
public class AlertViewModel
{
    /// <summary>Only the three semantic tones apply to a banner; see <see cref="StatusKind"/>.</summary>
    public required StatusKind Kind { get; init; }

    public required string Message { get; init; }

    /// <summary>Optional monospaced payload rendered under the message (API key / webhook secret reveal).</summary>
    public string? Detail { get; init; }

    /// <summary>Renders the × close button and auto-hides after <c>Dashboard:MessageDisplayMs</c>.</summary>
    public bool Dismissible { get; init; }

    /// <summary>Extra inline style for the odd one-off spacing case.</summary>
    public string? Style { get; init; }

    public string StatusClass => Kind.ToCssClass() ?? "status-info";
}

/// <summary>
/// Implemented by page models that surface the standard status/error message pair so the
/// <c>Shared/_StatusMessages</c> partial can render both without knowing the concrete model.
/// </summary>
public interface IStatusMessages
{
    string? StatusMessage { get; }

    string? ErrorMessage { get; }
}
