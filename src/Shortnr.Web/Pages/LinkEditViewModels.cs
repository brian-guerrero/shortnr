using Shortnr.Data.Entities;

namespace Shortnr.Web.Pages;

public class LinkEditViewModel
{
    public long Code { get; init; }
    public string LongUrl { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Tags { get; init; } = "";
    public string? ErrorMessage { get; init; }

    public static LinkEditViewModel From(ShortenedUrl link, string? errorMessage = null) => new()
    {
        Code = link.Id,
        LongUrl = link.LongUrl,
        Slug = link.ShortCode,
        Title = link.Title ?? "",
        Description = link.Description ?? "",
        Tags = string.Join(", ", link.Tags?.Select(t => t.Name) ?? []),
        ErrorMessage = errorMessage
    };
}

public class LinkEditSuccessViewModel
{
    public List<ShortenedUrl> Links { get; init; } = [];
    public string Message { get; init; } = "";
}

public class LinkTransferViewModel
{
    public long Code { get; init; }
    public string CurrentWorkspace { get; init; } = "personal";
    public List<Workspace> Workspaces { get; init; } = [];
    public string? ErrorMessage { get; init; }
}

public class LinkTransferSuccessViewModel
{
    public List<ShortenedUrl> Links { get; init; } = [];
    public string Message { get; init; } = "";
}
