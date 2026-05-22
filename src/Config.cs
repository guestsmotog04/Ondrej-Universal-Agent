using Microsoft.Extensions.Configuration;
using Thio_Universal_Agent.AI_API.Gemini;
using Thio_Universal_Agent.Logic;

namespace Thio_Universal_Agent;

/// <summary>
/// Central, strongly-typed store for all application configuration.
/// Loaded once from <see cref="IConfiguration"/> at startup and registered as a singleton.
/// Mutable properties (e.g. <see cref="GeminiApiKey"/>, <see cref="GeminiModel"/>,
/// <see cref="AgentMonitorIndex"/>) may be updated at runtime by endpoint handlers.
/// </summary>
public class AppConfig
{
    // ── Gemini ────────────────────────────────────────────────────────────────

    /// <summary>Gemini REST API key. May be set at runtime via the web UI.</summary>
    public string? GeminiApiKey { get; set; }

    /// <summary>Gemini model name (e.g. "gemini-2.0-flash"). May be set at runtime via the web UI.</summary>
    public string GeminiModel { get; set; } = "gemini-flash-latest";

    public GeminiMediaResolution GeminiMediaResolution { get; set; } = GeminiMediaResolution.High;
    public float? GeminiTemperature { get; set; }
    public float? GeminiTopP { get; set; }
    public int?   GeminiTopK { get; set; }
    public int?   GeminiMaxOutputTokens { get; set; }
    public int?   GeminiCoordinateMaxOutputTokens { get; set; }
    public int?   GeminiThinkingBudget { get; set; }
    public string? GeminiThinkingLevel { get; set; }

    // ── Agent ─────────────────────────────────────────────────────────────────

    public int AgentSettleDelayMs { get; set; } = 1500;
    public CoordinateMode AgentCoordinateMode { get; set; } = CoordinateMode.DirectAutoNormalize;

    /// <summary>Zero-based monitor index, or <c>null</c> for all-monitors mode. May be set at runtime per session.</summary>
    public int? AgentMonitorIndex { get; set; }

    public bool AgentEnableContextReset { get; set; } = true;
    public bool AgentStripHistoryImages { get; set; } = true;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>Creates an <see cref="AppConfig"/> with all default values (no settings loaded).</summary>
    public AppConfig() { }

    /// <summary>Creates an <see cref="AppConfig"/> and loads values from <paramref name="configuration"/>.</summary>
    public AppConfig(IConfiguration configuration)
    {
        // Gemini
        GeminiApiKey = configuration["Gemini:ApiKey"] is { } k && !string.IsNullOrWhiteSpace(k) ? k : null;
        GeminiModel  = configuration["Gemini:Model"] ?? GeminiModel;

        if (Enum.TryParse<GeminiMediaResolution>(configuration["Gemini:MediaResolution"], ignoreCase: true, out var res))
            GeminiMediaResolution = res;

        if (float.TryParse(configuration["Gemini:Temperature"], out var temp))  GeminiTemperature = temp;
        if (float.TryParse(configuration["Gemini:TopP"], out var topP))          GeminiTopP = topP;
        if (int.TryParse(configuration["Gemini:TopK"], out var topK))            GeminiTopK = topK;
        if (int.TryParse(configuration["Gemini:MaxOutputTokens"], out var mOut)) GeminiMaxOutputTokens = mOut;
        if (int.TryParse(configuration["Gemini:CoordinateMaxOutputTokens"], out var cOut)) GeminiCoordinateMaxOutputTokens = cOut;
        if (int.TryParse(configuration["Gemini:ThinkingBudget"], out var tb))   GeminiThinkingBudget = tb;
        GeminiThinkingLevel = configuration["Gemini:ThinkingLevel"];

        // Agent
        if (int.TryParse(configuration["Agent:SettleDelayMs"], out var d) && d > 0)
            AgentSettleDelayMs = d;

        if (Enum.TryParse<CoordinateMode>(configuration["Agent:CoordinateMode"], ignoreCase: true, out var coordMode))
            AgentCoordinateMode = coordMode;

        AgentMonitorIndex = configuration.GetValue<int?>("Agent:MonitorIndex");

        if (bool.TryParse(configuration["Agent:EnableContextReset"], out var r)) AgentEnableContextReset = r;
        if (bool.TryParse(configuration["Agent:StripHistoryImages"], out var s)) AgentStripHistoryImages = s;
    }
}
