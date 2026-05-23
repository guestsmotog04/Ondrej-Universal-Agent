using Thio_Universal_Agent.AI_API.Gemini;

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
