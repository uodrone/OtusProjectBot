using Microsoft.Extensions.Options;
using static Telegram.Bot.TelegramBotClient;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types;
using HRProBot.Models;

namespace HRProBot.Controllers
{
    public class BotController
    {
        private readonly ILogger<HomeController> _logger;
        private string _TlgBotToken;
        private static string[] _Administrators;
        public BotController(IOptionsSnapshot<AppSettings> appSettings)
        {

            _TlgBotToken = appSettings.Value.TlgBotToken;
            _Administrators = appSettings.Value.TlgBotAdministrators.Split(';');

            var initBot = new TelegramBotClient(_TlgBotToken);
            var cts = new CancellationTokenSource(); // прерыватель соединения с ботом

            initBot.StartReceiving(UpdateReceived,
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
        private static async Task UpdateReceived(ITelegramBotClient botClient, Update update, CancellationToken token)
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

            if (update.Type == UpdateType.Message)
            {
                if (update.Message.Type == MessageType.Text)
                {

                    switch (update.Message.Text)
                    {
                        case "/start":
                            StartMessage(botClient, UserParams, update);
                            break;
                        case "Подписаться на курс":
                            botClient.SendTextMessageAsync(ChatId, $"Вы подписаны на курс");
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

            foreach (string admin in _Administrators)
            {
                if (admin == userParams.Id.ToString())
                {
                    IsUserAdmin = true;
                }
            }

            return IsUserAdmin;
        }

        static void StartMessage(ITelegramBotClient botClient, User? UserParams, Update update)
        {
            string StartMessage = "Привет, я бот HR Pro. С моей момощью Вы можете:\r\n•\tУзнать больше о системе HR Pro\r\n•\tУзнать больше об экспертах\r\n•\tЗадать вопрос эксперту\r\n•\tПодписаться на курсы обучения HR Pro\r\n";
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
                botClient.SendTextMessageAsync(update.Message.Chat.Id, "Пользователь является администратором и ему доступны особые команды");
            }
            botClient.SendTextMessageAsync(update.Message.Chat.Id, StartMessage, replyMarkup: Buttons);            
        }

        static void GetUserData(ITelegramBotClient botClient, Update update, BotUser BotUser)
        {
            long ChatId = update.Message.Chat.Id;            
            botClient.SendTextMessageAsync(ChatId, "Введите ваше имя");
            BotUser.Name = update.Message.Text;
        }
    }
}
