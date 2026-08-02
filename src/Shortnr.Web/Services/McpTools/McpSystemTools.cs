using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Shortnr.Web.Services.McpTools;

/// <summary>
/// System-level MCP tools. <c>ping</c> lets agents verify the endpoint is reachable
/// and that the authenticated principal made it into the tool pipeline (stateless
/// HTTP transport). Read tools are added in the read-tools layer.
/// </summary>
[McpServerToolType]
public static class McpSystemTools
{
    [McpServerTool(Name = "ping", Title = "Ping", ReadOnly = true)]
    public static string Ping(McpServer server, RequestContext<CallToolRequestParams> context) =>
        context.User.Identity?.IsAuthenticated == true ? "pong" : "unauthenticated";
}
