using Microsoft.Extensions.Configuration;
using Thio_Universal_Agent.AI_API.Gemini;
using Thio_Universal_Agent.Logic;

namespace Thio_Universal_Agent;

// ── Provider config interface ─────────────────────────────────────────────────

/// <summary>
/// Common contract for every AI provider's configuration block.
/// Each concrete implementation (e.g. <see cref="GeminiConfig"/>) adds provider-specific settings.
/// </summary>
public interface IAiProviderConfig
{
    /// <summary>Identifies the provider (e.g. "Gemini", "Anthropic").</summary>
    string ProviderName { get; }

    /// <summary>REST API key. May be updated at runtime via the web UI.</summary>
    string? ApiKey { get; set; }

    /// <summary>Model identifier. May be updated at runtime via the web UI.</summary>
    string Model { get; set; }
}

// ── Gemini ────────────────────────────────────────────────────────────────────

/// <summary>Configuration for the Google Gemini provider.</summary>
public class GeminiConfig : IAiProviderConfig
{
    public string ProviderName => "Gemini";

    /// <inheritdoc/>
    public string? ApiKey { get; set; }

    /// <inheritdoc/>
    public string Model { get; set; } = "gemini-flash-latest";

    public GeminiMediaResolution MediaResolution { get; set; } = GeminiMediaResolution.High;
    public float? Temperature { get; set; }
    public float? TopP { get; set; }
    public int?   TopK { get; set; }
    public int?   MaxOutputTokens { get; set; }

    /// <summary>Token limit applied only to coordinate-finding requests (typically much smaller than the main limit).</summary>
    public int?   CoordinateMaxOutputTokens { get; set; }

    public int?   ThinkingBudget { get; set; }
    public string? ThinkingLevel { get; set; }

    // ── Constructors ──────────────────────────────────────────────────────────

    /// <summary>Creates a <see cref="GeminiConfig"/> with all default values.</summary>
    public GeminiConfig() { }

    /// <summary>Creates a <see cref="GeminiConfig"/> loaded from a <c>Gemini</c> configuration section.</summary>
    public GeminiConfig(IConfigurationSection section)
    {
        ApiKey = section["ApiKey"] is { } k && !string.IsNullOrWhiteSpace(k) ? k : null;
        Model  = section["Model"] ?? Model;

        if (Enum.TryParse<GeminiMediaResolution>(section["MediaResolution"], ignoreCase: true, out var res))
            MediaResolution = res;

        if (float.TryParse(section["Temperature"], out var temp))           Temperature = temp;
        if (float.TryParse(section["TopP"], out var topP))                  TopP = topP;
        if (int.TryParse(section["TopK"], out var topK))                    TopK = topK;
        if (int.TryParse(section["MaxOutputTokens"], out var mOut))         MaxOutputTokens = mOut;
        if (int.TryParse(section["CoordinateMaxOutputTokens"], out var cOut)) CoordinateMaxOutputTokens = cOut;
        if (int.TryParse(section["ThinkingBudget"], out var tb))            ThinkingBudget = tb;
        ThinkingLevel = section["ThinkingLevel"];
    }
}

// ── Agent ─────────────────────────────────────────────────────────────────────

/// <summary>Configuration for the agent loop and session behaviour.</summary>
public class AgentConfig
{
    public int AgentSettleDelayMs { get; set; } = 1500;
    public CoordinateMode CoordinateMode { get; set; } = CoordinateMode.DirectAutoNormalize;

    /// <summary>Zero-based monitor index, or <c>null</c> for all-monitors mode. May be updated at runtime per session.</summary>
    public int? MonitorIndex { get; set; }

    public bool EnableContextReset { get; set; } = true;
    public bool StripHistoryImages { get; set; } = true;

    // ── Constructors ──────────────────────────────────────────────────────────

    /// <summary>Creates an <see cref="AgentConfig"/> with all default values.</summary>
    public AgentConfig() { }

    /// <summary>Creates an <see cref="AgentConfig"/> loaded from an <c>Agent</c> configuration section.</summary>
    public AgentConfig(IConfigurationSection section)
    {
        if (int.TryParse(section["SettleDelayMs"], out var d) && d > 0)
            AgentSettleDelayMs = d;

        if (Enum.TryParse<CoordinateMode>(section["CoordinateMode"], ignoreCase: true, out var coordMode))
            CoordinateMode = coordMode;

        MonitorIndex = section.GetValue<int?>("MonitorIndex");

        if (bool.TryParse(section["EnableContextReset"], out var r)) EnableContextReset = r;
        if (bool.TryParse(section["StripHistoryImages"], out var s)) StripHistoryImages = s;
    }
}

// ── AppConfig (root) ──────────────────────────────────────────────────────────

/// <summary>
/// Central, strongly-typed store for all application configuration.
/// Loaded once from <see cref="IConfiguration"/> at startup and registered as a singleton.
/// Sub-objects (<see cref="Gemini"/>, <see cref="Agent"/>) hold provider- and feature-specific settings.
/// Mutable properties on those sub-objects may be updated at runtime by endpoint handlers.
/// </summary>
public class AppConfig
{
    public GeminiConfig Gemini { get; set; } = new();
    public AgentConfig  Agent  { get; set; } = new();

    // ── Constructors ──────────────────────────────────────────────────────────

    /// <summary>Creates an <see cref="AppConfig"/> with all default values (no settings loaded).</summary>
    public AppConfig() { }

    /// <summary>Creates an <see cref="AppConfig"/> and loads values from <paramref name="configuration"/>.</summary>
    public AppConfig(IConfiguration configuration)
    {
        Gemini = new GeminiConfig(configuration.GetSection("Gemini"));
        Agent  = new AgentConfig(configuration.GetSection("Agent"));
    }
}
