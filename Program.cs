using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

class Program
{
    private static readonly string botToken = "8359077534:AAGRZjDS6we0_YYTsK2IOSLJ1uF-NbtAGfA";
    private static readonly HttpClient http = new HttpClient();
    private static readonly string apiBaseUrl = "https://musicsearchbotapi-production.up.railway.app/api/music";

    static async Task Main()
    {
        var botClient = new TelegramBotClient(botToken);

        http.DefaultRequestHeaders.Add("Accept", "application/json");

        using var cts = new CancellationTokenSource();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions,
            cts.Token
        );

        Console.WriteLine("Бот запущено...");
        await Task.Delay(-1);
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message?.Text is string msg)
        {
            long chatId = update.Message.Chat.Id;

            if (msg == "/start")
            {
                await botClient.SendMessage(chatId, "Вітаю! Я бот для пошуку музики.\n\nНадішли мені назву пісні або виконавця.", cancellationToken: cancellationToken);
            }
            else
            {
                await SearchMusic(botClient, chatId, msg, cancellationToken);
            }
        }
    }

    private static async Task SearchMusic(ITelegramBotClient botClient, long chatId, string query, CancellationToken cancellationToken)
    {
        try
        {
            await botClient.SendChatAction(chatId, ChatAction.Typing, cancellationToken: cancellationToken);

            string url = $"{apiBaseUrl}/search?query={Uri.EscapeDataString(query)}";
            string json = await http.GetStringAsync(url);
            JArray songs = JArray.Parse(json);

            if (songs.Count == 0)
            {
                await botClient.SendMessage(chatId, "Нічого не знайдено.", cancellationToken: cancellationToken);
                return;
            }

            string message = $"Знайдено {songs.Count} результатів:\n\n";
            for (int i = 0; i < Math.Min(5, songs.Count); i++)
            {
                var song = songs[i];
                string title = song["title"]?.ToString() ?? "Невідомо";
                string artist = song["artists"]?[0]?["name"]?.ToString() ?? "Невідомий";
                string videoId = song["videoId"]?.ToString();

                message += $"{i + 1}. {artist} - {title}\n";
                if (!string.IsNullOrEmpty(videoId))
                {
                    message += $"   https://youtu.be/{videoId}\n";
                }
                message += "\n";
            }

            await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await botClient.SendMessage(chatId, $"Помилка: {ex.Message}", cancellationToken: cancellationToken);
        }
    }

    private static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Помилка: {exception.Message}");
        return Task.CompletedTask;
    }
}