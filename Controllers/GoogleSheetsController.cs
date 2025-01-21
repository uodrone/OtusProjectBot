using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Sheets.v4;
using HRProBot.Models;
using Microsoft.Extensions.Options;
using System.Text;
using System.Net;
using System;

namespace HRProBot.Controllers
{
    public class GoogleSheetsController
    {
        static string _spreadsheetId;
        static GoogleCredential _credential;
        private readonly ILogger<HomeController> _logger;

        public GoogleSheetsController(IOptionsSnapshot<AppSettings> appSettings) {
            // ID Google таблицы (это часть URL таблицы)
            _spreadsheetId = appSettings.Value.GoogleSheetsTableId;

            // Укажите путь к вашему JSON-файлу с учетными данными
            string credentialPath = "/credentials.json";

            using (var stream = new System.IO.FileStream(credentialPath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
            {
                _credential = GoogleCredential.FromStream(stream)
                    .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);
            }
        }

        public string GetData(string range) {
            // Создаем сервис для работы с Google Sheets API
            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = _credential,
                ApplicationName = "Google Sheets API HRProBot",
            });

            // Запрос данных из таблицы
            SpreadsheetsResource.ValuesResource.GetRequest request =
                service.Spreadsheets.Values.Get(_spreadsheetId, range);

            ValueRange response = request.Execute();
            IList<IList<object>> values = response.Values;
            StringBuilder stringBuilder = new StringBuilder();

            if (values != null && values.Count > 0)
            {
                foreach (var row in values)
                {
                    stringBuilder.Append(row.ToString());
                    Console.WriteLine(string.Join(", ", row));
                }
            }

            return stringBuilder.ToString();
        }
    }
}
