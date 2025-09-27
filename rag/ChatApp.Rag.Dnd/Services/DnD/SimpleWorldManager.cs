using Microsoft.Extensions.AI;
using System.Text.Json;

namespace ChatApp.Rag.Services.DnD;

public class SimpleWorldManager(IChatClient chatClient)
{
    private readonly IChatClient _chatClient = chatClient;
    private readonly Dictionary<string, List<string>> _worldElements = new();
    private readonly Dictionary<string, DateTime> _worldSessions = new();

    public async Task<string> ProcessGameMessageAsync(string worldId, string message, string? author = null, bool isDM = false)
    {
        // Initialize world if not exists
        if (!_worldElements.ContainsKey(worldId))
        {
            _worldElements[worldId] = [];
            _worldSessions[worldId] = DateTime.UtcNow;
        }

        // Add message to world elements
        var elementDescription = $"[{(isDM ? "DM" : author ?? "Player")}] {message}";
        _worldElements[worldId].Add(elementDescription);

        // Generate response based on world context
        var response = await GenerateGameResponseAsync(worldId, message, isDM);

        return response;
    }

    public async Task<string> GetWorldDescriptionAsync(string worldId)
    {
        if (!_worldElements.ContainsKey(worldId) || !_worldElements[worldId].Any())
        {
            return "Мир пока пуст. Начните игру, чтобы создать историю!";
        }

        var elements = _worldElements[worldId];
        var summary = string.Join("\n", elements.TakeLast(20)); // Show last 20 elements

        var prompt = $@"Создай краткое описание D&D мира на основе следующих событий и сообщений:

{summary}

Напиши живое описание мира, объединяя все элементы в единую картину. Фокусируйся на атмосфере и ключевых деталях.";

        try
        {
            var response = await _chatClient.GetResponseAsync(prompt);
            return response.ToString() ?? summary;
        }
        catch
        {
            return summary;
        }
    }

    public async Task<IReadOnlyList<string>> SearchWorldElementsAsync(string worldId, string query, int maxResults = 10)
    {
        if (!_worldElements.ContainsKey(worldId))
            return [];

        var elements = _worldElements[worldId];

        // Simple text search
        var matches = elements
            .Where(e => e.Contains(query, StringComparison.OrdinalIgnoreCase))
            .TakeLast(maxResults)
            .ToList();

        return matches;
    }

    private async Task<string> GenerateGameResponseAsync(string worldId, string message, bool isDM)
    {
        // Get recent world context
        var context = "";
        if (_worldElements.ContainsKey(worldId))
        {
            var recentElements = _worldElements[worldId].TakeLast(10);
            context = string.Join("\n", recentElements);
        }

        var systemPrompt = isDM
            ? @"Ты помощник Мастера в D&D. Помогай развивать сюжет, предлагай идеи для развития истории, отслеживай детали мира. Используй контекст мира для создания связной истории. Отвечай кратко и по существу."
            : @"Ты помощник игрока в D&D. Помогай понимать игровой мир, предлагай варианты действий, консультируй по правилам. Не принимай решения за игрока, только консультируй. Отвечай кратко и по существу.";

        var prompt = $@"{systemPrompt}

Контекст недавних событий в мире:
{context}

Текущее сообщение: {message}

Ответь кратко и по существу, учитывая контекст игрового мира.";

        try
        {
            var response = await _chatClient.GetResponseAsync(prompt);
            return response.ToString() ?? "Не могу сгенерировать ответ.";
        }
        catch
        {
            return "Не могу сгенерировать ответ.";
        }
    }
}