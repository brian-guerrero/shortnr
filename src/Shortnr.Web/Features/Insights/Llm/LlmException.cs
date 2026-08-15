namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>Category of LLM failure used for friendly messages and usage logging.</summary>
public enum LlmErrorKind
{
    None = 0,
    Network = 1,
    Timeout = 2,
    Auth = 3,
    RateLimit = 4,
    PaymentRequired = 5,
    BadRequest = 6,
    Upstream = 7,
    BudgetExceeded = 8,
    Disabled = 9,
    NotFound = 10,
    NotConfigured = 11,
    InvalidResponse = 12
}

/// <summary>Thrown by <see cref="LlmClient"/> when a provider call fails.</summary>
public sealed class LlmException : Exception
{
    public LlmErrorKind Kind { get; }

    public LlmException(string message, LlmErrorKind kind, Exception? inner = null) : base(message, inner)
    {
        Kind = kind;
    }

    /// <summary>Friendly, non-technical copy shown in the /insights UI.</summary>
    public string FriendlyMessage => Kind switch
    {
        LlmErrorKind.Network => "Couldn't reach the AI provider. Check your network and that BaseUrl is correct.",
        LlmErrorKind.Timeout => "The AI provider took too long to respond. Try again.",
        LlmErrorKind.Auth => "The AI provider rejected the API key. Check AiInsights:Llm:ApiKey.",
        LlmErrorKind.RateLimit => "The AI provider is rate-limited right now. Try again in a minute.",
        LlmErrorKind.PaymentRequired => "The AI provider needs a funded account or more credits.",
        LlmErrorKind.BadRequest => "The AI provider rejected the request. Check that the configured model exists for this provider.",
        LlmErrorKind.NotConfigured => "AI insights aren't configured. Set AiInsights:Llm:Enabled, an ApiKey and a Model.",
        LlmErrorKind.InvalidResponse => "The AI provider returned an unreadable response.",
        _ => "The AI provider returned an error. Showing deterministic insights instead."
    };
}