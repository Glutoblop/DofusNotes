using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using ChangeLogTracker;
using ChangeLogTracker.Core;
using ChangeLogTracker.Core.Interfaces;
using ChangeLogTracker.Core.Logging;
using ChangeLogTracker.Database;
using RunMode = Discord.Interactions.RunMode;

namespace ChangeLogTracker
{
    public class Program
    {
        private static bool clientReady = false;

        public static Task Main() => new Program().MainAsync();

        public async Task MainAsync()
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Utc };

            try
            {
                var config = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("config.json")
                    .Build();

                using IHost host = Host.CreateDefaultBuilder()
                    .ConfigureServices((_, services) => services
                        .AddSingleton(config)
                        .AddSingleton(x => new DiscordSocketClient(new DiscordSocketConfig
                        {
                            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.GuildMembers,
                            AlwaysDownloadUsers = true
                        }))
                        .AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>(),
                            new InteractionServiceConfig() { DefaultRunMode = RunMode.Async }))
                        .AddSingleton<InteractionHandler>()
                        .AddSingleton(x => new CommandService(new CommandServiceConfig()
                        {
                            DefaultRunMode = Discord.Commands.RunMode.Async
                        }))
                        .AddSingleton<ILogger>(s => new ConsoleLogger(ConstantData.LogType))
                        .AddSingleton<IDatabase>(s => new CachedDatabase())
                        .AddSingleton(s => new ChangeLogChecker(s))
                    ).Build();

                await RunAsync(host);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Big crash: {e}");
            }
        }

        public async Task RunAsync(IHost host)
        {
            using IServiceScope serviceScope = host.Services.CreateScope();
            IServiceProvider services = serviceScope.ServiceProvider;

            var config = services.GetRequiredService<IConfigurationRoot>();

            var db = services.GetRequiredService<IDatabase>();
            await db.Init("ChangeLogCache");

            var client = services.GetRequiredService<DiscordSocketClient>();
            var sCommands = services.GetRequiredService<InteractionService>();
            await services.GetRequiredService<InteractionHandler>().InitialiseAsync();

            services.GetRequiredService<ChangeLogChecker>().Start();

            client.Log += async (msg) => { Console.WriteLine($"[{DateTime.Now:t}] Log: {msg}"); };
            sCommands.Log += async (msg) => { Console.WriteLine($"[{DateTime.Now:t}] Interaction: {msg}"); };

            client.Ready += async () =>
            {
                Console.WriteLine($"Bot is ready.");
                if (clientReady) return;

                foreach (var guild in client.Guilds)
                {
                    await sCommands.RegisterCommandsToGuildAsync(guild.Id);
                    if (clientReady) continue;
                }

                clientReady = true;
            };

            client.Connected += () =>
            {
                return Task.CompletedTask;
            };

            client.Disconnected += exception =>
            {
                return Task.CompletedTask;
            };

            client.GuildAvailable += async guild =>
            {
                Console.WriteLine($"Guild available");
                if (!clientReady) return;
                await sCommands.RegisterCommandsToGuildAsync(guild.Id);
            };

            await client.LoginAsync(TokenType.Bot, config["discordtoken"]);

            await client.StartAsync();

            await Task.Delay(-1);
        }
    }
}
