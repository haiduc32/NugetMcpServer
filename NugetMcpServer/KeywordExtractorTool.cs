using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.AI;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace NugetMcpServer;

[McpServerToolType]
public static class KeywordExtractorTool
{
    [McpServerTool, Description("Converts a free-text user query into a list of keywords via Sampling.")]
    public static async Task<string> ExtractKeywordsAsync(
        IMcpServer thisServer,
        [Description("The user's natural language query")] string userQuery,
        CancellationToken cancellationToken)
    {
        // 1. Формируем сообщение для модели: сделать ставку на извлечение ключевых слов
        var messages = new[]
        {
            // Роль User: «Извлеки ключевые слова из следующего запроса: ...»
            new ChatMessage(
                role: ChatRole.User,
                content: $"Extract the main keywords from this query: \"{userQuery}\""
            )
        };

        // 2. Определяем параметры LLM-запроса (максимум 50 токенов, без случайности)
        var options = new ChatOptions
        {
            MaxOutputTokens = 50,
            Temperature = 0.0f
        };

        // 3. Делаем Sampling-вызов через клиента (AsSamplingChatClient), чтобы модель выполнила LLM-инференс
        var responseText = await thisServer
            .AsSamplingChatClient()
            .GetResponseAsync(messages, options, cancellationToken);

        // 4. Возвращаем текст ответа модели (список ключевых слов)
        return responseText.ToString();
    }
}
