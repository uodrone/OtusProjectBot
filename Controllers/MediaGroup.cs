public class MediaGroup
{
    public string MediaGroupId { get; }
    public List<(long ChatId, string FileId)> Files { get; } = new List<(long ChatId, string FileId)>();
    public DateTime LastMessageTime { get; private set; } = DateTime.UtcNow;
    // Флаг обработки
    public bool IsProcessing { get; set; } = false;
    // Текст сообщения (подпись)
    public string Caption { get; set; }

    public MediaGroup(string mediaGroupId)
    {
        MediaGroupId = mediaGroupId;
    }

    // Добавляем файл в медиагруппу
    public void AddFile(long chatId, string fileId)
    {
        Files.Add((chatId, fileId));
        LastMessageTime = DateTime.UtcNow;
    }

    // Проверяем, завершена ли медиагруппа (прошло ли более 500 мс с последнего сообщения)
    public bool IsComplete()
    {
        return (DateTime.UtcNow - LastMessageTime).TotalMilliseconds > 500;
    }
}