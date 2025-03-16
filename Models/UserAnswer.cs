using LinqToDB.Mapping;

namespace HRProBot.Models
{
    [Table(Name = "UserAnswer")]
    public class UserAnswer
    {
        [PrimaryKey, Identity]
        [Column(Name = "Id")]
        public long Id { get; set; }

        [Column(Name = "BotUserId")]
        public long BotUserId { get; set; }

        [Column(Name = "AnswerText")]
        public string AnswerText { get; set; }

        // Навигационное свойство для пользователя
        [Association(ThisKey = nameof(BotUserId), OtherKey = nameof(BotUser.Id))]
        public BotUser User { get; set; }
    }
}