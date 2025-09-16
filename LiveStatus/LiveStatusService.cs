using ChangeLogTracker.Core.Interfaces;
using ChangeLogTracker.Data;
using ChangeLogTracker.Timer;
using Discord;
using Discord.WebSocket;
using DofusNotes.Data;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace DofusNotes.LiveStatus
{
    public class LiveStatusService
    {
        private IServiceProvider _Services;
        private IDatabase _Database;
        private DiscordSocketClient _Client;

        public TimeSpan TICK_INTERVAL_TIMESPAN = TimeSpan.FromSeconds(30);

        private TimeSpan NormalTick = TimeSpan.FromMinutes(1);
        private TimeSpan TuesdayTick = TimeSpan.FromSeconds(10);

        private DateTime LastChecked;

        private BackgroundTask _Ticker;
        private bool _IsProcessing = false;

        private bool _RestartedForTuesday = false;

        private bool _ForceUpdateNextTick = false;

        public LiveStatusService(IServiceProvider services)
        {
            _Services = services;
            _Database = _Services.GetRequiredService<IDatabase>();
            _Client = _Services.GetRequiredService<DiscordSocketClient>();
            LastChecked = DateTime.UtcNow;
        }

        public void Start()
        {
            _Ticker = new BackgroundTask(TICK_INTERVAL_TIMESPAN, OnTick, TICK_INTERVAL_TIMESPAN);
            _Ticker.Start();
        }

        public async Task TriggerNow()
        {
            _ForceUpdateNextTick = true;
            await OnTick();
        }

        private async Task OnTick()
        {
            var dif = DateTime.UtcNow - LastChecked;
            //Dont trigger on Tuesday if its been shorter than tuesday tick time
            if (DateTime.UtcNow.DayOfWeek == DayOfWeek.Tuesday && dif < TuesdayTick)
            {
                return;
            }
            //Dont trigger on other days if its been shorter than normal tick time
            if (DateTime.UtcNow.DayOfWeek != DayOfWeek.Tuesday && dif < NormalTick)
            {
                return;
            }

            if (_IsProcessing) return;
            _IsProcessing = true;

            Console.WriteLine($"Checking ..");

            LastChecked = DateTime.UtcNow;

            Dictionary<string, bool> previousSnapshot = await _Database.GetAsync<Dictionary<string, bool>>("LiveStatusSnapShot");
            Dictionary<string, bool> currentSnapshot = await FetchStatuses();
            if (previousSnapshot == null) previousSnapshot = currentSnapshot;
            await _Database.PutAsync("LiveStatusSnapShot", currentSnapshot);

            Dictionary<string, bool> changedStatus = new();
            foreach (var snapShot in currentSnapshot)
            {
                if (previousSnapshot.ContainsKey(snapShot.Key))
                {
                    bool force = _ForceUpdateNextTick;
#if DEBUG
                    //force = true;
#endif
                    //Has changed from previous check and the service is now up!
                    if (previousSnapshot[snapShot.Key] != snapShot.Value)
                    {
                        changedStatus.TryAdd(snapShot.Key, snapShot.Value);
                    }
                    else if (force)
                    {
                        changedStatus.TryAdd(snapShot.Key, snapShot.Value);
                    }
                }
            }

            await TriggerStatusChange(changedStatus);

            _IsProcessing = false;
        }

        public class ServiceStatus
        {
            public List<string> Tags { get; set; }
            public Names Names { get; set; }
            public Status Status { get; set; }
        }

        public class Names
        {
            public string En { get; set; }
        }

        public enum Status { Maintenance, Up };

        private async Task<Dictionary<string, bool>> FetchStatuses()
        {
            await AnkamaPoll.AwaitTimestampDelay(_Services);
            var dofusStatus = new Dictionary<string, bool>();

            try
            {
                const string url = "https://status.cdn.ankama.com/export.json";
                var json = await MyHttpClient.Simple.GetStringAsync(url);

                List<ServiceStatus> statuses = JsonConvert.DeserializeObject<List<ServiceStatus>>(json) ?? new List<ServiceStatus>();

                foreach (var status in statuses)
                {
                    if (!status.Tags.Contains("game-server")) continue;
                    if (status.Tags.Any(s => s.Equals("dofus2", StringComparison.InvariantCultureIgnoreCase) || s.Equals("dofus3", StringComparison.InvariantCultureIgnoreCase)))
                    {
                        dofusStatus.TryAdd(status.Names.En, status.Status == Status.Up);
                    }
                }
            }
            catch (Exception ex)
            {
            }

            return dofusStatus;
        }


        private async Task TriggerStatusChange(Dictionary<string, bool> statusChanged)
        {
            foreach (var guild in _Client.Guilds)
            {
                var iGuild = guild as IGuild;
                if (iGuild == null) continue;
                var config = await _Database.GetAsync<LiveStatusData>($"LiveStatusConfig/{guild.Id}");
                if (config == null) continue;
                var status = statusChanged.Where(s => config.DofusUpMentionRole.ContainsKey(s.Key)).ToList();
                if (!status.Any()) continue;

                var channel = await iGuild.GetChannelAsync(config.ChannelId) as ITextChannel;
                if (channel == null) continue;

                var content = $"";
                for (int i = 0; i < status.Count; i++)
                {
                    if (status[i].Value)
                    {
                        content += $"* <@&{config.DofusUpMentionRole[status[i].Key]}> **{status[i]}** is Online 🟢\n";
                    }
                    else
                    {
                        content += $"* **{status[i]}** is Offline 🔴\n";
                    }
                }
                try
                {
                    await channel.SendMessageAsync($"# Dofus Servers Are Up ⬆️\n{content}");
                }
                catch (Exception ex)
                {
                }
            }
        }
    }
}
