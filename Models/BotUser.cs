using System.ComponentModel.DataAnnotations;

namespace HRProBot.Models
{
    public class BotUser
    {
        public long Id { get; set; }
        public string UserName { get; set; }
        [MaxLength(100)]
        public string FirstName { get; set; }
        [MaxLength(100)]
        public string LastName { get; set; }
        public string Organization { get; set; }
        public string Phone { get; set; }
        public string Question { get; set; }
        public string Answer { get; set; }
        public bool IsSubscribed { get; set; }
        public DateTime? DateStartSubscribe { get; set; }
        public int DataCollectStep {  get; set; }
        public int CurrentCourseStep { get; set; } = 1;        
    }
}
