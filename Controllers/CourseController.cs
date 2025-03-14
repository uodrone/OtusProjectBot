using System;
using System.Threading;
using System.Threading.Tasks;
using HRProBot.Models;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

namespace HRProBot.Controllers
{
    public class CourseController : IDisposable
    {
        private readonly BotUser _user;
        private readonly ITelegramBotClient _botClient;
        private readonly AppDbContext _context;
        private Timer _timer;
        private CancellationTokenSource _cts;

        public CourseController(BotUser user,ITelegramBotClient botClient, AppDbContext context)
        {
            _user = user;
            _botClient = botClient;
            _context = context;
            _cts = new CancellationTokenSource();
        }

        private async void SendTrainingCourseMessage(object state)
        {
            try
            {
                string courseMessage = null;
                var user = await _context.BotUsers.FirstOrDefaultAsync(u => u.Id == _user.Id);

                if (user == null || !user.IsSubscribed || user.DateStartSubscribe > DateTime.Now)
                {
                    return;
                }   
                    

                switch (user.CurrentCourseStep)
                {
                    case 1:
                        courseMessage = "Отправляю первый материал курса";
                        user.CurrentCourseStep++;
                        break;
                    case 2:
                        courseMessage = "Отправляю второй материал курса";
                        user.CurrentCourseStep++;
                        break;
                    case 3:
                        courseMessage = "Отправляю третий материал курса";
                        user.CurrentCourseStep++;
                        break;
                    case 4:
                        courseMessage = "Отправляю четвертый материал курса";
                        user.CurrentCourseStep++;
                        break;
                    case 5:
                        courseMessage = "Отправляю пятый материал курса";
                        user.IsSubscribed = false;
                        StopSendingMaterials();
                        break;
                }

                if (courseMessage != null)
                {
                    await _context.SaveChangesAsync(_cts.Token);
                    await _botClient.SendTextMessageAsync(user.Id, courseMessage);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в записи на курс: {ex.Message}");
                StopSendingMaterials();
            }
        }

        public void StartSendingMaterials()
        {
            // Запускаем таймер с интервалом 7 дней (вместо 7 секунд для демонстрации)
            _timer = new Timer(SendTrainingCourseMessage, null, TimeSpan.Zero, TimeSpan.FromDays(7));
        }

        public void StopSendingMaterials()
        {
            _timer?.Change(Timeout.Infinite, 0);
            _cts.Cancel();
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _cts?.Dispose();
            _context?.Dispose();
        }
    }
}