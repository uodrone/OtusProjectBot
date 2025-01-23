using Microsoft.Extensions.Options;
using static Telegram.Bot.TelegramBotClient;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types;
using HRProBot.Models;
using Microsoft.Extensions.Logging;
using System.Threading;
using HRProBot.Interfaces;

namespace HRProBot.Controllers
{
    public class BotController
    {
        private readonly ILogger<HomeController> _logger;
        private static string _tlgBotToken;
        private static string[] _administrators;
        private static GoogleSheetsController _googleSheets;
        private static ITelegramBotClient _botClient;
        public BotController(IOptionsSnapshot<AppSettings> appSettings)
        {

            _tlgBotToken = appSettings.Value.TlgBotToken;
            _administrators = appSettings.Value.TlgBotAdministrators.Split(';');
            _googleSheets = new GoogleSheetsController(appSettings);
            _botClient = new TelegramBotClient(_tlgBotToken);
            var cts = new CancellationTokenSource(); // прерыватель соединения с ботом

            _botClient.StartReceiving(UpdateHandler,
            ErrorHandler,
            new ReceiverOptions()
            {
                AllowedUpdates = [UpdateType.Message]
            }, cts.Token);
        }
        /// <summary>
        /// Обработчик ошибок бота
        /// </summary>
        /// <param name="botClient"></param>
        /// <param name="ex"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private static async Task ErrorHandler(ITelegramBotClient botClient, Exception ex, CancellationToken token)
        {

        }
        /// <summary>
        /// Обработчик сообщений
        /// </summary>
        /// <param name="botClient"></param>
        /// <param name="update"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private static async Task UpdateHandler(ITelegramBotClient botClient, Update update, CancellationToken token)
        {
            var Me = await botClient.GetMe();
            var UserParams = update.Message?.From;
            string? BotName = Me.FirstName; //имя бота            
            long ChatId = update.Message.Chat.Id;
            var BotUser = new BotUser();

            if (UserParams is null)
            {
                Console.WriteLine("Message is null");
                return;
            }

            if (update.Type == UpdateType.Message && update.Message.Type == MessageType.Text)
            {
                switch (update.Message.Text)
                {
                    case "/start":
                        await StartMessage(botClient, token, ChatId, UserParams);
                        break;
                    case "Подписаться на курс":
                        await SubcribeToTrainingCource(ChatId, token);
                        break;
                    case "Узнать об экспертах":
                        botClient.SendTextMessageAsync(ChatId, $"Ознакомьтесь с нашими экспертами");
                        break;
                    case "О системе HR Pro":
                        botClient.SendTextMessageAsync(ChatId, $"Вот больше информации о продукте HR Pro");
                        break;
                    case "Задать вопрос эксперту":
                        botClient.SendTextMessageAsync(ChatId, $"Наш эксперт ответит на ваш вопрос в течение 3 рабочих дней. Чтобы сформировать обращение мы должны знать ваши данные.");
                        break;
                    default:
                        botClient.SendTextMessageAsync(ChatId, $"Попробуйте еще раз! Ник: {UserParams.Username}, Имя: {UserParams.FirstName}, id: {UserParams.Id} ");
                        break;
                }
            }
        }

        /// <summary>
        /// Проверка является ли пользователь администратором
        /// </summary>
        /// <param name="userParams"></param>
        /// <returns></returns>
        private static bool IsBotAdministrator (User? userParams)
        {
            User? UserParams = userParams;
            bool IsUserAdmin = false;

            foreach (string admin in _administrators)
            {
                if (admin == userParams.Id.ToString())
                {
                    IsUserAdmin = true;
                }
            }

            return IsUserAdmin;
        }

        static async Task StartMessage(ITelegramBotClient botClient, CancellationToken token, long ChatId, User? UserParams)
        {
            //string StartMessage = _googleSheets.GetData("Sheet1!A1:A1");
            string StartMessage = _googleSheets.GetData("Лист1!A1:C2");
            var Buttons = new ReplyKeyboardMarkup(
                            new[]
                            {
                                new[] {
                                    new KeyboardButton("Подписаться на курс"),
                                    new KeyboardButton("Узнать об экспертах")
                                },
                                new[] {
                                    new KeyboardButton("О системе HR Pro"),
                                    new KeyboardButton("Задать вопрос эксперту")
                                }
                            });
            Buttons.ResizeKeyboard = true;

            if (IsBotAdministrator(UserParams))
            {
                await botClient.SendTextMessageAsync(ChatId, "Пользователь является администратором и ему доступны особые команды");
            }
            await botClient.SendTextMessageAsync(ChatId, StartMessage, replyMarkup: Buttons);
        }

        static async Task SubcribeToTrainingCource(long chatId, CancellationToken cancellationToken)
        {
            await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Вы выбрали подписку на курс. Введите ваш email:",
            cancellationToken: cancellationToken);

        }

        static void GetUserData(ITelegramBotClient botClient, Update update, BotUser BotUser)
        {
            long ChatId = update.Message.Chat.Id;            
            botClient.SendTextMessageAsync(ChatId, "Введите ваше имя");
            BotUser.Name = update.Message.Text;
        }
        
    }
}
