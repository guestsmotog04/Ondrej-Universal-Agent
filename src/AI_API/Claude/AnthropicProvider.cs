// src/AI_API/Anthropic/AnthropicProvider.cs
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thio_Universal_Agent.AI_API.Anthropic;

/// <summary>
/// Anthropic REST API implementation of <see cref="IAiProvider"/>.
/// Automatically handles the alternating User/Assistant role requirement.
/// </summary>
public sealed class AnthropicProvider(HttpClient httpClient, AppConfig appConfig, ILogger<AnthropicProvider> logger) : IAiProvider
{
    private const string BaseUrl = "https://api.anthropic.com/v1/messages";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string? _apiKey = appConfig.Anthropic.ApiKey;
    private readonly string _model = appConfig.Anthropic.Model;

    public Task<AiResponse> SendPromptAsync(string prompt, CancellationToken cancellationToken = default, AiRequestOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var request = new AnthropicRequest(_model, [new AnthropicMessage("user", [new AnthropicContentPart("text", prompt, null)])], appConfig.Anthropic.Temperature, options?.MaxOutputTokens ?? appConfig.Anthropic.MaxOutputTokens ?? 4096);
        return SendRequestAsync(request, cancellationToken);
    }

    public Task<AiResponse> SendPromptWithImageAsync(string prompt, byte[] imageBytes, string mimeType = "image/jpeg", CancellationToken cancellationToken = default, AiRequestOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(imageBytes);

        var request = new AnthropicRequest(_model, [new AnthropicMessage("user", [
            new AnthropicContentPart("image", null, new AnthropicImageSource("base64", mimeType, Convert.ToBase64String(imageBytes))),
            new AnthropicContentPart("text", prompt, null)
        ])], appConfig.Anthropic.Temperature, options?.MaxOutputTokens ?? appConfig.Anthropic.MaxOutputTokens ?? 4096);

        return SendRequestAsync(request, cancellationToken);
    }

    public async Task<(AiConversation Conversation, AiResponse Response)> StartConversationAsync(string prompt, CancellationToken cancellationToken = default, AiRequestOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var conversation = new AiConversation();
        var userMessage = new AiChatMessage { Role = AiChatRole.User, Text = prompt };

        var request = BuildRequest(conversation, userMessage, options);
        var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Success)
        {
            conversation.AddMessage(userMessage);
            conversation.AddMessage(new AiChatMessage { Role = AiChatRole.Model, Text = response.Text });
        }

        return (conversation, response);
    }

    public Task<AiResponse> ContinueConversationAsync(AiConversation conversation, string prompt, CancellationToken cancellationToken = default, AiRequestOptions? options = null)
    {
        var userMessage = new AiChatMessage { Role = AiChatRole.User, Text = prompt };
        return ContinueConversationCoreAsync(conversation, userMessage, cancellationToken, options);
    }

    public Task<AiResponse> ContinueConversationAsync(AiConversation conversation, byte[] imageBytes, string mimeType = "image/jpeg", CancellationToken cancellationToken = default, AiRequestOptions? options = null)
    {
        var userMessage = new AiChatMessage { Role = AiChatRole.User, ImageBytes = imageBytes, MimeType = mimeType };
        return ContinueConversationCoreAsync(conversation, userMessage, cancellationToken, options);
    }

    public Task<AiResponse> ContinueConversationAsync(AiConversation conversation, string prompt, byte[] imageBytes, string mimeType = "image/jpeg", CancellationToken cancellationToken = default, AiRequestOptions? options = null)
    {
        var userMessage = new AiChatMessage { Role = AiChatRole.User, Text = prompt, ImageBytes = imageBytes, MimeType = mimeType };
        return ContinueConversationCoreAsync(conversation, userMessage, cancellationToken, options);
    }

    private async Task<AiResponse> ContinueConversationCoreAsync(AiConversation conversation, AiChatMessage userMessage, CancellationToken cancellationToken, AiRequestOptions? options)
    {
        var request = BuildRequest(conversation, userMessage, options);
        var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Success)
        {
            conversation.AddMessage(userMessage);
            conversation.AddMessage(new AiChatMessage { Role = AiChatRole.Model, Text = response.Text });
        }

        return response;
    }

    private async Task<AiResponse> SendRequestAsync(AnthropicRequest request, CancellationToken cancellationToken)
    {
        var apiKey = _apiKey ?? appConfig.Anthropic.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Anthropic:ApiKey is not configured. Provide an API key via the web UI.");

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Sending prompt to Anthropic model {Model}.", _model);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            logger.LogError("Anthropic API returned {StatusCode}. Body: {ErrorBody}", (int)response.StatusCode, errorBody);
            return new AiResponse(false, string.Empty, $"HTTP {(int)response.StatusCode}: {errorBody}");
        }

        var anthropicResponse = await response.Content.ReadFromJsonAsync<AnthropicResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        var text = anthropicResponse?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;

        if (string.IsNullOrWhiteSpace(text))
            return new AiResponse(false, string.Empty, "Anthropic returned an empty response.");

        return new AiResponse(true, text);
    }

    private AnthropicRequest BuildRequest(AiConversation conversation, AiChatMessage additionalMessage, AiRequestOptions? options)
    {
        bool stripHistoryImages = appConfig.General.StripHistoryImages;
        var messages = new List<AnthropicMessage>(conversation.Messages.Count + 1);

        foreach (var message in conversation.Messages)
            messages.Add(ToAnthropicMessage(message, stripImages: stripHistoryImages));

        messages.Add(ToAnthropicMessage(additionalMessage, stripImages: false));

        return new AnthropicRequest(_model, messages, appConfig.Anthropic.Temperature, options?.MaxOutputTokens ?? appConfig.Anthropic.MaxOutputTokens ?? 4096);
    }

    private static AnthropicMessage ToAnthropicMessage(AiChatMessage message, bool stripImages)
    {
        var role = message.Role == AiChatRole.User ? "user" : "assistant";
        var content = new List<AnthropicContentPart>();

        if (!stripImages && message.ImageBytes is not null)
            content.Add(new AnthropicContentPart("image", null, new AnthropicImageSource("base64", message.MimeType ?? "image/jpeg", Convert.ToBase64String(message.ImageBytes))));

        if (message.Text is not null)
            content.Add(new AnthropicContentPart("text", message.Text, null));

        return new AnthropicMessage(role, content);
    }

    // --- Private DTOs ---

    private record AnthropicRequest(string Model, List<AnthropicMessage> Messages, float? Temperature, [property: JsonPropertyName("max_tokens")] int MaxTokens);
    private record AnthropicMessage(string Role, List<AnthropicContentPart> Content);
    private record AnthropicContentPart(string Type, string? Text, AnthropicImageSource? Source);
    private record AnthropicImageSource(string Type, [property: JsonPropertyName("media_type")] string MediaType, string Data);
    private record AnthropicResponse(List<AnthropicContentResponse>? Content, AnthropicError? Error, [property: JsonPropertyName("stop_reason")] string? StopReason);
    private record AnthropicContentResponse(string Type, string? Text);
    private record AnthropicError(string? Message);
}