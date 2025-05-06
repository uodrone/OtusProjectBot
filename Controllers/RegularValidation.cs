using System.Text.RegularExpressions;

namespace HRProBot.Controllers
{
    public class RegularValidation
    {
        public bool ValidateName(string name)
        {
            // Регулярное выражение для имени и фамилии:
            string pattern = @"^[a-zA-Zа-яА-Я]{2,100}$";
            return Regex.IsMatch(name, pattern);
        }

        public bool ValidateOrganization(string org)
        {
            // Регулярное выражение для организации:
            string pattern = @"^.{2,300}$";
            return Regex.IsMatch(org, pattern);
        }

        public bool ValidatePhone(string phone)
        {
            // Регулярное выражение для телефона:
            string pattern = @"^(?=(?:.*\d){11})(\+?(\d{1,3})[-\s]?)?(\d{1,4}[-\s]?){2,}\d{1,4}$";
            return Regex.IsMatch(phone, pattern);
        }
    }
}
