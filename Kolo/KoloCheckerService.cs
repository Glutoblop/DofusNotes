using ChangeLogTracker.Core.Interfaces;
using ChangeLogTracker.Data;
using ChangeLogTracker.Timer;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using DofusNotes.Data;
using HtmlAgilityPack;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace DofusNotes.PatchNotes
{
    public class KoloCheckerService
    {
        /// <summary>How often it will attempt to query new change logs.</summary>
#if DEBUG
        public static TimeSpan TICK_INTERVAL_TIMESPAN = TimeSpan.FromSeconds(5);
#else
        public static TimeSpan TICK_INTERVAL_TIMESPAN = TimeSpan.FromMinutes(30);
#endif

        private IServiceProvider _Services;

        private BackgroundTask _Ticker;
        private bool _IsProcessing = false;

        public KoloCheckerService(IServiceProvider services)
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

            List<List<KolossiumRanking>> playlistRankings = new();

            for (int i = 0; i < KolossiumRanking.KolossiumPlaylists.Length; i++)
            {
                string url = "https://www.dofus.com/en/mmorpg/community/rankings/kolossium?type={0}";
                url = string.Format(url, KolossiumRanking.KolossiumPlaylists[i]);
                List<KolossiumRanking> rankings = await GetKolossiumRankingsAsync(url);
                playlistRankings.Add(rankings);
            }

            await UpdateChannels(playlistRankings);

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
            return Convert.ToBase64String(plainTextBytes);
        }

        private static string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }

        public static async Task<List<KolossiumRanking>> GetKolossiumRankingsAsync(string url)
        {
            var rankings = new List<KolossiumRanking>();

            try
            {
                using (var httpClientHandler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                })
                using (var httpClient = new HttpClient(httpClientHandler))
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                                                                      "AppleWebKit/537.36 (KHTML, like Gecko) " +
                                                                      "Chrome/117.0.0.0 Safari/537.36");
                    httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml");
                    httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");

                    var html = await httpClient.GetStringAsync(url);
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var rows = doc.DocumentNode.SelectNodes("//tr[contains(@class, 'ak-first-ladder') or contains(@class, 'ak-bg-odd') or contains(@class, 'ak-bg-even')]");

                    if (rows == null)
                    {
                        return rankings;
                    }

                    foreach (var row in rows)
                    {
                        var cells = row.SelectNodes("td");
                        if (cells == null || cells.Count < 7)
                        {
                            continue;
                        }

                        var rankHtml = new HtmlDocument();
                        rankHtml.LoadHtml(cells[0].InnerHtml);

                        var rankNode = rankHtml.DocumentNode.SelectSingleNode("//span[contains(@class, 'ak-icon-position')]");
                        if (rankNode == null || !int.TryParse(rankNode.InnerText.Trim(), out int rank))
                        {
                            continue;
                        }

                        var levelCell = cells[4];
                        var levelText = levelCell.InnerText.Trim();
                        int level = int.TryParse(levelText, out int parsedLevel) ? parsedLevel : 0;

                        // Re-parse the cell to reliably detect nested tags
                        var levelHtml = new HtmlDocument();
                        levelHtml.LoadHtml(levelCell.InnerHtml);

                        // Now correctly checking for <span> instead of <div>
                        bool isOmega = levelHtml.DocumentNode.SelectSingleNode("//span[contains(@class, 'ak-omega-level')]") != null;
                        if (isOmega)
                        {
                            level += 200;
                        }

                        var ranking = new KolossiumRanking
                        {
                            Rank = int.Parse(cells[0].InnerText.Trim()),
                            Name = cells[1].InnerText.Trim(),
                            Class = cells[2].InnerText.Trim(),
                            Server = cells[3].InnerText.Trim(),
                            Level = level,
                            Rating = int.Parse(cells[5].InnerText.Trim()),
                            Winrate = cells[6].InnerText.Trim()
                        };
                        rankings.Add(ranking);
                    }

                    return rankings;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Parsing Error for Kolo {url}: {e.Message}");
            }

            return rankings;
        }

        public async Task UpdateChannels(List<List<KolossiumRanking>> koloData)
        {
            var client = _Services.GetRequiredService<DiscordSocketClient>();
            var db = _Services.GetRequiredService<IDatabase>();
            var logger = _Services.GetRequiredService<ILogger>();

            foreach (var guild in client.Guilds)
            {
                if (guild == null)
                {
                    logger.Log($"Guild is null.. for some reason?");
                    continue;
                }

                ITextChannel textChannel = null;
                try
                {
                    var hostedChanel = await db.GetAsync<HostedChannel>($"KoloHostedChannel/{guild.Id}");
                    if (hostedChanel == null)
                    {
                        logger.Log($"No KoloHostedChannel data for guild {guild.Name}");
                        continue;
                    }

                    var channel = await ((IGuild)guild).GetChannelAsync(hostedChanel.ChannelId);

                    if (channel == null)
                    {
                        await db.DeleteAsync($"KoloHostedChannel/{guild.Id}");
                        logger.Log($"Channel was null, removed KoloHostedChannel from data.");
                        continue;
                    }

                    if (channel is not ITextChannel)
                    {
                        logger.Log($"Channel {channel?.Name ?? "[null]"} for {guild.Name} not a TextChannel");
                        continue;
                    }
                    textChannel = (ITextChannel)channel;

                    // DELETE ALL OLD MESSAGES (Update maybe?)
                    var msgCount = 0;
                    do
                    {
                        msgCount = 0;
                        var eventMessages = await textChannel.GetMessagesAsync(100).FlattenAsync();
                        foreach (var eventMessage in eventMessages)
                        {
                            if (eventMessage.Author.Id != client.CurrentUser.Id)
                            {
                                continue;
                            }

                            await textChannel.DeleteMessageAsync(eventMessage);
                            msgCount++;
                        }
                    } while (msgCount > 0);

                    List<EmbedBuilder> rankingEmbeds = new();

                    for (int i = 0; i < koloData.Count; i++)
                    {
                        var embedBuilder = new EmbedBuilder
                        {
                            Title = $"{KolossiumRanking.KolossiumPlaylists[i]} Leader Board",
                        };

                        var content = "";

                        for (int j = 0; j < 10; j++)
                        {
                            string playerInfo = "* ";
                            if (j == 0) playerInfo += "🥇 ";
                            else if (j == 1) playerInfo += "🥈 ";
                            else if (j == 2) playerInfo += "🥉 ";
                            playerInfo += $"**{koloData[i][j].Name}** | {koloData[i][j].Class} | {koloData[i][j].Rating} | {koloData[i][j].Winrate}";

                            if (j != 0) content += "\n";
                            content += playerInfo;
                        }

                        embedBuilder.Description = content;
                        rankingEmbeds.Add(embedBuilder);
                    }
                                        
                    await textChannel.SendMessageAsync($"# Kolossium Leaderboards {DateTime.UtcNow:yyyy/MM/dd}",
                        embeds: rankingEmbeds.Select(s => s.Build()).ToArray());
                }
                catch (Exception ex)
                {
                    logger.Log($"{ex}");
                }
            }
        }
    }
}
