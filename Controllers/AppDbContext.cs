using HRProBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HRProBot.Controllers
{
    public class AppDbContext : DbContext
    {
        public DbSet<BotUser> _botUsers { get; set; }
        string _connectionPath;

        public AppDbContext(DbSet<BotUser> botUsers, IOptionsSnapshot<AppSettings> appSettings)
        {
            _botUsers = botUsers;
            _connectionPath = appSettings.Value.DBConnection;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(_connectionPath);
        }
    }
}
