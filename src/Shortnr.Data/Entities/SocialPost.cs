namespace Shortnr.Data.Entities;

/// <summary>
/// A cached post/clip fetched from a linked <see cref="SocialAccount"/> (PRD-021).
/// Rows are upserted by <c>(SocialAccountId, ExternalPostId)</c> on every
/// background refresh, so reruns never create duplicates.
/// </summary>
public class SocialPost
{
    public long Id { get; set; }
    public long SocialAccountId { get; set; }
    public string ExternalPostId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Text { get; set; }
    public string? MediaUrl { get; set; }
    public string? Permalink { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime FetchedAtUtc { get; set; }

    public SocialAccount? Account { get; set; }
}