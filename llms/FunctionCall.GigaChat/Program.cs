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
        "Search for restaurants based on query"),

        AIFunctionFactory.Create((string fromCurrency, string toCurrency, decimal amount = 1) =>
        {
            Console.WriteLine($"*** FUNCTION CALLED: get_exchange_rate from {fromCurrency} to {toCurrency} for amount {amount} ***");

            // Simulated exchange rates (in real implementation, you'd call a financial API)
            var exchangeRates = new Dictionary<(string, string), decimal>
            {
                { ("USD", "EUR"), 0.92m },
                { ("USD", "JPY"), 149.50m },
                { ("USD", "RUB"), 97.25m },
                { ("EUR", "USD"), 1.09m },
                { ("EUR", "JPY"), 163.20m },
                { ("EUR", "RUB"), 106.15m },
                { ("JPY", "USD"), 0.0067m },
                { ("JPY", "EUR"), 0.0061m },
                { ("JPY", "RUB"), 0.65m },
                { ("RUB", "USD"), 0.0103m },
                { ("RUB", "EUR"), 0.0094m },
                { ("RUB", "JPY"), 1.54m }
            };

            var key = (fromCurrency.ToUpper(), toCurrency.ToUpper());
            if (exchangeRates.TryGetValue(key, out var rate))
            {
                var convertedAmount = amount * rate;
                var result = new
                {
                    fromCurrency = fromCurrency.ToUpper(),
                    toCurrency = toCurrency.ToUpper(),
                    originalAmount = amount,
                    exchangeRate = rate,
                    convertedAmount = Math.Round(convertedAmount, 2),
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };
                return System.Text.Json.JsonSerializer.Serialize(result);
            }
            else
            {
                var result = new
                {
                    error = $"Exchange rate not available for {fromCurrency} to {toCurrency}",
                    availableCurrencies = new[] { "USD", "EUR", "JPY", "RUB" }
                };
                return System.Text.Json.JsonSerializer.Serialize(result);
            }
        },
        "get_exchange_rate",
        "Get exchange rate between two currencies and convert specified amount"),

        AIFunctionFactory.Create((int courseId) =>
        {
            Console.WriteLine($"*** FUNCTION CALLED: students_in_course with id {courseId} ***");

            // Simulated exchange rates (in real implementation, you'd call a financial API)
            var courses = new Dictionary<int, List<string>>
            {
                { 1,["Ivan", "Andrey"] },
                { 2,["Masha", "Kate"] },
                { 2,["Daniel", "Dima"] },

            };

            var key = courseId;
            if (courses.TryGetValue(key, out var students))
            {
                var result = students;
                return System.Text.Json.JsonSerializer.Serialize(result);
            }
            else
            {
                var result = new
                {
                    error = $"We don't have any courses with id {courseId}",
                    avaliableCoursesId = new[] { 1, 2, 3}
                };
                return System.Text.Json.JsonSerializer.Serialize(result);
            }
        },
        "students_in_course",
        "Get list of students in course by specified id of the course"),
    ]
};

List<ChatMessage> chatHistory = [new(ChatRole.System, """
    You are a helpful assistant that can provide information about various topics including exchange rates, weather, hiking trails, and restaurants.
    You are knowledgeable and friendly.
    """)];

// Test exchange rate functionality with multiple currencies
var queries = new[]
{
    "What's the current exchange rate from USD to EUR for 100 dollars?",
    "How much is 50 euros in Japanese yen?",
    "Convert 10000 Japanese yen to US dollars",
    "Какие студенты обучаются на курсе с id = 2"
};

foreach (var query in queries)
{
    Console.WriteLine($"\n=== Testing query: {query} ===");

    chatHistory.Add(new(ChatRole.User, query));
    Console.WriteLine($"{chatHistory.Last().Role} >>> {chatHistory.Last().Text}");

    ChatResponse response = await client.GetResponseAsync(chatHistory, chatOptions);
    chatHistory.Add(new(ChatRole.Assistant, response.Text));

    Console.WriteLine($"{chatHistory.Last().Role} >>> {chatHistory.Last().Text}");
}
