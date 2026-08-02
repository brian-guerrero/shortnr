using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using ModelContextProtocol.AspNetCore;
using Shortnr.Web.Services;

namespace Shortnr.Web.Extensions;

/// <summary>
/// Maps the Model Context Protocol endpoint at <c>/mcp</c>. The endpoint requires
/// the "mcp" policy (an API key carrying at least one <c>mcp:*</c> scope); individual
/// tools enforce read vs write scope themselves from the authenticated principal.
/// </summary>
public static class McpEndpoints
{
    public static void MapMcpEndpoints(this WebApplication app)
    {
        app.MapMcp("/mcp")
            .RequireAuthorization("mcp")
            .RequireRateLimiting("mcp-tools");
    }
}
