using HRProBot.Models;
using LinqToDB;
using Telegram.Bot;

namespace HRProBot.Controllers
{
    public class AppDBUpdate
    {
        public void UserDbUpdate(BotUser user, string dbConnection)
        {
            using (var db = new LinqToDB.Data.DataConnection(ProviderName.PostgreSQL, dbConnection))
            {
                var RowUser = db.GetTable<BotUser>().FirstOrDefault(x => x.Id == user.Id);

                if (RowUser != null)
                {
                    db.Update(user);
                }
            }
        }
    }
}
