using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Sheets.v4;
using HRProBot.Models;
using Microsoft.Extensions.Options;
using System.Text;
using HRProBot.Interfaces;

namespace HRProBot.Controllers
{
    public class GoogleSheetsController : IGoogleSheets
    {
        static string _spreadsheetId;
        static GoogleCredential _credential;

        public GoogleSheetsController(IOptionsSnapshot<AppSettings> appSettings) {
            // ID Google таблицы (это часть URL таблицы)
            _spreadsheetId = appSettings.Value.GoogleSheetsTableId;

            // Путь к JSON-файлу с учетными данными таблиц
            string credentialPath = appSettings.Value.GoogleCredentialsFile;

            using (var stream = new FileStream(credentialPath, FileMode.Open, FileAccess.Read))
            {
                _credential = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);
            }
        }

        public IList<IList<object>> GetData(string range) {
            // Создаем сервис для работы с Google Sheets API
            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = _credential,
                ApplicationName = "Google Sheets API HRProBot",
            });

            // Запрос данных из таблицы
            SpreadsheetsResource.ValuesResource.GetRequest request = service.Spreadsheets.Values.Get(_spreadsheetId, range);

            ValueRange response = request.Execute();
            IList<IList<object>> responseValues = response.Values;


            return responseValues;
        }
    }
}
