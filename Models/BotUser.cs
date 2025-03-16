using LinqToDB.Mapping;
using System.ComponentModel.DataAnnotations;

namespace HRProBot.Models
{
    public class BotUser
    {
        [PrimaryKey]
        public long Id { get; set; }
        [MaxLength(255)]
        public string UserName { get; set; }
        [MaxLength(255)]
        public string FirstName { get; set; }
        [MaxLength(255)]
        public string LastName { get; set; }
        public string Organization { get; set; }
        [MaxLength(32)]
        public string Phone { get; set; }
        public List<string> Question { get; set; } = new List<string>();
        public List<string> Answer { get; set; } = new List<string>();
        public bool IsSubscribed { get; set; }
        public DateTime? DateStartSubscribe { get; set; }
        public int DataCollectStep {  get; set; }
        public int CurrentCourseStep { get; set; } = 1;        
    }
}
