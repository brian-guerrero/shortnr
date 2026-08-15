namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Visual tone shared by banners (<c>Shared/_Alert</c>) and badges (<c>Shared/_Badge</c>);
/// maps 1:1 onto the <c>status-*</c> CSS classes in <c>site.css</c>.
/// </summary>
/// <remarks>
/// <see cref="Neutral"/> and <see cref="Plain"/> are badge-only — a banner always carries one of
/// the three semantic tones, so nothing renders an alert with them.
/// </remarks>
public enum StatusKind
{
    Error,
    Success,
    Info,

    /// <summary>Badge-only: a muted state that is neither good nor bad (unverified, archived, revoked).</summary>
    Neutral,

    /// <summary>Badge-only: no status colour at all, just the stamp treatment.</summary>
    Plain,
}

public static class StatusKindExtensions
{
    /// <summary>The <c>status-*</c> CSS class for a tone, or <c>null</c> for <see cref="StatusKind.Plain"/>.</summary>
    public static string? ToCssClass(this StatusKind kind) => kind switch
    {
        StatusKind.Error => "status-error",
        StatusKind.Success => "status-success",
        StatusKind.Info => "status-info",
        StatusKind.Neutral => "status-neutral",
        _ => null,
    };
}
