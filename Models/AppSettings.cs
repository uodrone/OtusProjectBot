namespace HRProBot.Models
{
    public class AppSettings
    {
        public string TlgBotToken { get; set; }
        public string TlgBotAdministrators { get; set; }
        public string GoogleSheetsTableId { get; set; }
        public string GoogleSheetsRange { get; set; }
        public string GoogleSheetsCourseRange { get; set; }
        public string GoogleSheetsMailing { get; set; }
        public string GoogleCredentialsFile { get; set; }
        public string DBConnection { get; set; }
    }
}
