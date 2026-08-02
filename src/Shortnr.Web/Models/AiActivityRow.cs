namespace Shortnr.Web.Models;

/// <summary>Display model for a single <c>AiActivityLog</c> row on the AI activity dashboard.</summary>
public class AiActivityRow
{
    public long Id { get; init; }
    public string Action { get; init; } = "";
    public string? TargetEntityType { get; init; }
    public long? TargetEntityId { get; init; }
    public string Summary { get; init; } = "";
    public DateTime CreatedAtUtc { get; init; }
    public string? ApiKeyLabel { get; init; }
}
