using System;
using System.Threading;
using System.Threading.Tasks;
using HRProBot.Interfaces;
using HRProBot.Models;
using HRProBot.Services;
using LinqToDB;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace HRProBot.Controllers
{
    public class CourseController
    {
        private BotUser _user;
        private ITelegramBotClient _botClient;
        private static IOptionsSnapshot<AppSettings> _appSettings;
        private static GoogleSheetsController _googleSheets;
        private static IList<IList<object>> _botCourseData;
        private static MessageSender _messageSender;
        private Timer _timer;
        private string _dbConnection;
        private CancellationToken _cantellationToken;

        public CourseController(BotUser user, ITelegramBotClient botClient, IOptionsSnapshot<AppSettings> appSettings, string dbConnection)
        {
            _user = user;
            _botClient = botClient;
            _appSettings = appSettings;
            _googleSheets = new GoogleSheetsController(_appSettings);
            _botCourseData = _googleSheets.GetData(_appSettings.Value.GoogleSheetsCourseRange);
            _messageSender = new MessageSender(botClient);
            _dbConnection = dbConnection;
            _cantellationToken = new CancellationTokenSource().Token;
        }

        private async void SendTrainingCourseMessage(object state)
        {
            string courseMessage = null;
            string courseImg = null;
            var buttons = new ReplyKeyboardMarkup(
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
            buttons.ResizeKeyboard = true;
            var appDbUpdate = new AppDBUpdate();

            try
            {
                if (_user.IsSubscribed && _user.DateStartSubscribe <= DateTime.Now)
                {
                    switch (_user.CurrentCourseStep)
                    {
                        case 1:
                            courseMessage = _botCourseData[1][1].ToString();
                            courseImg = _botCourseData[1][2].ToString();
                            appDbUpdate.UserDbUpdate(_user, _dbConnection);
                            _user.CurrentCourseStep++;
                            break;
                        case 2:
                            courseMessage = _botCourseData[2][1].ToString();
                            courseImg = _botCourseData[2][2].ToString();
                            appDbUpdate.UserDbUpdate(_user, _dbConnection);
                            _user.CurrentCourseStep++;
                            break;
                        case 3:
                            courseMessage = _botCourseData[3][1].ToString();
                            courseImg = _botCourseData[3][2].ToString();
                            appDbUpdate.UserDbUpdate(_user, _dbConnection);
                            _user.CurrentCourseStep++;
                            break;
                        case 4:
                            courseMessage = _botCourseData[4][1].ToString();
                            courseImg = _botCourseData[4][2].ToString();
                            appDbUpdate.UserDbUpdate(_user, _dbConnection);
                            _user.CurrentCourseStep++;
                            break;
                        case 5:
                            courseMessage = _botCourseData[5][1].ToString();
                            courseImg = _botCourseData[5][2].ToString();
                            appDbUpdate.UserDbUpdate(_user, _dbConnection);
                            _user.CurrentCourseStep++;
                            break;
                        case 6:
                            courseMessage = _botCourseData[6][1].ToString();
                            courseImg = _botCourseData[6][2].ToString();
                            appDbUpdate.UserDbUpdate(_user, _dbConnection);
                            _user.CurrentCourseStep++;
                            _timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(0));
                            break;
                        case 7:
                            courseMessage = _botCourseData[7][1].ToString();
                            courseImg = _botCourseData[7][2].ToString();
                            appDbUpdate.UserDbUpdate(_user, _dbConnection);
                            StopSendingMaterials();
                            break;
                    }

                    if (courseMessage != null)
                    {
                        if (string.IsNullOrEmpty(courseImg))
                        {
                            await _messageSender.SendMessage(_user.Id, _cantellationToken, courseMessage, buttons);
                        }
                        else
                        {
                            var mediaGroup = await _messageSender.ConvertImgStringToMediaListAsync(courseImg);
                            if (mediaGroup.Count > 1)
                            {
                                await _messageSender.SendMediaGroupWithCaption(_user.Id, _cantellationToken, mediaGroup, courseMessage, buttons);
                            }
                            else
                            {
                                await _messageSender.SendPhotoWithCaption(_user.Id, _cantellationToken, courseImg, courseMessage, buttons);
                            }
                        }
                    }
                }
                else
                {
                    await _botClient.SendTextMessageAsync(_user.Id, "Ошибка подписки");
                }
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public void StartSendingMaterials()
        {
            // Запускаем таймер с интервалом 7 секунд
            _timer = new Timer(SendTrainingCourseMessage, null, TimeSpan.Zero, TimeSpan.FromSeconds(7));
        }

        public async void StopSendingMaterials()
        {
            // Останавливаем таймер
            _timer?.Change(Timeout.Infinite, 0);
        }
    }
}