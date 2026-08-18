namespace Shortnr.Web.Features.Theming;

/// <summary>
/// Theming has no runtime services today — <see cref="ThemeCatalog"/> is a static
/// vocabulary — but the module keeps the feature's composition boundary in the
/// same shape as every other feature so theme services have an obvious home.
/// </summary>
public static class ThemingModule
{
    public static IServiceCollection AddThemingFeature(this IServiceCollection services) => services;
}
