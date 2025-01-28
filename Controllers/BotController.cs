using Microsoft.Extensions.Options;
using static Telegram.Bot.TelegramBotClient;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using Telegram.Bot;
using HRProBot.Models;

namespace HRProBot.Controllers
{
    public class BotController
    {
        private readonly ILogger<HomeController> _logger;
        private static string _tlgBotToken;
        private static ITelegramBotClient _botClient;
        public BotController(IOptionsSnapshot<AppSettings> appSettings)
        {

            _tlgBotToken = appSettings.Value.TlgBotToken;
            _botClient = new TelegramBotClient(_tlgBotToken);
            var range = appSettings.Value.GoogleSheetsRange;
            var cts = new CancellationTokenSource(); // прерыватель соединения с ботом
            var updateHandler = new UpdateHandler(appSettings, _botClient);


            _botClient.StartReceiving(updateHandler.HandleUpdateAsync,
            updateHandler.HandleErrorAsync,
            new ReceiverOptions()
            {
                AllowedUpdates = [UpdateType.Message]
            }, cts.Token);
        }
    }
}
