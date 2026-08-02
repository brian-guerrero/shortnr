namespace Shortnr.Data.Entities;

/// <summary>
/// Audit trail of AI/agent (MCP) actions performed on behalf of an owner. Rows are
/// written asynchronously by the <c>AiActivityProcessor</c> background service so a
/// tool call never blocks on a DB write (mirrors <c>ClickEvent</c> batching).
/// </summary>
public class AiActivityLog
{
    public long Id { get; set; }
    public long OwnerUserId { get; set; }
    /// <summary>ApiKeys.Id of the key that authorized the call, when known.</summary>
    public long? ApiKeyId { get; set; }
    /// <summary>Stable action name, e.g. <c>create_short_link</c> or <c>list_short_links</c>.</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Entity type the action targeted, e.g. <c>ShortenedUrl</c> or <c>BioPageLink</c>.</summary>
    public string? TargetEntityType { get; set; }
    /// <summary>Primary key of the affected entity, when known.</summary>
    public long? TargetEntityId { get; set; }
    /// <summary>Human-readable summary rendered on the AI activity dashboard.</summary>
    public string Summary { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public User? Owner { get; set; }
    public ApiKey? ApiKey { get; set; }
}
