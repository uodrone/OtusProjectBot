using Google.Apis.Auth.OAuth2;

namespace HRProBot.Interfaces
{
    public interface IGoogleSheets
    {
        static string _spreadsheetId;
        static GoogleCredential _credential;

        string GetData(string range);
    }
}
