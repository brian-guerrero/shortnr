using Shortnr.Cli.Services;

namespace Shortnr.Cli.Commands;

public static class ListCommand
{
    public static async Task<int> ExecuteAsync(string[] args)
    {
        int? page = null;
        int? pageSize = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--page" when i + 1 < args.Length && int.TryParse(args[i + 1], out var p):
                    page = p;
                    i++;
                    break;
                case "--page-size" when i + 1 < args.Length && int.TryParse(args[i + 1], out var ps):
                    pageSize = ps;
                    i++;
                    break;
            }
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var config = ConfigLoader.Load();
        if (!ConfigLoader.HasApiKey(config))
        {
            Console.Error.WriteLine("Error: API key not configured.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Set SHORTNR_API_KEY environment variable or create ~/.shortnr/config with:");
            Console.Error.WriteLine("  { \"api_key\": \"snr_...\" }");
            return 1;
        }

        using var http = new HttpClient { BaseAddress = new Uri(config.BaseUrl!) };
        var client = new ShortnrClient(http, config.ApiKey!);

        var result = await client.ListLinksAsync(page, pageSize, cts.Token);
        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"Error: {result.Error}");
            return 1;
        }

        var list = result.Value;
        if (list!.Links.Count == 0)
        {
            Console.WriteLine("No links found.");
            return 0;
        }

        Console.WriteLine($"{"Short Code",-12} {"Clicks",8} {"Created",20} {"Short URL"}");
        Console.WriteLine(new string('-', 80));

        foreach (var link in list.Links)
        {
            var created = link.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm");
            Console.WriteLine($"{link.ShortCode,-12} {link.ClickCount,8} {created,20} {link.ShortUrl}");
        }

        Console.WriteLine();
        Console.WriteLine($"Page {list.Page} of {((list.Total + list.PageSize - 1) / list.PageSize)} ({list.Total} total)");
        return 0;
    }
}
