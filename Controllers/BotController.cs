using Microsoft.Extensions.Options;
using static Telegram.Bot.TelegramBotClient;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using Telegram.Bot;
using HRProBot.Models;
using LinqToDB;
using System.Linq.Expressions;
using static LinqToDB.Common.Configuration;

namespace HRProBot.Controllers
{
    public class BotController
    {
        private readonly ILogger<HomeController> _logger;
        private static string _tlgBotToken;
        private static ITelegramBotClient _botClient;
        private static string _dbConnection;
        public BotController(IOptionsSnapshot<AppSettings> appSettings)
        {

            _tlgBotToken = appSettings.Value.TlgBotToken;
            _botClient = new TelegramBotClient(_tlgBotToken);
            _dbConnection = appSettings.Value.DBConnection;
            var cts = new CancellationTokenSource(); // прерыватель соединения с ботом
            var updateHandler = new UpdateHandler(appSettings, _botClient, _dbConnection);            


            _botClient.StartReceiving(updateHandler.HandleUpdateAsync,
            updateHandler.HandleErrorAsync,
            new ReceiverOptions()
            {
                AllowedUpdates = [UpdateType.Message]
            }, cts.Token);
        }
    }
}
