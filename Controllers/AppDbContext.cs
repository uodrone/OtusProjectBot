using HRProBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HRProBot.Controllers
{
    public class AppDbContext : DbContext
    {
        public DbSet<BotUser> BotUsers { get; set; }
        private readonly string _connectionString;

        // Конструктор должен принимать только конфигурацию
        public AppDbContext(DbContextOptions<AppDbContext> options, IOptions<AppSettings> appSettings):base(options)
        {
            _connectionString = appSettings.Value.DBConnection;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseNpgsql(_connectionString);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BotUser>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FirstName).HasMaxLength(100);
                entity.Property(e => e.LastName).HasMaxLength(100);
            });
        }
    }
}
