using ChangeLogTracker.Core.Interfaces;
using ChangeLogTracker.Data;
using ChangeLogTracker.Timer;
using Discord;
using Discord.WebSocket;
using HtmlAgilityPack;
using Microsoft.Extensions.DependencyInjection;

namespace ChangeLogTracker
{
    public class ChangeLogChecker
    {
        /// <summary>How often it will attempt to query new change logs.</summary>
        public static TimeSpan TICK_INTERVAL_TIMESPAN = TimeSpan.FromMinutes(10);

        private IServiceProvider _Services;

        private BackgroundTask _Ticker;
        private bool _IsProcessing = false;

        public ChangeLogChecker(IServiceProvider services)
        {
            _Services = services;
        }

        public void Start()
        {
            _Ticker = new BackgroundTask(TICK_INTERVAL_TIMESPAN, OnTick, TICK_INTERVAL_TIMESPAN);
            _Ticker.Start();
        }

        public async Task TriggerNow()
        {
            await OnTick();
        }

        private async Task OnTick()
        {
            if (_IsProcessing) return;
            _IsProcessing = true;

            var changeLogs = await FetchChangelogs("https://www.dofus.com/fr/mmorpg/actualites/maj/correctifs");

            if (changeLogs != null && changeLogs.Count > 0)
            {
                var newChange = changeLogs.First();
                await NotifyChannels(newChange);
            }

            _IsProcessing = false;
        }

        private async Task AwaitPollingDelay()
        {
            var db = _Services.GetRequiredService<IDatabase>();
            var timestamp = await db.GetAsync<TimeStamp>($"PollingDelay");
            if (timestamp == null)
            {
                timestamp = new()
                {
                    Date = DateTime.UtcNow
                };
                await db.PutAsync($"PollingDelay", timestamp);
            }

            var dif = DateTime.UtcNow - timestamp.Date;
            if (dif.TotalSeconds < 1.8f && dif.TotalSeconds > 0)
            {
                await Task.Delay(dif);
            }

            timestamp.Date = DateTime.UtcNow;
            await db.PutAsync($"PollingDelay", timestamp);
        }

        private static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        private static string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }

        private async Task<List<ChangeLogData>> FetchChangelogs(string changeLogPage)
        {
            var db = _Services.GetRequiredService<IDatabase>();

            await AwaitPollingDelay();

            var web = new HtmlWeb();
            var doc = await web.LoadFromWebAsync(changeLogPage);

            await db.PutAsync<TimeStamp>($"LastUpdated", new TimeStamp() { Date = DateTime.UtcNow });

            List<ChangeLogData> changelogs = new();

            try
            {
                var changeNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'ak-item-elt-title')]").ToList();

                foreach (var changeNode in changeNodes)
                {
                    ChangeLogData changeLogData = new ChangeLogData();

                    var titles = new List<string>();

                    var title = changeNode.InnerText;
                    title = title.Replace("\n", "");

                    var titleSplit = title.Split('-');
                    foreach (var sTitle in titleSplit)
                    {
                        var newTitle = "";
                        int i = 0;
                        for (; i < sTitle.Length; i++)
                        {
                            if (sTitle[i] != ' ') break;
                        }
                        int spaceCount = 0;
                        for (; i < sTitle.Length; i++)
                        {
                            if (sTitle[i] == ' ')
                            {
                                spaceCount++;
                            }
                            else
                            {
                                spaceCount = 0;
                            }

                            if (spaceCount > 1)
                            {
                                break;
                            }

                            newTitle += sTitle[i];
                        }
                        titles.Add(newTitle);
                    }

                    for (int i = 0; i < titles.Count; i++)
                    {
                        string? t = titles[i];
                        changeLogData.Title += $"{t}";
                        if (i != titles.Count - 1) changeLogData.Title += "- ";
                    }

                    //var date = changeNode.InnerText;
                    //var dateSplit = date.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                    //foreach (var checkDate in dateSplit)
                    //{
                    //    if (DateOnly.TryParse(checkDate, out var changeDate))
                    //    {
                    //        TODO - This is broken when dockerised, always 01/01/0001
                    //        changeLogData.Date = changeDate;
                    //        break;
                    //    }
                    //}

                    var linkNode = changeNode.ChildNodes.FirstOrDefault(s => s.Name == "a");
                    if (linkNode != null && linkNode.Attributes.Contains("href"))
                    {
                        var linkValue = linkNode.Attributes["href"].Value;
                        changeLogData.URL = $"https://www.dofus.com{linkValue}";
                    }

                    changelogs.Add(changeLogData);

                }

                return changelogs;
            }
            catch (Exception ex)
            {

            }

            return null;
        }

        public async Task NotifyChannels(ChangeLogData changeData)
        {
            var client = _Services.GetRequiredService<DiscordSocketClient>();
            var db = _Services.GetRequiredService<IDatabase>();
            var logger = _Services.GetRequiredService<ILogger>();

            foreach (var guild in client.Guilds)
            {
                var hostedChanel = await db.GetAsync<HostedChannel>($"HostedChannel/{guild.Id}");
                if (hostedChanel == null)
                {
                    logger.Log($"No HostedChannel data for guild {guild.Name}");
                    continue;
                }

                var channel = await ((IGuild)guild).GetChannelAsync(hostedChanel.ChannelId);
                if (channel is not ITextChannel txtChannel)
                {
                    logger.Log($"Channel {channel.Name} for {guild.Name} not a TextChannel");
                    continue;
                }

                var lastChangeLog = await db.GetAsync<ChangeLogData>($"LastChangeLog/{guild.Id}/{channel.Id}");
                //If you have previously shown this change log, then ignore it.
                if (lastChangeLog != null && changeData.URL == lastChangeLog.URL)
                {
                    logger.Log($"{guild.Name} previously updated for {lastChangeLog.Title}");
                    continue;
                }

                var notifyRole = await db.GetAsync<NotifyRole>($"NotifyRole/{guild.Id}");

                var roleMention = notifyRole != null ? $"<@&{notifyRole.RoleId}>\n" : "";
                var content = $"{roleMention}# Change Log Posted - {changeData.Title}\n\n{changeData.URL}";
                try
                {
                    await txtChannel.SendMessageAsync(content);
                }
                catch (Exception ex)
                {

                }

                await db.PutAsync<ChangeLogData>($"LastChangeLog/{guild.Id}/{channel.Id}", changeData);
            }
        }
    }
}
