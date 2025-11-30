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
                // Local PostgreSQL
                var host = Environment.GetEnvironmentVariable("PGHOST") ?? "localhost";
                var port = Environment.GetEnvironmentVariable("PGPORT") ?? "5432";
                var database = Environment.GetEnvironmentVariable("PGDATABASE") ?? "scholar_local";
                var username = Environment.GetEnvironmentVariable("PGUSER") ?? "postgres";
                var password = Environment.GetEnvironmentVariable("PGPASSWORD") ?? "1234";

                connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password};Pooling=true;Trust Server Certificate=true";

                Console.WriteLine($"💻 Running locally → Using PostgreSQL database at {host}:{port}");

                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseNpgsql(connectionString));
            }

            return services;
        }
    }
}
