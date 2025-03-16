using LinqToDB.Mapping;

namespace HRProBot.Models
{
    [Table(Name = "UserQuestion")]
    public class UserQuestion
    {
        [PrimaryKey, Identity]
        [Column(Name = "Id")]
        public long Id { get; set; }

        [Column(Name = "BotUserId")]
        public long BotUserId { get; set; }

        [Column(Name = "QuestionText")]
        public string QuestionText { get; set; }
    }
}