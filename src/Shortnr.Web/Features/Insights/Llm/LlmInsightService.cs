using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Shortnr.Web.Features.Insights.Llm;

/// <summary>
/// The request-path gateway for the PRD-023 LLM layer. Owns the flow every
/// analysis endpoint shares: config gate → monthly-budget check → provider call
/// → usage recording → typed result. Provider failures never escape this class;
/// they're turned into a friendly <see cref="LlmInsightResult"/> so the /insights
/// page keeps rendering and the deterministic PRD-006 insights stay visible.
/// Short-circuits (disabled, unconfigured, over budget) never hit the network.
/// </summary>
public class LlmInsightService(
    IChatClient chatClient,
    LlmUsageService usage,
    LlmPricing pricing,
    IOptions<LlmOptions> options,
    ILogger<LlmInsightService> logger)
{
    private LlmOptions Options => options.Value;

    public bool IsEnabled => Options.Enabled;
    public string Model => Options.Model;
    public string Provider => Options.Provider.ToString();
    public decimal MonthlyBudget => Options.MonthlyBudget;

    public async Task<LlmInsightResult> CompleteAsync(LlmRequest request, long? ownerUserId, CancellationToken ct = default)
    {
        if (!IsEnabled)
            return Disabled("AI insights are not enabled. Set AiInsights:Llm:Enabled to true and add an API key and model.");

        if (string.IsNullOrWhiteSpace(Options.Model))
            return new LlmInsightResult
            {
                Status = LlmInsightStatus.Error,
                FriendlyMessage = "No AI model is configured. Set AiInsights:Llm:Model (e.g. gpt-4o-mini) to enable AI insights.",
                ErrorKindLabel = LlmErrorKind.NotConfigured.ToString()
            };

        if (!await usage.IsWithinBudgetAsync(request, ct))
        {
            logger.LogWarning("LLM {Operation} blocked: monthly budget of {Budget:C} reached", request.Operation, Options.MonthlyBudget);
            return new LlmInsightResult
            {
                Status = LlmInsightStatus.BudgetExceeded,
                FriendlyMessage = "This instance's AI monthly budget has been reached. Calls resume at the start of next month."
            };
        }

        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, request.SystemPrompt),
                new(ChatRole.User, request.UserPrompt)
            };
            var chatOptions = new ChatOptions
            {
                ModelId = request.Model,
                Temperature = (float)request.Temperature,
                MaxOutputTokens = request.MaxTokens
            };
            var response = await chatClient.GetResponseAsync(messages, chatOptions, ct);

            var promptTokens = (int)(response.Usage?.InputTokenCount ?? 0);
            var completionTokens = (int)(response.Usage?.OutputTokenCount ?? 0);
            var modelUsed = response.ModelId ?? request.Model;

            var cost = pricing.EstimateCost(promptTokens, completionTokens, modelUsed);
            await usage.RecordAsync(new LlmUsageRecord(
                Options.Provider.ToString(), modelUsed, request.Operation.ToString(),
                promptTokens, completionTokens, cost, true, null, ownerUserId), ct);

            return new LlmInsightResult { Status = LlmInsightStatus.Success, Content = response.Text };
        }
        catch (LlmException ex)
        {
            logger.LogWarning(ex, "LLM {Operation} failed with {Kind}", request.Operation, ex.Kind);
            await RecordFailureAsync(request, ex.Kind, ownerUserId, ct);
            return Failure(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LLM {Operation} failed unexpectedly", request.Operation);
            return new LlmInsightResult
            {
                Status = LlmInsightStatus.Error,
                FriendlyMessage = "Something went wrong while contacting the AI provider. Showing deterministic insights instead.",
                ErrorKindLabel = "unexpected"
            };
        }
    }

    private static LlmInsightResult Disabled(string message) => new()
    {
        Status = LlmInsightStatus.Disabled,
        FriendlyMessage = message
    };

    private static LlmInsightResult Failure(LlmException ex) => new()
    {
        Status = LlmInsightStatus.Error,
        FriendlyMessage = ex.FriendlyMessage,
        ErrorKindLabel = ex.Kind.ToString()
    };

    private async Task RecordFailureAsync(LlmRequest request, LlmErrorKind kind, long? ownerUserId, CancellationToken ct)
    {
        try
        {
            await usage.RecordAsync(new LlmUsageRecord(
                Options.Provider.ToString(), request.Model, request.Operation.ToString(),
                0, 0, 0, false, kind.ToString(), ownerUserId), ct);
        }
        catch (Exception recordEx)
        {
            logger.LogWarning(recordEx, "Failed to record LLM usage for {Operation}", request.Operation);
        }
    }
}