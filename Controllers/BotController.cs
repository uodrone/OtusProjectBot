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
using System.IO;
using Newtonsoft.Json.Linq;

namespace HRProBot.Controllers
{
    public class BotController
    {
        private readonly ILogger<HomeController> _logger;
        private static string _tlgBotToken;
        private static string[] _administrators;
        private static GoogleSheetsController _googleSheets;
        private static ITelegramBotClient _botClient;
        private static IList<IList<object>> _botMessagesData;
        private static Dictionary<long, BotUser> _userStates = new Dictionary<long, BotUser>();
        public BotController(IOptionsSnapshot<AppSettings> appSettings)
        {

            _tlgBotToken = appSettings.Value.TlgBotToken;
            _administrators = appSettings.Value.TlgBotAdministrators.Split(';');
            _googleSheets = new GoogleSheetsController(appSettings);
            _botClient = new TelegramBotClient(_tlgBotToken);
            var range = appSettings.Value.GoogleSheetsRange;
            _botMessagesData = _googleSheets.GetData(range);
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


            if (update.Type == UpdateType.Message && update.Message.Type == MessageType.Text && UserParams != null)
            {
                if (_userStates.ContainsKey(ChatId))
                {
                    // Если пользователь находится в процессе сбора данных
                    var botUser = _userStates[ChatId];
                    await GetUserData(update, token, botUser);
                    if (botUser.DataCollectStep == 0)
                    {
                        _userStates.Remove(ChatId); // Завершаем сбор данных
                    }
                }
                else
                {
                    // Обработка обычных команд
                    switch (update.Message.Text)
                    {
                        case "🚩 К началу":
                        case "/start":
                            await HandleStartCommand(ChatId, token);
                            break;
                        case "📅 Подписаться на курс":
                        case "/course":
                            await HandleCourseCommand(ChatId, token);
                            break;
                        case "🤵‍♂️ Узнать об экспертах":
                        case "/experts":
                            await HandleExpertsCommand(ChatId, token);
                            break;
                        case "🔍 О системе HR Pro":
                        case "/hrpro":
                            await HandleAboutHrProCommand(ChatId, token);
                            break;
                        case "🙋‍♂️ Задать вопрос эксперту":
                        case "/ask":
                            _userStates[ChatId] = new BotUser { DataCollectStep = 0 };
                            await GetUserData(update, token, _userStates[ChatId]);
                            break;
                        case "/mailing":
                            if (IsBotAdministrator(UserParams))
                            {
                                await SendMessage(ChatId, token, "Массовая рассылка началась", null);
                            }
                            break;
                        case "/testmailing":
                            if (IsBotAdministrator(UserParams))
                            {
                                await SendMessage(ChatId, token, "Отправляю тестовую рассылку", null);
                            }
                            break;
                        case "/report":
                            if (IsBotAdministrator(UserParams))
                            {
                                await SendMessage(ChatId, token, "Формирую отчет в Excel", null);
                            }
                            break;
                        case "/answer":
                            if (IsBotAdministrator(UserParams))
                            {
                                await SendMessage(ChatId, token, "Введите id пользователя, которому вы хотите ответить", null);
                            }
                            break;
                        default:
                            await botClient.SendTextMessageAsync(ChatId, $"Попробуйте еще раз! Ник: {UserParams.Username}, Имя: {UserParams.FirstName}, id: {UserParams.Id} ");
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

            foreach (string admin in _administrators)
            {
                if (admin == userParams.Id.ToString())
                {
                    IsUserAdmin = true;
                }
            }

            return IsUserAdmin;
        }
        /// <summary>
        /// Обработчик стартовой команды
        /// </summary>
        /// <param name="chatId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private static async Task HandleStartCommand(long chatId, CancellationToken cancellationToken)
        {
            string Message = _botMessagesData[1][3].ToString();
            var Buttons = new ReplyKeyboardMarkup(
                new[]
                {
                    new[] {
                        new KeyboardButton("📅 Подписаться на курс"),
                        new KeyboardButton("🤵‍♂️ Узнать об экспертах")
                    },
                    new[] {
                        new KeyboardButton("🔍 О системе HR Pro"),
                        new KeyboardButton("🙋‍♂️ Задать вопрос эксперту")
                    }
                });
            Buttons.ResizeKeyboard = true;

            await SendMessage(chatId, cancellationToken, Message, Buttons);
        }
        /// <summary>
        /// Обработчик записи на курсы
        /// </summary>
        /// <param name="chatId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private static async Task HandleCourseCommand(long chatId, CancellationToken cancellationToken)
        {
            string Message = _botMessagesData[2][3].ToString();
            var Buttons = new ReplyKeyboardMarkup(
                new[] {
                    new KeyboardButton("🚩 К началу")
                });
            Buttons.ResizeKeyboard = true;
            DateTime date = DateTime.Now;
            if (SubcribeToTrainingCource(date))
            {
                await SendMessage(chatId, cancellationToken, Message, Buttons);
            }
        }
        /// <summary>
        /// Обработчик команды с информацией об искспердах
        /// </summary>
        /// <param name="chatId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private static async Task HandleExpertsCommand(long chatId, CancellationToken cancellationToken)
        {
            string Message = _botMessagesData[3][3].ToString();
            var Buttons = new ReplyKeyboardMarkup(
                new[] {
                    new KeyboardButton("🚩 К началу"),
                    new KeyboardButton("🙋‍♂️ Задать вопрос эксперту")
                });
            Buttons.ResizeKeyboard = true;
            await SendMessage(chatId, cancellationToken, Message, Buttons);
        }
        /// <summary>
        /// обработчик команды с информацией об HR Pro
        /// </summary>
        /// <param name="chatId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private static async Task HandleAboutHrProCommand(long chatId, CancellationToken cancellationToken)
        {
            string Message = _botMessagesData[4][3].ToString();
            var Buttons = new ReplyKeyboardMarkup(
                new[] {
                    new KeyboardButton("🚩 К началу")
                });
            Buttons.ResizeKeyboard = true;
            string imageUrl = "https://www.directum.ru/application/images/hr-pro_logo_vertical.png";
            await SendMessage(chatId, cancellationToken, imageUrl, Message, Buttons);
        }

        /// <summary>
        /// Отправка текста
        /// </summary>
        /// <param name="chatId"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="textMessage"></param>
        /// <param name="buttons"></param>
        /// <returns></returns>
        static async Task SendMessage(long chatId, CancellationToken cancellationToken, string textMessage, ReplyKeyboardMarkup? buttons)
        {
            await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: textMessage,
            replyMarkup: buttons,
            cancellationToken: cancellationToken);
        }
        /// <summary>
        /// Отправка фотки с текстом
        /// </summary>
        /// <param name="chatId"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="imageUrl"></param>
        /// <param name="textMessage"></param>
        /// <param name="buttons"></param>
        /// <returns></returns>
        static async Task SendMessage(long chatId, CancellationToken cancellationToken, string imageUrl, string textMessage, ReplyKeyboardMarkup? buttons)
        {
            await _botClient.SendPhotoAsync(
            chatId: chatId,
            imageUrl,
            caption: textMessage,
            replyMarkup: buttons,
            cancellationToken: cancellationToken);
        }
        /// <summary>
        /// Отправка видосика с текстом
        /// </summary>
        /// <param name="chatId"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="imageUrl"></param>
        /// <param name="textMessage"></param>
        /// <param name="buttons"></param>
        /// <returns></returns>
        static async Task SendMessage(long chatId, CancellationToken cancellationToken, string videoUrl, bool isVideo, string textMessage, ReplyKeyboardMarkup? buttons)
        {
            await _botClient.SendVideoAsync(
            chatId: chatId,
            videoUrl,
            caption: textMessage,
            replyMarkup: buttons,
            cancellationToken: cancellationToken);
        }
        /// <summary>
        /// Отправка галереи фоток
        /// </summary>
        /// <param name="chatId"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="images"></param>
        /// <returns></returns>
        static async Task SendMessage(long chatId, CancellationToken cancellationToken, List<InputMediaPhoto> images)
        {
            await _botClient.SendMediaGroupAsync(
            chatId: chatId,
            images,
            cancellationToken: cancellationToken);
        }
        /// <summary>
        /// Отправка галереи видосиков
        /// </summary>
        /// <param name="chatId"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="video"></param>
        /// <returns></returns>
        static async Task SendMessage(long chatId, CancellationToken cancellationToken, List<InputMediaVideo> video)
        {
            await _botClient.SendMediaGroupAsync(
            chatId: chatId,
            video,
            cancellationToken: cancellationToken);
        }
        /// <summary>
        /// Отправка видео кружочком
        /// </summary>
        /// <returns></returns>
        static async Task SendVideoNote(long chatId, CancellationToken cancellationToken, string videoUrl)
        {
            int lastSlashIndex = videoUrl.LastIndexOf('/'); // Находим индекс последнего слэша
            if (lastSlashIndex != -1)
            {
                string fileName = videoUrl.Substring(lastSlashIndex + 1); // Выделяем всё после последнего слэша
                using (var fileStream = System.IO.File.OpenRead(videoUrl))
                {
                    await _botClient.SendVideoNoteAsync(
                        chatId,
                        new InputFileStream(fileStream, fileName),
                        cancellationToken: cancellationToken);
                }
            }            
        }
        

        static bool SubcribeToTrainingCource(DateTime date)
        {
            return true;
        }

        private static async Task GetUserData(Update update, CancellationToken cancellationToken, BotUser botUser)
        {
            long ChatId = update.Message.Chat.Id;

            switch (botUser.DataCollectStep)
            {
                case 0:
                    await SendMessage(ChatId, cancellationToken, "Наш эксперт ответит на ваш вопрос в течение 3 рабочих дней. Чтобы сформировать обращение мы должны знать ваши данные.", null);
                    await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите ваше имя:", null);
                    botUser.DataCollectStep = 1;
                    break;
                case 1:
                    botUser.FirstName = update.Message.Text;
                    await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите вашу фамилию:", null);
                    botUser.DataCollectStep = 2;
                    break;
                case 2:
                    botUser.LastName = update.Message.Text;
                    await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите вашу организацию:", null);
                    botUser.DataCollectStep = 3;
                    break;
                case 3:
                    botUser.Organization = update.Message.Text;
                    await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите ваш телефон:", null);
                    botUser.DataCollectStep = 4;
                    break;
                case 4:
                    botUser.Phone = update.Message.Text;
                    await SendMessage(ChatId, cancellationToken, "Спасибо, ваши данные сохранены", null);
                    await SendMessage(ChatId, cancellationToken, $"Имя: {botUser.FirstName}\nФамилия: {botUser.LastName}\nОрганизация: {botUser.Organization}\nТелефон: {botUser.Phone}", null);
                    botUser.DataCollectStep = 0; // Сброс состояния для нового диалога
                    break;
            }
        }       

    }
}
