using HRProBot.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace HRProBot.Controllers
{
    public class HomeController : Controller
    {
        private readonly IOptionsSnapshot<AppSettings> _appSettings;

        public HomeController(IOptionsSnapshot<AppSettings> appSettings)
        {
            _appSettings = appSettings;
        }

        public IActionResult Index()
        {
            //Инициализируем бот
            new BotController(_appSettings);
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
