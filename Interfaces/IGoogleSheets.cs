using Google.Apis.Auth.OAuth2;

namespace HRProBot.Interfaces
{
    public interface IGoogleSheets
    {
        static string _spreadsheetId;
        static GoogleCredential _credential;

        IList<IList<object>> GetData(string range);
    }
}
