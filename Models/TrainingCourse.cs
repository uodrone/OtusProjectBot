namespace HRProBot.Models
{
    public class TrainingCourse
    {
        public int Id { get; set; }
        public string Text { get; set; }        
        public string? Image { get; set; }
        public string? Video { get; set; }
        public List<string> MediaGroup { get; set; }
    }
}
