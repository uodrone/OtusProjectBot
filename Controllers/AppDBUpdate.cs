using HRProBot.Models;
using LinqToDB;
using Telegram.Bot;

namespace HRProBot.Controllers
{
    public class AppDBUpdate
    {
        /// <summary>
        /// Обновляет запись в таблице базы данных.
        /// </summary>
        /// <typeparam name="T">Тип модели, соответствующей таблице.</typeparam>
        /// <param name="entity">Объект модели для обновления.</param>
        /// <param name="dbConnection">Строка подключения к базе данных.</param>
        public void UserDbUpdate<T>(T entity, string dbConnection) where T : class
        {
            using (var db = new LinqToDB.Data.DataConnection(ProviderName.PostgreSQL, dbConnection))
            {
                // Получаем первичный ключ сущности
                var primaryKey = GetPrimaryKey(db, entity);

                // Ищем запись в таблице по первичному ключу
                var existingEntity = db.GetTable<T>().FirstOrDefault(primaryKey);

                if (existingEntity != null)
                {
                    // Обновляем запись
                    db.Update(entity);
                }
            }
        }

        /// <summary>
        /// Получает условие для поиска записи по первичному ключу.
        /// </summary>
        /// <typeparam name="T">Тип модели.</typeparam>
        /// <param name="db">Контекст базы данных.</param>
        /// <param name="entity">Объект модели.</param>
        /// <returns>Условие для поиска по первичному ключу.</returns>
        private Func<T, bool> GetPrimaryKey<T>(LinqToDB.Data.DataConnection db, T entity) where T : class
        {
            var table = db.MappingSchema.GetEntityDescriptor(typeof(T));
            var primaryKey = table.Columns.FirstOrDefault(c => c.IsPrimaryKey);

            if (primaryKey == null)
            {
                throw new InvalidOperationException($"Таблица {typeof(T).Name} не имеет первичного ключа.");
            }

            // Получаем значение первичного ключа из сущности
            var primaryKeyValue = primaryKey.MemberAccessor.Getter(entity);

            // Создаем условие для поиска по первичному ключу
            return x => primaryKey.MemberAccessor.Getter(x).Equals(primaryKeyValue);
        }
    }
}
