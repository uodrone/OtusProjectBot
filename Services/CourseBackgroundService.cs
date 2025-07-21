using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;
using HRProBot.Controllers;

namespace HRProBot.Services
{
    public class CourseBackgroundService : BackgroundService
    {
        private readonly ILogger<CourseBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(30); // Проверяем каждые 30 минут

        public CourseBackgroundService(
            ILogger<CourseBackgroundService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Course Background Service запущен");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var courseController = scope.ServiceProvider.GetRequiredService<CourseController>();

                        _logger.LogInformation("Проверка пользователей на отправку уроков...");
                        await courseController.CheckAllSubscribedUsersAsync();
                        _logger.LogInformation("Проверка завершена");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при проверке пользователей");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Course Background Service остановлен");
            await base.StopAsync(cancellationToken);
        }
    }
}