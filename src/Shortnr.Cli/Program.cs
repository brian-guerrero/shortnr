using Shortnr.Cli.Commands;

if (args.Length == 0)
{
    PrintHelp();
    return 1;
}

var command = args[0].ToLowerInvariant();
var commandArgs = args.Skip(1).ToArray();

return command switch
{
    "shorten" => await ShortenCommand.ExecuteAsync(commandArgs),
    "list" => await ListCommand.ExecuteAsync(commandArgs),
    "stats" => await StatsCommand.ExecuteAsync(commandArgs),
    "delete" => await DeleteCommand.ExecuteAsync(commandArgs),
    "help" or "--help" or "-h" => PrintHelp(),
    _ => UnknownCommand(command)
};

static int PrintHelp()
{
    Console.WriteLine("shortnr CLI - manage your shortnr links from the command line");
    Console.WriteLine();
    Console.WriteLine("Usage: shortnr <command> [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  shorten <url> [--slug <slug>] [--domain <domain>]  Shorten a URL");
    Console.WriteLine("  list [--page <n>] [--page-size <n>]                List your short links");
    Console.WriteLine("  stats <code> [--clicks]                            Show statistics for a link");
    Console.WriteLine("  delete <code> [--force]                            Delete a short link");
    Console.WriteLine("  help                                               Show this help message");
    Console.WriteLine();
    Console.WriteLine("Configuration:");
    Console.WriteLine("  Set SHORTNR_API_KEY environment variable or create ~/.shortnr/config with:");
    Console.WriteLine("    { \"api_key\": \"snr_...\", \"base_url\": \"http://localhost:5156\" }");
    return 0;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    Console.Error.WriteLine("Run 'shortnr help' for usage information.");
    return 1;
}
