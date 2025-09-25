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
        var functionCallCount = 0;
        const int maxFunctionCalls = 5; // Prevent infinite loops

        while (true)
        {

            var request = new GigaChatRequest
            {
                Model = "GigaChat",
                Messages = [.. messages.Select(m => ConvertToGigaChatMessage(m))],
                Temperature = options?.Temperature ?? 0.7f,
                MaxTokens = options?.MaxOutputTokens ?? 1024
            };

            // Add function/tool information if tools are available and we haven't exceeded the limit
            if (tools?.Any() == true && functionCallCount < maxFunctionCalls)
            {
                request.Functions = GenerateOpenAIFunctions(tools);
                request.FunctionCall = "auto";
            }

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("api/v1/chat/completions", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Response status code does not indicate success: {response.StatusCode}. Details: {errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var gigaChatResponse = JsonSerializer.Deserialize<GigaChatResponse>(responseJson, _jsonOptions);

            if (gigaChatResponse?.Choices?.Length > 0)
            {
                var choice = gigaChatResponse.Choices[0];
                var responseContent = choice.Message.Content;

                // Check if this is a function call response and we haven't exceeded the limit
                if (choice.Message.FunctionCall != null && tools?.Any() == true && functionCallCount < maxFunctionCalls)
                {
                    var functionCall = choice.Message.FunctionCall;
                    var tool = tools.OfType<AIFunction>().FirstOrDefault(t => t.Name == functionCall.Name);

                    if (tool != null)
                    {
                        try
                        {
                            // Parse arguments for the function call
                            var argumentsString = functionCall.GetArgumentsAsString();
                            var functionResult = await CallFunctionAsync(tool, argumentsString, cancellationToken);

                            // Add the function call message to conversation
                            var assistantMessage = new ChatMessage(ChatRole.Assistant, "");
                            if (assistantMessage.AdditionalProperties == null)
                            {
                                assistantMessage.AdditionalProperties = new Microsoft.Extensions.AI.AdditionalPropertiesDictionary();
                            }

                            assistantMessage.AdditionalProperties["function_call"] = new
                            {
                                name = functionCall.Name,
                                arguments = argumentsString
                            };
                            messages.Add(assistantMessage);

                            // Add the function result as a function message with role "function"
                            var functionMessage = new ChatMessage(new ChatRole("function"), functionResult);
                            if (functionMessage.AdditionalProperties == null)
                            {
                                functionMessage.AdditionalProperties = new Microsoft.Extensions.AI.AdditionalPropertiesDictionary();
                            }

                            functionMessage.AdditionalProperties["name"] = functionCall.Name;
                            messages.Add(functionMessage);

                            // Increment the function call counter
                            functionCallCount++;

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

    private GigaChatFunction[] GenerateOpenAIFunctions(IEnumerable<AITool> tools)
    {
        var functions = new List<GigaChatFunction>();

        foreach (var tool in tools.OfType<AIFunction>())
        {
            var function = new GigaChatFunction
            {
                Name = tool.Name,
                Description = tool.Description ?? "No description provided",
                Parameters = GenerateParametersSchema(tool)
            };

            functions.Add(function);
        }

        return functions.ToArray();
    }

    private GigaChatMessage ConvertToGigaChatMessage(ChatMessage message)
    {
        var gigaChatMessage = new GigaChatMessage
        {
            Role = message.Role.Value,
            Content = message.Text ?? ""
        };

        // Handle function calls
        if (message.AdditionalProperties?.ContainsKey("function_call") == true)
        {
            var functionCallData = message.AdditionalProperties["function_call"];
            if (functionCallData != null)
            {
                var jsonElement = JsonSerializer.SerializeToElement(functionCallData);
                var name = jsonElement.GetProperty("name").GetString() ?? "";
                var argumentsText = jsonElement.GetProperty("arguments").GetString() ?? "";

                gigaChatMessage.FunctionCall = new GigaChatFunctionCall
                {
                    Name = name,
                    Arguments = JsonDocument.Parse(argumentsText).RootElement
                };
            }
        }

        // Handle function results (set name for function messages)
        if (message.AdditionalProperties?.ContainsKey("name") == true)
        {
            gigaChatMessage.Name = message.AdditionalProperties["name"]?.ToString();
        }

        return gigaChatMessage;
    }

    private Dictionary<string, object> GenerateParametersSchema(AIFunction function)
    {
        var parameters = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>(),
            ["required"] = new List<string>()
        };

        var properties = (Dictionary<string, object>)parameters["properties"];
        var required = (List<string>)parameters["required"];

        try
        {
            // Extract parameter information using reflection on the underlying method
            var methodInfo = GetFunctionMethod(function);

            if (methodInfo != null)
            {
                var paramInfos = methodInfo.GetParameters();

                foreach (var param in paramInfos)
                {
                    var paramName = param.Name ?? "unknown";
                    var paramType = GetJsonSchemaType(param.ParameterType);
                    var isRequired = !param.HasDefaultValue && !IsNullable(param.ParameterType);

                    var paramSchema = new Dictionary<string, object>
                    {
                        ["type"] = paramType
                    };

                    // Extract description from parameter attributes if available
                    var description = GetParameterDescription(param);
                    if (!string.IsNullOrEmpty(description))
                    {
                        paramSchema["description"] = description;
                    }

                    // Add enum values if parameter is an enum
                    if (param.ParameterType.IsEnum)
                    {
                        paramSchema["enum"] = Enum.GetNames(param.ParameterType);
                    }

                    properties[paramName] = paramSchema;

                    if (isRequired)
                    {
                        required.Add(paramName);
                    }
                }
            }
            else
            {
                // Fallback: create a single generic input parameter
                properties["input"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Input for the function"
                };
                required.Add("input");
            }
        }
        catch
        {
            // Final fallback: create a single generic input parameter
            properties["input"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "Input for the function"
            };
            required.Add("input");
        }
        return parameters;
    }

    private static System.Reflection.MethodInfo? GetFunctionMethod(AIFunction function)
    {
        try
        {
            // Use the UnderlyingMethod property directly
            return function.UnderlyingMethod;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"*** DEBUG: Exception in GetFunctionMethod: {ex.Message} ***");
        }

        return null;
    }

    private static string GetParameterDescription(System.Reflection.ParameterInfo parameter)
    {
        // Try to get description from various attributes
        try
        {
            // Check for Description attribute
            var descAttr = parameter.GetCustomAttributes(false)
                .FirstOrDefault(attr => attr.GetType().Name.Contains("Description"));

            if (descAttr != null)
            {
                var descProp = descAttr.GetType().GetProperty("Description");
                if (descProp?.GetValue(descAttr) is string desc)
                    return desc;
            }

            // Fallback: generate from parameter name and type
            return $"Parameter {parameter.Name} of type {parameter.ParameterType.Name}";
        }
        catch
        {
            return $"Parameter {parameter.Name}";
        }
    }

    private string GetJsonSchemaType(Type type)
    {
        if (type == typeof(string) || type == typeof(char))
            return "string";
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) ||
            type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort))
            return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return "number";
        if (type == typeof(bool))
            return "boolean";
        if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
            return "array";

        return "string"; // Default fallback
    }

    private bool IsNullable(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) ||
               !type.IsValueType;
    }


    private static async Task<string> CallFunctionAsync(AIFunction tool, string arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var functionArgs = ParseFunctionArguments(arguments, tool);
            var result = await tool.InvokeAsync(functionArgs, cancellationToken);
            return result?.ToString() ?? "Function executed successfully";
        }
        catch (Exception ex)
        {
            return $"Error executing function: {ex.Message}";
        }
    }

    private static AIFunctionArguments ParseFunctionArguments(string arguments, AIFunction function)
    {
        var functionArgs = new AIFunctionArguments();

        try
        {
            // Try to parse as JSON first
            if (arguments.Trim().StartsWith('{') && arguments.Trim().EndsWith('}'))
            {
                var jsonDoc = JsonDocument.Parse(arguments);

                // Check if we have a generic "input" parameter that needs to be mapped
                if (jsonDoc.RootElement.TryGetProperty("input", out var inputElement) &&
                    jsonDoc.RootElement.EnumerateObject().Count() == 1)
                {
                    // We have a single "input" parameter - try to map it to the actual function parameters
                    var inputValue = inputElement.GetString() ?? "";
                    MapInputToActualParameters(functionArgs, function, inputValue);
                }
                else
                {
                    // Parse normally - convert JSON values to appropriate C# types
                    foreach (var property in jsonDoc.RootElement.EnumerateObject())
                    {
                        object? value = ConvertJsonValueToObject(property.Value);
                        functionArgs[property.Name] = value;
                    }
                }

                // Add default values for missing parameters based on method signature
                AddDefaultValuesFromMethodSignature(functionArgs, function);
            }
            else
            {
                // Fallback to positional argument parsing
                ParsePositionalArguments(arguments, function, functionArgs);
            }
        }
        catch (JsonException)
        {
            // If JSON parsing fails, use positional approach
            ParsePositionalArguments(arguments, function, functionArgs);
        }

        return functionArgs;
    }

    private static object? ConvertJsonValueToObject(JsonElement jsonElement)
    {
        return jsonElement.ValueKind switch
        {
            JsonValueKind.String => jsonElement.GetString(),
            JsonValueKind.Number when jsonElement.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when jsonElement.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Number => jsonElement.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => jsonElement.EnumerateArray().Select(ConvertJsonValueToObject).ToArray(),
            JsonValueKind.Object => jsonElement.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonValueToObject(p.Value)),
            _ => jsonElement.ToString()
        };
    }

    private static void MapInputToActualParameters(AIFunctionArguments functionArgs, AIFunction function, string inputValue)
    {
        var methodInfo = GetFunctionMethod(function);
        if (methodInfo == null)
        {
            functionArgs["input"] = inputValue;
            return;
        }

        var parameters = methodInfo.GetParameters();

        // Clear the "input" key if it exists since we're mapping to actual parameters
        functionArgs.Remove("input");

        // For functions with multiple parameters, try to intelligently parse the input
        if (parameters.Length > 1)
        {
            // Use simple heuristics based on common patterns
            var words = inputValue.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var param in parameters)
            {
                if (param.Name == null) continue;

                // Set default values first
                if (param.HasDefaultValue)
                {
                    functionArgs[param.Name] = param.DefaultValue;
                }
                else
                {
                    // Provide reasonable defaults based on parameter name and type
                    var defaultValue = GetReasonableDefault(param);
                    functionArgs[param.Name] = defaultValue;
                }
            }

            // Try to extract meaningful values from the input
            foreach (var word in words)
            {
                if (decimal.TryParse(word, out var number))
                {
                    // Find first numeric parameter that doesn't have a value yet
                    var numericParam = parameters.FirstOrDefault(p =>
                        (p.ParameterType == typeof(decimal) || p.ParameterType == typeof(double) || p.ParameterType == typeof(int))
                        && p.Name != null);
                    if (numericParam?.Name != null)
                    {
                        var convertedValue = ConvertArgumentToType(word, numericParam.ParameterType);
                        functionArgs[numericParam.Name] = convertedValue;
                    }
                }
            }
        }
        else if (parameters.Length == 1)
        {
            // Single parameter - just map directly
            var param = parameters[0];
            if (param.Name != null)
            {
                var convertedValue = ConvertArgumentToType(inputValue, param.ParameterType);
                functionArgs[param.Name] = convertedValue;
            }
        }
    }

    private static object GetReasonableDefault(System.Reflection.ParameterInfo parameter)
    {
        if (parameter.ParameterType == typeof(string))
        {
            return parameter.Name?.ToLower() switch
            {
                var name when name.Contains("currency") || name.Contains("from") => "USD",
                var name when name.Contains("to") || name.Contains("target") => "EUR",
                var name when name.Contains("location") => "New York",
                var name when name.Contains("query") => "search",
                var name when name.Contains("difficulty") => "moderate",
                var name when name.Contains("unit") => "celsius",
                _ => ""
            };
        }
        else if (parameter.ParameterType == typeof(decimal))
        {
            return 1.0m;
        }
        else if (parameter.ParameterType == typeof(double))
        {
            return 1.0;
        }
        else if (parameter.ParameterType == typeof(int))
        {
            return parameter.Name?.ToLower().Contains("max") == true ? 5 : 1;
        }
        else if (parameter.ParameterType == typeof(bool))
        {
            return false;
        }

        return Activator.CreateInstance(parameter.ParameterType) ?? "";
    }

    private static void AddDefaultValuesFromMethodSignature(AIFunctionArguments functionArgs, AIFunction function)
    {
        var methodInfo = GetFunctionMethod(function);
        if (methodInfo == null) return;

        // Add default values for parameters that have defaults and are missing from arguments
        foreach (var param in methodInfo.GetParameters())
        {
            var paramName = param.Name;
            if (paramName != null && !functionArgs.ContainsKey(paramName) && param.HasDefaultValue)
            {
                functionArgs[paramName] = param.DefaultValue;
            }
        }
    }

    private static void ParsePositionalArguments(string arguments, AIFunction function, AIFunctionArguments functionArgs)
    {
        var argParts = arguments.Split(',').Select(s => s.Trim().Trim('"')).ToArray();
        var methodInfo = GetFunctionMethod(function);

        if (methodInfo != null)
        {
            // Map positional arguments to parameter names
            var parameters = methodInfo.GetParameters();
            for (int i = 0; i < Math.Min(argParts.Length, parameters.Length); i++)
            {
                var paramName = parameters[i].Name;
                if (paramName != null)
                {
                    functionArgs[paramName] = ConvertArgumentToType(argParts[i], parameters[i].ParameterType);
                }
            }

            // Add default values for remaining parameters
            for (int i = argParts.Length; i < parameters.Length; i++)
            {
                var paramName = parameters[i].Name;
                if (paramName != null && parameters[i].HasDefaultValue)
                {
                    functionArgs[paramName] = parameters[i].DefaultValue;
                }
            }
        }
        else
        {
            // Generic fallback: use positional argument names
            for (int i = 0; i < argParts.Length; i++)
            {
                functionArgs[$"arg{i}"] = argParts[i];
            }

            // If only one argument and it's not named, use "input" as the key
            if (argParts.Length == 1)
            {
                functionArgs["input"] = argParts[0];
            }
        }
    }

    private static object ConvertArgumentToType(string argument, Type targetType)
    {
        try
        {
            if (targetType == typeof(string))
                return argument;
            if (targetType == typeof(int))
                return int.Parse(argument);
            if (targetType == typeof(double))
                return double.Parse(argument);
            if (targetType == typeof(bool))
                return bool.Parse(argument);
            if (targetType.IsEnum)
                return Enum.Parse(targetType, argument, ignoreCase: true);

            // Default to string if conversion fails
            return argument;
        }
        catch
        {
            return argument;
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

    [JsonPropertyName("functions")]
    public GigaChatFunction[]? Functions { get; set; }

    [JsonPropertyName("function_call")]
    public object? FunctionCall { get; set; }
}

public class GigaChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("function_call")]
    public GigaChatFunctionCall? FunctionCall { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
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

public class GigaChatFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("parameters")]
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public class GigaChatFunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; set; }

    public string GetArgumentsAsString()
    {
        return Arguments.ValueKind == JsonValueKind.String
            ? Arguments.GetString() ?? ""
            : Arguments.ToString();
    }
}