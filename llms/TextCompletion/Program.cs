using System.ClientModel;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
IConfigurationRoot config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

var credentials = new ApiKeyCredential(config["GithubModels:Token"] ?? throw new InvalidOperationException("need token"));
var options = new OpenAIClientOptions()
{
    Endpoint = new Uri("https://models.github.ai/inference")
};

IChatClient client = new OpenAIClient(credentials, options).GetChatClient("openai/gpt-4o-mini").AsIChatClient();

// await BasicCompletion(client);
// await Streaming(client);
// await Classification(client);
// await Summarization(client);
// await SentimentAnalysis(client);
// await StructuredOutput(client);
await ChatApp(client);



#pragma warning disable CS8321 // Local function is declared but never used
static async Task StructuredOutput(IChatClient client)
{



    var carListings = new[]
    {
   "Check out this stylish 2019 Toyota Camry. It has a clean title, only 40,000 miles on the odometer, and a well-maintained interior. The car offers great fuel efficiency, a spacious trunk, and modern safety features like lane departure alert. Minimum offer price: $18,000. Contact Metro Auto at (555) 111-2222 to schedule a test drive.",
   "Lease this sporty 2021 Honda Civic! With only 10,000 miles, it includes a sunroof, premium sound system, and backup camera. Perfect for city driving with its compact size and great fuel mileage. Located in Uptown Motors, monthly lease starts at $250 (excl. taxes). Call (555) 333-4444 for more info.",
   "A classic 1968 Ford Mustang, perfect for enthusiasts. The vehicle needs some interior restoration, but the engine runs smoothly. V8 engine, manual transmission, around 80,000 miles. This vintage gem is priced at $25,000. Contact Retro Wheels at (555) 777-8888 if you’re interested.",
   "Brand new 2023 Tesla Model 3 for lease. Zero miles, fully electric, autopilot capabilities, and a sleek design. Monthly lease starts at $450. Clean lines, minimalist interior, top-notch performance. For more details, call EVolution Cars at (555) 999-0000.",
   "Selling a 2015 Subaru Outback in good condition. 60,000 miles on it, includes all-wheel drive, heated seats, and ample cargo space for family getaways. Minimum offer price: $14,000. Contact Forrest Autos at (555) 222-1212 if you want a reliable adventure companion.",
};

    foreach (var listingText in carListings)
    {
        var response = await client.GetResponseAsync<CarDetails>(
            $"""
       Преобразуй следующее объявление о продаже автомобиля в JSON объект, соответствующий этой C# схеме:
       Condition: "New" или "Used" (Новый или Б/у)
       Make: (производитель автомобиля)
       Model: (модель автомобиля)
       Year: (четырехзначный год)
       ListingType: "Sale" или "Lease" (Продажа или Аренда)
       Price: только целое число
       Features: массив коротких строк
       TenWordSummary: ровно десять слов для резюме этого объявления

       Вот объявление:
       {listingText}
       """);

        if (response.TryGetResult(out var info))
        {
            // Convert the CarDetails object to JSON for display
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                info, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine("Ответ не в ожидаемом формате.");
        }
    }
}
#pragma warning restore CS8321 // Local function is declared but never used


#pragma warning disable CS8321 // Local function is declared but never used
static async Task BasicCompletion(IChatClient client)
{
    string prompt = "Что такое ИИ? Объясни с юмором и максимум 20 словами";
    System.Console.WriteLine($"user >>> {prompt}");

    ChatResponse response = await client.GetResponseAsync(prompt);

    System.Console.WriteLine($"assistant >>> {response}");

    Console.WriteLine($"Токенов использовано: вход={response.Usage?.InputTokenCount}, выход={response.Usage?.OutputTokenCount}");
}
#pragma warning restore CS8321 // Local function is declared but never used

#pragma warning disable CS8321 // Local function is declared but never used
static async Task Streaming(IChatClient client)
{
    string prompt = "Что такое ИИ? Объясни максимум 200 словами";
    Console.WriteLine($"user >>> {prompt}");

    var responseStream = client.GetStreamingResponseAsync(prompt);
    await foreach (var message in responseStream)
    {
        Console.Write(message.Text);
    }
}
#pragma warning restore CS8321 // Local function is declared but never used

#pragma warning disable CS8321 // Local function is declared but never used
static async Task Classification(IChatClient client)
{
    var classificationPrompt = """
Пожалуйста, классифицируй следующие предложения по категориям:
- 'жалоба'
- 'предложение'
- 'похвала'
- 'другое'.

1) "Мне нравится новый дизайн!"
2) "Вам следует добавить ночной режим."
3) "Когда я пытаюсь войти в систему, это не удается."
4) "Это приложение неплохое."
""";

    Console.WriteLine($"user >>> {classificationPrompt}");

    ChatResponse classificationResponse = await client.GetResponseAsync(classificationPrompt);

    Console.WriteLine($"assistant >>>\n{classificationResponse}");
}
#pragma warning restore CS8321 // Local function is declared but never used

#pragma warning disable CS8321 // Local function is declared but never used
static async Task Summarization(IChatClient client)
{
    var summaryPrompt = """
Кратко изложи следующий блог в 1 предложении:

"Архитектура микросервисов становится все более популярной для создания сложных приложений, но она создает дополнительные накладные расходы. Крайне важно обеспечить, чтобы каждый сервис был максимально небольшим и сфокусированным, а команда инвестировала в надежные CI/CD пайплайны для управления развертываниями и обновлениями. Правильный мониторинг также необходим для поддержания надежности по мере роста системы."
""";

    Console.WriteLine($"user >>> {summaryPrompt}");

    ChatResponse summaryResponse = await client.GetResponseAsync(summaryPrompt);

    Console.WriteLine($"assistant >>> \n{summaryResponse}");
}
#pragma warning restore CS8321 // Local function is declared but never used

#pragma warning disable CS8321 // Local function is declared but never used
static async Task SentimentAnalysis(IChatClient client)
{
    var analysisPrompt = """
       Ты будешь анализировать настроение следующих отзывов о продукте.
       Каждая строка - это отдельный отзыв. Выведи настроение каждого отзыва в виде маркированного списка, а затем предоставь общее настроение всех отзывов.

       Я купил этот продукт и он потрясающий. Я его обожаю!
       Этот продукт ужасен. Я его ненавижу.
       Я не уверен насчет этого продукта. Он нормальный.
       Я нашел этот продукт на основе других отзывов. Он работал некоторое время, а потом перестал.
       """;

    Console.WriteLine($"user >>> {analysisPrompt}");

    ChatResponse responseAnalysis = await client.GetResponseAsync(analysisPrompt);

    Console.WriteLine($"assistant >>> \n{responseAnalysis}");
}
#pragma warning restore CS8321 // Local function is declared but never used

static async Task ChatApp(IChatClient client)
{
    List<ChatMessage> chatHistory = new()
    {
        new ChatMessage(ChatRole.System, """
            Ты дружелюбный энтузиаст пеших прогулок, который помогает людям открывать интересные маршруты в их районе.
            Ты представляешься при первом приветствии.
            Помогая людям, ты всегда спрашиваешь у них следующую информацию
            для составления рекомендации по пешим прогулкам:

            1. Место, где они хотели бы совершить поход
            2. Какую интенсивность похода они ищут

            После получения этой информации ты предоставишь три предложения для близлежащих походов разной протяженности.
            Ты также поделишься интересным фактом о местной природе на маршрутах при составлении рекомендации.
            В конце своего ответа спроси, есть ли что-то еще, с чем ты можешь помочь.
        """)
    };

    while (true)
    {
        // Get user prompt and add to chat history
        Console.WriteLine("Ваш запрос:");
        var userPrompt = Console.ReadLine();
        chatHistory.Add(new ChatMessage(ChatRole.User, userPrompt));

        // Stream the AI response and add to chat history
        Console.WriteLine("Ответ ИИ:");
        var response = "";
        await foreach (var item in
            client.GetStreamingResponseAsync(chatHistory))
        {
            Console.Write(item.Text);
            response += item.Text;
        }
        chatHistory.Add(new ChatMessage(ChatRole.Assistant, response));
        Console.WriteLine();
    }
}

class CarDetails
{
    public required string Condition { get; set; }  // e.g. "New" or "Used"
    public required string Make { get; set; }
    public required string Model { get; set; }
    public int Year { get; set; }
    public CarListingType ListingType { get; set; }
    public int Price { get; set; }
    public required string[] Features { get; set; }
    public required string TenWordSummary { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
enum CarListingType
{
    Sale, Lease
}