using HRProBot.Interfaces;
using HRProBot.Models;
using HRProBot.Services;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Extensions.Options;
using NLog;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace HRProBot.Controllers
{
    public class CourseController
    {
        private readonly ITelegramBotClient _botClient;
        private readonly IOptionsSnapshot<AppSettings> _appSettings;
        private readonly GoogleSheetsController _googleSheets;
        private readonly IList<IList<object>> _botMessagesData;
        private readonly IList<IList<object>> _botCourseData;
        private readonly MessageSender _messageSender;
        private readonly ReplyKeyboardMarkup _standardButtons;
        private readonly ReplyKeyboardMarkup _startButton;
        private readonly string _dbConnection;
        private readonly CancellationToken _cancellationToken;
        private static DateTime _lastGlobalCheck = DateTime.MinValue;
        private static readonly object _lockObject = new object();
        private static Logger _logger = LogManager.GetCurrentClassLogger();

        public CourseController(ITelegramBotClient botClient, IOptionsSnapshot<AppSettings> appSettings)
        {
            _botClient = botClient;
            _appSettings = appSettings;
            _googleSheets = new GoogleSheetsController(_appSettings);
            _botCourseData = _googleSheets.GetData(_appSettings.Value.GoogleSheetsCourseRange);
            _botMessagesData = _googleSheets.GetData(_appSettings.Value.GoogleSheetsRange);
            _messageSender = new MessageSender(botClient);
            _standardButtons = _messageSender.GetStandardButtons();
            _startButton = _messageSender.GetStartButton();
            _dbConnection = _appSettings.Value.DBConnection; // Предполагается, что строка подключения в AppSettings
            _cancellationToken = new CancellationTokenSource().Token;
        }

        // Метод для проверки всех подписанных пользователей (публичный для фонового сервиса)
        public async Task CheckAllSubscribedUsersAsync()
        {
            try
            {
                using (var db = new DataConnection(ProviderName.PostgreSQL, _dbConnection))
                {
                    var subscribedUsers = await db.GetTable<BotUser>()
                        .Where(u => u.IsSubscribed && u.DateStartSubscribe.HasValue)
                        .ToListAsync();

                    var tasks = subscribedUsers.Select(CheckAndSendLessonForUser);
                    await Task.WhenAll(tasks);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при проверке пользователей: {ex.Message}");
            }
        }

        // Быстрая проверка конкретного пользователя при получении сообщения (подстраховка)
        public async Task CheckUserOnMessageAsync(long userId)
        {
            try
            {
                using (var db = new DataConnection(ProviderName.PostgreSQL, _dbConnection))
                {
                    var user = await db.GetTable<BotUser>()
                        .Where(u => u.Id == userId && u.IsSubscribed && u.DateStartSubscribe.HasValue)
                        .FirstOrDefaultAsync();

                    if (user != null)
                    {
                        await CheckAndSendLessonForUser(user);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка при проверке пользователя {userId} после сообщения: {ex.Message}");
            }
        }

        // Метод для проверки и отправки урока конкретному пользователю
        private async Task CheckAndSendLessonForUser(BotUser user)
        {
            try
            {
                // Определяем, когда должен быть отправлен следующий урок
                DateTime nextLessonDate = CalculateNextLessonDate(user);

                // Если время пришло и курс не завершен, отправляем урок
                if (DateTime.Now >= nextLessonDate && user.CurrentCourseStep < 8)
                {
                    Console.WriteLine($"Отправляем урок пользователю {user.Id}, шаг {user.CurrentCourseStep}");
                    await SendTrainingCourseMessage(user);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка при отправке урока пользователю {user.Id}: {ex.Message}");
            }
        }

        // Метод для расчета даты следующего урока
        private DateTime CalculateNextLessonDate(BotUser user)
        {
            if (user.LastLessonSentDate.HasValue)
            {
                return user.LastLessonSentDate.Value.AddSeconds(7); // или .AddDays(7)
            }
            else
            {
                return user.DateStartSubscribe ?? DateTime.Now;
            }
        }

        // Отправка урока конкретному пользователю
        private async Task<BotUser> SendTrainingCourseMessage(BotUser user)
        {
            string courseMessage = null;
            string courseImg = null;
            var appDbUpdate = new AppDBUpdate();
            var buttons = _standardButtons;

            try
            {
                if (user.IsSubscribed && user.DateStartSubscribe <= DateTime.Now)
                {
                    switch (user.CurrentCourseStep)
                    {
                        case 1:
                            courseMessage = _botCourseData[1][1].ToString();
                            courseImg = _botCourseData[1][2].ToString();
                            break;
                        case 2:
                            courseMessage = _botCourseData[2][1].ToString();
                            courseImg = _botCourseData[2][2].ToString();
                            break;
                        case 3:
                            courseMessage = _botCourseData[3][1].ToString();
                            courseImg = _botCourseData[3][2].ToString();
                            break;
                        case 4:
                            courseMessage = _botCourseData[4][1].ToString();
                            courseImg = _botCourseData[4][2].ToString();
                            break;
                        case 5:
                            courseMessage = _botCourseData[5][1].ToString();
                            courseImg = _botCourseData[5][2].ToString();
                            break;
                        case 6:
                            courseMessage = _botCourseData[6][1].ToString();
                            courseImg = _botCourseData[6][2].ToString();
                            break;
                    }

                    if (courseMessage != null)
                    {
                        // Отправляем сообщение
                        if (string.IsNullOrEmpty(courseImg))
                        {
                            await _messageSender.SendMessage(user.Id, _cancellationToken, courseMessage, buttons);
                        }
                        else
                        {
                            var mediaGroup = await _messageSender.ConvertImgStringToMediaListAsync(courseImg);
                            if (mediaGroup.Count > 1)
                            {
                                await _messageSender.SendMediaGroupWithCaption(user.Id, _cancellationToken, mediaGroup, courseMessage, buttons);
                            }
                            else
                            {
                                await _messageSender.SendPhotoWithCaption(user.Id, _cancellationToken, courseImg, courseMessage, buttons);
                            }
                        }

                        //отдельная механика в середине курса с предложением по улучшению курса
                        if (user.CurrentCourseStep == 4)
                        {
                            buttons = new ReplyKeyboardMarkup(
                                        new KeyboardButton("✍ Предложить тему"),
                                        new KeyboardButton("🚩 К началу")
                                       );
                            buttons.ResizeKeyboard = true;
                            await _messageSender.SendMessage(user.Id, _cancellationToken, _botMessagesData[10][3].ToString(), buttons);
                        }

                        //отдельная механика для последнего урока
                        if (user.CurrentCourseStep == 6)
                        {
                            courseMessage = _botCourseData[7][1].ToString();
                            courseImg = _botCourseData[7][2].ToString();
                            buttons = new ReplyKeyboardMarkup(
                                new[] {
                                    new KeyboardButton("5️⃣"),
                                    new KeyboardButton("4️⃣"),
                                    new KeyboardButton("3️⃣"),
                                    new KeyboardButton("2️⃣"),
                                    new KeyboardButton("1️⃣")
                                });
                            buttons.ResizeKeyboard = true;
                            // Активируем флаг голосования
                            user.IsVotingForCourse = true;

                            // Устанавливаем CurrentCourseStep = 7, чтобы курс считался завершённым
                            user.CurrentCourseStep = 7;

                            var mediaGroup = await _messageSender.ConvertImgStringToMediaListAsync(courseImg);
                            if (mediaGroup.Count > 1)
                            {
                                await _messageSender.SendMediaGroupWithCaption(user.Id, _cancellationToken, mediaGroup, courseMessage, buttons);
                            }
                            else
                            {
                                await _messageSender.SendPhotoWithCaption(user.Id, _cancellationToken, courseImg, courseMessage, buttons);
                            }
                        }

                        // Обновляем пользователя в базе данных
                        user.LastLessonSentDate = DateTime.Now;
                        if (user.CurrentCourseStep < 7)
                        {
                            user.CurrentCourseStep++;
                        }

                        try
                        {
                            appDbUpdate.UpdateBotUserFields(user, _dbConnection);
                            return user;
                        }
                        catch (PostgresException pex)
                        {
                            _logger.Error(pex, $"Ошибка PostgreSQL: {pex.Message}");
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, $"Неизвестная ошибка: {ex.Message}");
                            throw;
                        }
                    }
                }

                return user;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка при отправке урока: {ex.Message}");
                throw;
            }
        }

        // Метод для подписки пользователя на курс
        public async Task<BotUser> SubscribeUserToCourse(BotUser user)
        {
            try
            {
                DateTime date = DateTime.Now;
                if (!user.IsSubscribed)
                {
                    user.IsSubscribed = true;
                    user.DateStartSubscribe = date;
                    user.LastLessonSentDate = null;
                    user.CurrentCourseStep = 1;

                    var appDbUpdate = new AppDBUpdate();
                    appDbUpdate.UpdateBotUserFields(user, _dbConnection);

                    // Отправляем первый урок сразу
                    user = await SendTrainingCourseMessage(user);
                }
                else
                {
                    await _messageSender.SendMessage(user.Id, _cancellationToken,
                        "Ты уже подписан(а) на курс. Обучающие материалы выходят каждую неделю. Следи за обновлениями",
                        _startButton);
                }

                return user;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка при подписке пользователя: {ex.Message}");                
                throw;
            }
        }

        // Метод для принудительной проверки пользователя
        public async Task ForceCheckUser(long userId)
        {
            try
            {
                using (var db = new DataConnection(_dbConnection))
                {
                    var user = await db.GetTable<BotUser>()
                        .FirstOrDefaultAsync(u => u.Id == userId);

                    if (user != null)
                    {
                        await CheckAndSendLessonForUser(user);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при принудительной проверке пользователя: {ex.Message}");
            }
        }
    }
}
