namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>Outcome category of an AI analysis request shown on the /insights page.</summary>
public enum LlmInsightStatus
{
    Success = 0,
    Disabled = 1,
    BudgetExceeded = 2,
    NotFound = 3,
    Error = 4
}

/// <summary>View model rendered by the shared <c>_LlmInsightResult</c> partial.</summary>
public sealed class LlmInsightResult
{
    public required LlmInsightStatus Status { get; init; }
    /// <summary>Generated text (success only).</summary>
    public string Content { get; init; } = string.Empty;
    /// <summary>Friendly explanation shown for every non-success status.</summary>
    public string FriendlyMessage { get; init; } = string.Empty;
    /// <summary>Underlying <see cref="LlmErrorKind"/> when the call failed, for logging/debug.</summary>
    public string? ErrorKindLabel { get; init; }

    public bool IsSuccess => Status == LlmInsightStatus.Success;
    /// <summary>True when the result is a failure that should render with error styling.</summary>
    public bool IsError => Status is LlmInsightStatus.Error or LlmInsightStatus.NotFound;
}