namespace Shortnr.Web.Features.Workspaces;

public static class WorkspacesModule
{
    public static IServiceCollection AddWorkspacesFeature(this IServiceCollection services)
    {
        services.AddScoped<WorkspaceService>();
        services.AddScoped<WorkspaceAuthorizationService>();
        return services;
    }
}