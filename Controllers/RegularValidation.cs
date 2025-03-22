using System.Text.RegularExpressions;

namespace HRProBot.Controllers
{
    public class RegularValidation
    {
        public bool ValidateName(string name)
        {
            // Регулярное выражение для имени и фамилии:
            // - Допустимы только буквы (латиница или кириллица)
            // - Длина от 2 до 50 символов
            string pattern = @"^[a-zA-Zа-яА-Я]{2,100}$";
            return Regex.IsMatch(name, pattern);
        }

        // Валидация организации
        public bool ValidateOrganization(string org)
        {
            // Регулярное выражение для организации:
            // - Допустимы буквы, цифры, пробелы и дефисы
            // - Длина от 2 до 100 символов
            string pattern = @"^.{2,300}$";
            return Regex.IsMatch(org, pattern);
        }

        // Валидация телефона
        public bool ValidatePhone(string phone)
        {
            // Регулярное выражение для телефона:
            string pattern = @"^(\+?(\d{1,3})[-\s]?)?(\d{1,4}[-\s]?){2,}\d{1,4}$";
            return Regex.IsMatch(phone, pattern);
        }
    }
}
