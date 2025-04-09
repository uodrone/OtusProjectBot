using System;
using System.Threading;
using System.Threading.Tasks;
using HRProBot.Interfaces;
using HRProBot.Models;
using LinqToDB;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace HRProBot.Controllers
{
    public class CourseController
    {
        private BotUser _user;
        private ITelegramBotClient _botClient;
        private static IOptionsSnapshot<AppSettings> _appSettings;
        private static GoogleSheetsController _googleSheets;
        private static IList<IList<object>> _botCourseData;
        private Timer _timer;
        private string _dbConnection;

        public CourseController(BotUser user, ITelegramBotClient botClient, IOptionsSnapshot<AppSettings> appSettings, string dbConnection)
        {
            _user = user;
            _botClient = botClient;
            _appSettings = appSettings;
            _googleSheets = new GoogleSheetsController(_appSettings);
            _botCourseData = _googleSheets.GetData(_appSettings.Value.GoogleSheetsCourseRange);
            _dbConnection = dbConnection;
        }

        private async void SendTrainingCourseMessage(object state)
        {
            string courseMessage = null;
            var appDbUpdate = new AppDBUpdate();

            if (_user.IsSubscribed && _user.DateStartSubscribe <= DateTime.Now)
            {
                switch (_user.CurrentCourseStep)
                {
                    case 1:
                        courseMessage = _botCourseData[1][1].ToString();
                        appDbUpdate.UserDbUpdate(_user, _dbConnection);
                        _user.CurrentCourseStep++;
                        break;
                    case 2:
                        courseMessage = _botCourseData[2][1].ToString();
                        appDbUpdate.UserDbUpdate(_user, _dbConnection);
                        _user.CurrentCourseStep++;
                        break;
                    case 3:
                        courseMessage = _botCourseData[3][1].ToString();
                        appDbUpdate.UserDbUpdate(_user, _dbConnection);
                        _user.CurrentCourseStep++;
                        break;
                    case 4:
                        courseMessage = _botCourseData[4][1].ToString();
                        appDbUpdate.UserDbUpdate(_user, _dbConnection);
                        _user.CurrentCourseStep++;
                        break;
                    case 5:
                        courseMessage = _botCourseData[5][1].ToString();
                        appDbUpdate.UserDbUpdate(_user, _dbConnection);
                        _user.CurrentCourseStep++;
                        break;
                    case 6:
                        courseMessage = _botCourseData[6][1].ToString();
                        appDbUpdate.UserDbUpdate(_user, _dbConnection);
                        StopSendingMaterials();
                        break;
                }

                if (courseMessage != null)
                {
                    await _botClient.SendTextMessageAsync(_user.Id, courseMessage);
                }
            } 
            else
            {
                await _botClient.SendTextMessageAsync(_user.Id, "Ошибка подписки");
            }
        }

        public void StartSendingMaterials()
        {
            // Запускаем таймер с интервалом 7 секунд
            _timer = new Timer(SendTrainingCourseMessage, null, TimeSpan.Zero, TimeSpan.FromSeconds(7));
        }

        public void StopSendingMaterials()
        {
            // Останавливаем таймер
            _timer?.Change(Timeout.Infinite, 0);
        }
    }
}