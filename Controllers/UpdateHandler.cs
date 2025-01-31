using HRProBot.Models;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace HRProBot.Controllers
{
    public class UpdateHandler : IUpdateHandler
    {
        private static string[] _administrators;
        private static GoogleSheetsController _googleSheets;
        private static ITelegramBotClient _botClient;
        private static IList<IList<object>> _botMessagesData;
        private static Dictionary<long, BotUser> _userStates = new Dictionary<long, BotUser>();

        public UpdateHandler(IOptionsSnapshot<AppSettings> appSettings, ITelegramBotClient botClient)
        {
            _administrators = appSettings.Value.TlgBotAdministrators.Split(';');
            _googleSheets = new GoogleSheetsController(appSettings);
            _botClient = botClient;
            var range = appSettings.Value.GoogleSheetsRange;
            _botMessagesData = _googleSheets.GetData(range);
            var cts = new CancellationTokenSource(); // прерыватель соединения с ботом
        }

        public async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            var Me = await botClient.GetMe();
            var UserParams = update.Message?.From;
            string? BotName = Me.FirstName; //имя бота            
            long ChatId = update.Message.Chat.Id;
            var User = new BotUser();
            User.Id = UserParams.Id;


            if (update.Type == UpdateType.Message && update.Message.Type == MessageType.Text && UserParams != null)
            {
                if (_userStates.ContainsKey(ChatId))
                {
                    // Если пользователь находится в процессе сбора данных
                    var botUser = _userStates[ChatId];
                    await GetUserData(update, cancellationToken, botUser);
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
                            await HandleStartCommand(ChatId, cancellationToken);
                            break;
                        case "📅 Подписаться на курс":
                        case "/course":
                            await HandleCourseCommand(ChatId, cancellationToken, User);
                            break;
                        case "🤵‍♂️ Узнать об экспертах":
                        case "/experts":
                            await HandleExpertsCommand(ChatId, cancellationToken);
                            break;
                        case "🔍 О системе HR Pro":
                        case "/hrpro":
                            await HandleAboutHrProCommand(ChatId, cancellationToken);
                            break;
                        case "🙋‍♂️ Задать вопрос эксперту":
                        case "/ask":
                            User.DataCollectStep = 0;
                            _userStates[ChatId] = User;
                            await GetUserData(update, cancellationToken, _userStates[ChatId]);
                            break;
                        case "/mailing":
                            if (IsBotAdministrator(UserParams))
                            {
                                await SendMessage(ChatId, cancellationToken, "Массовая рассылка началась", null);
                            }
                            break;
                        case "/testmailing":
                            if (IsBotAdministrator(UserParams))
                            {
                                await SendMessage(ChatId, cancellationToken, "Отправляю тестовую рассылку", null);
                            }
                            break;
                        case "/report":
                            if (IsBotAdministrator(UserParams))
                            {
                                await SendMessage(ChatId, cancellationToken, "Формирую отчет в Excel", null);
                            }
                            break;
                        case "/answer":
                            if (IsBotAdministrator(UserParams))
                            {
                                await SendMessage(ChatId, cancellationToken, "Введите id пользователя, которому вы хотите ответить", null);
                            }
                            break;
                        default:
                            await botClient.SendTextMessageAsync(ChatId, $"Попробуйте еще раз! Ник: {UserParams.Username}, Имя: {UserParams.FirstName}, id: {UserParams.Id} ");
                            break;
                    }
                }
            }
        }

        private static bool IsBotAdministrator(User? userParams)
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
        private static async Task HandleCourseCommand(long chatId, CancellationToken cancellationToken, BotUser user)
        {
            string Message = _botMessagesData[2][3].ToString();
            var Buttons = new ReplyKeyboardMarkup(
                new[] {
            new KeyboardButton("🚩 К началу")
                });
            Buttons.ResizeKeyboard = true;
            DateTime date = DateTime.Now;
            if (!user.IsSubscribed)
            {
                user.IsSubscribed = true;
                user.DateStartSubscribe = date;
                await SendMessage(chatId, cancellationToken, Message, Buttons);
                var courseController = new CourseController(user, _botClient);
                courseController.StartSendingMaterials();
            }
            else
            {
                await SendMessage(chatId, cancellationToken, "Вы уже подписаны на курс. Обучающие материалы выходят каждую неделю. Следите за обновлениями", Buttons);
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
            ReplyKeyboardRemove removeKeyboard = new ReplyKeyboardRemove();

            if (buttons == null)
            {
                await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: textMessage,
                replyMarkup: removeKeyboard,
                cancellationToken: cancellationToken);
            } 
            else
            {
                await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: textMessage,
                replyMarkup: buttons,
                cancellationToken: cancellationToken);
            }
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

        private static async Task GetUserData(Update update, CancellationToken cancellationToken, BotUser botUser)
        {
            long ChatId = update.Message.Chat.Id;
            var regular = new RegularValidation();

            switch (botUser.DataCollectStep)
            {
                case 0:
                    await SendMessage(ChatId, cancellationToken, "Наш эксперт ответит на ваш вопрос в течение 3 рабочих дней. Чтобы сформировать обращение мы должны знать ваши данные.", null);
                    await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите ваше имя:", null);
                    botUser.DataCollectStep = 1;
                    break;
                case 1:
                    if (regular.ValidateName(update.Message.Text))
                    {
                        botUser.FirstName = update.Message.Text;
                        await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите вашу фамилию:", null);
                        botUser.DataCollectStep = 2;
                    }
                    else
                    {
                        await SendMessage(ChatId, cancellationToken, "Имя неверное, введите правильное имя", null);
                    } 
                    break;
                case 2:
                    if (regular.ValidateName(update.Message.Text))
                    {
                        botUser.LastName = update.Message.Text;
                        await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите вашу организацию:", null);
                        botUser.DataCollectStep = 3;
                    }
                    else
                    {
                        await SendMessage(ChatId, cancellationToken, "Фамилия неверная, введите правильную фамилию", null);
                    }                    
                    break;
                case 3:
                    if (regular.ValidateOrganization(update.Message.Text))
                    {
                        botUser.Organization = update.Message.Text;
                        await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите ваш телефон:", null);
                        botUser.DataCollectStep = 4;
                    }
                    else
                    {
                        await SendMessage(ChatId, cancellationToken, "Организация неверная, введите правильную организацию", null);
                    }
                    break;
                case 4:
                    if (regular.ValidatePhone(update.Message.Text))
                    {
                        botUser.Phone = update.Message.Text;
                        var Buttons = new ReplyKeyboardMarkup(
                                       new[] {
                                        new KeyboardButton("🚩 К началу")
                                       });
                        Buttons.ResizeKeyboard = true;
                        await SendMessage(ChatId, cancellationToken, "Спасибо, ваши данные сохранены", null);
                        await SendMessage(ChatId, cancellationToken,
                            $"Имя: {botUser.FirstName}\nФамилия: {botUser.LastName}\nОрганизация: {botUser.Organization}\nТелефон: {botUser.Phone}\nId пользователя: {botUser.Id}",
                            Buttons);
                        botUser.DataCollectStep = 0; // Сброс состояния для нового диалога
                    }
                    else
                    {
                        await SendMessage(ChatId, cancellationToken, "Телефон неверный, введите правильный номер телефона", null);
                    }
                    
                    break;
            }
        }
    }
}
