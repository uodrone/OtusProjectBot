using System;
using System.Threading;
using System.Threading.Tasks;
using HRProBot.Models;
using Telegram.Bot;

namespace HRProBot.Controllers
{
    public class CourseController
    {
        private BotUser _user;
        private ITelegramBotClient _botClient;
        private Timer _timer;

        public CourseController(BotUser user, ITelegramBotClient botClient)
        {
            _user = user;
            _botClient = botClient;
        }

        private async void SendTrainingCourseMessage(object state)
        {
            string CourseMessage = null;

            if (_user.IsSubscribed && _user.DateStartSubscribe <= DateTime.Now)
            {
                switch (_user.CurrentCourseStep)
                {
                    case 1:
                        CourseMessage = "Отправляю первый материал курса";
                        _user.CurrentCourseStep++;
                        break;
                    case 2:
                        CourseMessage = "Отправляю второй материал курса";
                        _user.CurrentCourseStep++;
                        break;
                    case 3:
                        CourseMessage = "Отправляю третий материал курса";
                        _user.CurrentCourseStep++;
                        break;
                    case 4:
                        CourseMessage = "Отправляю четвертый материал курса";
                        _user.CurrentCourseStep++;
                        break;
                    case 5:
                        CourseMessage = "Отправляю пятый материал курса";
                        StopSendingMaterials();
                        break;
                }

                if (CourseMessage != null)
                {
                    await _botClient.SendTextMessageAsync(_user.Id, CourseMessage);
                }
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