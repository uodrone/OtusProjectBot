using HRProBot.Models;
using HRProBot.Services;
using LinqToDB;
using LinqToDB.Common;
using LinqToDB.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NLog;
using Npgsql;
using OfficeOpenXml;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
        private static IList<IList<object>> _botMailingData;
        private static string _dbConnection;
        private static BotUser _user;
        private static AppDBUpdate _appDbUpdate = new AppDBUpdate();
        private static long _answerUserId;
        private static bool _askFlag;
        private static bool _answerFlag;
        private static bool _mailingFlag;
        private static MessageSender _messageSender;
        private static ReplyKeyboardMarkup _standardButtons;
        private static ReplyKeyboardMarkup _startButton;
        private static IOptionsSnapshot<AppSettings> _appSettings;
        private static Logger _logger = LogManager.GetCurrentClassLogger();
        private static ConcurrentDictionary<string, MediaGroup> _mediaGroups = new ConcurrentDictionary<string, MediaGroup>();
        private static CourseController _courseController;

        public UpdateHandler(CourseController courseController, IOptionsSnapshot<AppSettings> appSettings, ITelegramBotClient botClient, string dbConnection)
        {
            try
            {
                _appSettings = appSettings;
                _administrators = _appSettings.Value.TlgBotAdministrators.Split(';');
                _googleSheets = new GoogleSheetsController(_appSettings);
                _botClient = botClient;
                _dbConnection = dbConnection;
                _botMessagesData = _googleSheets.GetData(_appSettings.Value.GoogleSheetsRange);              
                _messageSender = new MessageSender(botClient);
                _standardButtons = _messageSender.GetStandardButtons();
                _startButton = _messageSender.GetStartButton();
                var cts = new CancellationTokenSource(); // прерыватель соединения с ботом
                _courseController = courseController;
            }
            catch (Exception ex) {
                _logger.Error(ex, $"Ошибка подключения к сервису: {ex.Message}");
            }
        }

        public async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("Неизвестная ошибка обработки");
        }

        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            // Игнорируем сообщения без отправителя и всё, кроме текстовых или медиа-сообщений
            if (update.Type != UpdateType.Message || update.Message == null || update.Message.From == null)
            {
                return;
            }

            var me = await botClient.GetMe();
            var userParams = update.Message.From;
            long chatId = update.Message.Chat.Id;

            BotUser user = null;

            using (var db = new DataConnection(ProviderName.PostgreSQL, _dbConnection))
            {
                try
                {
                    var table = db.GetTable<BotUser>();
                    user = await table.FirstOrDefaultAsync(u => u.Id == userParams.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Ошибка при загрузке пользователя: {ex.Message}");
                    return;
                }

                if (user == null)
                {
                    // Создаем нового пользователя
                    user = new BotUser
                    {
                        Id = userParams.Id,
                        UserName = userParams.Username
                    };
                    db.Insert(user);
                }
            }

            // Проверяем только текущего пользователя как подстраховку для фонового сервиса
            // Это быстрая проверка на случай, если фоновый сервис пропустил отправку
            await _courseController.CheckAllSubscribedUsersAsync();

            // Загружаем пользователя заново перед каждой проверкой
            BotUser latestUser;
            using (var db = new DataConnection(ProviderName.PostgreSQL, _dbConnection))
            {
                latestUser = await db.GetTable<BotUser>()
                    .Where(u => u.Id == user.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            // Если начали собирать данные, но они ещё не собраны до конца — дособираем
            if (latestUser.IsCollectingData && latestUser.DataCollectStep > 0 && latestUser.DataCollectStep < 6)
            {
                await GetUserData(update, latestUser, cancellationToken);
                return;
            }

            if (_answerFlag)
            {
                await AnswerToUser(update, cancellationToken);
                return;
            }

            if (_mailingFlag)
            {
                if (update.Message.Text == "✅ Да")
                {
                    Mailing(update, cancellationToken, false);
                }
                else
                {
                    await HandleStartCommand(chatId, cancellationToken);
                }
                _mailingFlag = false;
                return;
            }

            // Обработка медиагрупп
            if (!string.IsNullOrEmpty(update.Message.MediaGroupId))
            {
                var mediaGroupId = update.Message.MediaGroupId;

                // Проверяем, есть ли хотя бы одно фото
                if (update.Message.Photo == null || !update.Message.Photo.Any())
                {
                    await _messageSender.SendMessage(update.Message.Chat.Id, cancellationToken, "Получено сообщение с медиагруппой, но фото не найдено.", null);
                    return;
                }

                var photoFileId = update.Message.Photo.Last().FileId;
                var mediaGroup = _mediaGroups.GetOrAdd(mediaGroupId, id => new MediaGroup(id));

                if (mediaGroup.ContainsFile(photoFileId))
                {
                    return;
                }

                mediaGroup.AddFile(update.Message.Chat.Id, photoFileId);

                if (!string.IsNullOrEmpty(update.Message.Caption))
                {
                    if (mediaGroup.Caption.Length < 1024)
                    {
                        mediaGroup.Caption = update.Message.Caption;
                    }
                    else
                    {
                        await _messageSender.SendMessage(me.Id, cancellationToken, "Текст слишком длинный", null);
                    }
                }

                if (!mediaGroup.IsProcessing)
                {
                    mediaGroup.IsProcessing = true;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var timeout = TimeSpan.FromSeconds(5);
                            var sw = Stopwatch.StartNew();

                            while (sw.Elapsed < timeout)
                            {
                                await Task.Delay(100, cancellationToken);
                                if (mediaGroup.IsComplete())
                                {
                                    await HandleMediaGroup(_botClient, mediaGroup, cancellationToken);
                                    _mediaGroups.TryRemove(mediaGroupId, out _);
                                    return;
                                }
                            }

                            if (mediaGroup.Files.Count > 0)
                            {
                                await HandleMediaGroup(_botClient, mediaGroup, cancellationToken);
                            }

                            _mediaGroups.TryRemove(mediaGroupId, out _);
                        }
                        catch (OperationCanceledException)
                        {
                            _mediaGroups.TryRemove(mediaGroupId, out _);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, $"Ошибка при обработке медиагруппы ID: {mediaGroupId}");
                            _mediaGroups.TryRemove(mediaGroupId, out _);
                        }
                    }, cancellationToken);
                }

                return;
            }

            // Обработка текстовых команд
            if (update.Message.Type == MessageType.Text)
            {
                await HandleTextCommands(update, latestUser, cancellationToken);
            }
            else
            {
                if (_answerFlag)
                {
                    await AnswerToUser(update, cancellationToken);
                }
                else
                {
                    await _messageSender.SendMessage(me.Id, cancellationToken, "Неподдерживаемый тип сообщения. Используйте текстовые команды.", null);
                }
            }
        }

        /// <summary>
        /// Обработка медиагрупп
        /// </summary>
        private static async Task HandleMediaGroup(ITelegramBotClient botClient, MediaGroup mediaGroup, CancellationToken cancellationToken)
        {
            // Формируем медиагруппу для отправки
            var mediaGroupToSend = mediaGroup.Files
                .Select((item, index) => new InputMediaPhoto(item.FileId)
                {
                    Caption = index == 0 ? mediaGroup.Caption : null // Текст только для первой фотографии
                })
                .ToList();

            // Отправляем медиагруппу
            await botClient.SendMediaGroupAsync(
                chatId: _answerUserId,
                media: mediaGroupToSend,
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Обработка текстовых команд
        /// </summary>
        private async Task HandleTextCommands(Update update, BotUser user, CancellationToken cancellationToken)
        {
            long ChatId = update.Message.Chat.Id;
            var appDbUpdate = new AppDBUpdate();

            switch (update.Message.Text)
            {
                case "🚩 К началу":
                case "/start":
                    await HandleStartCommand(ChatId, cancellationToken);
                    break;

                case "📅 Подписаться на курс обучения":
                case "/course":
                    _askFlag = false;

                    using (var db = new DataConnection(ProviderName.PostgreSQL, _dbConnection))
                    {
                        var currentUser = await db.GetTable<BotUser>().FirstOrDefaultAsync(u => u.Id == ChatId, cancellationToken);

                        if (currentUser != null)
                        {
                            if (currentUser.DataCollectStep == 5)
                                currentUser.DataCollectStep = 6;

                            currentUser.IsCollectingData = true;

                            appDbUpdate.UpdateBotUserFields(currentUser, _dbConnection,
                                u => u.DataCollectStep,
                                u => u.IsCollectingData);
                        }
                    }

                    await GetUserData(update, user, cancellationToken);
                    break;

                case "🤵‍♂️ Об экспертах":
                case "/experts":
                    await HandleExpertsCommand(ChatId, cancellationToken);
                    break;

                case "🔍 О системе HR Pro":
                case "/hrpro":
                    await HandleAboutHrProCommand(ChatId, cancellationToken);
                    break;

                case "💪 О решениях":
                case "/solutions":
                    await HandleAboutSolutionsCommand(ChatId, cancellationToken);
                    break;

                case "🙋‍♂️ Задать вопрос эксперту":
                case "/ask":
                    _askFlag = true;

                    using (var db = new DataConnection(ProviderName.PostgreSQL, _dbConnection))
                    {
                        var currentUser = await db.GetTable<BotUser>().FirstOrDefaultAsync(u => u.Id == ChatId, cancellationToken);

                        if (currentUser != null)
                        {
                            if (currentUser.DataCollectStep == 6)
                                currentUser.DataCollectStep = 5;

                            currentUser.IsCollectingData = true;

                            appDbUpdate.UpdateBotUserFields(currentUser, _dbConnection,
                                u => u.DataCollectStep,
                                u => u.IsCollectingData);
                        }
                    }

                    await GetUserData(update, user, cancellationToken);
                    break;

                case "/mailing":
                    if (IsBotAdministrator(update.Message.From))
                    {
                        _botMailingData = _googleSheets.GetData(_appSettings.Value.GoogleSheetsMailing);
                        var buttons = new ReplyKeyboardMarkup(
                            new[]
                            {
                        new KeyboardButton("✅ Да"),
                        new KeyboardButton("❌ Нет")
                            });
                        buttons.ResizeKeyboard = true;

                        await _messageSender.SendMessage(ChatId, cancellationToken, "Вы точно уверены что хотите отправить массовую рассылку?", buttons);
                        _mailingFlag = true;
                    }
                    break;

                case "/testmailing":
                    if (IsBotAdministrator(update.Message.From))
                    {
                        _botMailingData = _googleSheets.GetData(_appSettings.Value.GoogleSheetsMailing);
                        await _messageSender.SendMessage(ChatId, cancellationToken, "Тестовая массовая рассылка началась", null);
                        Mailing(update, cancellationToken, true);
                    }
                    break;

                case "/report":
                    if (IsBotAdministrator(update.Message.From))
                    {
                        var reportStream = GenerateUserReport();
                        await _botClient.SendDocumentAsync(
                            chatId: ChatId,
                            document: new InputFileStream(reportStream, "UsersReport.xlsx"),
                            caption: "Отчет по пользователям",
                            cancellationToken: cancellationToken);
                    }
                    break;

                case "/answer":
                    if (IsBotAdministrator(update.Message.From))
                    {
                        await _messageSender.SendMessage(ChatId, cancellationToken, "Введите id пользователя, которому вы хотите ответить", null);
                        _answerFlag = true;
                    }
                    break;

                default:
                    // Проверяем голосование до основного текста
                    if (user.IsVotingForCourse && IsRating(update.Message.Text))
                    {
                        await HandleCourseRating(update, cancellationToken);
                        return;
                    }

                    // Если это обычное сообщение, но не команда — выводим ошибку
                    var Buttons = new ReplyKeyboardMarkup(new[] { new KeyboardButton("🚩 К началу") });
                    Buttons.ResizeKeyboard = true;
                    var Message = $"Неверная команда. Попробуйте еще раз!";
                    await _messageSender.SendMessage(ChatId, cancellationToken, Message, Buttons);
                    break;
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
            try
            {
                string message = _botMessagesData[1][3].ToString();
                string imageUrl = _botMessagesData[1][4].ToString();

                await _messageSender.SendPhotoWithCaption(chatId, cancellationToken, imageUrl, message, _standardButtons);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка получения данных стартового меню: {ex.Message}");
            }
        }
        /// <summary>
        /// Обработчик записи на курсы
        /// </summary>
        /// <param name="update"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private static async Task<BotUser> HandleCourseCommand(long chatId, CancellationToken cancellationToken)
        {
            try
            {
                using (var db = new DataConnection(ProviderName.PostgreSQL, _dbConnection))
                {
                    var user = await db.GetTable<BotUser>()
                        .FirstOrDefaultAsync(u => u.Id == chatId, cancellationToken);

                    if (user != null)
                    {
                        await _courseController.SubscribeUserToCourse(user);
                    }

                    return user;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка получения данных о курсах: {ex.Message}");
                throw;
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
            try
            {
                string message = _botMessagesData[3][3].ToString();
                string imagesUrl = _botMessagesData[3][4].ToString();

                // Создаем список фоток из урлов
                var mediaGroup = await _messageSender.ConvertImgStringToMediaListAsync(imagesUrl);
                await _messageSender.SendMediaGroupWithCaption(chatId, cancellationToken, mediaGroup, message, _standardButtons);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка получения данных об экспертах: {ex.Message}");
            }
        }
        /// <summary>
        /// обработчик команды с информацией об HR Pro
        /// </summary>
        /// <param name="chatId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private static async Task HandleAboutHrProCommand(long chatId, CancellationToken cancellationToken)
        {
            try
            {
                string Message = _botMessagesData[4][3].ToString();
                string imageUrl = _botMessagesData[4][4].ToString();
                await _messageSender.SendPhotoWithCaption(chatId, cancellationToken, imageUrl, Message, _standardButtons);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка получения данных об HR Pro: {ex.Message}");
            }
        }
        /// <summary>
        /// обработчик команды с информацией о решениях HR Pro
        /// </summary>
        /// <param name="chatId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private static async Task HandleAboutSolutionsCommand(long chatId, CancellationToken cancellationToken)
        {
            try
            {
                string message = _botMessagesData[6][3].ToString();
                string imagesUrl = _botMessagesData[6][4].ToString();
                // Создаем список фоток из урлов
                var mediaGroup = await _messageSender.ConvertImgStringToMediaListAsync(imagesUrl);
                await _messageSender.SendMediaGroupWithCaption(chatId, cancellationToken, mediaGroup, message, _standardButtons);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка получения решений: {ex.Message}");
            }
        }

        private async Task GetUserData(Update update, BotUser user, CancellationToken cancellationToken)
        {
            long ChatId = update.Message.Chat.Id;
            var regular = new RegularValidation();
            var buttonsWithContact = new ReplyKeyboardMarkup(
                new[]
                {
            new KeyboardButton[] {
                KeyboardButton.WithRequestContact("📱 Отправить номер телефона"),
                new KeyboardButton("🚩 К началу")
            }
                });
            buttonsWithContact.ResizeKeyboard = true;

            // Загружаем пользователя заново перед началом работы
            using (var db = new DataConnection(ProviderName.PostgreSQL, _dbConnection))
            {
                user = await db.GetTable<BotUser>().FirstOrDefaultAsync(u => u.Id == ChatId, cancellationToken);
            }

            if (user == null)
            {
                await HandleStartCommand(ChatId, cancellationToken);
                return;
            }

            switch (user.DataCollectStep)
            {
                case 0:
                    if (update.Message.Text == "🚩 К началу" || update.Message.Text == "/start")
                    {
                        await HandleStartCommand(ChatId, cancellationToken);
                        user.IsCollectingData = false;
                        _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                            u => u.IsCollectingData);
                        return;
                    }

                    if (_askFlag)
                    {
                        await _messageSender.SendMessage(ChatId, cancellationToken, _botMessagesData[7][3].ToString(), null);
                    }
                    else
                    {
                        await _messageSender.SendMessage(ChatId, cancellationToken, _botMessagesData[8][3].ToString(), null);
                    }

                    await _messageSender.SendMessage(ChatId, cancellationToken, "Пожалуйста, введи свое имя:", _startButton);
                    user.DataCollectStep = 1;
                    _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                        u => u.DataCollectStep);
                    break;

                case 1:
                    if (update.Message.Text == "🚩 К началу" || update.Message.Text == "/start")
                    {
                        await HandleStartCommand(ChatId, cancellationToken);
                        user.IsCollectingData = false;
                        _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                            u => u.IsCollectingData);
                        return;
                    }
                    else if (update.Message.Text == "📅 Подписаться на курс обучения" ||
                             update.Message.Text == "🙋‍♂️ Задать вопрос эксперту" ||
                             update.Message.Text == "/ask")
                    {
                        if (_askFlag)
                        {
                            await _messageSender.SendMessage(ChatId, cancellationToken, _botMessagesData[7][3].ToString(), null);
                        }
                        else
                        {
                            await _messageSender.SendMessage(ChatId, cancellationToken, _botMessagesData[8][3].ToString(), null);
                        }

                        await _messageSender.SendMessage(ChatId, cancellationToken, "Пожалуйста, введи имя:", _startButton);
                        return;
                    }
                    else if (regular.ValidateName(update.Message.Text))
                    {
                        user.FirstName = update.Message.Text;
                        await _messageSender.SendMessage(ChatId, cancellationToken, "Пожалуйста, введи фамилию:", _startButton);
                        user.DataCollectStep = 2;
                        _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                            u => u.FirstName,
                            u => u.DataCollectStep);
                    }
                    else
                    {
                        await _messageSender.SendMessage(ChatId, cancellationToken, "Имя неверное, введи правильное имя", _startButton);
                    }
                    break;

                case 2:
                    if (update.Message.Text == "🚩 К началу" || update.Message.Text == "/start")
                    {
                        await HandleStartCommand(ChatId, cancellationToken);
                        user.IsCollectingData = false;
                        _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                            u => u.IsCollectingData);
                        return;
                    }
                    else if (update.Message.Text == "📅 Подписаться на курс обучения" ||
                             update.Message.Text == "🙋‍♂️ Задать вопрос эксперту" ||
                             update.Message.Text == "/ask")
                    {
                        await _messageSender.SendMessage(ChatId, cancellationToken, "Пожалуйста, введи фамилию:", _startButton);
                        return;
                    }
                    else if (!string.IsNullOrEmpty(update.Message.Text))
                    {
                        user.LastName = update.Message.Text;
                        await _messageSender.SendMessage(ChatId, cancellationToken, "Пожалуйста, введи организацию:", _startButton);
                        user.DataCollectStep = 3;
                        _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                            u => u.LastName,
                            u => u.DataCollectStep);
                    }
                    else
                    {
                        await _messageSender.SendMessage(ChatId, cancellationToken, "Фамилия неверная, введи правильную фамилию", _startButton);
                    }
                    break;

                case 3:
                    if (update.Message.Text == "🚩 К началу" || update.Message.Text == "/start")
                    {
                        await HandleStartCommand(ChatId, cancellationToken);
                        user.IsCollectingData = false;
                        _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                            u => u.IsCollectingData);
                        return;
                    }
                    else if (update.Message.Text == "📅 Подписаться на курс обучения" ||
                             update.Message.Text == "🙋‍♂️ Задать вопрос эксперту" ||
                             update.Message.Text == "/ask")
                    {
                        await _messageSender.SendMessage(ChatId, cancellationToken, "Пожалуйста, введи организацию:", _startButton);
                        return;
                    }
                    else if (regular.ValidateOrganization(update.Message.Text))
                    {
                        user.Organization = update.Message.Text;
                        await _messageSender.SendMessage(ChatId, cancellationToken, "Пожалуйста, введи номер телефона:", buttonsWithContact);
                        user.DataCollectStep = 4;
                        _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                            u => u.Organization,
                            u => u.DataCollectStep);
                    }
                    else
                    {
                        await _messageSender.SendMessage(ChatId, cancellationToken, "Организация неверная, введи правильную организацию", _startButton);
                    }
                    break;

                case 4:
                    if (update.Message.Text == "🚩 К началу" || update.Message.Text == "/start")
                    {
                        await HandleStartCommand(ChatId, cancellationToken);
                        user.IsCollectingData = false;
                        _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                            u => u.IsCollectingData);
                        return;
                    }
                    else if (update.Message.Text == "📅 Подписаться на курс обучения" ||
                             update.Message.Text == "🙋‍♂️ Задать вопрос эксперту" ||
                             update.Message.Text == "/ask")
                    {
                        await _messageSender.SendMessage(ChatId, cancellationToken, "Пожалуйста, введи номер телефона:", buttonsWithContact);
                        return;
                    }
                    else if (update.Message.Contact != null && regular.ValidatePhone(update.Message.Contact.PhoneNumber))
                    {
                        user.Phone = update.Message.Contact.PhoneNumber;
                        _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                            u => u.Phone,
                            u => u.DataCollectStep);
                        await _messageSender.SendMessage(ChatId, cancellationToken, "Спасибо, данные сохранены", null);
                        await _messageSender.SendMessage(ChatId, cancellationToken,
                            $"Имя: {user.FirstName}\nФамилия: {user.LastName}\nОрганизация: {user.Organization}\nТелефон: {user.Phone}", null);

                        user.DataCollectStep = 5;
                        if (!_askFlag)
                        {                            
                            user = await HandleCourseCommand(ChatId, cancellationToken);
                            user.DataCollectStep = 6;
                        }
                        else
                        {
                            await _messageSender.SendMessage(ChatId, cancellationToken, "Пожалуйста, введи вопрос эксперту:", _startButton);
                        }

                        _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                            u => u.Phone,
                            u => u.DataCollectStep);
                    }
                    else if (regular.ValidatePhone(update.Message.Text))
                    {
                        user.Phone = update.Message.Text;
                        await _messageSender.SendMessage(ChatId, cancellationToken, "Спасибо, данные сохранены", null);
                        await _messageSender.SendMessage(ChatId, cancellationToken,
                            $"Имя: {user.FirstName}\nФамилия: {user.LastName}\nОрганизация: {user.Organization}\nТелефон: {user.Phone}", null);

                        user.DataCollectStep = 5;
                        if (!_askFlag)
                        {                            
                            user = await HandleCourseCommand(ChatId, cancellationToken);
                            user.DataCollectStep = 6;
                        }
                        else
                        {
                            await _messageSender.SendMessage(ChatId, cancellationToken, "Пожалуйста, введи вопрос эксперту:", _startButton);
                        }

                        _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                            u => u.Phone,
                            u => u.DataCollectStep);
                    }
                    else
                    {
                        await _messageSender.SendMessage(ChatId, cancellationToken, "Телефон неверный, введи правильный номер телефона", buttonsWithContact);
                    }
                    break;

                case 5:
                    if (update.Message.Text == "🚩 К началу" || update.Message.Text == "/start")
                    {
                        await HandleStartCommand(ChatId, cancellationToken);
                        user.IsCollectingData = false;
                        _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                            u => u.IsCollectingData);
                        return;
                    }
                    else if (!string.IsNullOrEmpty(update.Message.Text))
                    {
                        if (update.Message.Text == "🙋‍♂️ Задать вопрос эксперту" || update.Message.Text == "/ask")
                        {
                            await _messageSender.SendMessage(ChatId, cancellationToken, "Пожалуйста, введи вопрос эксперту:", _startButton);
                            return;
                        }

                        var question = new UserQuestion
                        {
                            BotUserId = user.Id,
                            QuestionText = update.Message.Text
                        };

                        using (var db = new DataConnection(ProviderName.PostgreSQL, _dbConnection))
                        {
                            db.Insert(question);
                        }

                        foreach (string admin in _administrators)
                        {
                            await _messageSender.SendMessage(Int64.Parse(admin), cancellationToken,
                                $"Новый вопрос от пользователя {user.UserName} ({user.Id}): {question.QuestionText}", _standardButtons);
                        }

                        await _messageSender.SendMessage(ChatId, cancellationToken, "Спасибо, вопрос получен!", _standardButtons);
                        user.DataCollectStep = 6;
                        _askFlag = false;
                        _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                            u => u.DataCollectStep);
                    }
                    else
                    {
                        await _messageSender.SendMessage(ChatId, cancellationToken, "Введи не пустой вопрос", null);
                    }
                    break;

                case 6:
                    if (!_askFlag)
                    {
                        user = await HandleCourseCommand(ChatId, cancellationToken);
                        user.IsCollectingData = false;
                        user.DataCollectStep = 6;                        
                        _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                            u => u.IsCollectingData);
                    }
                    break;
            }
        }

        public static MemoryStream GenerateUserReport()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("UsersReport");

            // Заголовки столбцов
            worksheet.Cells[1, 1].Value = "ID";
            worksheet.Cells[1, 2].Value = "Ник";
            worksheet.Cells[1, 3].Value = "Имя";
            worksheet.Cells[1, 4].Value = "Фамилия";
            worksheet.Cells[1, 5].Value = "Организация";
            worksheet.Cells[1, 6].Value = "Телефон";
            worksheet.Cells[1, 7].Value = "Вопросы";
            worksheet.Cells[1, 8].Value = "Ответы";
            worksheet.Cells[1, 9].Value = "Подписан на курс?";
            worksheet.Cells[1, 10].Value = "Дата подписки на курс";
            worksheet.Cells[1, 11].Value = "Этап отправки курсов";
            worksheet.Cells[1, 12].Value = "Итоговая оценка";

            // Стиль для заголовков
            using (var range = worksheet.Cells[1, 1, 1, 12])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);
            }

            // Включаем фильтры для первой строки
            worksheet.Cells[1, 1, 1, 12].AutoFilter = true;

            using (var db = new LinqToDB.Data.DataConnection(ProviderName.PostgreSQL, _dbConnection))
            {
                // Получаем всех пользователей
                var allUsers = db.GetTable<BotUser>().ToList();
                // Получаем все вопросы
                var allQuestions = db.GetTable<UserQuestion>().ToList();
                // Получаем все ответы
                var allAnswers = db.GetTable<UserAnswer>().ToList();

                // Заполнение данных
                int row = 2;
                foreach (var user in allUsers)
                {
                    worksheet.Cells[row, 1].Value = user.Id;
                    worksheet.Cells[row, 2].Value = user.UserName;
                    worksheet.Cells[row, 3].Value = user.FirstName;
                    worksheet.Cells[row, 4].Value = user.LastName;
                    worksheet.Cells[row, 5].Value = user.Organization;
                    worksheet.Cells[row, 6].Value = user.Phone;

                    // Получаем вопросы пользователя и объединяем их через ";"
                    var userQuestions = allQuestions
                        .Where(q => q.BotUserId == user.Id)
                        .Select(q => q.QuestionText)
                        .ToList();
                    worksheet.Cells[row, 7].Value = string.Join("; ", userQuestions);
                    // Получаем ответы пользователя и объединяем их через ";"
                    var userAnswers = allAnswers
                        .Where(q => q.BotUserId == user.Id)
                        .Select(q => q.AnswerText)
                        .ToList();
                    worksheet.Cells[row, 8].Value = string.Join("; ", userAnswers);

                    worksheet.Cells[row, 9].Value = user.IsSubscribed ? "Yes" : "No";
                    worksheet.Cells[row, 10].Value = user.DateStartSubscribe?.ToString("dd.MM.yyyy");
                    worksheet.Cells[row, 11].Value = user.CurrentCourseStep;
                    worksheet.Cells[row, 12].Value = user.CourseAssesment;

                    // Включаем перенос текста для ячеек с вопросами и ответами
                    worksheet.Cells[row, 7].Style.WrapText = true; // Вопросы
                    worksheet.Cells[row, 8].Style.WrapText = true; // Ответы

                    row++;
                }
            }

            // Авто-ширина для колонок
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            // Устанавливаем максимальную ширину столбцов вопросов и ответов
            worksheet.Column(7).Width = 100; // Вопросы
            worksheet.Column(8).Width = 100; // Ответы

            var stream = new MemoryStream();
            package.SaveAs(stream);
            stream.Position = 0;
            return stream;
        }

        private static async Task AnswerToUser(Update update, CancellationToken cancellationToken)
        {
            try
            {
                long chatId = update.Message.Chat.Id;

                // Если пользователь нажал "🚩 К началу" или отправил команду /start
                if (update.Message.Text == "🚩 К началу" || update.Message.Text == "/start")
                {
                    await HandleStartCommand(chatId, cancellationToken);
                    return;
                }

                // Если пользователь повторно отправил команду /answer
                if (update.Message.Text == "/answer")
                {
                    _answerFlag = true;
                    await _messageSender.SendMessage(chatId, cancellationToken, "Введите ID пользователя, которому вы хотите ответить:", _startButton);
                    return;
                }

                // Если _answerUserId (пользователя, которому отвечаем) ещё не установлен, ожидаем ввода ID пользователя
                if (_answerUserId == 0)
                {
                    if (long.TryParse(update.Message.Text, out long userId))
                    {
                        using (var db = new LinqToDB.Data.DataConnection(ProviderName.PostgreSQL, _dbConnection))
                        {
                            var userExists = db.GetTable<BotUser>().Any(u => u.Id == userId);

                            if (userExists)
                            {
                                _answerUserId = userId;
                                await _messageSender.SendMessage(chatId, cancellationToken, "Введите ответ пользователю (текст + фото/видео, если нужно):", _startButton);
                            }
                            else
                            {
                                await _messageSender.SendMessage(chatId, cancellationToken, "Пользователь не найден в базе данных", _startButton);
                                _answerFlag = false;
                            }
                        }
                    }
                    else
                    {
                        await _messageSender.SendMessage(chatId, cancellationToken, "Неверный ID пользователя. Введите ID из отчёта.", _startButton);
                        _answerFlag = false;
                    }
                }
                else
                {
                    // Обработка ответа пользователю (текст + медиа)
                    string answerText = update.Message.Caption ?? update.Message.Text;

                    if (string.IsNullOrEmpty(answerText))
                    {
                        await _messageSender.SendMessage(chatId, cancellationToken, "Ответ должен содержать текст.", _startButton);
                        return;
                    }

                    // Сохраняем текст ответа в базу данных
                    await SaveAnswerToDatabase(_answerUserId, answerText);

                    // Если это медиагруппа, создаём экземпляр MediaGroup и добавляем файлы
                    if (!string.IsNullOrEmpty(update.Message.MediaGroupId))
                    {
                        var mediaGroupId = update.Message.MediaGroupId;

                        // Получаем или создаем экземпляр MediaGroup
                        var mediaGroup = _mediaGroups.GetOrAdd(mediaGroupId, id => new MediaGroup(id));

                        // Устанавливаем текст сообщения (подпись)
                        mediaGroup.Caption = answerText;

                        // Добавляем файл в медиагруппу
                        if (update.Message.Type == MessageType.Photo)
                        {
                            var photoFileId = update.Message.Photo.Last().FileId;
                            mediaGroup.AddFile(update.Message.Chat.Id, photoFileId);
                        }
                        else if (update.Message.Type == MessageType.Video)
                        {
                            var videoFileId = update.Message.Video.FileId;
                            mediaGroup.AddFile(update.Message.Chat.Id, videoFileId);
                        }

                        // Если медиагруппа еще не обрабатывается, запускаем фоновую задачу
                        if (!mediaGroup.IsProcessing)
                        {
                            mediaGroup.IsProcessing = true;

                            _ = Task.Run(async () =>
                            {
                                while (true)
                                {
                                    await Task.Delay(100); // Проверяем каждые 100 мс

                                    // Если медиагруппа завершена, отправляем её
                                    if (mediaGroup.IsComplete())
                                    {
                                        await HandleMediaGroup(_botClient, mediaGroup, cancellationToken);

                                        // Удаляем медиагруппу из словаря после отправки
                                        _mediaGroups.TryRemove(mediaGroupId, out _);
                                        break;
                                    }
                                }
                            }, cancellationToken);
                        }
                    }
                    else
                    {
                        // Если это одиночное сообщение (текст, фото, видео и т.д.)
                        if (update.Message.Type == MessageType.Photo)
                        {
                            // Отправляем фото с текстом
                            var photoFileId = update.Message.Photo.Last().FileId;
                            await _messageSender.SendPhotoWithCaption(_answerUserId, cancellationToken, photoFileId, answerText, _startButton);
                        }
                        else if (update.Message.Type == MessageType.Video)
                        {
                            // Отправляем видео с текстом
                            var videoFileId = update.Message.Video.FileId;
                            await _messageSender.SendVideoWithCaption(_answerUserId, cancellationToken, videoFileId, answerText, _startButton);
                        }
                        else if (update.Message.Type == MessageType.VideoNote)
                        {
                            // Отправляем текст пере кружком
                            await _messageSender.SendMessage(_answerUserId, cancellationToken, answerText, _startButton);
                            // Отправляем видеосообщение (кружок)
                            await _messageSender.SendVideoNote(_answerUserId, cancellationToken, update.Message.VideoNote.FileId, _startButton);
                        }
                        else
                        {
                            // Отправляем только текст
                            await _messageSender.SendMessage(_answerUserId, cancellationToken, answerText, _startButton);
                        }

                        // Сбрасываем состояние
                        _answerUserId = 0;
                    }

                    
                    _answerFlag = false;                    
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка отправки ответа пользователю: {ex.Message}");
            }
        }

        private static async Task Mailing (Update update, CancellationToken cancellationToken, bool isTest)
        {
            try
            {
                // Получаем данные с проверкой на null
                string message = _botMailingData[1]?.Count > 0 ? _botMailingData[1][0]?.ToString() ?? string.Empty : string.Empty;
                string imagesUrl = _botMailingData[1]?.Count > 1 ? _botMailingData[1][1]?.ToString() ?? string.Empty : string.Empty;
                string videoUrl = _botMailingData[1]?.Count > 2 ? _botMailingData[1][2]?.ToString() ?? string.Empty : string.Empty;
                string videoNoteUrl = _botMailingData[1]?.Count > 3 ? _botMailingData[1][3]?.ToString() ?? string.Empty : string.Empty;
                var buttons = new ReplyKeyboardMarkup(new[] { new KeyboardButton("🚩 К началу") });
                buttons.ResizeKeyboard = true;

                var mediaGroup = await _messageSender.ConvertImgStringToMediaListAsync(imagesUrl);

                if (isTest)
                {
                    foreach (var admin in _administrators)
                    {
                        if (long.TryParse(admin, out long adminId))
                        {
                            await _messageSender.MailMessage(message, imagesUrl, mediaGroup, videoUrl, videoNoteUrl, cancellationToken, buttons, adminId);
                        }
                    }
                }
                else
                {
                    var userIds = new List<long>();
                    using (var db = new LinqToDB.Data.DataConnection(ProviderName.PostgreSQL, _dbConnection))
                    {
                        var table = db.GetTable<BotUser>();
                        userIds = table.Select(user => user.Id).ToList();
                    }

                    if (userIds.Count > 0)
                    {
                        foreach (var userId in userIds)
                        {
                            await _messageSender.MailMessage(message, imagesUrl, mediaGroup, videoUrl, videoNoteUrl, cancellationToken, buttons, userId);
                        }
                    }
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, $"Ошибка массовой рассылки: {ex.Message}");
            }
        }

        /// <summary>
        /// Сохраняет ответ в базу данных.
        /// </summary>
        private static async Task SaveAnswerToDatabase(long userId, string answerText)
        {
            using (var db = new LinqToDB.Data.DataConnection(ProviderName.PostgreSQL, _dbConnection))
            {
                var table = db.GetTable<UserAnswer>();
                db.Insert(new UserAnswer { BotUserId = userId, AnswerText = answerText });
            }
        }

        private static bool IsRating(string text)
        {
            return text switch
            {
                "1️⃣" or "2️⃣" or "3️⃣" or "4️⃣" or "5️⃣" => true,
                _ => false
            };
        }

        private async Task HandleCourseRating(Update update, CancellationToken cancellationToken)
        {
            var chatId = update.Message.Chat.Id;

            int rating = update.Message.Text switch
            {
                "1️⃣" => 1,
                "2️⃣" => 2,
                "3️⃣" => 3,
                "4️⃣" => 4,
                "5️⃣" => 5,
                _ => 0
            };

            if (rating > 0)
            {
                using (var db = new DataConnection(ProviderName.PostgreSQL, _dbConnection))
                {
                    var user = await db.GetTable<BotUser>().FirstOrDefaultAsync(u => u.Id == chatId, cancellationToken);

                    if (user != null)
                    {
                        user.CourseAssesment = rating;
                        user.IsVotingForCourse = false;

                        // Обновляем только эти два поля:
                        _appDbUpdate.UpdateBotUserFields(user, _dbConnection,
                            u => u.CourseAssesment,
                            u => u.IsVotingForCourse);
                    }
                }

                await _messageSender.SendMessage(chatId, cancellationToken, _botMessagesData[9][3].ToString(), _standardButtons);
            }
            else
            {
                await _messageSender.SendMessage(chatId, cancellationToken,
                    "Пожалуйста, выбери оценку от 1️⃣ до 5️⃣.", null);
            }
        }
    }
}
    