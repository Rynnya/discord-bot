using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordReborn.Structures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Lavalink4NET.Extensions;
using Lavalink4NET.InactivityTracking.Trackers.Users;
using Lavalink4NET.InactivityTracking.Extensions;
using Lavalink4NET;
using Lavalink4NET.InactivityTracking;
using Lavalink4NET.Integrations.SponsorBlock.Extensions;

namespace DiscordReborn
{
    class Program
    {
        private readonly IServiceProvider _serviceProvider = null!;

        private readonly DiscordSocketClient _client = null!;
        private readonly InteractionService _interactionService = null!;

        public Program()
        {
            _serviceProvider = new ServiceCollection()
                .AddLogging(builder => builder.AddConsole())
                .AddSingleton(JsonConvert.DeserializeObject<Config>(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "config.json"))))
                .AddSingleton(new DiscordSocketConfig()
                {
                    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildVoiceStates
                })
                .AddSingleton<DiscordSocketClient>()
                .AddSingleton(serviceProvider => new InteractionService(serviceProvider.GetRequiredService<DiscordSocketClient>()))
                .AddInactivityTracking()
                .ConfigureInactivityTracking(config =>
                {
                    config.UseDefaultTrackers = false;
                })
                .Configure<UsersInactivityTrackerOptions>(options =>
                {
                    options.Threshold = 1;
                    options.Timeout = TimeSpan.FromMinutes(1);
                    options.ExcludeBots = true;
                })
                .AddInactivityTracker<UsersInactivityTracker>()
                .ConfigureLavalink(audioConfig =>
                {
                    var config = _serviceProvider.GetRequiredService<Config>();
                    audioConfig.BaseAddress = new Uri($"http://{config.LavalinkBaseURL}:{config.LavalinkPort}");
                    audioConfig.WebSocketUri = new Uri($"ws://{config.LavalinkBaseURL}:{config.LavalinkPort}/v4/websocket");
                    audioConfig.Passphrase = "youshallnotpass";
                })
                .AddLavalink()
                .BuildServiceProvider();

            _client = _serviceProvider.GetRequiredService<DiscordSocketClient>();
            _interactionService = _serviceProvider.GetRequiredService<InteractionService>();
        }

        public static Task Main() => new Program().RunAsync();

        private async Task RunAsync()
        {
            var config = _serviceProvider.GetRequiredService<Config>();

            _client.InteractionCreated += HandleInteration;

            _client.Log += Log;
            _client.Ready += Ready;

            await _client.LoginAsync(TokenType.Bot, config.Token);
            await _client.StartAsync();

            await Task.Delay(Timeout.Infinite);
        }

        public async Task HandleInteration(SocketInteraction socketInteraction)
            => await _interactionService.ExecuteCommandAsync(new SocketInteractionContext(_client, socketInteraction), _serviceProvider);

        public Task Log(LogMessage message)
        {
            Console.WriteLine(message.ToString());
            return Task.CompletedTask;
        }

        public async Task Ready()
        {
            await _interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), _serviceProvider);
            await _interactionService.RegisterCommandsGloballyAsync();

            await _serviceProvider.GetRequiredService<IAudioService>().UseSponsorBlock().StartAsync();
            await _serviceProvider.GetRequiredService<IInactivityTrackingService>().StartAsync();

            _client.Ready -= Ready;
        }
    }
}
