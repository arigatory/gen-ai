using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace FunctionCall.GigaChat;

public class GigaChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _accessToken;
    private readonly JsonSerializerOptions _jsonOptions;

    public GigaChatClient(string authData)
    {
        var handler = new HttpClientHandler()
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };

        _httpClient = new HttpClient(handler);
        _httpClient.BaseAddress = new Uri("https://gigachat.devices.sberbank.ru/");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "GigaChatClient/1.0");
        _accessToken = GetAccessTokenAsync(authData).GetAwaiter().GetResult();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public ChatClientMetadata Metadata => new("GigaChat", new Uri("https://gigachat.devices.sberbank.ru/"));

    public TService? GetService<TService>(object? key = null) where TService : class => null;

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await GetResponseCoreAsync(chatMessages, options, cancellationToken);
    }

    public async Task<ChatResponse<T>> GetResponseAsync<T>(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await GetResponseCoreAsync(chatMessages, options, cancellationToken);
        var content = response.Messages[0].Text ?? "";

        // Assume the entire response is JSON since we asked for it
        string jsonContent = content.Trim();

        // Quick fixes for common JSON issues
        Console.WriteLine($"Before cleaning: {jsonContent}");
        jsonContent = jsonContent.Replace("\"Price\":null", "\"Price\":0"); // Replace null with 0 for Price
        jsonContent = jsonContent.Replace(":null", ":0"); // Additional null replacement
        jsonContent = jsonContent.Replace("\\ ", " "); // Fix escaped spaces
        jsonContent = jsonContent.Replace("\\u00A0", " "); // Replace unicode non-breaking spaces
        Console.WriteLine($"After cleaning: {jsonContent}");

        try
        {
            var result = JsonSerializer.Deserialize<T>(jsonContent);

            // Create response with the JSON content as the message
            var structuredResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, jsonContent)])
            {
                Usage = response.Usage
            };

            var typedResponse = new ChatResponse<T>(structuredResponse, new JsonSerializerOptions());

            // Use reflection to set the Result property
            var backingField = typeof(ChatResponse<T>).GetField("<Result>k__BackingField",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            backingField?.SetValue(typedResponse, result);

            return typedResponse;
        }
        catch (JsonException)
        {
            // If parsing fails, return unsuccessful response with original content
            var structuredResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, content)])
            {
                Usage = response.Usage
            };

            return new ChatResponse<T>(structuredResponse, new JsonSerializerOptions());
        }
    }

    private async Task<ChatResponse> GetResponseCoreAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var messages = chatMessages.ToList();
        var tools = options?.Tools?.ToList();

        while (true)
        {
            var request = new GigaChatRequest
            {
                Model = "GigaChat",
                Messages = [.. messages.Select(m => new GigaChatMessage
                {
                    Role = m.Role.Value,
                    Content = m.Text ?? ""
                })],
                Temperature = options?.Temperature ?? 0.7f,
                MaxTokens = options?.MaxOutputTokens ?? 1024
            };

            // Add function/tool information to the system message if tools are available
            if (tools?.Any() == true)
            {
                var systemMessage = request.Messages.FirstOrDefault(m => m.Role == "system");
                var functionsInfo = string.Join("\n", tools.OfType<AIFunction>().Select(func =>
                    $"Function: {func.Name ?? "unknown"}\nDescription: {func.Description ?? "No description"}"));

                if (systemMessage != null)
                {
                    systemMessage.Content += $"\n\nYou have access to the following functions:\n{functionsInfo}\n\nWhen you need to call a function, respond with: CALL_FUNCTION:function_name:arguments";
                }
                else
                {
                    var functionSystemMessage = new GigaChatMessage
                    {
                        Role = "system",
                        Content = $"You have access to the following functions:\n{functionsInfo}\n\nWhen you need to call a function, respond with: CALL_FUNCTION:function_name:arguments"
                    };
                    request.Messages = [functionSystemMessage, .. request.Messages];
                }
            }

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("api/v1/chat/completions", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"Request JSON: {json}");
                Console.WriteLine($"Error {response.StatusCode}: {errorContent}");
                throw new HttpRequestException($"Response status code does not indicate success: {response.StatusCode}. Details: {errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var gigaChatResponse = JsonSerializer.Deserialize<GigaChatResponse>(responseJson, _jsonOptions);

            if (gigaChatResponse?.Choices?.Length > 0)
            {
                var choice = gigaChatResponse.Choices[0];
                var responseContent = choice.Message.Content;

                // Check if this is a function call
                if (responseContent.StartsWith("CALL_FUNCTION:") && tools?.Any() == true)
                {
                    var parts = responseContent.Substring("CALL_FUNCTION:".Length).Split(':', 3);
                    if (parts.Length >= 2)
                    {
                        var functionName = parts[0];
                        var arguments = parts.Length > 2 ? parts[2] : parts[1];

                        var tool = tools.OfType<AIFunction>().FirstOrDefault(t => t.Name == functionName);
                        if (tool != null)
                        {
                            try
                            {
                                // Parse arguments for the function call
                                var functionResult = await CallFunctionAsync(tool, arguments, cancellationToken);

                                // Add the function call and result to the conversation
                                messages.Add(new ChatMessage(ChatRole.Assistant, $"Calling function {functionName} with arguments: {arguments}"));
                                messages.Add(new ChatMessage(ChatRole.User, $"Function result: {functionResult}"));

                                // Continue the conversation loop to get the final response
                                continue;
                            }
                            catch (Exception ex)
                            {
                                messages.Add(new ChatMessage(ChatRole.User, $"Function error: {ex.Message}"));
                                continue;
                            }
                        }
                    }
                }

                return new ChatResponse([new ChatMessage(ChatRole.Assistant, responseContent)])
                {
                    Usage = new UsageDetails
                    {
                        InputTokenCount = gigaChatResponse.Usage?.PromptTokens,
                        OutputTokenCount = gigaChatResponse.Usage?.CompletionTokens,
                        TotalTokenCount = gigaChatResponse.Usage?.TotalTokens
                    }
                };
            }

            throw new InvalidOperationException("Не удалось получить ответ от GigaChat");
        }
    }

    private async Task<string> CallFunctionAsync(AIFunction tool, string arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            // Parse arguments - simple comma-separated parsing
            var argParts = arguments.Split(',').Select(s => s.Trim().Trim('"')).ToArray();

            // Create AIFunctionArguments object
            var functionArgs = new AIFunctionArguments();

            // For the weather function, we expect location and optionally unit
            if (tool.Name == "get_current_weather" && argParts.Length > 0)
            {
                functionArgs["location"] = argParts[0];
                if (argParts.Length > 1)
                    functionArgs["unit"] = argParts[1];
                else
                    functionArgs["unit"] = "celsius";
            }

            var result = await tool.InvokeAsync(functionArgs, cancellationToken);
            return result?.ToString() ?? "Function executed successfully";
        }
        catch (Exception ex)
        {
            return $"Error executing function: {ex.Message}";
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseCoreAsync(chatMessages, options, cancellationToken);
        var text = response.Messages[0].Text ?? "";

        // Simulate streaming by breaking text into chunks
        const int chunkSize = 5;
        for (int i = 0; i < text.Length; i += chunkSize)
        {
            var chunk = text.Substring(i, Math.Min(chunkSize, text.Length - i));
            var update = new GigaChatStreamingUpdate(chunk);
            yield return update;
            await Task.Delay(50, cancellationToken); // Small delay to simulate streaming
        }
    }

    private async Task<string> GetAccessTokenAsync(string authData)
    {
        var handler = new HttpClientHandler()
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authData);
        client.DefaultRequestHeaders.Add("RqUID", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("User-Agent", "GigaChatClient/1.0");

        var formParams = new List<KeyValuePair<string, string>>
        {
            new("scope", "GIGACHAT_API_PERS")
        };

        var content = new FormUrlEncodedContent(formParams);

        var response = await client.PostAsync("https://ngw.devices.sberbank.ru:9443/api/v2/oauth", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonSerializer.Deserialize<GigaChatTokenResponse>(responseJson);

        return tokenResponse?.AccessToken ?? throw new InvalidOperationException("Не удалось получить токен доступа");
    }

    private static string ExtractJsonFromResponse(string content)
    {
        // Look for JSON in markdown code blocks
        var jsonStartPattern = "```json";
        var jsonEndPattern = "```";

        var jsonStart = content.IndexOf(jsonStartPattern, StringComparison.OrdinalIgnoreCase);
        if (jsonStart != -1)
        {
            jsonStart += jsonStartPattern.Length;
            var jsonEnd = content.IndexOf(jsonEndPattern, jsonStart);
            if (jsonEnd != -1)
            {
                var rawJson = content.Substring(jsonStart, jsonEnd - jsonStart).Trim();
                return CleanJsonString(rawJson);
            }
        }

        // If no markdown block found, look for JSON object pattern
        var openBrace = content.IndexOf('{');
        var closeBrace = content.LastIndexOf('}');
        if (openBrace != -1 && closeBrace != -1 && closeBrace > openBrace)
        {
            var rawJson = content.Substring(openBrace, closeBrace - openBrace + 1).Trim();
            return CleanJsonString(rawJson);
        }

        // Return original content if no JSON pattern found
        return content;
    }

    public static string CleanJsonStringPublic(string json) => CleanJsonString(json);

    private static string CleanJsonString(string json)
    {
        // Remove JSON comments (// style)
        var lines = json.Split('\n');
        var cleanedLines = new List<string>();

        foreach (var line in lines)
        {
            var cleanLine = line;

            // Remove // comments but be careful not to remove // inside strings
            var commentIndex = -1;
            var inString = false;
            var escapeNext = false;

            for (int i = 0; i < line.Length - 1; i++)
            {
                if (escapeNext)
                {
                    escapeNext = false;
                    continue;
                }

                if (line[i] == '\\')
                {
                    escapeNext = true;
                    continue;
                }

                if (line[i] == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (!inString && line[i] == '/' && line[i + 1] == '/')
                {
                    commentIndex = i;
                    break;
                }
            }

            if (commentIndex != -1)
            {
                cleanLine = line.Substring(0, commentIndex).TrimEnd();
                // Remove trailing comma if it exists after removing comment
                if (cleanLine.EndsWith(","))
                {
                    cleanLine = cleanLine.Substring(0, cleanLine.Length - 1);
                }
            }

            if (!string.IsNullOrWhiteSpace(cleanLine))
            {
                cleanedLines.Add(cleanLine);
            }
        }

        return string.Join('\n', cleanedLines);
    }

    private static string RemoveJsonComments(string json)
    {
        // Simple regex to remove // comments from JSON
        return System.Text.RegularExpressions.Regex.Replace(json, @"//.*$", "", System.Text.RegularExpressions.RegexOptions.Multiline);
    }
}

public class GigaChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "GigaChat";

    [JsonPropertyName("messages")]
    public GigaChatMessage[] Messages { get; set; } = [];

    [JsonPropertyName("temperature")]
    public float Temperature { get; set; } = 0.7f;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 1024;
}

public class GigaChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

public class GigaChatResponse
{
    [JsonPropertyName("choices")]
    public GigaChatChoice[]? Choices { get; set; }

    [JsonPropertyName("usage")]
    public GigaChatUsage? Usage { get; set; }
}

public class GigaChatChoice
{
    [JsonPropertyName("message")]
    public GigaChatMessage Message { get; set; } = new();

    [JsonPropertyName("finish_reason")]
    public string FinishReason { get; set; } = "";
}

public class GigaChatUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int? TotalTokens { get; set; }
}

public class GigaChatTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; set; }
}

public class GigaChatStreamingUpdate : ChatResponseUpdate
{
    public GigaChatStreamingUpdate(string text)
    {
        // Set the text using reflection since Text property is read-only
        var textProperty = typeof(ChatResponseUpdate).GetProperty("Text");
        if (textProperty != null && textProperty.CanWrite)
        {
            textProperty.SetValue(this, text);
        }
        else
        {
            // Fallback: use additional properties
            AdditionalProperties ??= [];
            AdditionalProperties["text"] = text;
        }
    }

    public string GetText()
    {
        if (!string.IsNullOrEmpty(Text))
            return Text;

        if (AdditionalProperties?.TryGetValue("text", out var value) == true)
            return value?.ToString() ?? "";

        return "";
    }
}