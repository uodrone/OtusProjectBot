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
            var Me = await botClient.GetMe();
            var UserParams = update.Message?.From;
            string? BotName = Me.FirstName; //имя бота            
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


            if (update.Type == UpdateType.Message && update.Message.Type == MessageType.Text && UserParams != null)
            {
                //если начали собирать данные, но они еще не собраны до конца - дособираем
                if (_user.DataCollectStep > 0 && _user.DataCollectStep < 6)
                {
                    await GetUserData(update, cancellationToken);
                    return;
                }

                // Обработка обычных команд
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
                        }
                        
                        await GetUserData(update, cancellationToken);
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
                            var reportStream = GenerateUserReport();
                            await _botClient.SendDocumentAsync(
                                chatId: ChatId,
                                document: new InputFileStream(reportStream, "UsersReport.xlsx"),
                                caption: "Отчет по пользователям",
                                cancellationToken: cancellationToken);
                        }
                        break;
                    case "/answer":
                        if (IsBotAdministrator(UserParams))
                        {
                            await SendMessage(ChatId, cancellationToken, "Введите id пользователя, которому вы хотите ответить", null);
                        }
                        break;
                    default:
                        var Buttons = new ReplyKeyboardMarkup(
                            new[] {
                                 new KeyboardButton("🚩 К началу")
                            });
                        Buttons.ResizeKeyboard = true;
                        var Message = $"Попробуйте еще раз! Ник: {UserParams.Username}, Имя: {UserParams.FirstName}, id: {UserParams.Id} ";
                        await SendMessage(ChatId, cancellationToken, Message, Buttons);                        
                        break;
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
                var AppDbUpdate = new AppDBUpdate();
                AppDbUpdate.UserDbUpdate(_user, _dbConnection);
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

        private static async Task GetUserData(Update update, CancellationToken cancellationToken)
        {
            var AppDbUpdate = new AppDBUpdate();
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
                        AppDbUpdate.UserDbUpdate(_user, _dbConnection);
                        return;
                    }

                    await SendMessage(ChatId, cancellationToken, "Наш эксперт ответит на ваш вопрос в течение 3 рабочих дней. Чтобы сформировать обращение мы должны знать ваши данные.", null);
                    await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите ваше имя:", Buttons);
                    _user.DataCollectStep = 1;
                    AppDbUpdate.UserDbUpdate(_user, _dbConnection);
                    break;
                case 1:
                    if (update.Message.Text == "🚩 К началу" || update.Message.Text == "/start")
                    {
                        await HandleStartCommand(ChatId, cancellationToken);
                        _user.DataCollectStep = 0;
                        AppDbUpdate.UserDbUpdate(_user, _dbConnection);
                        return;
                    }
                    else if (regular.ValidateName(update.Message.Text))
                    {
                        _user.FirstName = update.Message.Text;
                        await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите вашу фамилию:", Buttons);
                        _user.DataCollectStep = 2;
                        AppDbUpdate.UserDbUpdate(_user, _dbConnection);
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
                        AppDbUpdate.UserDbUpdate(_user, _dbConnection);
                        return;
                    }
                    else if (!string.IsNullOrEmpty(update.Message.Text))
                    {
                        _user.LastName = update.Message.Text;
                        await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите вашу организацию:", Buttons);
                        _user.DataCollectStep = 3;
                        AppDbUpdate.UserDbUpdate(_user, _dbConnection);
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
                        AppDbUpdate.UserDbUpdate(_user, _dbConnection);
                        return;
                    }
                    else if (regular.ValidateOrganization(update.Message.Text))
                    {
                        _user.Organization = update.Message.Text;
                        await SendMessage(ChatId, cancellationToken, "Пожалуйста, введите ваш телефон:", Buttons);
                        _user.DataCollectStep = 4;
                        AppDbUpdate.UserDbUpdate(_user, _dbConnection);
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
                        AppDbUpdate.UserDbUpdate(_user, _dbConnection);
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
                        AppDbUpdate.UserDbUpdate(_user, _dbConnection);
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
                        AppDbUpdate.UserDbUpdate(_user, _dbConnection);
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
                        AppDbUpdate.UserDbUpdate(_user, _dbConnection);
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
            worksheet.Cells[1, 8].Value = "Подписан на курс?";
            worksheet.Cells[1, 9].Value = "Дата подписки на курс";
            worksheet.Cells[1, 10].Value = "Этап отправки курсов";

            // Стиль для заголовков
            using (var range = worksheet.Cells[1, 1, 1, 10])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);
            }

            using (var db = new LinqToDB.Data.DataConnection(ProviderName.PostgreSQL, _dbConnection))
            {
                // Получаем всех пользователей
                var allUsers = db.GetTable<BotUser>().ToList();
                // Получаем все вопросы
                var allQuestions = db.GetTable<UserQuestion>().ToList();

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

                    worksheet.Cells[row, 8].Value = user.IsSubscribed ? "Yes" : "No";
                    worksheet.Cells[row, 9].Value = user.DateStartSubscribe?.ToString("dd.MM.yyyy");
                    worksheet.Cells[row, 10].Value = user.CurrentCourseStep;
                    row++;
                }
            }

            // Авто-ширина для всех колонок
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

            var stream = new MemoryStream();
            package.SaveAs(stream);
            stream.Position = 0;
            return stream;
        }
    }
}
