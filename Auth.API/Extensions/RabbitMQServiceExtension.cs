using Auth.Services.Interfaces;
using Auth.Services.Services;
using DotNetEnv;

namespace Auth.API.Extensions
{
    public static class RabbitMQServiceExtensions
    {
        public static IServiceCollection AddRabbitMQServices(this IServiceCollection services, ConfigurationManager configuration)
        {
            // RABBITMQ_ENABLED existed in configuration but was read nowhere, so the app
            // connected to RABBITMQ_HOST regardless. In production that host is "localhost"
            // with no broker, so every startup attempted a connection, failed, and wrote
            // several pages of stack trace to the deploy log — once per consumed queue.
            // That noise is what a real startup failure has to compete with.
            var enabled = (Env.GetString("RABBITMQ_ENABLED") ?? "true")
                .Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

            if (!enabled)
            {
                var logger = LoggerFactory.Create(b => b.AddConsole())
                    .CreateLogger("RabbitMQServiceExtensions");
                logger.LogInformation(
                    "RABBITMQ_ENABLED is false — using the no-op message broker. Queued email is not delivered.");

                services.AddSingleton<IMessageBrokerService, NoOpMessageBrokerService>();
                return services;
            }

            try
            {
                var rabbitMqHost = Env.GetString("RABBITMQ_HOST") ?? "localhost";
                var rabbitMqUser = Env.GetString("RABBITMQ_USER") ?? "guest";
                var rabbitMqPassword = Env.GetString("RABBITMQ_PASSWORD") ?? "guest";
                var rabbitMqPort = Env.GetInt("RABBITMQ_PORT", 5672);

                // Use factory pattern for better error handling
                services.AddSingleton<IMessageBrokerService>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<RabbitMQService>>();
                    try
                    {
                        logger.LogInformation("Attempting to create RabbitMQ service");
                        return new RabbitMQService(rabbitMqHost, rabbitMqUser, rabbitMqPassword, rabbitMqPort, logger);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to initialize RabbitMQ service, falling back to no-op implementation");
                        return new NoOpMessageBrokerService(sp.GetRequiredService<ILogger<NoOpMessageBrokerService>>());
                    }
                });

                services.AddHostedService<EmailConsumerService>();
            }
            catch (Exception ex)
            {
                var logger = LoggerFactory.Create(builder => builder.AddConsole())
                    .CreateLogger("RabbitMQServiceExtensions");
                logger.LogError(ex, "Error setting up RabbitMQ services");

                services.AddSingleton<IMessageBrokerService, NoOpMessageBrokerService>();
            }

            return services;
        }
    }
}