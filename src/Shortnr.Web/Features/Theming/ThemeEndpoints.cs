using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Web.Features.Authentication;

namespace Shortnr.Web.Features.Theming;

/// <summary>
/// Maps <c>POST /theme/switch</c> — persists the current user's app-wide
/// theme preference (<see cref="Shortnr.Data.Entities.User.PreferredTheme"/>),
/// used by the command palette's Theme group
/// (<c>Pages/Shared/_CommandPalette.cshtml</c>). Gated the same way
/// <c>/workspace/switch</c> is, since the preference lives on the <c>User</c>
/// row and needs a resolvable owner.
/// </summary>
public static class ThemeEndpoints
{
    public static IEndpointRouteBuilder MapThemeEndpoints(this IEndpointRouteBuilder app, IConfiguration config)
    {
        if (!config.GetValue<bool>("Authentication:Enabled", defaultValue: true))
            return app;

        app.MapPost("/theme/switch", async (
            [FromForm] string themeId,
            HttpContext ctx,
            IUserIdentityService identity,
            IThemeResolver themeResolver,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = await identity.ResolveOwnerUserIdAsync(ctx.User);
            if (userId is null)
                return Results.Unauthorized();

            // Unknown/deleted ids fall back to Default rather than erroring —
            // same fallback every other theme consumer (IThemeResolver.ResolveAsync) gets.
            var theme = await themeResolver.ResolveAsync(themeId, ct);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
            if (user is not null)
            {
                // Store null rather than "default" so "reset to default" doesn't
                // leave a stale non-null value behind.
                user.PreferredTheme = theme.Id == ThemeCatalog.DefaultId ? null : theme.Id;
                await db.SaveChangesAsync(ct);
            }

            return Results.Redirect("/");
        // No antiforgery token: cross-origin CSRF is blocked by the SameSite=Lax
        // session cookie (browsers never attach it to a cross-site POST, so the
        // forged request hits the 401 above), and this is an idempotent,
        // owner-validated preference switch. See /workspace/switch for the same
        // reasoning (AuthenticationEndpointExtensions.cs).
        }).DisableAntiforgery();

        return app;
    }
}
