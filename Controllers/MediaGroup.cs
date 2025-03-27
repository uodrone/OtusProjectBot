public class MediaGroup
{
    public string MediaGroupId { get; } // ID медиагруппы
    public List<(long ChatId, string FileId)> Files { get; } = new List<(long ChatId, string FileId)>(); // Список файлов
    public DateTime LastMessageTime { get; private set; } = DateTime.UtcNow; // Время последнего сообщения
    public bool IsProcessing { get; set; } = false; // Флаг обработки
    public string Caption { get; set; } // Текст сообщения (подпись)

    public MediaGroup(string mediaGroupId)
    {
        MediaGroupId = mediaGroupId;
    }

    // Добавляем файл в медиагруппу
    public void AddFile(long chatId, string fileId)
    {
        Files.Add((chatId, fileId));
        LastMessageTime = DateTime.UtcNow; // Обновляем время последнего сообщения
    }

    // Проверяем, завершена ли медиагруппа (прошло ли более 500 мс с последнего сообщения)
    public bool IsComplete()
    {
        return (DateTime.UtcNow - LastMessageTime).TotalMilliseconds > 500;
    }
}