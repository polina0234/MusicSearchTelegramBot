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
    private static readonly Dictionary<long, (int Id, string VideoId)> _pendingUpdate = new();

    static async Task Main()
    {
        http.DefaultRequestHeaders.Add("Accept", "application/json");

        bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync);

        Console.WriteLine("Бот запущено...");
        await Task.Delay(-1);
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken ct)
    {
        if (update.CallbackQuery != null)
        {
            await HandleCallback(update.CallbackQuery, ct);
            return;
        }

        if (update.Message?.Text is string msg)
        {
            long chatId = update.Message.Chat.Id;

            if (_pendingUpdate.ContainsKey(chatId))
            {
                var (id, videoId) = _pendingUpdate[chatId];
                _pendingUpdate.Remove(chatId);
                await UpdateFavoriteTitle(chatId, id, videoId, msg, ct);
                return;
            }

            // Обробка відповідей на питання про схожі треки
            if (msg == "✅ Так")
            {
                await bot.SendMessage(chatId, "Введіть назву пісні або виконавця для пошуку схожих:", cancellationToken: ct);
                // Повертаємо головне меню
                var keyboard = new ReplyKeyboardMarkup(new[]
                {
                    new KeyboardButton[] { "🔍 Пошук музики", "⭐ Мої улюблені" },
                    new KeyboardButton[] { "📋 Допомога" }
                })
                { ResizeKeyboard = true };
                await bot.SendMessage(chatId, "Головне меню:", replyMarkup: keyboard, cancellationToken: ct);
                return;
            }
            else if (msg == "❌ Ні")
            {
                // Повертаємо головне меню
                var keyboard = new ReplyKeyboardMarkup(new[]
                {
                    new KeyboardButton[] { "🔍 Пошук музики", "⭐ Мої улюблені" },
                    new KeyboardButton[] { "📋 Допомога" }
                })
                { ResizeKeyboard = true };
                await bot.SendMessage(chatId, "Головне меню:", replyMarkup: keyboard, cancellationToken: ct);
                return;
            }

            if (msg == "/start")
            {
                var keyboard = new ReplyKeyboardMarkup(new[]
                {
                    new KeyboardButton[] { "🔍 Пошук музики", "⭐ Мої улюблені" },
                    new KeyboardButton[] { "📋 Допомога" }
                })
                {
                    ResizeKeyboard = true
                };

                await bot.SendMessage(chatId, "Вітаю! Оберіть дію:", replyMarkup: keyboard, cancellationToken: ct);
            }
            else if (msg.Contains("Пошук"))
            {
                await bot.SendMessage(chatId, "Введіть назву пісні або виконавця:", cancellationToken: ct);
            }
            else if (msg.Contains("улюблені"))
            {
                await ShowFavorites(chatId, ct);
            }
            else if (msg.Contains("Допомога"))
            {
                await bot.SendMessage(chatId, "Просто введи назву пісні :Р", cancellationToken: ct);
            }
            else
            {
                await SearchMusic(chatId, msg, 0, ct);
            }
        }
    }

    private static async Task SearchMusic(long chatId, string query, int page, CancellationToken ct)
    {
        try
        {
            await bot.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);

            string url = $"{apiBaseUrl}/search?query={Uri.EscapeDataString(query)}";
            string json = await http.GetStringAsync(url);

            var allResults = JsonConvert.DeserializeObject<Class1[]>(json);
            var songs = allResults
                .Where(s => !string.IsNullOrEmpty(s.videoId) && s.resultType == "song")
                .ToArray();

            if (songs == null || songs.Length == 0)
            {
                await bot.SendMessage(chatId, "Нічого не знайдено.", cancellationToken: ct);
                return;
            }

            _searchStates[chatId] = new UserSearchState
            {
                Query = query,
                Results = songs,
                CurrentPage = page
            };

            int pageSize = 5;
            int totalPages = (int)Math.Ceiling((double)songs.Length / pageSize);

            int start = page * pageSize;
            int end = Math.Min(start + pageSize, songs.Length);

            var inlineKeyboard = new List<List<InlineKeyboardButton>>();

            for (int i = start; i < end; i++)
            {
                try
                {
                    var song = songs[i];

                    string title = song.title ?? "Невідомо";
                    string artist = song.artists?[0]?.name ?? "Невідомий";
                    string videoId = song.videoId;

                    var row = new List<InlineKeyboardButton>();

                    if (!string.IsNullOrEmpty(videoId))
                    {
                        row.Add(InlineKeyboardButton.WithUrl($"🎧 {artist} - {title}", $"https://youtu.be/{videoId}"));
                        row.Add(InlineKeyboardButton.WithCallbackData("📄 Деталі", $"details_{videoId}"));
                        row.Add(InlineKeyboardButton.WithCallbackData("⭐ Додати", $"add_{videoId}"));
                    }
                    else
                    {
                        row.Add(InlineKeyboardButton.WithCallbackData($"❌ {artist} - {title}", "none"));
                    }

                    inlineKeyboard.Add(row);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"BUTTON ERROR: {ex.Message}");
                }
            }

            var navRow = new List<InlineKeyboardButton>();

            if (page > 0)
                navRow.Add(InlineKeyboardButton.WithCallbackData("⬅️", $"page_{page - 1}_{Uri.EscapeDataString(query)}"));

            navRow.Add(InlineKeyboardButton.WithCallbackData($"{page + 1}/{totalPages}", "none"));

            if (page < totalPages - 1)
                navRow.Add(InlineKeyboardButton.WithCallbackData("➡️", $"page_{page + 1}_{Uri.EscapeDataString(query)}"));

            inlineKeyboard.Add(navRow);

            await bot.SendMessage(
                chatId,
                $"🔍 \"{query}\" (стор. {page + 1}/{totalPages})",
                replyMarkup: new InlineKeyboardMarkup(inlineKeyboard),
                cancellationToken: ct
            );
        }
        catch (Exception ex)
        {
            await bot.SendMessage(chatId, $"Помилка: {ex.Message}", cancellationToken: ct);
        }
    }

    private static async Task HandleCallback(CallbackQuery callback, CancellationToken ct)
    {
        if (callback.Message == null || callback.Data == null) return;

        long chatId = callback.Message.Chat.Id;
        string data = callback.Data;

        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        if (data == "none") return;

        if (data.StartsWith("page_"))
        {
            var parts = data.Split('_');
            int page = int.Parse(parts[1]);
            string query = Uri.UnescapeDataString(string.Join("_", parts.Skip(2)));
            await SearchMusic(chatId, query, page, ct);
        }
        else if (data.StartsWith("details_"))
        {
            string videoId = data.Replace("details_", "");
            await ShowDetails(chatId, videoId, ct);
        }
        else if (data.StartsWith("add_"))
        {
            string videoId = data.Replace("add_", "");
            if (_searchStates.TryGetValue(chatId, out var state))
            {
                var song = state.Results.FirstOrDefault(s => s.videoId == videoId);
                if (song != null)
                {
                    await AddToFavorites(chatId, videoId, song.title, song.artists?[0]?.name, ct);
                }
            }
        }
        else if (data.StartsWith("update_"))
        {
            var parts = data.Split('_');
            int id = int.Parse(parts[1]);
            string videoId = parts[2];
            _pendingUpdate[chatId] = (id, videoId);
            await bot.SendMessage(chatId, "Введіть нову назву для пісні:", cancellationToken: ct);
        }
        else if (data.StartsWith("favdetails_"))
        {
            int id = int.Parse(data.Replace("favdetails_", ""));
            await ShowFavoriteDetails(chatId, id, ct);
        }
        else if (data.StartsWith("remove_"))
        {
            int id = int.Parse(data.Replace("remove_", ""));
            await RemoveFromFavorites(chatId, id, ct);
        }
    }

    private static async Task ShowDetails(long chatId, string videoId, CancellationToken ct)
    {
        try
        {
            await bot.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);

            string url = $"{apiBaseUrl}/details/{videoId}";
            string json = await http.GetStringAsync(url);

            var details = JsonConvert.DeserializeObject<VideoDetailsResponse>(json);

            if (details?.items == null || details.items.Length == 0)
            {
                await bot.SendMessage(chatId, "Не вдалося отримати деталі.", cancellationToken: ct);
                return;
            }

            var video = details.items[0];
            var snippet = video.snippet;

            string duration = "Невідомо";
            if (video.contentDetails?.duration != null)
            {
                duration = video.contentDetails.duration
                    .Replace("PT", "")
                    .Replace("H", " год ")
                    .Replace("M", " хв ")
                    .Replace("S", " сек");
                if (string.IsNullOrWhiteSpace(duration)) duration = "0 сек";
            }

            string viewCount = "Немає даних";
            string likeCount = "Немає даних";
            string commentCount = "Немає даних";

            if (video.statistics?.viewCount != null && long.TryParse(video.statistics.viewCount, out long views))
                viewCount = views.ToString("N0");

            if (video.statistics?.likeCount != null && long.TryParse(video.statistics.likeCount, out long likes))
                likeCount = likes.ToString("N0");

            if (video.statistics?.commentCount != null && long.TryParse(video.statistics.commentCount, out long comments))
                commentCount = comments.ToString("N0");

            string message = $"📌 *{snippet.title}*\n\n" +
                             $"🎤 Канал: {snippet.channelTitle}\n" +
                             $"📅 Дата: {snippet.publishedAt:yyyy-MM-dd}\n" +
                             $"⏱ Тривалість: {duration}\n" +
                             $"👁 Переглядів: {viewCount}\n" +
                             $"❤️ Лайків: {likeCount}\n" +
                             $"💬 Коментарів: {commentCount}\n\n" +
                             $"🔗 [Посилання](https://youtu.be/{videoId})";

            await bot.SendMessage(chatId, message, parseMode: ParseMode.Markdown, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            await bot.SendMessage(chatId, $"Помилка отримання деталей: {ex.Message}", cancellationToken: ct);
        }
    }

    private static async Task AddToFavorites(long chatId, string videoId, string title, string artist, CancellationToken ct)
    {
        var favorite = new { videoId, title, artist };

        var request = new HttpRequestMessage(HttpMethod.Post, favoritesApiUrl);
        request.Headers.Add("userId", chatId.ToString());
        request.Content = new StringContent(JsonConvert.SerializeObject(favorite), Encoding.UTF8, "application/json");

        var response = await http.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            await bot.SendMessage(chatId, $"✅ {artist} - {title}", cancellationToken: ct);
            // Питаємо про схожі треки
            var keyboard = new ReplyKeyboardMarkup(new[] { new KeyboardButton[] { "✅ Так", "❌ Ні" } })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };
            await bot.SendMessage(chatId, $"🎵 Пісня \"{title}\" додана!\n\nБажаєте знайти схожі треки?", replyMarkup: keyboard, cancellationToken: ct);
        }
        else
        {
            await bot.SendMessage(chatId, $"❌ Помилка: {response.StatusCode}", cancellationToken: ct);
        }
    }

    private static async Task ShowFavorites(long chatId, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, favoritesApiUrl);
        request.Headers.Add("userId", chatId.ToString());

        var response = await http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            await bot.SendMessage(chatId, "Не вдалося отримати список.", cancellationToken: ct);
            return;
        }

        string json = await response.Content.ReadAsStringAsync();
        var favorites = JsonConvert.DeserializeObject<List<JObject>>(json);

        if (favorites.Count == 0)
        {
            await bot.SendMessage(chatId, "У вас немає улюблених пісень.", cancellationToken: ct);
            return;
        }

        var keyboard = new List<List<InlineKeyboardButton>>();

        foreach (var fav in favorites)
        {
            int id = fav.Value<int>("id");
            string title = fav.Value<string>("title");
            string artist = fav.Value<string>("artist");
            string videoId = fav.Value<string>("videoId");

            keyboard.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithUrl($"🎧 {artist} - {title}", $"https://youtu.be/{videoId}"),
                InlineKeyboardButton.WithCallbackData("📄 Деталі", $"favdetails_{id}"),
                InlineKeyboardButton.WithCallbackData("✏ Оновити", $"update_{id}_{videoId}"),
                InlineKeyboardButton.WithCallbackData("❌", $"remove_{id}")
            });
        }

        await bot.SendMessage(chatId, "⭐ Ваші улюблені пісні:", replyMarkup: new InlineKeyboardMarkup(keyboard), cancellationToken: ct);
    }

    private static async Task ShowFavoriteDetails(long chatId, int id, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{favoritesApiUrl}/{id}");
        request.Headers.Add("userId", chatId.ToString());
        var response = await http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            await bot.SendMessage(chatId, "Не вдалося отримати деталі.", cancellationToken: ct);
            return;
        }

        string json = await response.Content.ReadAsStringAsync();
        var song = JsonConvert.DeserializeObject<JObject>(json);

        string title = song["title"]?.ToString();
        string artist = song["artist"]?.ToString();
        string videoId = song["videoId"]?.ToString();

        string message = $"📌 *{artist} - {title}*\n\n🔗 [Посилання](https://youtu.be/{videoId})";
        await bot.SendMessage(chatId, message, parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private static async Task UpdateFavoriteTitle(long chatId, int id, string videoId, string newTitle, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"{favoritesApiUrl}/{id}");
        request.Headers.Add("userId", chatId.ToString());

        // Виправлено: прибрано "Оновлено"
        var song = new { id, videoId, title = newTitle };
        request.Content = new StringContent(JsonConvert.SerializeObject(song), Encoding.UTF8, "application/json");

        var response = await http.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            await bot.SendMessage(chatId, $"✅ Назву змінено на \"{newTitle}\"", cancellationToken: ct);
            await ShowFavorites(chatId, ct);
        }
        else
        {
            await bot.SendMessage(chatId, $"❌ Помилка: {response.StatusCode}", cancellationToken: ct);
        }
    }

    private static async Task RemoveFromFavorites(long chatId, int id, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"{favoritesApiUrl}/{id}");
        request.Headers.Add("userId", chatId.ToString());

        var response = await http.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            await bot.SendMessage(chatId, "✅ Видалено!", cancellationToken: ct);
            await ShowFavorites(chatId, ct);
        }
        else
        {
            await bot.SendMessage(chatId, $"❌ Помилка видалення: {response.StatusCode}", cancellationToken: ct);
        }
    }

    private static Task HandleErrorAsync(ITelegramBotClient client, Exception exception, CancellationToken ct)
    {
        Console.WriteLine(exception.Message);
        return Task.CompletedTask;
    }
}

public class UserSearchState
{
    public string Query { get; set; }
    public Class1[] Results { get; set; }
    public int CurrentPage { get; set; }
}

public class VideoDetailsResponse
{
    public VideoItem[] items { get; set; }
}

public class VideoItem
{
    public VideoSnippet snippet { get; set; }
    public VideoContentDetails contentDetails { get; set; }
    public VideoStatistics statistics { get; set; }
}

public class VideoSnippet
{
    public DateTime publishedAt { get; set; }
    public string channelTitle { get; set; }
    public string title { get; set; }
}

public class VideoContentDetails
{
    public string duration { get; set; }
}

public class VideoStatistics
{
    public string viewCount { get; set; }
    public string likeCount { get; set; }
    public string commentCount { get; set; }
}