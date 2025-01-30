using HRProBot.Models;
using Telegram.Bot.Types;

namespace HRProBot.Controllers
{
    public class CourseController
    {
        private static Timer timer;
        private BotUser _user;
        private DateTime _date;
        private static int daysInterval = 7;
        public CourseController(BotUser user, DateTime date)
        {
            _user = user;
            _date = date;
        }

        public string SendTrainingCourceMessage()
        {
            string CourseMessage = null;

            if (_user.IsSubscribed && _user.DateStartSubscribe <= DateTime.Now) { 
                switch (_user.CurrentCourseStep)
                {
                    case 1:
                        CourseMessage = "Отправляю первый материал курса";
                        _user.CurrentCourseStep++;
                        // Установка первого таймера для отправки первого сообщения сразу после запуска
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
                        break;
                }                
            }

            

            return CourseMessage;
        }
    }
}
