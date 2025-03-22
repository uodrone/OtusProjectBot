using HRProBot.Models;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using OfficeOpenXml;
using System.IO;
using LinqToDB.Common;
using LinqToDB;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Collections.Concurrent;

namespace HRProBot.Controllers
{
    public class UpdateHandler : IUpdateHandler
    {
        private static string[] _administrators;
        private static GoogleSheetsController _googleSheets;
        private static ITelegramBotClient _botClient;
        private static IList<IList<object>> _botMessagesData;
        private static string _dbConnection;
        private static BotUser _user;
        private static AppDBUpdate _appDbUpdate = new AppDBUpdate();
        private static long _answerUserId;
        private static bool _answerFlag;
        // Кэш для хранения медиагрупп
        private static readonly ConcurrentDictionary<string, List<(long ChatId, string FileId)>> _mediaGroupCache =
            new ConcurrentDictionary<string, List<(long ChatId, string FileId)>>();

        public UpdateHandler(IOptionsSnapshot<AppSettings> appSettings, ITelegramBotClient botClient, string dbConnection)
        {
            _administrators = appSettings.Value.TlgBotAdministrators.Split(';');
            _googleSheets = new GoogleSheetsController(appSettings);
            _botClient = botClient;
            _dbConnection = dbConnection;
            var range = appSettings.Value.GoogleSheetsRange;
            _botMessagesData = _googleSheets.GetData(range);
            var cts = new CancellationTokenSource(); // прерыватель соединения с ботом
        }

        public async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("Неизвестная ошибка обработки");
        }

        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            // Критически важная проверка
            if (update.Type != UpdateType.Message || update.Message == null || update.Message.From == null)
            {
                return; // Игнорируем всё, кроме сообщений, и сообщения без отправителя
            }

            var Me = await botClient.GetMe();
            var UserParams = update.Message.From;
            string? BotName = Me.FirstName; // Имя бота            
            long ChatId = update.Message.Chat.Id;

            using (var db = new LinqToDB.Data.DataConnection(ProviderName.PostgreSQL, _dbConnection))
            {
                var table = db.GetTable<BotUser>();
                _user = table.Where(x => x.Id == UserParams.Id).FirstOrDefault();

                if (_user == null)
                {
                    // Создаем нового пользователя
                    _user = new BotUser
                    {
                        Id = UserParams.Id,
                        UserName = UserParams.Username
                    };

                    // Вставляем нового пользователя в базу данных
                    db.Insert(_user);
                }
            }

            // Если начали собирать данные, но они еще не собраны до конца - дособираем
            if (_user.DataCollectStep > 0 && _user.DataCollectStep < 6)
            {
                await GetUserData(update, cancellationToken);
                return;
            }

            if (_answerFlag)
            {
                await AnswerToUser(update, cancellationToken);
                return;
            }

            // Обработка медиагрупп
            if (!string.IsNullOrEmpty(update.Message.MediaGroupId))
            {
                await HandleMediaGroup(update, cancellationToken);
                return;
            }

            // Обработка текстовых команд
            if (update.Message.Type == MessageType.Text)
            {
                await HandleTextCommands(update, cancellationToken);
            }
            else
            {
                // Обработка медиафайлов (фото, видео и т.д.)
                if (_answerFlag)
                {
                    await AnswerToUser(update, cancellationToken);
                }
                else
                {
                    await SendMessage(ChatId, cancellationToken, "Неподдерживаемый тип сообщения. Используйте текстовые команды.", null);
                }
            }
        }

        /// <summary>
        /// Обработка медиагрупп
        /// </summary>
        private async Task HandleMediaGroup(Update update, CancellationToken cancellationToken)
        {
            var mediaGroupId = update.Message.MediaGroupId;
            var photoFileId = update.Message.Photo.Last().FileId;

            // Добавляем фотографию в кэш
            _mediaGroupCache.AddOrUpdate(
                mediaGroupId,
                new List<(long ChatId, string FileId)> { (update.Message.Chat.Id, photoFileId) },
                (key, existingList) =>
                {
                    existingList.Add((update.Message.Chat.Id, photoFileId));
                    return existingList;
                });

            // Если это последнее сообщение в медиагруппе, отправляем все фотографии
            if (_mediaGroupCache.TryGetValue(mediaGroupId, out var mediaGroupList))
            {
                var mediaGroup = mediaGroupList
                    .Select((item, index) => new InputMediaPhoto(item.FileId)
                    {
                        Caption = index == 0 ? "Ваши фотографии:" : null // Текст только для первой фотографии
                    })
                    .ToList();

                await _botClient.SendMediaGroupAsync(
                    chatId: update.Message.Chat.Id,
                    media: mediaGroup,
                    cancellationToken: cancellationToken);

                // Очищаем кэш для этой медиагруппы
                _mediaGroupCache.TryRemove(mediaGroupId, out _);
            }
        }

        /// <summary>
        /// Обработка текстовых команд
        /// </summary>
        private async Task HandleTextCommands(Update update, CancellationToken cancellationToken)
        {
            long ChatId = update.Message.Chat.Id;

            switch (update.Message.Text)
            {
                case "🚩 К началу":
                case "/start":
                    await HandleStartCommand(ChatId, cancellationToken);
                    break;
                case "📅 Подписаться на курс":
                case "/course":
                    await HandleCourseCommand(ChatId, cancellationToken);
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
                    if (_user.DataCollectStep == 6)
                    {
                        _user.DataCollectStep = 5;
                        _appDbUpdate.UserDbUpdate(_user, _dbConnection);
                    }

                    await GetUserData(update, cancellationToken);
                    break;
                case "/mailing":
                    if (IsBotAdministrator(update.Message.From))
                    {
                        await SendMessage(ChatId, cancellationToken, "Массовая рассылка началась", null);
                    }
                    break;
                case "/testmailing":
                    if (IsBotAdministrator(update.Message.From))
                    {
                        await SendMessage(ChatId, cancellationToken, "Отправляю тестовую рассылку", null);
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
                        await SendMessage(ChatId, cancellationToken, "Введите id пользователя, которому вы хотите ответить", null);
                        _answerFlag = true;
                    }
                    break;
                default:
                    var Buttons = new ReplyKeyboardMarkup(new[] { new KeyboardButton("🚩 К началу") });
                    Buttons.ResizeKeyboard = true;
                    var Message = $"Попробуйте еще раз! Ник: {update.Message.From.Username}, Имя: {update.Message.From.FirstName}, id: {update.Message.From.Id} ";
                    await SendMessage(ChatId, cancellationToken, Message, Buttons);
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
            if (!_user.IsSubscribed)
            {
                _user.IsSubscribed = true;
                _user.DateStartSubscribe = date;
                _appDbUpdate.UserDbUpdate(_user, _dbConnection);
                await SendMessage(chatId, cancellationToken, Message, Buttons);
                var courseController = new CourseController(_user, _botClient, _dbConnection);
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
            await SendPhotoWithCaption(chatId, cancellationToken, imageUrl, Message, Buttons);
        }
        
        private static async Task GetUserData(Update update, CancellationToken cancellationToken)
        {
            long ChatId = update.Message.Chat.Id;
            var regular = new RegularValidation();
            var Buttons = new ReplyKeyboardMarkup(
                            new[] {
                            new KeyboardButton("🚩 К началу")
                            });
            Buttons.ResizeKeyboard = true;

            switch (_user.DataCollectStep)
            {
                case 0:
                    if (update.Message.Text == "🚩 К началу" || update.Message.Text == "/start")
                    {
                        await HandleStartCommand(ChatId, cancellationToken);
                        _user.DataCollectStep = 0;
                        _appDbUpdate.UserDbUpdate(_user, _dbConnection);
                        return;
                    }

                    await SendMessage(ChatId, cancellationToken, "Наш эксперт ответит на ваш вопрос в течение 3 рабочих дней. Чтобы сформировать обращение мы должны знать ваши данные.", null);
                    await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите ваше имя:", Buttons);
                    _user.DataCollectStep = 1;
                    _appDbUpdate.UserDbUpdate(_user, _dbConnection);
                    break;
                case 1:
                    if (update.Message.Text == "🚩 К началу" || update.Message.Text == "/start")
                    {
                        await HandleStartCommand(ChatId, cancellationToken);
                        _user.DataCollectStep = 0;
                        _appDbUpdate.UserDbUpdate(_user, _dbConnection);
                        return;
                    }
                    else if (regular.ValidateName(update.Message.Text))
                    {
                        _user.FirstName = update.Message.Text;
                        await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите вашу фамилию:", Buttons);
                        _user.DataCollectStep = 2;
                        _appDbUpdate.UserDbUpdate(_user, _dbConnection);
                    }
                    else
                    {
                        await SendMessage(ChatId, cancellationToken, "Имя неверное, введите правильное имя", Buttons);
                    }
                    break;
                case 2:
                    if (update.Message.Text == "🚩 К началу" || update.Message.Text == "/start")
                    {
                        await HandleStartCommand(ChatId, cancellationToken);
                        _user.DataCollectStep = 0;
                        _appDbUpdate.UserDbUpdate(_user, _dbConnection);
                        return;
                    }
                    else if (!string.IsNullOrEmpty(update.Message.Text))
                    {
                        _user.LastName = update.Message.Text;
                        await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите вашу организацию:", Buttons);
                        _user.DataCollectStep = 3;
                        _appDbUpdate.UserDbUpdate(_user, _dbConnection);
                    }
                    else
                    {
                        await SendMessage(ChatId, cancellationToken, "Фамилия неверная, введите правильную фамилию", Buttons);
                    }
                    break;
                case 3:
                    if (update.Message.Text == "🚩 К началу" || update.Message.Text == "/start")
                    {
                        await HandleStartCommand(ChatId, cancellationToken);
                        _user.DataCollectStep = 0;
                        _appDbUpdate.UserDbUpdate(_user, _dbConnection);
                        return;
                    }
                    else if (regular.ValidateOrganization(update.Message.Text))
                    {
                        _user.Organization = update.Message.Text;
                        await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите ваш телефон:", Buttons);
                        _user.DataCollectStep = 4;
                        _appDbUpdate.UserDbUpdate(_user, _dbConnection);
                    }
                    else
                    {
                        await SendMessage(ChatId, cancellationToken, "Организация неверная, введите правильную организацию", Buttons);
                    }
                    break;
                case 4:
                    if (update.Message.Text == "🚩 К началу" || update.Message.Text == "/start")
                    {
                        await HandleStartCommand(ChatId, cancellationToken);
                        _user.DataCollectStep = 0;
                        _appDbUpdate.UserDbUpdate(_user, _dbConnection);
                        return;
                    }
                    else if (regular.ValidatePhone(update.Message.Text))
                    {
                        _user.Phone = update.Message.Text;
                        await SendMessage(ChatId, cancellationToken, "Спасибо, ваши данные сохранены", null);
                        await SendMessage(ChatId, cancellationToken,
                            $"Имя: {_user.FirstName}\nФамилия: {_user.LastName}\nОрганизация: {_user.Organization}\nТелефон: {_user.Phone}\nId пользователя: {_user.Id}", null);
                        await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите ваш запрос:", Buttons);
                        _user.DataCollectStep = 5;
                        _appDbUpdate.UserDbUpdate(_user, _dbConnection);
                    }
                    else
                    {
                        await SendMessage(ChatId, cancellationToken, "Телефон неверный, введите правильный номер телефона", Buttons);
                    }

                    break;
                case 5:
                    if (update.Message.Text == "🚩 К началу" || update.Message.Text == "/start")
                    {
                        await HandleStartCommand(ChatId, cancellationToken);
                        _user.DataCollectStep = 0;
                        _appDbUpdate.UserDbUpdate(_user, _dbConnection);
                        return;
                    }
                    else if (!string.IsNullOrEmpty(update.Message.Text))
                    {
                        // Игнорируем текст кнопки или команду "/ask"
                        if (update.Message.Text == "🙋‍♂️ Задать вопрос эксперту" || update.Message.Text == "/ask")
                        {
                            await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите ваш запрос:", Buttons);
                            return; // При вызове повторного вопроса прерываем выполнение, чтобы дождаться следующего ввода собственно вопроса
                        }

                        // Создаем новый вопрос  
                        var question = new UserQuestion
                        {
                            BotUserId = _user.Id,
                            QuestionText = update.Message.Text
                        };

                        using (var db = new LinqToDB.Data.DataConnection(ProviderName.PostgreSQL, _dbConnection))
                        {
                            var table = db.GetTable<UserQuestion>();                                                    

                            // Вставляем вопрос в таблицу UserQuestion
                            db.Insert(question);
                        }                        

                        Buttons.ResizeKeyboard = true;
                        await SendMessage(ChatId, cancellationToken, $"Спасибо, ваш вопрос получен:\n{question.QuestionText}", Buttons);
                        _user.DataCollectStep = 6;
                        _appDbUpdate.UserDbUpdate(_user, _dbConnection);
                    }
                    else
                    {
                        await SendMessage(ChatId, cancellationToken, "Введите не пустой вопрос", null);
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

            // Стиль для заголовков
            using (var range = worksheet.Cells[1, 1, 1, 11])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);
            }

            // Включаем фильтры для первой строки
            worksheet.Cells[1, 1, 1, 11].AutoFilter = true;

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
            long ChatId = update.Message.Chat.Id;
            var Buttons = new ReplyKeyboardMarkup(new[] { new KeyboardButton("🚩 К началу") });
            Buttons.ResizeKeyboard = true;

            // Если пользователь нажал "🚩 К началу" или отправил команду /start
            if (update.Message.Text == "🚩 К началу" || update.Message.Text == "/start")
            {
                await HandleStartCommand(ChatId, cancellationToken);
                return;
            }

            // Если пользователь отправил команду /answer
            if (update.Message.Text == "/answer")
            {
                _answerFlag = true;
                await SendMessage(ChatId, cancellationToken, "Введите ID пользователя, которому вы хотите ответить:", Buttons);
                return;
            }

            // Если _answerUserId ещё не установлен, ожидаем ввода ID пользователя
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
                            await SendMessage(ChatId, cancellationToken, "Введите ответ пользователю (текст + фото/видео, если нужно):", Buttons);
                        }
                        else
                        {
                            await SendMessage(ChatId, cancellationToken, "Пользователь не найден в базе данных", Buttons);
                            _answerFlag = false;
                        }
                    }
                }
                else
                {
                    await SendMessage(ChatId, cancellationToken, "Неверный ID пользователя. Введите ID из отчёта.", Buttons);
                    _answerFlag = false;
                }
            }
            else
            {
                // Обработка ответа пользователю (текст + медиа)
                string answerText = update.Message.Caption ?? update.Message.Text;

                if (string.IsNullOrEmpty(answerText))
                {
                    await SendMessage(ChatId, cancellationToken, "Ответ должен содержать текст.", Buttons);
                    return;
                }

                // Сохраняем текст ответа в базу данных
                await SaveAnswerToDatabase(_answerUserId, answerText);

                // Отправляем текст и медиафайлы
                if (update.Message.Type == MessageType.Photo)
                {
                    // Получаем последнюю фотографию (самую большую)
                    var photoFileId = update.Message.Photo.Last().FileId;

                    // Если это медиагруппа, используем _mediaGroupCache
                    if (!string.IsNullOrEmpty(update.Message.MediaGroupId))
                    {
                        // Добавляем фотографию в кэш
                        _mediaGroupCache.AddOrUpdate(
                            update.Message.MediaGroupId,
                            new List<(long ChatId, string FileId)> { (update.Message.Chat.Id, photoFileId) },
                            (key, existingList) =>
                            {
                                existingList.Add((update.Message.Chat.Id, photoFileId));
                                return existingList;
                            });

                        // Если это последнее сообщение в медиагруппе, отправляем все фотографии
                        if (_mediaGroupCache.TryGetValue(update.Message.MediaGroupId, out var mediaGroupList))
                        {
                            var mediaGroup = mediaGroupList
                                .Select((item, index) => new InputMediaPhoto(item.FileId)
                                {
                                    Caption = index == 0 ? answerText : null // Текст только для первой фотографии
                                })
                                .ToList();

                            await _botClient.SendMediaGroupAsync(
                                chatId: _answerUserId,
                                media: mediaGroup,
                                cancellationToken: cancellationToken);

                            // Очищаем кэш для этой медиагруппы
                            _mediaGroupCache.TryRemove(update.Message.MediaGroupId, out _);
                        }
                    }
                    else
                    {
                        // Если это одиночная фотография, отправляем её отдельно
                        await SendPhotoWithCaption(_answerUserId, cancellationToken, photoFileId, answerText, Buttons);
                    }
                }
                else if (update.Message.Type == MessageType.Video)
                {
                    // Одно видео с текстом
                    await SendVideoWithCaption(_answerUserId, cancellationToken, update.Message.Video.FileId, answerText, Buttons);
                }
                else if (update.Message.Type == MessageType.VideoNote)
                {
                    // Видеосообщение (кружок) с текстом
                    await SendVideoNote(_answerUserId, cancellationToken, update.Message.VideoNote.FileId);
                    await SendMessage(_answerUserId, cancellationToken, answerText, Buttons); // Отправляем текст отдельно
                }
                else
                {
                    // Только текст
                    await SendMessage(_answerUserId, cancellationToken, answerText, Buttons);
                }

                // Сбрасываем состояние
                _answerUserId = 0;
                _answerFlag = false;
                await SendMessage(ChatId, cancellationToken, "Ответ отправлен пользователю.", Buttons);
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
        /// Отправляет одну фотографию с текстом.
        /// </summary>
        private static async Task SendPhotoWithCaption(long chatId, CancellationToken cancellationToken, string fileId, string caption, ReplyKeyboardMarkup? buttons)
        {
            await _botClient.SendPhotoAsync(
                chatId: chatId,
                photo: fileId,
                caption: caption,
                replyMarkup: buttons,
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Отправляет несколько фотографий с текстом.
        /// </summary>
        private static async Task SendMediaGroupWithCaption(long chatId, CancellationToken cancellationToken, List<InputMediaPhoto> photos, string caption)
        {
            // Устанавливаем текст для первой фотографии
            photos[0].Caption = caption;

            await _botClient.SendMediaGroupAsync(
                chatId: chatId,
                media: photos,
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Отправляет одно видео с текстом.
        /// </summary>
        private static async Task SendVideoWithCaption(long chatId, CancellationToken cancellationToken, string fileId, string caption, ReplyKeyboardMarkup? buttons)
        {
            await _botClient.SendVideoAsync(
                chatId: chatId,
                video: fileId,
                caption: caption,
                replyMarkup: buttons,
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Отправляет видеосообщение (кружок).
        /// </summary>
        private static async Task SendVideoNote(long chatId, CancellationToken cancellationToken, string fileId)
        {
            await _botClient.SendVideoNoteAsync(
                chatId: chatId,
                videoNote: fileId,
                cancellationToken: cancellationToken);
        }
    }
}
    