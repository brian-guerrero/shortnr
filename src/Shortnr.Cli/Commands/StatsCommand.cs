using Shortnr.Cli.Services;

namespace Shortnr.Cli.Commands;

public static class StatsCommand
{
    public static async Task<int> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: shortnr stats <code> [--clicks]");
            return 1;
        }

        var code = args[0];
        var showClicks = args.Contains("--clicks");

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

        var linkResult = await client.GetLinkAsync(code, cts.Token);
        if (!linkResult.IsSuccess)
        {
            Console.Error.WriteLine($"Error: {linkResult.Error}");
            return 1;
        }

        var link = linkResult.Value;
        Console.WriteLine($"Short URL:   {link!.ShortUrl}");
        Console.WriteLine($"Long URL:    {link.LongUrl}");
        Console.WriteLine($"Short Code:  {link.ShortCode}");
        Console.WriteLine($"Domain:      {link.Domain ?? "(default)"}");
        Console.WriteLine($"Clicks:      {link.ClickCount}");
        Console.WriteLine($"Created:     {link.CreatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        if (link.Workspace is not null)
            Console.WriteLine($"Workspace:   {link.Workspace}");

        if (showClicks && link.ClickCount > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Recent clicks:");

            var clicksResult = await client.GetClicksAsync(code, 1, 10, cts.Token);
            if (clicksResult.IsSuccess && clicksResult.Value?.Clicks.Count > 0)
            {
                Console.WriteLine($"{"Time",-20} {"Browser",-15} {"OS",-15} {"Location"}");
                Console.WriteLine(new string('-', 70));

                foreach (var click in clicksResult.Value.Clicks)
                {
                    var time = click.ClickedAtUtc.ToString("yyyy-MM-dd HH:mm");
                    var browser = click.Browser ?? "-";
                    var os = click.OperatingSystem ?? "-";
                    var location = click.CityName ?? click.CountryName ?? "-";
                    Console.WriteLine($"{time,-20} {browser,-15} {os,-15} {location}");
                }
            }
        }

        return 0;
    }
}
