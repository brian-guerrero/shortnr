namespace Shortnr.Web.Models;

/// <summary>
/// Queued AI/MCP activity to be written to <c>AiActivityLog</c> by
/// <see cref="Services.AiActivityProcessor"/>. Kept off the request path so a
/// tool call never blocks on a DB write (mirrors <c>ClickRecord</c>).
/// </summary>
public class AiActivityRecord
{
    public required long OwnerUserId { get; init; }
    public long? ApiKeyId { get; init; }
    public required string Action { get; init; }
    public string? TargetEntityType { get; init; }
    public long? TargetEntityId { get; init; }
    public required string Summary { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
