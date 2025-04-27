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
using System.Threading;

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
        private static bool _answerFlag;
        private static bool _mailingFlag;
        private static IOptionsSnapshot<AppSettings> _appSettings;  

        private static readonly ConcurrentDictionary<string, MediaGroup> _mediaGroups = new ConcurrentDictionary<string, MediaGroup>();

        public UpdateHandler(IOptionsSnapshot<AppSettings> appSettings, ITelegramBotClient botClient, string dbConnection)
        {
            _appSettings = appSettings;
            _administrators = _appSettings.Value.TlgBotAdministrators.Split(';');
            _googleSheets = new GoogleSheetsController(_appSettings);
            _botClient = botClient;
            _dbConnection = dbConnection;
            _botMessagesData = _googleSheets.GetData(_appSettings.Value.GoogleSheetsRange);
            _botMailingData = _googleSheets.GetData(_appSettings.Value.GoogleSheetsMailing);     
            var cts = new CancellationTokenSource(); // прерыватель соединения с ботом
        }

        public async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("Неизвестная ошибка обработки");
        }

        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            // Игнорируем всё, кроме сообщений, и сообщения без отправителя
            if (update.Type != UpdateType.Message || update.Message == null || update.Message.From == null)
            {
                return; 
            }

            var me = await botClient.GetMe();
            var userParams = update.Message.From;       
            long chatId = update.Message.Chat.Id;

            using (var db = new LinqToDB.Data.DataConnection(ProviderName.PostgreSQL, _dbConnection))
            {
                var table = db.GetTable<BotUser>();
                _user = table.Where(x => x.Id == userParams.Id).FirstOrDefault();

                if (_user == null)
                {
                    // Создаем нового пользователя
                    _user = new BotUser
                    {
                        Id = userParams.Id,
                        UserName = userParams.Username
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

            if (_mailingFlag)
            {
                if (update.Message.Text == "✅ Да")
                {
                    Mailing(update, cancellationToken, false);
                } 
                else
                {
                    await HandleStartCommand(update.Message.Chat.Id, cancellationToken);
                }

                _mailingFlag = false;
                return;
            }

            // Обработка медиагрупп
            if (!string.IsNullOrEmpty(update.Message.MediaGroupId))
            {
                var mediaGroupId = update.Message.MediaGroupId;
                var photoFileId = update.Message.Photo.Last().FileId;

                // Получаем или создаем экземпляр MediaGroup
                var mediaGroup = _mediaGroups.GetOrAdd(mediaGroupId, id => new MediaGroup(id));

                // Добавляем файл в медиагруппу
                mediaGroup.AddFile(update.Message.Chat.Id, photoFileId);

                // Если есть текст сообщения, сохраняем его в медиагруппе
                if (!string.IsNullOrEmpty(update.Message.Caption))
                {
                    mediaGroup.Caption = update.Message.Caption;
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
                                await HandleMediaGroup(botClient, mediaGroup, cancellationToken);

                                // Удаляем медиагруппу из словаря после отправки
                                _mediaGroups.TryRemove(mediaGroupId, out _);
                                break;
                            }
                        }
                    }, cancellationToken);
                }
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
                    await SendMessage(chatId, cancellationToken, "Неподдерживаемый тип сообщения. Используйте текстовые команды.", null);
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
                chatId: mediaGroup.Files.First().ChatId,
                media: mediaGroupToSend,
                cancellationToken: cancellationToken);
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
                case "📅 Подписаться на курс обучения":
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
                case "💪 Подробно о решениях системы":
                case "/solutions":                    
                     await HandleAboutSolutionsCommand(ChatId, cancellationToken);                    
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
                        var buttons = new ReplyKeyboardMarkup(
                        new[]
                        {
                            new KeyboardButton("✅ Да"),
                            new KeyboardButton("❌ Нет")
                        });
                        buttons.ResizeKeyboard = true;

                        await SendMessage(ChatId, cancellationToken, "Вы точно уверены что хотите отправить массовую рассылку?", buttons);
                        _mailingFlag = true;
                    }
                    break;
                case "/testmailing":
                    if (IsBotAdministrator(update.Message.From))
                    {
                        await SendMessage(ChatId, cancellationToken, "Тестовая массовая расылка началась", null);
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
                        new KeyboardButton("🔍 О системе HR Pro"),
                        new KeyboardButton("💪 Подробно о решениях системы"),
                        new KeyboardButton("🤵‍♂️ Узнать об экспертах")
                    },
                    new[] {
                        new KeyboardButton("📅 Подписаться на курс обучения"),
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
                var courseController = new CourseController(_user, _botClient, _appSettings, _dbConnection);
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
            string message = _botMessagesData[3][3].ToString();
            string imagesUrl = _botMessagesData[3][4].ToString();
            // Разделяем строку с URL изображений, если она не пустая
            string[] imageArray = !string.IsNullOrEmpty(imagesUrl) ? imagesUrl.Split(';') : Array.Empty<string>();
            // Создаем список фоток из урлов
            var mediaGroup = new List<InputMediaPhoto>();

            foreach (var url in imageArray)
            {
                // Добавляем каждую фотографию в медиагруппу, используя URL
                mediaGroup.Add(new InputMediaPhoto(url));
            }
            var buttons = new ReplyKeyboardMarkup(
                new[] {
                    new KeyboardButton("🚩 К началу"),
                    new KeyboardButton("🙋‍♂️ Задать вопрос эксперту")
                });
            buttons.ResizeKeyboard = true;
            SendMediaGroupWithCaption(chatId, cancellationToken, mediaGroup, message, buttons);
            //await SendMessage(chatId, cancellationToken, Message, buttons);
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
            string imageUrl = _botMessagesData[4][4].ToString();
            var Buttons = new ReplyKeyboardMarkup(
                new[] {
                    new KeyboardButton("🚩 К началу")
                });
            Buttons.ResizeKeyboard = true;            
            await SendPhotoWithCaption(chatId, cancellationToken, imageUrl, Message, Buttons);
        }
        /// <summary>
        /// обработчик команды с информацией о решениях HR Pro
        /// </summary>
        /// <param name="chatId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private static async Task HandleAboutSolutionsCommand(long chatId, CancellationToken cancellationToken)
        {
            string Message = _botMessagesData[6][3].ToString();
            string imageUrl = _botMessagesData[6][4].ToString();
            var Buttons = new ReplyKeyboardMarkup(
                new[] {
                    new KeyboardButton("🚩 К началу")
                });
            Buttons.ResizeKeyboard = true;
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
            long chatId = update.Message.Chat.Id;
            var buttons = new ReplyKeyboardMarkup(new[] { new KeyboardButton("🚩 К началу") });
            buttons.ResizeKeyboard = true;

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
                await SendMessage(chatId, cancellationToken, "Введите ID пользователя, которому вы хотите ответить:", buttons);
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
                            await SendMessage(chatId, cancellationToken, "Введите ответ пользователю (текст + фото/видео, если нужно):", buttons);
                        }
                        else
                        {
                            await SendMessage(chatId, cancellationToken, "Пользователь не найден в базе данных", buttons);
                            _answerFlag = false;
                        }
                    }
                }
                else
                {
                    await SendMessage(chatId, cancellationToken, "Неверный ID пользователя. Введите ID из отчёта.", buttons);
                    _answerFlag = false;
                }
            }
            else
            {
                // Обработка ответа пользователю (текст + медиа)
                string answerText = update.Message.Caption ?? update.Message.Text;

                if (string.IsNullOrEmpty(answerText))
                {
                    await SendMessage(chatId, cancellationToken, "Ответ должен содержать текст.", buttons);
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
                        await SendPhotoWithCaption(_answerUserId, cancellationToken, photoFileId, answerText, buttons);
                    }
                    else if (update.Message.Type == MessageType.Video)
                    {
                        // Отправляем видео с текстом
                        var videoFileId = update.Message.Video.FileId;
                        await SendVideoWithCaption(_answerUserId, cancellationToken, videoFileId, answerText, buttons);
                    }
                    else if (update.Message.Type == MessageType.VideoNote)
                    {
                        // Отправляем текст пере кружком
                        await SendMessage(_answerUserId, cancellationToken, answerText, buttons);
                        // Отправляем видеосообщение (кружок)
                        await SendVideoNote(_answerUserId, cancellationToken, update.Message.VideoNote.FileId, buttons);                        
                    }
                    else
                    {
                        // Отправляем только текст
                        await SendMessage(_answerUserId, cancellationToken, answerText, buttons);
                    }
                }

                // Сбрасываем состояние
                _answerUserId = 0;
                _answerFlag = false;
                await SendMessage(chatId, cancellationToken, "Ответ отправлен пользователю.", buttons);
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

                // Разделяем строку с URL изображений, если она не пустая
                string[] imageArray = !string.IsNullOrEmpty(imagesUrl) ? imagesUrl.Split(';') : Array.Empty<string>();

                // Создаем список фоток из урлов
                var mediaGroup = new List<InputMediaPhoto>();
                var buttons = new ReplyKeyboardMarkup(new[] { new KeyboardButton("🚩 К началу") });
                buttons.ResizeKeyboard = true;

                foreach (var url in imageArray)
                {
                    // Добавляем каждую фотографию в медиагруппу, используя URL
                    mediaGroup.Add(new InputMediaPhoto(url));
                }


                if (isTest)
                {
                    foreach (var admin in _administrators)
                    {
                        if (long.TryParse(admin, out long adminId))
                        {
                            MailMessage(message, imagesUrl, mediaGroup, videoUrl, videoNoteUrl, cancellationToken, buttons, adminId);
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
                            MailMessage(message, imagesUrl, mediaGroup, videoUrl, videoNoteUrl, cancellationToken, buttons, userId);
                        }
                    }
                }
            }
            catch (Exception ex) {
                await SendMessage(update.Message.Chat.Id, cancellationToken, ex.Message, null);
            }
        }

        private static async Task MailMessage 
           (string message,
            string imagesUrl,
            List<InputMediaPhoto> mediaGroup,
            string videoUrl, string videoNoteUrl,
            CancellationToken cancellationToken,
            ReplyKeyboardMarkup buttons, long chatId)
        {
            if (!string.IsNullOrEmpty(videoNoteUrl))
            {
                if (!string.IsNullOrEmpty(message))
                {
                    await SendMessage(chatId, cancellationToken, message, buttons);
                }
                await SendVideoNote(chatId, cancellationToken, videoNoteUrl, buttons);

            }
            else if (!string.IsNullOrEmpty(videoUrl))
            {
                await SendVideoWithCaption(chatId, cancellationToken, videoUrl, message, buttons);
            }
            else if (!string.IsNullOrEmpty(imagesUrl))
            {
                if (mediaGroup.Count == 1)
                {
                    await SendPhotoWithCaption(chatId, cancellationToken, imagesUrl, message, buttons);
                }
                else if (mediaGroup.Count > 1)
                {
                    await SendMediaGroupWithCaption(chatId, cancellationToken, mediaGroup, message, null);
                }
            }
            else
            {
                await SendMessage(chatId, cancellationToken, message, buttons);
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
                parseMode: ParseMode.Html,
                replyMarkup: removeKeyboard,
                cancellationToken: cancellationToken);
            }
            else
            {
                await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: textMessage,
                parseMode: ParseMode.Html,
                replyMarkup: buttons,
                cancellationToken: cancellationToken);
            }
        }

        /// <summary>
        /// Отправляет одну фотографию с текстом.
        /// </summary>
        private static async Task SendPhotoWithCaption(long chatId, CancellationToken cancellationToken, string fileId, string caption, ReplyKeyboardMarkup? buttons)
        {
            try
            {
                int maxCaptionLength = 1024;

                if (string.IsNullOrEmpty(caption) || caption.Length <= maxCaptionLength)
                {
                    // Если текст короткий, отправляем всё вместе
                    await _botClient.SendPhotoAsync(
                        chatId: chatId,
                        photo: fileId,
                        caption: caption,
                        parseMode: ParseMode.Html,
                        replyMarkup: buttons,
                        cancellationToken: cancellationToken);
                    return;
                }

                // Находим позицию для разбиения текста
                int splitPosition = FindSplitPosition(caption, maxCaptionLength);

                if (splitPosition == -1)
                {
                    // Если подходящей позиции не найдено, режем по 1024 символам
                    splitPosition = maxCaptionLength;
                }

                string photoCaption = caption.Substring(0, splitPosition).Trim();
                string remainingText = caption.Substring(splitPosition).Trim();

                // Отправляем фото с первым куском текста
                await _botClient.SendPhotoAsync(
                    chatId: chatId,
                    photo: fileId,
                    caption: photoCaption,
                    parseMode: ParseMode.Html,
                    replyMarkup: buttons,
                    cancellationToken: cancellationToken);

                // Отправляем оставшийся текст отдельным сообщением
                if (!string.IsNullOrEmpty(remainingText))
                {
                    await SendMessage(chatId, cancellationToken, remainingText, buttons);
                }
            }
            catch (Exception ex)
            {
                // <todo> удолить перед продом
                await SendMessage(chatId, cancellationToken, "Не удалось отправить изображение.", buttons);
            }
        }

        /// <summary>
        /// Отправляет несколько фотографий с текстом.
        /// </summary>
        private static async Task SendMediaGroupWithCaption(long chatId, CancellationToken cancellationToken, List<InputMediaPhoto> photos, string caption, ReplyKeyboardMarkup? buttons)
        {
            try
            {
                int maxCaptionLength = 1024;

                // Если текст короткий, отправляем всё вместе
                if (string.IsNullOrEmpty(caption) || caption.Length <= maxCaptionLength)
                {
                    // Устанавливаем текст для первой фотографии
                    // Применяем форматирование HTML только к первой фотографии
                    photos[0] = new InputMediaPhoto(photos[0].Media)
                    {
                        Caption = caption,
                        ParseMode = ParseMode.Html
                    };

                    await _botClient.SendMediaGroupAsync(
                        chatId: chatId,
                        media: photos,
                        cancellationToken: cancellationToken);
                    return;
                }

                // Находим позицию для разбиения текста
                int splitPosition = FindSplitPosition(caption, maxCaptionLength);

                if (splitPosition == -1)
                {
                    // Если подходящей позиции не найдено, режем по 1024 символам
                    splitPosition = maxCaptionLength;
                }

                string photoCaption = caption.Substring(0, splitPosition).Trim();
                string remainingText = caption.Substring(splitPosition).Trim();

                // Устанавливаем текст для первой фотографии
                photos[0].Caption = caption;

                await _botClient.SendMediaGroupAsync(
                    chatId: chatId,
                    media: photos,
                    cancellationToken: cancellationToken);

                // Отправляем оставшийся текст отдельным сообщением
                if (!string.IsNullOrEmpty(remainingText))
                {
                    await SendMessage(chatId, cancellationToken, remainingText, buttons);
                }
            }
            catch (Exception ex)
            {
                // <todo> удолить перед продом
                await SendMessage(chatId, cancellationToken, "Не удалось отправить.", null);
            }            
        }

        /// <summary>
        /// Отправляет одно видео с текстом.
        /// </summary>
        private static async Task SendVideoWithCaption(
            long chatId,
            CancellationToken cancellationToken,
            string fileIdOrUrl,
            string caption,
            ReplyKeyboardMarkup? buttons)
        {
            try
            {
                // Отправляем видео по file_id или URL
                await _botClient.SendVideoAsync(
                    chatId: chatId,
                    video: fileIdOrUrl, // Может быть file_id или URL
                    caption: caption,
                    replyMarkup: buttons,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                // <todo> удолить перед продом
                await SendMessage(chatId, cancellationToken, $"Видео не может быть отправлено: {ex.Message}", buttons);
            }
        }

        /// <summary>
        /// Отправляет видеосообщение (кружок).
        /// </summary>
        private static async Task SendVideoNote(long chatId, CancellationToken cancellationToken, string fileIdOrUrl, ReplyKeyboardMarkup? buttons)
        {
            try
            {
                string filePath = null;

                // Если это URL, сохраняем файл локально
                if (Uri.TryCreate(fileIdOrUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    var FileController = new FileController();
                    filePath = await FileController.SaveFileFromUrl(fileIdOrUrl, "VideoNotes");
                }
                else
                {
                    // Если это file_id или локальный путь, используем его напрямую
                    filePath = fileIdOrUrl;
                }

                // Отправляем видеосообщение
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    await _botClient.SendVideoNoteAsync(
                        chatId: chatId,
                        videoNote: InputFile.FromStream(fileStream),
                        replyMarkup: buttons,
                        cancellationToken: cancellationToken);
                }
            }
            catch (Exception ex)
            {
                ///<todo>Удолить перед публикаций на прод</todo>
                await SendMessage(chatId, cancellationToken, $"Видеосообщение не может быть отправлено: {ex.Message}", buttons);
            }
        }

        private static int FindSplitPosition(string text, int maxLength)
        {
            // Ищем ближайший перенос строки перед максимумом символов
            int newlinePosition = text.LastIndexOf('\n', maxLength - 1);

            if (newlinePosition != -1)
            {
                return newlinePosition;
            }

            // Если не нашли перенос строки, ищем точку
            int dotPosition = text.LastIndexOf('.', maxLength - 1);

            if (dotPosition != -1)
            {
                return dotPosition;
            }

            // Если ничего не нашли, режем по максимальной длине
            return maxLength;
        }
    }
}
    