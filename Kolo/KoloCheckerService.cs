using ChangeLogTracker.Core.Interfaces;
using ChangeLogTracker.Data;
using ChangeLogTracker.Timer;
using Discord;
using Discord.WebSocket;
using DofusNotes.Data;
using HtmlAgilityPack;
using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using ScottPlot;
using System.Net;

namespace DofusNotes.PatchNotes
{
    public class KoloCheckerService
    {
        private static string[] CLASS_NAMES = new string[]
       {
            "Feca",
            "Osamodas",
            "Enutrof",
            "Sram",
            "Xelor",
            "Ecaflip",
            "Eniripsa",
            "Iop",
            "Cra",
            "Sadida",
            "Sacrier",
            "Pandawa",
            "Rogue",
            "Masqueraider",
            "Foggernaut",
            "Eliotrope",
            "Huppermage",
            "Ouginak",
            "Forgelance"
       };

        private static List<ScottPlot.Color> PIE_COLOUR = new List<ScottPlot.Color>
        {
            Colors.CornflowerBlue,
            Colors.MediumSeaGreen,
            Colors.Orange,
            Colors.IndianRed,
            Colors.MediumPurple,
            Colors.SkyBlue,
            Colors.LightCoral,
            Colors.DarkKhaki,
            Colors.Violet,
            Colors.DarkGreen, 
            Colors.DarkOrange,
            Colors.Maroon, 
            Colors.Olive, 
            Colors.Pink, 
            Colors.Turquoise,
            Colors.Gold,
            Colors.Red, Colors.Green, Colors.Blue,
            Colors.Orange, Colors.Purple, Colors.Teal, Colors.Brown,
            Colors.Cyan, Colors.Magenta, Colors.Navy, Colors.Lime,
        };


        /// <summary>How often it will attempt to query new change logs.</summary>
        public static TimeSpan TICK_INTERVAL_TIMESPAN = TimeSpan.FromMinutes(30);

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

            await UpdateChannels(KolossiumRanking.KolossiumPlaylists, playlistRankings);

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

        public async Task<List<KolossiumRanking>> GetKolossiumRankingsAsync(string url)
        {
            await AwaitPollingDelay();

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

        public async Task UpdateChannels(string[] playlists, List<List<KolossiumRanking>> koloData)
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

                    for (int i = 0; i < koloData.Count; i++)
                    {
                        var embedBuilder = new EmbedBuilder
                        {
                            Title = $"{playlists[i]} Leader Board",
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

                        List<Tuple<string, double>> usageData = new();
                        foreach (var className in CLASS_NAMES)
                        {
                            var percentage = GetPercentageOfClass(koloData[i], className);
                            if (percentage > 0)
                            {
                                usageData.Add(new Tuple<string, double>(className, percentage));
                            }
                        }
                        usageData = usageData.OrderBy(s => s.Item2).ToList();

                        double[] values = usageData.Select(s => s.Item2).ToArray();
                        string[] labels = usageData.Select(s => s.Item1).ToArray();

                        // Create pie slices
                        List<PieSlice> slices = new();
                        for (int sliceIndex = 0; sliceIndex < values.Length; sliceIndex++)
                        {
                            slices.Add(new PieSlice()
                            {
                                Value = values[sliceIndex],
                                FillColor = PIE_COLOUR[sliceIndex % PIE_COLOUR.Count],
                                LabelFontSize = 30,
                                LabelFontColor = Colors.White,
                                LabelBorderColor = Colors.Black,
                                LabelAlignment = Alignment.MiddleCenter,
                                LegendText = $"{labels[sliceIndex]} ({values[sliceIndex]}%)"
                            });
                        }

                        // Create plot
                        var plt = new ScottPlot.Plot();
                        var pie = plt.Add.Pie(slices);

                        // Style the pie
                        pie.ExplodeFraction = 0.0;
                        pie.SliceLabelDistance = 1.0;

                        // Make it clean and modern
                        plt.Axes.Frameless();
                        plt.HideGrid();
                        plt.FigureBackground.Color = Colors.Transparent;
                        plt.Title($"{playlists[i]} Class Usage", size: 60);
                        plt.Axes.Color(Colors.White);

                        plt.ShowLegend();

                        //------ EXPORT IMAGE
                        var bytes = plt.GetImageBytes(1400, 900, ScottPlot.ImageFormat.Png);
                        using MagickImage magickImage = new MagickImage(bytes);

                        QuantizeSettings settings = new QuantizeSettings();
                        settings.Colors = 256;
                        magickImage.Quantize(settings);

                        await using Stream pngStream = new MemoryStream();
                        await magickImage.WriteAsync(pngStream);

                        var msg = await textChannel.SendMessageAsync($"# {playlists[i]} Kolossium Leaderboard {DateTime.UtcNow:yyyy/MM/dd}",
                        embed: embedBuilder.Build());

                        await msg.ModifyAsync(properties =>
                        {
                            properties.Attachments = new Optional<IEnumerable<FileAttachment>>(new List<FileAttachment>()
                        { new(pngStream, "piechart.png") });
                        });
                    }
                }
                catch (Exception ex)
                {
                    logger.Log($"{ex}");
                }
            }
        }

        public static float GetPercentageOfClass(List<KolossiumRanking> rankings, string className)
        {
            var total = rankings.Count;
            if (total == 0) return 0;
            var matching = rankings.Count(s => s.Class == className);
            if (matching == 0)
            {
                return 0;
            }

            return (float)((float)total / (float)total * (float)matching);
        }

    }
}
