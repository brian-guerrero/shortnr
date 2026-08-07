namespace Shortnr.Web.Features.Webhooks;

public static class WebhookEventTypes
{
    public const string LinkCreated = "link.created";
    public const string LinkClicked = "link.clicked";
    public const string LinkDeleted = "link.deleted";

    public static readonly string[] All = [LinkCreated, LinkClicked, LinkDeleted];

    public static bool IsValid(string eventType) =>
        eventType is LinkCreated or LinkClicked or LinkDeleted;

    public static string Format(IEnumerable<string> eventTypes) =>
        string.Join(" ", eventTypes.Where(IsValid).Distinct().OrderBy(e => e));

    public static IReadOnlyList<string> Parse(string eventTypes)
    {
        if (string.IsNullOrWhiteSpace(eventTypes))
            return [];

        return eventTypes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(IsValid)
            .Distinct()
            .OrderBy(e => e)
            .ToList();
    }
}
