namespace Shortnr.Web.Features.Insights;

/// <summary>
/// Configuration for the AI link insights background analysis (PRD-006).
/// Reads from the <c>AiInsights</c> config section; the feature is off by
/// default and only registers the hosted service when <see cref="Enabled"/>
/// is true.
/// </summary>
public class AiInsightsOptions
{
    public bool Enabled { get; set; }
    /// <summary>How often (in hours) the analysis pass runs. Default 24.</summary>
    public int AnalysisIntervalHours { get; set; } = 24;
    /// <summary>
    /// Links with fewer clicks than this are skipped entirely. Default 10.
    /// </summary>
    public int MinClicksForAnalysis { get; set; } = 10;
}
