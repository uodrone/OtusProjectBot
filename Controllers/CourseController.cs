using HRProBot.Models;

namespace HRProBot.Controllers
{
    public class CourseController
    {
        BotUser _user;
        DateTime _date;
        public CourseController(BotUser user, DateTime date)
        {
            _user = user;
            _date = date;
        }

        public string SendTrainingCourceMessage()
        {
            
            return null;
        }
    }
}
