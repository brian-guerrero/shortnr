namespace Shortnr.Web.Features.Infrastructure;

/// <summary>
/// Model for the shared <c>Shared/_QrTrigger</c> partial — the button that swaps a QR code into the
/// <c>.qr-wrap</c> span around it.
/// </summary>
public class QrTriggerViewModel
{
    public required string ShortCode { get; init; }

    public string Label { get; init; } = "QR";

    /// <summary>Extra CSS class on the wrapping span, e.g. <c>qr-wrap-inline</c> after a link.</summary>
    public string? WrapCssClass { get; init; }
}
