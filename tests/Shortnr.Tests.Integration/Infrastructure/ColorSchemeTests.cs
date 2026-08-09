using Shortnr.Tests.Integration.Infrastructure;

namespace Shortnr.Tests.Integration.Infrastructure;

/// <summary>
/// Verifies the colour-scheme layer: the app shell ships the toggle and the
/// no-flash resolver, and public bio pages opt out of it.
/// </summary>
public class ColorSchemeTests : IAsyncLifetime
{
    private readonly ShortnrWebAppFactory _factory = new(authEnabled: false);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task Layout_RendersTheSchemeToggle()
    {
        var html = await _factory.CreateClient().GetStringAsync("/");

        Assert.Contains("data-scheme-toggle", html);
        Assert.Contains("aria-label=\"Switch to dark theme\"", html);
    }

    [Fact]
    public async Task Layout_ResolvesTheSchemeBeforeTheStylesheetPaints()
    {
        var html = await _factory.CreateClient().GetStringAsync("/");

        // The resolver has to be inline and ahead of <body>, or the page paints
        // in the wrong scheme first and flashes.
        var resolver = html.IndexOf("sn-color-scheme", StringComparison.Ordinal);
        var body = html.IndexOf("<body", StringComparison.Ordinal);

        Assert.InRange(resolver, 0, body);
        Assert.Contains("prefers-color-scheme: dark", html);
    }

    [Fact]
    public async Task BioPage_PinsTheLightScheme()
    {
        // The bio theme is the page owner's choice, not the visitor's, so a
        // visitor on a dark OS must not repaint someone's bio page. The
        // not-found body renders through the same shell, so it is enough to
        // assert the opt-out without seeding a page.
        var response = await _factory.CreateClient().GetAsync("/bio/nobody");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-color-scheme=\"light\"", html);
        Assert.DoesNotContain("data-scheme-toggle", html);
    }
}
