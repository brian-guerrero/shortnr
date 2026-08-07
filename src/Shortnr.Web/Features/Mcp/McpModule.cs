using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;

namespace Shortnr.Web.Features.Mcp;

public static class McpModule
{
    public static IServiceCollection AddMcpFeature(this IServiceCollection services)
    {
        services.AddMcpServer(options =>
        {
            options.ServerInstructions = "shortnr MCP server: manage short links and link-in-bio pages. Read tools require the mcp:read scope, write tools require mcp:write.";
            options.ServerInfo = new Implementation
            {
                Name = "shortnr",
                Version = "1.0.0",
                Description = "URL shortener and link-in-bio management"
            };
        })
        .WithHttpTransport(options => options.Stateless = true)
        .WithToolsFromAssembly()
        .WithResourcesFromAssembly();

        return services;
    }
}