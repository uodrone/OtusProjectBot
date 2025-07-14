using HRProBot.Controllers;
using HRProBot.Models;
using HRProBot.Services;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace HRProBot
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Регистрируем AppSettings как конфигурацию
            builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));

            // Регистрируем Telegram Bot Client
            builder.Services.AddScoped<ITelegramBotClient>(provider =>
            {
                var appSettings = provider.GetRequiredService<IOptionsSnapshot<AppSettings>>();
                return new TelegramBotClient(appSettings.Value.TlgBotToken);
            });

            // Регистрируем CourseController как сервис
            builder.Services.AddScoped<CourseController>();

            // Регистрируем фоновый сервис для курсов
            builder.Services.AddHostedService<CourseBackgroundService>();

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}