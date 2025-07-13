using LinqToDB.Mapping;
using System.Collections.Generic;

namespace HRProBot.Models
{
    [Table(Name = "BotUser")]
    public class BotUser
    {
        [PrimaryKey]
        [Column(Name = "Id")]
        public long Id { get; set; }

        [Column(Name = "UserName"), Nullable]
        public string UserName { get; set; }

        [Column(Name = "FirstName"), Nullable]
        public string FirstName { get; set; }

        [Column(Name = "LastName"), Nullable]
        public string LastName { get; set; }

        [Column(Name = "Organization"), Nullable]
        public string Organization { get; set; }

        [Column(Name = "Phone"), Nullable]
        public string Phone { get; set; }

        [Column(Name = "IsSubscribed")]
        public bool IsSubscribed { get; set; }

        [Column(Name = "DateStartSubscribe"), Nullable]
        public DateTime? DateStartSubscribe { get; set; }

        [Column(Name = "DataCollectStep")]
        public int DataCollectStep { get; set; }

        [Column(Name = "IsCollectingData")]
        public bool IsCollectingData { get; set; } = false;

        [Column(Name = "CurrentCourseStep")]
        public int CurrentCourseStep { get; set; } = 1;

        [Column(Name = "CourseAssesment")]
        public int CourseAssesment { get; set; } = 0;

        [Column(Name = "IsVotingForCourse")]
        public bool IsVotingForCourse { get; set; } = false;

        // Новое поле для хранения даты последней отправки урока
        [Column(Name = "LastLessonSentDate"), Nullable]
        public DateTime? LastLessonSentDate { get; set; }

        // Навигационное свойство для вопросов
        [Association(ThisKey = nameof(Id), OtherKey = nameof(UserQuestion.BotUserId))]
        public List<UserQuestion> Questions { get; set; } = new List<UserQuestion>();

        // Навигационное свойство для ответов
        [Association(ThisKey = nameof(Id), OtherKey = nameof(UserAnswer.BotUserId))]
        public List<UserAnswer> Answers { get; set; } = new List<UserAnswer>();
    }
}