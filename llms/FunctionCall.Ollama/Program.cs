using Microsoft.Extensions.AI;


IChatClient client = new ChatClientBuilder(
    new OllamaChatClient(new Uri("http://localhost:11434"), "llama3.2"))
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
    What's the weather like in Istanbul right now?
    """));

Console.WriteLine($"{chatHistory.Last().Role} >>> {chatHistory.Last()}");

ChatResponse response = await client.GetResponseAsync(chatHistory, chatOptions);

chatHistory.Add(new(ChatRole.Assistant, response.Text));

Console.WriteLine($"{chatHistory.Last().Role} >>> {chatHistory.Last()}");