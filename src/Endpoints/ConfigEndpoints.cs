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
                        SettleDelayMs:       appConfig.AgentSettleDelayMs,
                        CoordinateMode:      appConfig.AgentCoordinateMode.ToString(),
                        MonitorIndex:        appConfig.AgentMonitorIndex,
                        EnableContextReset:  appConfig.AgentEnableContextReset,
                        StripHistoryImages:  appConfig.AgentStripHistoryImages
                    ),
                Gemini: new GeminiConfigDto(
                    Model:                    appConfig.GeminiModel,
                    MediaResolution:          appConfig.GeminiMediaResolution.ToString(),
                    Temperature:              (double?)appConfig.GeminiTemperature,
                    TopP:                     (double?)appConfig.GeminiTopP,
                    TopK:                     appConfig.GeminiTopK,
                    CoordinateMaxOutputTokens: appConfig.GeminiCoordinateMaxOutputTokens,
                    ThinkingBudget:           appConfig.GeminiThinkingBudget,
                    ThinkingLevel:            appConfig.GeminiThinkingLevel
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
