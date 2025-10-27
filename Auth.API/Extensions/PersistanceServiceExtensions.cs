using Auth.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace Auth.API.Extensions
{
    public static class PersistenceServiceExtensions
    {
        public static IServiceCollection AddPersistenceServices(this IServiceCollection services, IConfiguration configuration)
        {
            string connectionString;
            var isRailway = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PGHOST"));

            if (isRailway)
            {
                var host = Environment.GetEnvironmentVariable("PGHOST");
                var port = Environment.GetEnvironmentVariable("PGPORT") ?? "5432";
                var database = Environment.GetEnvironmentVariable("PGDATABASE");
                var username = Environment.GetEnvironmentVariable("PGUSER");
                var password = Environment.GetEnvironmentVariable("PGPASSWORD");

                connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password};Pooling=true;Trust Server Certificate=true";

                Console.WriteLine($"🌍 Running on Railway → Using PostgreSQL database at {host}:{port}");

                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseNpgsql(connectionString));
            }
            else
            {
                connectionString = configuration.GetConnectionString("DefaultConnection")
                                   ?? Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

                Console.WriteLine("💻 Running locally → Using SQL Server.");

                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlServer(connectionString));
            }

            return services;
        }
    }
}
