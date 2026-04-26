using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

class Program
{
    private static readonly string botToken = "8359077534:AAGRZjDS6we0_YYTsK2IOSLJ1uF-NbtAGfA";
    private static readonly TelegramBotClient bot = new TelegramBotClient(botToken);
    private static readonly HttpClient http = new HttpClient();
    private static readonly string apiBaseUrl = "https://musicsearchbotapi-production.up.railway.app/api/music";
    private static readonly string favoritesApiUrl = "https://musicsearchbotapi-production.up.railway.app/api/favorites";

    private static readonly Dictionary<long, UserSearchState> _searchStates = new();

    static async Task Main()
    {
        http.DefaultRequestHeaders.Add("Accept", "application/json");
        bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync);
        Console.WriteLine("Бот запущено...");
        await Task.Delay(-1);
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken ct)
    {
        if (update.Message?.Text is string msg && msg.StartsWith("/"))
        {
            await HandleCommand(update.Message.Chat.Id, msg, ct);
        }
        else if (update.Message?.Text is string query)
        {
            await SearchMusic(update.Message.Chat.Id, query, 0, ct);
        }
        else if (update.CallbackQuery != null)
        {
            await HandleCallback(update.CallbackQuery, ct);
        }
    }

    private static async Task HandleCommand(long chatId, string command, CancellationToken ct)
    {
        if (command == "/start")
        {
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
            new KeyboardButton[] { " Пошук музики", " Мої улюблені" },
            new KeyboardButton[] { " Допомога" }
        })
            {
                ResizeKeyboard = true
            };
            await bot.SendMessage(chatId, "Вітаю! Я бот для пошуку музики. Оберіть дію:", replyMarkup: keyboard, cancellationToken: ct);
        }
        else if (command == " Пошук музики")
        {
            await bot.SendMessage(chatId, "Введіть назву пісні або виконавця:", cancellationToken: ct);
        }
        else if (command == " Мої улюблені")
        {
            await ShowFavorites(chatId, ct);
        }
        else if (command == " Допомога")
        {
            await bot.SendMessage(chatId, "Просто надішліть мені назву пісні або виконавця, і я знайду її. Також ви можете додавати треки в обране та переглядати їх.", cancellationToken: ct);
        }
    }

    private static async Task SearchMusic(long chatId, string query, int page, CancellationToken ct)
    {
        try
        {
            await bot.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);

            string url = $"{apiBaseUrl}/search?query={Uri.EscapeDataString(query)}";
            string json = await http.GetStringAsync(url);
            JArray songs = JArray.Parse(json);

            if (songs.Count == 0)
            {
                await bot.SendMessage(chatId, "Нічого не знайдено.", cancellationToken: ct);
                return;
            }

            // Зберігаємо результати для користувача
            _searchStates[chatId] = new UserSearchState
            {
                Query = query,
                Results = songs,
                CurrentPage = page
            };

            int pageSize = 5;
            int totalPages = (int)Math.Ceiling((double)songs.Count / pageSize);
            int start = page * pageSize;
            int end = Math.Min(start + pageSize, songs.Count);

            var inlineKeyboard = new List<List<InlineKeyboardButton>>();

            for (int i = start; i < end; i++)
            {
                var song = songs[i];
                string title = song["title"]?.ToString() ?? "Невідомо";
                string artist = song["artists"]?[0]?["name"]?.ToString() ?? "Невідомий";
                string videoId = song["videoId"]?.ToString();

                var row = new List<InlineKeyboardButton>();

                if (!string.IsNullOrEmpty(videoId))
                {
                    row.Add(InlineKeyboardButton.WithUrl($" {artist} - {title}", $"https://youtu.be/{videoId}"));
                }

                row.Add(InlineKeyboardButton.WithCallbackData("⭐ Додати", $"add_{videoId}_{title}_{artist}"));
                inlineKeyboard.Add(row);
            }

            // Кнопки пагінації
            var navRow = new List<InlineKeyboardButton>();
            if (page > 0)
                navRow.Add(InlineKeyboardButton.WithCallbackData("⬅️ Попередня", $"page_{page - 1}_{query}"));
            navRow.Add(InlineKeyboardButton.WithCallbackData($" {page + 1}/{totalPages}", "none"));
            if (page < totalPages - 1)
                navRow.Add(InlineKeyboardButton.WithCallbackData("➡️ Наступна", $"page_{page + 1}_{query}"));
            inlineKeyboard.Add(navRow);

            string message = $" Результати пошуку \"{query}\" (стор. {page + 1}/{totalPages}):\n\n";
            await bot.SendMessage(chatId, message, replyMarkup: new InlineKeyboardMarkup(inlineKeyboard), cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await bot.SendMessage(chatId, $"Помилка пошуку: {ex.Message}", cancellationToken: ct);
        }
    }

    private static async Task HandleCallback(CallbackQuery callback, CancellationToken ct)
    {
        long chatId = callback.Message.Chat.Id;
        string data = callback.Data;

        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        if (data.StartsWith("page_"))
        {
            var parts = data.Split('_');
            int page = int.Parse(parts[1]);
            string query = string.Join("_", parts.Skip(2));

            if (_searchStates.ContainsKey(chatId) && _searchStates[chatId].Query == query)
            {
                await SearchMusic(chatId, query, page, ct);
            }
            else
            {
                await SearchMusic(chatId, query, page, ct);
            }
        }
        else if (data.StartsWith("add_"))
        {
            var parts = data.Split('_');
            string videoId = parts[1];
            string title = parts[2];
            string artist = parts[3];

            await AddToFavorites(chatId, videoId, title, artist, ct);
        }
        else if (data.StartsWith("remove_"))
        {
            int id = int.Parse(data.Split('_')[1]);
            await RemoveFromFavorites(chatId, id, ct);
        }
        else if (data == "none")
        {
            // пуста кнопка
        }
    }

    private static async Task AddToFavorites(long chatId, string videoId, string title, string artist, CancellationToken ct)
    {
        try
        {
            var favorite = new
            {
                videoId = videoId,
                title = title,
                artist = artist
            };

            var content = new StringContent(JsonConvert.SerializeObject(favorite), Encoding.UTF8, "application/json");
            var response = await http.PostAsync(favoritesApiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                await bot.SendMessage(chatId, $" Додано в обране: {artist} - {title}", cancellationToken: ct);
            }
            else
            {
                await bot.SendMessage(chatId, $" Помилка додавання: {response.StatusCode}", cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            await bot.SendMessage(chatId, $" Помилка: {ex.Message}", cancellationToken: ct);
        }
    }

    private static async Task ShowFavorites(long chatId, CancellationToken ct)
    {
        try
        {
            var response = await http.GetAsync(favoritesApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                await bot.SendMessage(chatId, "Не вдалося отримати список улюблених.", cancellationToken: ct);
                return;
            }

            string json = await response.Content.ReadAsStringAsync();
            var favorites = JsonConvert.DeserializeObject<List<JObject>>(json);

            if (favorites.Count == 0)
            {
                await bot.SendMessage(chatId, "У вас ще немає улюблених пісень. Додайте їх під час пошуку!", cancellationToken: ct);
                return;
            }

            var inlineKeyboard = new List<List<InlineKeyboardButton>>();
            foreach (var fav in favorites)
            {
                int id = fav.Value<int>("id");
                string title = fav.Value<string>("title");
                string artist = fav.Value<string>("artist");
                string videoId = fav.Value<string>("videoId");

                var row = new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithUrl($"{artist} - {title}", $"https://youtu.be/{videoId}"),
                    InlineKeyboardButton.WithCallbackData("❌ Видалити", $"remove_{id}")
                };
                inlineKeyboard.Add(row);
            }

            await bot.SendMessage(chatId, "⭐ Ваші улюблені пісні:", replyMarkup: new InlineKeyboardMarkup(inlineKeyboard), cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await bot.SendMessage(chatId, $"Помилка: {ex.Message}", cancellationToken: ct);
        }
    }

    private static async Task RemoveFromFavorites(long chatId, int id, CancellationToken ct)
    {
        try
        {
            var response = await http.DeleteAsync($"{favoritesApiUrl}/{id}");

            if (response.IsSuccessStatusCode)
            {
                await bot.SendMessage(chatId, " Видалено з обраного!", cancellationToken: ct);
                await ShowFavorites(chatId, ct);
            }
            else
            {
                await bot.SendMessage(chatId, $" Помилка видалення: {response.StatusCode}", cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            await bot.SendMessage(chatId, $" Помилка: {ex.Message}", cancellationToken: ct);
        }
    }

    private static Task HandleErrorAsync(ITelegramBotClient client, Exception exception, CancellationToken ct)
    {
        Console.WriteLine($"Помилка: {exception.Message}");
        return Task.CompletedTask;
    }
}

public class UserSearchState
{
    public string Query { get; set; }
    public JArray Results { get; set; }
    public int CurrentPage { get; set; }
}