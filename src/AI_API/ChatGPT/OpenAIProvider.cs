// src/AI_API/OpenAI/OpenAIProvider.cs
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thio_Universal_Agent.AI_API.OpenAI;

/// <summary>
/// OpenAI REST API implementation of <see cref="IAiProvider"/>.
/// </summary>
public sealed class OpenAIProvider(HttpClient httpClient, AppConfig appConfig, ILogger<OpenAIProvider> logger) : IAiProvider
{
    private const string BaseUrl = "https://api.openai.com/v1/chat/completions";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string? _apiKey = appConfig.OpenAI.ApiKey;
    private readonly string _model = appConfig.OpenAI.Model;

    public Task<AiResponse> SendPromptAsync(string prompt, CancellationToken cancellationToken = default, AiRequestOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var request = new OpenAIRequest(_model, [new OpenAIMessage("user", [new OpenAIContentPart("text", prompt, null)])], appConfig.OpenAI.Temperature, options?.MaxOutputTokens ?? appConfig.OpenAI.MaxOutputTokens);
        return SendRequestAsync(request, cancellationToken);
    }

    public Task<AiResponse> SendPromptWithImageAsync(string prompt, byte[] imageBytes, string mimeType = "image/jpeg", CancellationToken cancellationToken = default, AiRequestOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(imageBytes);

        var request = new OpenAIRequest(_model, [new OpenAIMessage("user", [
            new OpenAIContentPart("text", prompt, null),
            new OpenAIContentPart("image_url", null, new OpenAIImageUrl($"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}"))
        ])], appConfig.OpenAI.Temperature, options?.MaxOutputTokens ?? appConfig.OpenAI.MaxOutputTokens);

        return SendRequestAsync(request, cancellationToken);
    }

    public async Task<(AiConversation Conversation, AiResponse Response)> StartConversationAsync(string prompt, CancellationToken cancellationToken = default, AiRequestOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        AiConversation conversation = new AiConversation();
        AiChatMessage userMessage = new AiChatMessage { Role = AiChatRole.User, Text = prompt };

        OpenAIRequest request = BuildRequest(conversation, userMessage, options);
        AiResponse response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Success)
        {
            conversation.AddMessage(userMessage);
            conversation.AddMessage(new AiChatMessage { Role = AiChatRole.Model, Text = response.Text });
        }

        return (conversation, response);
    }

    public Task<AiResponse> ContinueConversationAsync(AiConversation conversation, string prompt, CancellationToken cancellationToken = default, AiRequestOptions? options = null)
    {
        AiChatMessage userMessage = new AiChatMessage { Role = AiChatRole.User, Text = prompt };
        return ContinueConversationCoreAsync(conversation, userMessage, cancellationToken, options);
    }

    public Task<AiResponse> ContinueConversationAsync(AiConversation conversation, byte[] imageBytes, string mimeType = "image/jpeg", CancellationToken cancellationToken = default, AiRequestOptions? options = null)
    {
        AiChatMessage userMessage = new AiChatMessage { Role = AiChatRole.User, ImageBytes = imageBytes, MimeType = mimeType };
        return ContinueConversationCoreAsync(conversation, userMessage, cancellationToken, options);
    }

    public Task<AiResponse> ContinueConversationAsync(AiConversation conversation, string prompt, byte[] imageBytes, string mimeType = "image/jpeg", CancellationToken cancellationToken = default, AiRequestOptions? options = null)
    {
        AiChatMessage userMessage = new AiChatMessage { Role = AiChatRole.User, Text = prompt, ImageBytes = imageBytes, MimeType = mimeType };
        return ContinueConversationCoreAsync(conversation, userMessage, cancellationToken, options);
    }

    private async Task<AiResponse> ContinueConversationCoreAsync(AiConversation conversation, AiChatMessage userMessage, CancellationToken cancellationToken, AiRequestOptions? options)
    {
        var request = BuildRequest(conversation, userMessage, options);
        AiResponse response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Success)
        {
            conversation.AddMessage(userMessage);
            conversation.AddMessage(new AiChatMessage { Role = AiChatRole.Model, Text = response.Text });
        }

        return response;
    }

    private async Task<AiResponse> SendRequestAsync(OpenAIRequest request, CancellationToken cancellationToken)
    {
        var apiKey = _apiKey ?? appConfig.OpenAI.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI:ApiKey is not configured. Provide an API key via the web UI.");

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Sending prompt to OpenAI model {Model}.", _model);

        using HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

        using HttpResponseMessage response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            logger.LogError("OpenAI API returned {StatusCode}. Body: {ErrorBody}", (int)response.StatusCode, errorBody);
            return new AiResponse(false, string.Empty, $"HTTP {(int)response.StatusCode}: {errorBody}");
        }

        var openAiResponse = await response.Content.ReadFromJsonAsync<OpenAIResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        var text = openAiResponse?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(text))
            return new AiResponse(false, string.Empty, "OpenAI returned an empty response.");

        return new AiResponse(true, text);
    }

    private OpenAIRequest BuildRequest(AiConversation conversation, AiChatMessage additionalMessage, AiRequestOptions? options)
    {
        bool stripHistoryImages = appConfig.General.StripHistoryImages;
        var messages = new List<OpenAIMessage>(conversation.Messages.Count + 1);

        foreach (AiChatMessage message in conversation.Messages)
            messages.Add(ToOpenAIMessage(message, stripImages: stripHistoryImages));

        messages.Add(ToOpenAIMessage(additionalMessage, stripImages: false));

        return new OpenAIRequest(_model, messages, appConfig.OpenAI.Temperature, options?.MaxOutputTokens ?? appConfig.OpenAI.MaxOutputTokens);
    }

    private static OpenAIMessage ToOpenAIMessage(AiChatMessage message, bool stripImages)
    {
        var role = message.Role == AiChatRole.User ? "user" : "assistant";
        var content = new List<OpenAIContentPart>();

        if (message.Text is not null)
            content.Add(new OpenAIContentPart("text", message.Text, null));

        if (!stripImages && message.ImageBytes is not null)
            content.Add(new OpenAIContentPart("image_url", null, new OpenAIImageUrl($"data:{message.MimeType ?? "image/jpeg"};base64,{Convert.ToBase64String(message.ImageBytes)}")));

        return new OpenAIMessage(role, content);
    }

    // --- Private DTOs ---

    private record OpenAIRequest(string Model, List<OpenAIMessage> Messages, float? Temperature, [property: JsonPropertyName("max_tokens")] int? MaxTokens);
    private record OpenAIMessage(string Role, List<OpenAIContentPart> Content);
    private record OpenAIContentPart(string Type, string? Text, [property: JsonPropertyName("image_url")] OpenAIImageUrl? ImageUrl);
    private record OpenAIImageUrl([property: JsonPropertyName("url")] string Url);
    private record OpenAIResponse(List<OpenAIChoice>? Choices, OpenAIError? Error);
    private record OpenAIChoice(OpenAIMessageResponse? Message, [property: JsonPropertyName("finish_reason")] string? FinishReason);
    private record OpenAIMessageResponse(string? Content);
    private record OpenAIError(string? Message);
}