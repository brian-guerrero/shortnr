using Microsoft.AspNetCore.Http;
using Shortnr.Web.Features.Infrastructure;

namespace Shortnr.Tests.Unit.Services;

/// <summary>
/// Covers the logic behind the shared UI partials in <c>Pages/Shared</c> — the parts that used to
/// be copy-pasted into each view.
/// </summary>
public class UiComponentViewModelTests
{
    [Fact]
    public void ActionForm_DangerButton_GetsAConfirmEvenWhenNoneSupplied()
    {
        var form = new ActionFormViewModel
        {
            PostUrl = "/settings/domains?handler=Delete",
            Target = "#domains-list",
            ButtonLabel = "Delete",
            ButtonStyle = ActionButtonStyle.Danger,
        };

        Assert.Equal(ActionFormViewModel.DefaultDangerConfirm, form.ResolvedConfirm);
    }

    [Fact]
    public void ActionForm_DangerButton_PrefersTheSuppliedConfirm()
    {
        var form = new ActionFormViewModel
        {
            PostUrl = "/x",
            Target = "#y",
            ButtonLabel = "Delete",
            ButtonStyle = ActionButtonStyle.Danger,
            ConfirmMessage = "Delete the domain 'go.example.com'?",
        };

        Assert.Equal("Delete the domain 'go.example.com'?", form.ResolvedConfirm);
    }

    [Theory]
    [InlineData(ActionButtonStyle.Primary)]
    [InlineData(ActionButtonStyle.Secondary)]
    public void ActionForm_NonDangerButton_HasNoConfirm(ActionButtonStyle style)
    {
        var form = new ActionFormViewModel
        {
            PostUrl = "/x",
            Target = "#y",
            ButtonLabel = "Archive",
            ButtonStyle = style,
        };

        Assert.Null(form.ResolvedConfirm);
    }

    [Theory]
    [InlineData(ActionButtonStyle.Primary, true, "btn-sm")]
    [InlineData(ActionButtonStyle.Secondary, true, "secondary btn-sm")]
    [InlineData(ActionButtonStyle.Danger, false, "danger")]
    public void ActionForm_ButtonClasses_ComposeStyleAndSize(
        ActionButtonStyle style, bool small, string expected)
    {
        var form = new ActionFormViewModel
        {
            PostUrl = "/x",
            Target = "#y",
            ButtonLabel = "Go",
            ButtonStyle = style,
            Small = small,
        };

        Assert.Equal(expected, form.ButtonClasses);
    }

    [Theory]
    [InlineData(StatusKind.Error, "status-error")]
    [InlineData(StatusKind.Success, "status-success")]
    [InlineData(StatusKind.Info, "status-info")]
    [InlineData(StatusKind.Neutral, "status-neutral")]
    public void Badge_MapsKindOntoTheStatusCssClass(StatusKind kind, string expected)
    {
        var badge = new BadgeViewModel { Kind = kind, Text = "x" };

        Assert.Equal(expected, badge.CssClasses);
    }

    [Fact]
    public void Badge_PlainKind_HasNoStatusClass()
    {
        var badge = new BadgeViewModel { Kind = StatusKind.Plain, Text = "hidden" };

        Assert.Equal("", badge.CssClasses);
    }

    [Fact]
    public void Badge_ComposesPreserveCaseAndExtraClass()
    {
        var badge = new BadgeViewModel
        {
            Kind = StatusKind.Info,
            Text = "default",
            PreserveCase = true,
            CssClass = "badge-gap",
        };

        Assert.Equal("status-info preserve-case badge-gap", badge.CssClasses);
    }

    [Fact]
    public void TableSort_InactiveColumn_SortsAscendingAndReportsNoAriaSort()
    {
        var header = SortState(currentSort: "clickCount", currentDir: "asc").Header("shortCode", "Short URL");

        Assert.False(header.IsActive);
        Assert.Equal("none", header.AriaSort);
        Assert.Equal("/dashboard?linkSort=shortCode&linkDir=asc", header.Url);
    }

    [Fact]
    public void TableSort_ActiveAscendingColumn_TogglesToDescending()
    {
        var header = SortState(currentSort: "clickCount", currentDir: "asc").Header("clickCount", "Clicks");

        Assert.True(header.IsActive);
        Assert.True(header.IsAscending);
        Assert.Equal("ascending", header.AriaSort);
        Assert.Equal("/dashboard?linkSort=clickCount&linkDir=desc", header.Url);
    }

    [Fact]
    public void TableSort_ActiveDescendingColumn_TogglesBackToAscending()
    {
        var header = SortState(currentSort: "clickCount", currentDir: "desc").Header("clickCount", "Clicks");

        Assert.False(header.IsAscending);
        Assert.Equal("descending", header.AriaSort);
        Assert.Equal("/dashboard?linkSort=clickCount&linkDir=asc", header.Url);
    }

    [Fact]
    public void TableSort_PreservesExtraQueryAcrossASort()
    {
        var state = new TableSortState
        {
            SortParam = "clickSort",
            DirParam = "clickDir",
            BaseUrl = "/dashboard",
            Target = "#recent-clicks",
            ExtraQuery = new Dictionary<string, string?> { ["clickLimit"] = "20" },
        };

        Assert.Equal("/dashboard?clickSort=browser&clickDir=asc&clickLimit=20", state.Header("browser", "Browser").Url);
    }

    [Fact]
    public void TableSort_FromQuery_ReadsCurrentSortOffTheRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?linkSort=domain&linkDir=desc");

        var state = TableSortState.FromQuery(
            context.Request, "linkSort", "linkDir", "/dashboard", "#search-results");

        Assert.Equal("domain", state.CurrentSort);
        Assert.Equal("desc", state.CurrentDir);
        Assert.True(state.Header("domain", "Domain").IsActive);
    }

    private static TableSortState SortState(string currentSort, string currentDir) => new()
    {
        SortParam = "linkSort",
        DirParam = "linkDir",
        BaseUrl = "/dashboard",
        Target = "#search-results",
        CurrentSort = currentSort,
        CurrentDir = currentDir,
    };
}
