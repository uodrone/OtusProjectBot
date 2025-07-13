using Microsoft.Extensions.Options;
using static Telegram.Bot.TelegramBotClient;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using Telegram.Bot;
using HRProBot.Models;
using LinqToDB;
using System.Linq.Expressions;
using static LinqToDB.Common.Configuration;
using NLog;

namespace HRProBot.Controllers
{
    public class BotController
    {
        private static string _tlgBotToken;
        private static ITelegramBotClient _botClient;
        private static string _dbConnection;
        private static Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly CourseController _courseController;
        public BotController(IOptionsSnapshot<AppSettings> appSettings)
        {

            _tlgBotToken = appSettings.Value.TlgBotToken;
            _botClient = new TelegramBotClient(_tlgBotToken);
            _dbConnection = appSettings.Value.DBConnection;
            _courseController = new CourseController(_botClient, appSettings);
            var cts = new CancellationTokenSource();            
            var updateHandler = new UpdateHandler(_courseController, appSettings, _botClient, _dbConnection);            


            try
            {
                _botClient.StartReceiving(updateHandler.HandleUpdateAsync,
                updateHandler.HandleErrorAsync,
                new ReceiverOptions()
                {
                    AllowedUpdates = [UpdateType.Message]
                }, cts.Token);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка инициализации бота: {ex.Message}");
            }
        }
    }
}
