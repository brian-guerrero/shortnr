using Shortnr.Cli.Services;

namespace Shortnr.Cli.Commands;

public static class DeleteCommand
{
    public static async Task<int> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: shortnr delete <code> [--force]");
            return 1;
        }

        var code = args[0];
        var force = args.Contains("--force");

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

        if (!force)
        {
            Console.Write($"Delete short link '{code}'? This cannot be undone. [y/N] ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response != "y" && response != "yes")
            {
                Console.WriteLine("Cancelled.");
                return 0;
            }
        }

        using var http = new HttpClient { BaseAddress = new Uri(config.BaseUrl!) };
        var client = new ShortnrClient(http, config.ApiKey!);

        var result = await client.DeleteLinkAsync(code, cts.Token);
        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"Error: {result.Error}");
            return 1;
        }

        Console.WriteLine($"Deleted short link '{code}'.");
        return 0;
    }
}
