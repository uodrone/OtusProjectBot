using HRProBot.Models;
using LinqToDB;
using LinqToDB.Data;
using NLog;
using System.Linq.Expressions;
using Telegram.Bot.Types;

namespace HRProBot.Controllers
{
    public class AppDBUpdate
    {
        private static Logger _logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Обновляет указанные поля пользователя в БД.
        /// </summary>
        public async Task UpdateBotUserFields(BotUser user, string dbConnection, params Expression<Func<BotUser, object>>[] fields)
        {
            try
            {
                using (var db = new DataConnection(ProviderName.PostgreSQL, dbConnection))
                {
                    // Используем обновление с явным указанием полей
                    await db.GetTable<BotUser>()
                        .Where(u => u.Id == user.Id)
                        .Set(u => u.IsSubscribed, user.IsSubscribed)
                        .Set(u => u.DateStartSubscribe, user.DateStartSubscribe)
                        .Set(u => u.LastLessonSentDate, user.LastLessonSentDate)
                        .Set(u => u.CurrentCourseStep, user.CurrentCourseStep)
                        .Set(u => u.IsVotingForCourse, user.IsVotingForCourse)
                        .Set(u => u.UserName, user.UserName)
                        .Set(u => u.FirstName, user.FirstName)
                        .Set(u => u.LastName, user.LastName)
                        .Set(u => u.Organization, user.Organization)
                        .Set(u => u.Phone, user.Phone)
                        .Set(u => u.DataCollectStep, user.DataCollectStep)
                        .Set(u => u.IsCollectingData, user.IsCollectingData)
                        .Set(u => u.CourseAssesment, user.CourseAssesment)
                        .UpdateAsync();

                    _logger.Info($"Пользователь {user.Id} успешно обновлен (асинхронный метод Set)");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка обновления БД (асинхронный метод Set): {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Полное обновление пользователя (если нужно).
        /// </summary>
        public void UserDbUpdate(BotUser user, string dbConnection)
        {
            try
            {
                using (var db = new DataConnection(ProviderName.PostgreSQL, dbConnection))
                {
                    db.Update(user);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Ошибка обновления БД: {ex.Message}");
            }
        }
    }
}