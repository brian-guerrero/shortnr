using Shortnr.Cli.Services;

namespace Shortnr.Cli.Commands;

public static class ShortenCommand
{
    public static async Task<int> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: shortnr shorten <url> [--slug <slug>] [--domain <domain>]");
            return 1;
        }

        var url = args[0];
        string? slug = null;
        string? domain = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--slug" when i + 1 < args.Length:
                    slug = args[++i];
                    break;
                case "--domain" when i + 1 < args.Length:
                    domain = args[++i];
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

        var result = await client.CreateLinkAsync(url, slug, domain, cts.Token);
        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"Error: {result.Error}");
            return 1;
        }

        var link = result.Value;
        Console.WriteLine(link!.ShortUrl);
        return 0;
    }
}
