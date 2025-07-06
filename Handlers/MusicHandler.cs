using Discord.Interactions;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.DiscordNet;
using Lavalink4NET.Events.Players;
using Lavalink4NET.Filters;
using Lavalink4NET.Integrations.SponsorBlock;
using Lavalink4NET.Integrations.SponsorBlock.Extensions;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscordReborn.Handlers
{
    public enum InsertionAction
    {
        [ChoiceDisplay("Добавить в конец очереди")]
        AddToEndOfQueue = 0,

        [ChoiceDisplay("Добавить в начало очереди")]
        AddToStartOfQueue = 1,

        [ChoiceDisplay("Заменить текущий + добавить в конец очереди")]
        PlayThenAddToEnd = 2,

        [ChoiceDisplay("Заменить текущий + добавить в начало очереди")]
        PlayThenAddToStart = 3
    }

    public enum TrackRepeat
    {
        [ChoiceDisplay("Выключить повтор")]
        None = 0,

        [ChoiceDisplay("Включить повтор одного трека")]
        Track = 1,

        [ChoiceDisplay("Включить повтор всей очереди")]
        Queue = 2
    }

    public enum EqualizerSettings
    {
        [ChoiceDisplay("Стандартный")]
        None = 0,

        [ChoiceDisplay("Небольшой бас")]
        LowBass = 1,

        [ChoiceDisplay("Средний бас")]
        MediumBass = 2,

        [ChoiceDisplay("Сильный бас")]
        HighBass = 3,

        [ChoiceDisplay("Разрыв ушей")]
        Earrape = 4,
    }

    internal class ExtendedLavalinkPlayer(IPlayerProperties<QueuedLavalinkPlayer, QueuedLavalinkPlayerOptions> properties) : QueuedLavalinkPlayer(properties)
    {
        public static PlayerFactory<ExtendedLavalinkPlayer, QueuedLavalinkPlayerOptions> Factory { get; } = PlayerFactory.Create<ExtendedLavalinkPlayer, QueuedLavalinkPlayerOptions>(static properties => new ExtendedLavalinkPlayer(properties));

        public EqualizerSettings CurrentEqualizerSettings { get; set; }
    }

    [RequireContext(ContextType.Guild)]
    public class MusicHandler : InteractionModuleBase<SocketInteractionContext>
    {
        private const int MinVolumeValue = 1;
        private const int MaxVolumeValue = 150;
        private const float VolumeScale = 100.0f;
        private const float DefaultVolume = 15 / VolumeScale;
        private const uint MaxAmountOfCharsInQueue = 1800;

        private readonly EqualizerFilterOptions DefaultEqualizerOptions = new(new Equalizer.Builder()
        {
            Band0 = 0.25f,
            Band1 = 0.025f,
            Band2 = 0.0125f,
            Band3 = 0.0f,
            Band4 = 0.0f,
            Band5 = -0.0125f,
            Band6 = -0.025f,
            Band7 = -0.0175f,
            Band8 = 0.0f,
            Band9 = 0.0f,
            Band10 = 0.0125f,
            Band11 = 0.025f,
            Band12 = 0.25f,
            Band13 = 0.125f,
            Band14 = 0.125f,
        }.Build());

        private readonly EqualizerFilterOptions LowBassEqualizerOptions = new(new Equalizer.Builder()
        {
            Band0 = 0.0625f,
            Band1 = 0.125f,
            Band2 = -0.125f,
            Band3 = -0.0625f,
            Band4 = 0.0f,
            Band5 = -0.0125f,
            Band6 = -0.025f,
            Band7 = -0.0175f,
            Band8 = 0.0f,
            Band9 = 0.0f,
            Band10 = 0.0125f,
            Band11 = 0.025f,
            Band12 = 0.375f,
            Band13 = 0.125f,
            Band14 = 0.125f,
        }.Build());

        private readonly EqualizerFilterOptions MediumBassEqualizerOptions = new(new Equalizer.Builder()
        {
            Band0 = 0.125f,
            Band1 = 0.25f,
            Band2 = -0.25f,
            Band3 = -0.125f,
            Band4 = 0.0f,
            Band5 = -0.0125f,
            Band6 = -0.025f,
            Band7 = -0.0175f,
            Band8 = 0.0f,
            Band9 = 0.0f,
            Band10 = 0.0125f,
            Band11 = 0.025f,
            Band12 = 0.375f,
            Band13 = 0.125f,
            Band14 = 0.125f,
        }.Build());

        private readonly EqualizerFilterOptions HighBassEqualizerOptions = new(new Equalizer.Builder()
        {
            Band0 = 0.1875f,
            Band1 = 0.375f,
            Band2 = -0.375f,
            Band3 = -0.1875f,
            Band4 = 0.0f,
            Band5 = -0.0125f,
            Band6 = -0.025f,
            Band7 = -0.0175f,
            Band8 = 0.0f,
            Band9 = 0.0f,
            Band10 = 0.0125f,
            Band11 = 0.025f,
            Band12 = 0.375f,
            Band13 = 0.125f,
            Band14 = 0.125f,
        }.Build());

        private readonly EqualizerFilterOptions EarrapeEqualizerOptions = new(new Equalizer.Builder()
        {
            Band0 = 0.25f,
            Band1 = 0.5f,
            Band2 = -0.5f,
            Band3 = -0.25f,
            Band4 = 0.0f,
            Band5 = -0.0125f,
            Band6 = -0.025f,
            Band7 = -0.0175f,
            Band8 = 0.0f,
            Band9 = 0.0f,
            Band10 = 0.0125f,
            Band11 = 0.025f,
            Band12 = 0.375f,
            Band13 = 0.125f,
            Band14 = 0.125f,
        }.Build());

        private readonly IAudioService _audioService = null!;

        private readonly IPlayerManager _playerManager = null!;

        public MusicHandler(IAudioService audioService, IPlayerManager playerManager)
        {
            _audioService = audioService;
            _playerManager = playerManager;

            _playerManager.PlayerCreated += OnNewPlayerCreated;
        }

        public async Task OnNewPlayerCreated(object sender, PlayerCreatedEventArgs args)
        {
            args.Player.Filters.Equalizer = DefaultEqualizerOptions;
            await args.Player.Filters.CommitAsync();

            await args.Player.UpdateSponsorBlockCategoriesAsync([SegmentCategory.Outro, SegmentCategory.Sponsor, SegmentCategory.SelfPromotion, SegmentCategory.OfftopicMusic]);
        }

#nullable enable

        private async ValueTask<ExtendedLavalinkPlayer?> GetPlayerAsync(bool connectToVoiceChannel = false)
        {
            PlayerRetrieveOptions retrieveOptions = new()
            {
                ChannelBehavior = connectToVoiceChannel ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None,
                VoiceStateBehavior = connectToVoiceChannel ? MemberVoiceStateBehavior.RequireSame : MemberVoiceStateBehavior.Ignore
            };

            QueuedLavalinkPlayerOptions playerOptions = new()
            {
                InitialVolume = DefaultVolume,
                SelfDeaf = false,
                SelfMute = true
            };

            var result = await _audioService.Players
                .RetrieveAsync(Context, ExtendedLavalinkPlayer.Factory, playerOptions, retrieveOptions)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                if (!connectToVoiceChannel)
                {
                    await RespondAsync("Плеер не включен.");
                    return null;
                }

                var errorMessage = result.Status switch
                {
                    PlayerRetrieveStatus.UserNotInVoiceChannel => "Вы должны находиться в голосовом канале.",
                    PlayerRetrieveStatus.VoiceChannelMismatch => "Вы должны находиться в одном головосом канале с ботом.",
                    PlayerRetrieveStatus.BotNotConnected => "Не удалось подключиться к каналу :с",
                    _ => "Неизвестная ошибка :с",
                };

                await RespondAsync(errorMessage).ConfigureAwait(false);
                return null;
            }

            return result.Player;
        }

#nullable restore

        [SlashCommand("play", "Воспроизводит музыку, заданную по названию или URL", runMode: RunMode.Async)]
        public async Task HandlePlay(string query, InsertionAction insertionAction = InsertionAction.AddToEndOfQueue)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                await RespondAsync("Пожалуйста, задайте ключевые слова для поиска.");
                return;
            }

            var player = await GetPlayerAsync(true);

            if (player is null)
            {
                return;
            }

            await DeferAsync();

            TrackLoadOptions loadOptions = new()
            {
                SearchMode = TrackSearchMode.YouTube,
                SearchBehavior = StrictSearchBehavior.Passthrough
            };

            var searchResponse = await _audioService.Tracks.LoadTracksAsync(query, loadOptions);

            if (searchResponse.IsFailed || !searchResponse.HasMatches)
            {
                await FollowupAsync($"Не удалось найти что-либо по запросу `{query}`.");
                return;
            }

            if (!searchResponse.IsPlaylist)
            {
                if (player.State is PlayerState.Playing or PlayerState.Paused)
                {
                    switch (insertionAction)
                    {
                        case InsertionAction.AddToEndOfQueue:
                            await AddTrackToEnd(player, searchResponse.Track);
                            break;
                        case InsertionAction.AddToStartOfQueue:
                            await AddTrackToStart(player, searchResponse.Track);
                            break;
                        case InsertionAction.PlayThenAddToEnd:
                        case InsertionAction.PlayThenAddToStart:
                            await PlayTrack(player, searchResponse.Track);
                            break;
                    }
                }
                else
                {
                    // InsertionAction doesn't matter here because we don't have any tracks in queue or playing

                    await PlayTrack(player, searchResponse.Track);
                }
            }
            else
            {
                if (player.State is PlayerState.Playing or PlayerState.Paused)
                {
                    switch (insertionAction)
                    {
                        case InsertionAction.AddToEndOfQueue:
                            await AddTracksToEnd(player, searchResponse.Tracks);
                            break;
                        case InsertionAction.AddToStartOfQueue:
                            await AddTracksToStart(player, searchResponse.Tracks);
                            break;
                        case InsertionAction.PlayThenAddToEnd:
                            await PlayThenAddTracksToEnd(player, searchResponse.Tracks);
                            break;
                        case InsertionAction.PlayThenAddToStart:
                            await PlayThenAddTracksToStart(player, searchResponse.Tracks);
                            break;
                    }
                }
                else
                {
                    switch (insertionAction)
                    {
                        case InsertionAction.AddToEndOfQueue:
                        case InsertionAction.PlayThenAddToEnd:
                            await PlayThenAddTracksToEnd(player, searchResponse.Tracks);
                            break;
                        case InsertionAction.AddToStartOfQueue:
                        case InsertionAction.PlayThenAddToStart:
                            await PlayThenAddTracksToStart(player, searchResponse.Tracks);
                            break;
                    }
                }
            }
        }

        [SlashCommand("resume", "Возобновляет текущий трек")]
        public async Task HandleResume()
        {
            var player = await GetPlayerAsync();

            if (player is null)
            {
                return;
            }

            if (!player.IsPaused)
            {
                await RespondAsync("Нечего возобновлять, плеер остановлен.");
                return;
            }

            await player.ResumeAsync();
            await RespondAsync("\U0001F44C");
        }

        [SlashCommand("pause", "Приостанавливает текущий трек")]
        public async Task HandlePause()
        {
            var player = await GetPlayerAsync();

            if (player is null)
            {
                return;
            }

            if (player.State is not PlayerState.Playing)
            {
                await RespondAsync("Нечего возобновлять, плеер остановлен.");
                return;
            }

            await player.PauseAsync();
            await RespondAsync("\U0001F44C");
        }

        [SlashCommand("skip", "Пропускает текущий трек")]
        public async Task HandleSkip()
        {
            var player = await GetPlayerAsync();

            if (player is null)
            {
                return;
            }

            var skippedTrack = player.CurrentTrack;

            if (skippedTrack is null)
            {
                await RespondAsync($"Очередь пуста, плеер остановлен, нечего пропускать.");
                return;
            }

            var nextTrack = await player.Queue.TryDequeueAsync();

            if (nextTrack is not null)
            {
                await player.PlayAsync(nextTrack, false);
                await RespondAsync($"Пропущен трек: `{skippedTrack.Title}`\nТеперь играет: `{nextTrack.Track.Title}`");

                return;
            }

            await player.StopAsync();
            await RespondAsync($"Пропущен трек: `{skippedTrack.Title}`\nОчередь пуста.");
        }

        [SlashCommand("volume", "Выводит текущую громкость или устанавливает новую громкость (от 1 до 150)")]
        public async Task HandleVolume(int volume = 0)
        {
            var player = await GetPlayerAsync();

            if (player is null)
            {
                return;
            }

            if (volume == 0)
            {
                await RespondAsync($"Текущая громкость плеера: `{player.Volume * VolumeScale:0}%`");
            }
            else
            {
                volume = Math.Clamp(volume, MinVolumeValue, MaxVolumeValue);

                await player.SetVolumeAsync(volume / VolumeScale);
                await RespondAsync($"Громкость изменена на `{player.Volume * VolumeScale:0}%`");
            }
        }

        [SlashCommand("stop", "Останавливает музыкального бота")]
        public async Task HandleStop()
        {
            var player = await GetPlayerAsync();

            if (player is null)
            {
                return;
            }

            await player.Queue.ClearAsync();
            await player.StopAsync();
            await RespondAsync("\U0001F44C");
        }

        [SlashCommand("quit", "Останавливает музыкального бота и отключает его от голосового канала")]
        public async Task HandleQuit()
        {
            var player = await GetPlayerAsync();

            if (player is null)
            {
                return;
            }

            await DeferAsync();

            await player.Queue.ClearAsync();

            if (player.State is PlayerState.Playing or PlayerState.Paused)
            {
                await player.StopAsync();
            }

            await player.DisconnectAsync();
            await FollowupAsync("Музыкальный бот остановлен и выключен.");
        }

        [SlashCommand("clear", "Очищает очередь")]
        public async Task HandleClearQueue()
        {
            var player = await GetPlayerAsync();

            if (player is null)
            {
                return;
            }

            await DeferAsync();

            await player.Queue.ClearAsync();
            await FollowupAsync("Очередь очищена.");
        }

        [SlashCommand("queue", "Выводит всю текущую очередь")]
        public async Task HandleQueue()
        {
            var player = await GetPlayerAsync();

            if (player is null)
            {
                return;
            }

            await DeferAsync();

            var response = new StringBuilder();
            var queueList = new StringBuilder();
            var lastEntry = player.Queue.Count;

            for (int i = 0; i < player.Queue.Count; i++)
            {
                var currentLength = queueList.Length;
                var newEntry = $"{i + 1,-2} | {player.Queue.ElementAt(i).Track.Title}\n";

                if (currentLength + newEntry.Length >= MaxAmountOfCharsInQueue)
                {
                    lastEntry = i;
                    break;
                }

                queueList.Append(newEntry);
            }

            if (queueList.Length > 0)
            {
                response.Append("Текущая очередь:\n```");
                response.Append(queueList);

                if (lastEntry != player.Queue.Count)
                {
                    response.Append($"\nТреков не отображено: {player.Queue.Count - lastEntry}");
                }

                response.Append("```\n");
            }
            else
            {
                response.Append("Очередь пуста.\n");
            }

            response.Append($"Текущий трек: `{player.CurrentTrack?.Title ?? "Ничего не играет"}`");
            await FollowupAsync(response.ToString());
        }

        [SlashCommand("repeat", "Повторяет текущий трек (включается и выключается одной и той же командой)")]
        public async Task HandleRepeat(TrackRepeat repeatMode)
        {
            var player = await GetPlayerAsync();

            if (player is null)
            {
                return;
            }

            player.RepeatMode = repeatMode switch
            {
                TrackRepeat.Track => TrackRepeatMode.Track,
                TrackRepeat.Queue => TrackRepeatMode.Queue,
                _ => TrackRepeatMode.None
            };

            await RespondAsync($"Повтор был {player.RepeatMode switch
            {
                TrackRepeatMode.Track => "включен для одного трека",
                TrackRepeatMode.Queue => "включен для всей очереди",
                _ => "выключен"
            }}.");
        }

        [SlashCommand("shuffle", "Перемешивает все треки внутри текущей очереди")]
        public async Task HandleShuffle()
        {
            var player = await GetPlayerAsync();

            if (player is null)
            {
                return;
            }

            await DeferAsync();

            await player.Queue.ShuffleAsync();
            await FollowupAsync("\U0001F44C");
        }

        [SlashCommand("equalizer", "Выдает текущие настройки эквалайзера или выставляет новые")]
        public async Task HandleEqualizer(EqualizerSettings? equalizerSettings = null)
        {
            var player = await GetPlayerAsync();

            if (player is null)
            {
                return;
            }

            if (equalizerSettings is EqualizerSettings newEqualizerSettings)
            {
                await DeferAsync();

                player.CurrentEqualizerSettings = newEqualizerSettings;
                player.Filters.Equalizer = newEqualizerSettings switch
                {
                    EqualizerSettings.LowBass => LowBassEqualizerOptions,
                    EqualizerSettings.MediumBass => MediumBassEqualizerOptions,
                    EqualizerSettings.HighBass => HighBassEqualizerOptions,
                    EqualizerSettings.Earrape => EarrapeEqualizerOptions,
                    _ => DefaultEqualizerOptions,
                };

                await player.Filters.CommitAsync();
                await FollowupAsync($"Режим эквалайзера был сменен на `{player.CurrentEqualizerSettings switch
                {
                    EqualizerSettings.LowBass => "Небольшой бас",
                    EqualizerSettings.MediumBass => "Средний бас",
                    EqualizerSettings.HighBass => "Сильный бас",
                    EqualizerSettings.Earrape => "Разрыв ушей",
                    _ => "Стандартный",
                }}`.");

                return;
            }

            await RespondAsync($"Текущий режим эквалайзера: `{player.CurrentEqualizerSettings switch
            {
                EqualizerSettings.LowBass => "Небольшой бас",
                EqualizerSettings.MediumBass => "Средний бас",
                EqualizerSettings.HighBass => "Сильный бас",
                EqualizerSettings.Earrape => "Разрыв ушей",
                _ => "Стандартный",
            }}`.");
        }

        private async Task PlayTrack(QueuedLavalinkPlayer player, LavalinkTrack track)
        {
            await player.PlayAsync(track, false);

            await FollowupAsync($"Сейчас играет: `{track.Title}`");
        }

        private async Task AddTrackToStart(QueuedLavalinkPlayer player, LavalinkTrack track)
        {
            await player.Queue.InsertAsync(0, new TrackQueueItem(track));

            await FollowupAsync($"Добавлено в начало очереди: `{track.Title}`");
        }

        private async Task AddTrackToEnd(QueuedLavalinkPlayer player, LavalinkTrack track)
        {
            await player.Queue.AddAsync(new TrackQueueItem(track));

            await FollowupAsync($"Добавлено в конец очереди: `{track.Title}`");
        }

        private async Task AddTracksToStart(QueuedLavalinkPlayer player, ImmutableArray<LavalinkTrack> tracks)
        {
            await player.Queue.InsertRangeAsync(0, ImmutableArray.CreateRange(tracks, track => new TrackQueueItem(track)));

            await FollowupAsync($"Добавлен плейлист в начало очереди с количество треков: {tracks.Length}");
        }

        private async Task AddTracksToEnd(QueuedLavalinkPlayer player, ImmutableArray<LavalinkTrack> tracks)
        {
            await player.Queue.AddRangeAsync(ImmutableArray.CreateRange(tracks, track => new TrackQueueItem(track)));

            await FollowupAsync($"Добавлен плейлист в конец очереди с количество треков: {tracks.Length}");
        }

        private async Task PlayThenAddTracksToEnd(QueuedLavalinkPlayer player, ImmutableArray<LavalinkTrack> tracks)
        {
            await player.PlayAsync(tracks.First(), false);
            await player.Queue.AddRangeAsync(ImmutableArray.CreateRange(tracks.Skip(1).ToImmutableArray(), track => new TrackQueueItem(track)));

            await FollowupAsync($"Добавлен плейлист в конец очереди с количество треков: {tracks.Length}\nСейчас играет: `{tracks.First().Title}`");
        }

        private async Task PlayThenAddTracksToStart(QueuedLavalinkPlayer player, ImmutableArray<LavalinkTrack> tracks)
        {
            await player.PlayAsync(tracks.First(), false);
            await player.Queue.InsertRangeAsync(0, ImmutableArray.CreateRange(tracks.Skip(1).ToImmutableArray(), track => new TrackQueueItem(track)));

            await FollowupAsync($"Добавлен плейлист в начало очереди с количество треков: {tracks.Length}\nСейчас играет: `{tracks.First().Title}`");
        }
    }
}
