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
    Tools = [
        AIFunctionFactory.Create((string location, string unit = "celsius") =>
        {
            Console.WriteLine($"*** FUNCTION CALLED: get_current_weather for {location} in {unit} ***");
            // Here you would call a weather API to get the weather for the location
            var temperature = Random.Shared.Next(5, 20);
            var conditions = Random.Shared.Next(0, 1) == 0 ? "sunny" : "rainy";

            var result = new
            {
                location = location,
                temperature = temperature,
                unit = unit,
                conditions = conditions,
                description = $"The weather is {temperature} degrees {unit} and {conditions}."
            };
            return System.Text.Json.JsonSerializer.Serialize(result);
        },
        "get_current_weather",
        "Get the current weather in a given location"),

        AIFunctionFactory.Create((string location, string difficulty = "moderate") =>
        {
            Console.WriteLine($"*** FUNCTION CALLED: find_hiking_trails for {location} with difficulty {difficulty} ***");
            // Simulate finding hiking trails
            var trails = new[]
            {
                "Forest Trail - 5km loop through pine forest",
                "Mountain View Path - 8km with scenic overlooks",
                "River Walk - 3km easy path along the water",
                "Summit Challenge - 12km steep climb to peak"
            };

            var selectedTrails = trails.OrderBy(x => Random.Shared.Next()).Take(2).ToArray();
            var result = new
            {
                location = location,
                difficulty = difficulty,
                trails = selectedTrails,
                count = selectedTrails.Length
            };
            return System.Text.Json.JsonSerializer.Serialize(result);
        },
        "find_hiking_trails",
        "Find hiking trails near a location with specified difficulty level"),

        // Новая полностью произвольная функция для демонстрации универсальности
        AIFunctionFactory.Create((string query, int maxResults = 5) =>
        {
            Console.WriteLine($"*** FUNCTION CALLED: search_restaurants for {query} max {maxResults} results ***");
            var restaurants = new[]
            {
                "Pizza Palace - Italian cuisine",
                "Sushi Master - Japanese cuisine",
                "Burger Corner - American fast food",
                "Pasta House - Italian pasta",
                "Taco Bell - Mexican food"
            };
            var selectedRestaurants = restaurants.OrderBy(x => Random.Shared.Next()).Take(maxResults).ToArray();
            var result = new
            {
                query = query,
                restaurants = selectedRestaurants,
                count = selectedRestaurants.Length
            };
            return System.Text.Json.JsonSerializer.Serialize(result);
        },
        "search_restaurants",
        "Search for restaurants based on query")
    ]
};

List<ChatMessage> chatHistory = [new(ChatRole.System, """
    You are a hiking enthusiast who helps people discover fun hikes in their area.
    You are upbeat and friendly.
    """)];

// Test function selection with different types of questions
chatHistory.Add(new(ChatRole.User, """
    Найди мне рестораны с итальянской кухней в центре Москвы
    """));

Console.WriteLine($"{chatHistory.Last().Role} >>> {chatHistory.Last().Text}");

ChatResponse response = await client.GetResponseAsync(chatHistory, chatOptions);

chatHistory.Add(new(ChatRole.Assistant, response.Text));

Console.WriteLine($"{chatHistory.Last().Role} >>> {chatHistory.Last().Text}");
