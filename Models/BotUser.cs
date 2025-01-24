namespace HRProBot.Models
{
    public class BotUser
    {
        public long Id { get; set; }
        public string? UserName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Organization { get; set; }
        public string? Phone { get; set; }
        public string? Question { get; set; }
        public bool IsSubscribed { get; set; }
        public int DataCollectStep {  get; set; }
    }
}
