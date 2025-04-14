using System.IO;

namespace HRProBot.Controllers
{
    public class FileController
    {
        public static async Task<string> SaveFileFromUrl(string fileUrl, string folderName)
        {
            try
            {
                // Создаем папку, если она не существует
                string projectDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string folderPath = Path.Combine(projectDirectory, folderName);
                Directory.CreateDirectory(folderPath);

                // Получаем имя файла из URL
                string fileName = Path.GetFileName(new Uri(fileUrl).LocalPath);
                string filePath = Path.Combine(folderPath, fileName);

                // Скачиваем файл
                using (var httpClient = new HttpClient())
                {
                    var fileBytes = await httpClient.GetByteArrayAsync(fileUrl);
                    await File.WriteAllBytesAsync(filePath, fileBytes);
                }

                return filePath; // Возвращаем путь к сохраненному файлу
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка при сохранении файла: {ex.Message}");
            }
        }
    }
}
