using Thio_Universal_Agent;

namespace Thio_Universal_Agent.Endpoints;

/// <summary>
/// Exposes a read-only snapshot of the server configuration (appsettings.json) to the web UI.
/// Sensitive values such as API keys are intentionally excluded.
/// </summary>
internal static class ConfigEndpoints
{
    internal static void MapConfigEndpoints(this WebApplication app)
    {
        app.MapGet("/api/config", (AppConfig appConfig) =>
        {
            var response = new AppConfigResponse(
                Agent: new AgentConfigDto(
                        SettleDelayMs:       appConfig.Agent.AgentSettleDelayMs,
                        CoordinateMode:      appConfig.Agent.CoordinateMode.ToString(),
                        MonitorIndex:        appConfig.Agent.MonitorIndex,
                        EnableContextReset:  appConfig.Agent.EnableContextReset,
                        StripHistoryImages:  appConfig.Agent.StripHistoryImages
                    ),
                Gemini: new GeminiConfigDto(
                    Model:                    appConfig.Gemini.Model,
                    MediaResolution:          appConfig.Gemini.MediaResolution.ToString(),
                    Temperature:              (double?)appConfig.Gemini.Temperature,
                    TopP:                     (double?)appConfig.Gemini.TopP,
                    TopK:                     appConfig.Gemini.TopK,
                    CoordinateMaxOutputTokens: appConfig.Gemini.CoordinateMaxOutputTokens,
                    ThinkingBudget:           appConfig.Gemini.ThinkingBudget,
                    ThinkingLevel:            appConfig.Gemini.ThinkingLevel
                )
            );

            return Results.Ok(response);
        });
    }
}

// ---- DTOs ---------------------------------------------------------------

internal sealed record AppConfigResponse(
    AgentConfigDto  Agent,
    GeminiConfigDto Gemini
);

internal sealed record AgentConfigDto(
    int     SettleDelayMs,
    string? CoordinateMode,
    int?    MonitorIndex,
    bool    EnableContextReset,
    bool    StripHistoryImages
);

internal sealed record GeminiConfigDto(
    string? Model,
    string? MediaResolution,
    double? Temperature,
    double? TopP,
    int?    TopK,
    int?    CoordinateMaxOutputTokens,
    int?    ThinkingBudget,
    string? ThinkingLevel
);
