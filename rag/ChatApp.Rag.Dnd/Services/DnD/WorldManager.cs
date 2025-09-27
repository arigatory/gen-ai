using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace ChatApp.Rag.Services.DnD;

public class WorldManager(
    VectorStoreCollection<string, WorldElement> worldElementCollection,
    VectorStoreCollection<string, GameSession> sessionCollection,
    IChatClient chatClient)
{
    private readonly VectorStoreCollection<string, WorldElement> _worldElementCollection = worldElementCollection;
    private readonly VectorStoreCollection<string, GameSession> _sessionCollection = sessionCollection;
    private readonly IChatClient _chatClient = chatClient;

    public async Task<string> ProcessGameMessageAsync(string worldId, string message, string? author = null, bool isDM = false)
    {
        var session = await GetOrCreateSessionAsync(worldId);

        // Analyze message for world elements
        var extractedElements = await ExtractWorldElementsAsync(message, author, isDM);

        // Store elements in vector database
        foreach (var element in extractedElements)
        {
            element.WorldId = worldId;
            try
            {
                await _worldElementCollection.UpsertAsync(element);
            }
            catch
            {
                // If upsert fails, continue with other elements
            }
        }

        // Update session
        session.MessageCount++;
        session.LastActivityAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            await _sessionCollection.UpsertAsync(session);
        }
        catch
        {
            // If session update fails, continue
        }

        // Generate response based on world context
        var response = await GenerateGameResponseAsync(worldId, message, isDM);

        return response;
    }

    public async Task<string> GetWorldDescriptionAsync(string worldId)
    {
        var elements = await SearchWorldElementsAsync(worldId, "", maxResults: 50);

        if (!elements.Any())
        {
            return "Мир пока пуст. Начните игру, чтобы создать историю!";
        }

        var worldSummary = await GenerateWorldSummaryAsync(elements);

        // Update session summary
        var session = await GetOrCreateSessionAsync(worldId);
        session.WorldSummary = worldSummary;
        await _sessionCollection.UpsertAsync(session);

        return worldSummary;
    }

    public async Task<IReadOnlyList<WorldElement>> SearchWorldElementsAsync(string worldId, string query, int maxResults = 10)
    {
        try
        {
            var nearest = _worldElementCollection.SearchAsync(query, maxResults, new VectorSearchOptions<WorldElement>
            {
                Filter = record => record.WorldId == worldId,
            });

            return await nearest.Select(result => result.Record).ToListAsync();
        }
        catch
        {
            // If search fails (e.g., table doesn't exist), return empty list
            return [];
        }
    }

    private async Task<GameSession> GetOrCreateSessionAsync(string worldId)
    {
        try
        {
            var session = await _sessionCollection.GetAsync(worldId);
            if (session != null)
                return session;
        }
        catch
        {
            // Collection might not exist yet, will be created on first upsert
        }

        var newSession = CreateNewSession(worldId);
        try
        {
            await _sessionCollection.UpsertAsync(newSession);
        }
        catch
        {
            // If upsert fails, still return the session object
        }
        return newSession;
    }

    private static GameSession CreateNewSession(string worldId)
    {
        return new GameSession
        {
            Key = worldId,
            WorldId = worldId,
            SessionName = $"D&D Session {DateTime.Now:yyyy-MM-dd}",
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastActivityAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }

    private async Task<List<WorldElement>> ExtractWorldElementsAsync(string message, string? author, bool isDM)
    {
        var extractionPrompt = $@"
Проанализируй следующее сообщение из D&D игры и извлеки элементы мира.
Автор: {(isDM ? "Мастер" : author ?? "Игрок")}
Сообщение: {message}

Извлеки важные элементы мира и верни их в JSON формате. Каждый элемент должен содержать:
- name: название элемента
- elementType: тип из списка (Character, Location, Event, Item, Organization, Lore, Rule, Quest, Relationship, General)
- description: детальное описание элемента
- sourceMessage: исходное сообщение

Примеры типов:
- Character: персонажи, NPC, монстры
- Location: города, подземелья, комнаты, регионы
- Event: события, битвы, встречи
- Item: предметы, артефакты, оружие
- Organization: гильдии, фракции, группы
- Lore: история мира, легенды, знания
- Rule: игровые правила, механики
- Quest: задания, цели, миссии
- Relationship: отношения между персонажами
- General: общая информация о мире

Верни только JSON массив элементов, без дополнительного текста.";

        try
        {
            var response = await _chatClient.GetResponseAsync(extractionPrompt);
            var jsonResponse = response.ToString()?.Trim();

            if (string.IsNullOrEmpty(jsonResponse))
                return new List<WorldElement>();

            // Clean up JSON response
            if (jsonResponse.StartsWith("```json"))
                jsonResponse = jsonResponse[7..];
            if (jsonResponse.EndsWith("```"))
                jsonResponse = jsonResponse[..^3];

            var extractedData = JsonSerializer.Deserialize<List<ExtractedElement>>(jsonResponse);

            return extractedData?.Select(e => new WorldElement
            {
                Key = Guid.NewGuid().ToString(),
                Name = e.Name,
                ElementType = e.ElementType,
                Description = e.Description,
                SourceMessage = e.SourceMessage,
                MessageAuthor = isDM ? "DM" : author,
                WorldId = "", // Will be set by caller
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }).ToList() ?? [];
        }
        catch
        {
            // If extraction fails, create a general element
            return
            [
                new()
                {
                    Key = Guid.NewGuid().ToString(),
                    Name = "Игровое событие",
                    ElementType = WorldElementType.General,
                    Description = message,
                    SourceMessage = message,
                    MessageAuthor = isDM ? "DM" : author,
                    WorldId = "",
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }
            ];
        }
    }

    private async Task<string> GenerateGameResponseAsync(string worldId, string message, bool isDM)
    {
        // Get relevant world context
        var relevantElements = await SearchWorldElementsAsync(worldId, message, 10);

        var contextText = string.Join("\n", relevantElements.Select(e =>
            $"[{e.ElementType}] {e.Name}: {e.Description}"));

        var systemPrompt = isDM
            ? @"Ты - помощник Мастера в D&D игре. Помогай развивать сюжет, предлагай идеи для развития истории, отслеживай важные детали мира. Используй контекст мира для последовательного повествования."
            : @"Ты - помощник игрока в D&D. Помогай понимать мир, предлагай варианты действий, отвечай на вопросы о мире. Не принимай решения за игрока, только консультируй.";

        var prompt = $@"{systemPrompt}

Контекст мира:
{contextText}

Сообщение: {message}

Ответь кратко и по существу, учитывая контекст игрового мира.";

        var response = await _chatClient.GetResponseAsync(prompt);
        return response.ToString() ?? "Не могу сгенерировать ответ.";
    }

    private async Task<string> GenerateWorldSummaryAsync(IEnumerable<WorldElement> elements)
    {
        var elementsByType = elements.GroupBy(e => e.ElementType);

        var summaryParts = new List<string>();

        foreach (var group in elementsByType)
        {
            var elementsList = string.Join(", ", group.Select(e => $"{e.Name}: {e.Description}"));
            summaryParts.Add($"**{group.Key}:**\n{elementsList}");
        }

        var combinedSummary = string.Join("\n\n", summaryParts);

        var prompt = $@"Создай связное описание D&D мира на основе следующих элементов:

{combinedSummary}

Напиши краткое, но живое описание мира, объединяя все элементы в единую картину. Фокусируйся на атмосфере и ключевых деталях.";

        try
        {
            var response = await _chatClient.GetResponseAsync(prompt);
            return response.ToString() ?? combinedSummary;
        }
        catch
        {
            return combinedSummary;
        }
    }

    private class ExtractedElement
    {
        public string Name { get; set; } = "";
        public string ElementType { get; set; } = "";
        public string Description { get; set; } = "";
        public string SourceMessage { get; set; } = "";
    }
}