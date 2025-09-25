using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using FunctionCall.GigaChat;

// Get credentials from user secrets
IConfigurationRoot config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

string authData = config["GigaChat:Token"] ?? throw new InvalidOperationException("Missing configuration: GigaChat:AuthData.");

// Create a chat client using GigaChatClient with function invocation support
IChatClient client = new ChatClientBuilder(new GigaChatClient(authData))
    .UseFunctionInvocation()
    .Build();

var chatOptions = new ChatOptions
{
    Tools = [AIFunctionFactory.Create((string location, string unit) =>
    {
        Console.WriteLine($"*** FUNCTION CALLED: get_current_weather for {location} in {unit} ***");
        // Here you would call a weather API to get the weather for the location
        var temperature = Random.Shared.Next(5, 20);
        var conditions = Random.Shared.Next(0, 1) == 0 ? "sunny" : "rainy";

        return $"The weather is {temperature} degrees C and {conditions}.";
    },
    "get_current_weather",
    "Get the current weather in a given location")]
};

List<ChatMessage> chatHistory = [new(ChatRole.System, """
    You are a hiking enthusiast who helps people discover fun hikes in their area.
    You are upbeat and friendly.
    """)];

// Weather conversation relevant to the registered function.
chatHistory.Add(new(ChatRole.User, """
    Какая погода в Москве прямо сейчас?
    """));

Console.WriteLine($"{chatHistory.Last().Role} >>> {chatHistory.Last().Text}");

ChatResponse response = await client.GetResponseAsync(chatHistory, chatOptions);

chatHistory.Add(new(ChatRole.Assistant, response.Text));

Console.WriteLine($"{chatHistory.Last().Role} >>> {chatHistory.Last().Text}");
