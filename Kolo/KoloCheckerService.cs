using ChangeLogTracker.Core.Interfaces;
using ChangeLogTracker.Data;
using ChangeLogTracker.Timer;
using Discord;
using Discord.WebSocket;
using DofusNotes.Data;
using DofusNotes.Sheets;
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

        private static List<ScottPlot.Color> CHART_COLOUR = new List<ScottPlot.Color>
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


        //Tick rate for when to update the information manually, long time considering it only needs to do it once a day. 
        public static TimeSpan TICK_INTERVAL_TIMESPAN = TimeSpan.FromHours(6);

        private IServiceProvider _Services;

        private BackgroundTask _Ticker;
        private bool _IsProcessing = false;

        private bool _ForceUpdate = false;

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
            _ForceUpdate = true;
            await OnTick();
        }

        private async Task OnTick()
        {
            if (_IsProcessing) return;
            _IsProcessing = true;

            Console.WriteLine($"OnTick Processing..");

            var db = _Services.GetRequiredService<IDatabase>();

            List<KolossiumLadder> ladders = new();

            var playLists = Enum.GetValues(typeof(KolossiumLadder.EKolossiumPlaylist)).OfType<KolossiumLadder.EKolossiumPlaylist>().ToList();
            foreach (KolossiumLadder.EKolossiumPlaylist playlist in playLists)
            {
#if !DEBUG
                if (!_ForceUpdate && await db.GetAsync<KolossiumLadder>(KolossiumLadder.GetDatabaseUrl(playlist)) != null)
                {
                    _IsProcessing = false;
                    Console.WriteLine($"Kolo data already exists, ending early");
                    return;
                }
#endif

                string url = "https://www.dofus.com/en/mmorpg/community/rankings/kolossium?type={0}";
                url = string.Format(url, KolossiumLadder.GetPlaylistParam(playlist));
                var ladder = await GetKolossiumLadderAsync(playlist, url);
                ladders.Add(ladder);
            }
            await UpdateLadderChannels(ladders);


            Console.WriteLine($"Pushing data to Sheets..");
            var googleSheet = _Services.GetRequiredService<GoogleSheetSaver>();
            foreach (var ladder in ladders)
            {
                await googleSheet.PushDataToSheetAsync(ladder);
            }

            _ForceUpdate = false;
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

        public async Task<KolossiumLadder> GetKolossiumLadderAsync(KolossiumLadder.EKolossiumPlaylist playlist, string url)
        {
            var db = _Services.GetRequiredService<IDatabase>();
            var ladder = await db.GetAsync<KolossiumLadder>(KolossiumLadder.GetDatabaseUrl(playlist));
            if (ladder != null)
            {
                return ladder;
            }

            await AwaitPollingDelay();

            ladder = new KolossiumLadder()
            {
                LadderType = playlist,
                Rankings = new()
            };

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
                        return ladder;
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
                        ladder.Rankings.Add(ranking);
                    }
                }


                await db.PutAsync<KolossiumLadder>(KolossiumLadder.GetDatabaseUrl(playlist), ladder);

            }
            catch (Exception e)
            {
                Console.WriteLine($"Parsing Error for Kolo {url}: {e.Message}");
            }

            return ladder;
        }

        public async Task UpdateLadderChannels(List<KolossiumLadder> ladders)
        {
            Console.WriteLine($"Updating Channels with Ladder Data...");

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
                    await DeleteOldMessages(client, textChannel);

                    for (int i = 0; i < ladders.Count; i++)
                    {
                        var embedBuilder = new EmbedBuilder
                        {
                            Title = $"{ladders[i].GetPlaylistParam()} Leader Board",
                        };

                        KolossiumLadder ladder = ladders[i];
                        string content = GenerateRankList(ladder);
                        embedBuilder.Description = content;

                        List<Tuple<string, double>> usageData = GenerateClassUsageData(ladder);
                        double[] values = usageData.Select(s => s.Item2).ToArray();
                        string[] labels = usageData.Select(s => s.Item1).ToArray();

                        var msg = await textChannel.SendMessageAsync($"# 🏆 {ladder.GetPlaylistParam()} Kolossium Leaderboard 🏆\n`{DateTime.UtcNow:yyyy/MM/dd}`",
                        embed: embedBuilder.Build());

                        await UpdateWithUsageChart(ladder.GetPlaylistParam(), values, labels, msg);
                    }
                }
                catch (Exception ex)
                {
                    logger.Log($"{ex}");
                }
            }
        }

        private static async Task UpdateWithUsageChart(string playlist, double[] values, string[] labels, IUserMessage msg)
        {
            // Create plot
            var plt = new ScottPlot.Plot();

            // set the label for each bar
            var barPlot = plt.Add.Bars(values);
            for (int barIndex = 0; barIndex < barPlot.Bars.Count; barIndex++)
            {
                Bar? bar = barPlot.Bars[barIndex];
                bar.Label = $"{labels[barIndex]} ({bar.Value}%)";
                bar.FillColor = CHART_COLOUR[barIndex];
            }

            // customize label style
            barPlot.ValueLabelStyle.Bold = true;
            barPlot.ValueLabelStyle.FontSize = 18;
            barPlot.Horizontal = true;
            barPlot.ValueLabelStyle.ForeColor = Colors.White;

            // add extra margin to account for label
            plt.Axes.SetLimitsX(0, barPlot.Bars.Max(s => s.Value) * 2);

            // Make it clean and modern
            plt.Axes.Frameless();
            plt.HideGrid();
            plt.FigureBackground.Color = Colors.Transparent;
            plt.Title($"{playlist} Top 100 Class Breakdown", size: 60);
            plt.Axes.Color(Colors.White);

            plt.ShowLegend();

            //------ EXPORT IMAGE
            var bytes = plt.GetImageBytes(1400, 900, ScottPlot.ImageFormat.Png);
            var magickImage = new MagickImage(bytes);
            QuantizeSettings settings = new QuantizeSettings();
            settings.Colors = 256;
            magickImage.Quantize(settings);
            var pngStream = new MemoryStream();
            await magickImage.WriteAsync(pngStream);

            await msg.ModifyAsync(properties =>
            {
                properties.Attachments = new Optional<IEnumerable<FileAttachment>>(new List<FileAttachment>()
                        { new(pngStream, "chart.png") });
            });
        }

        private static List<Tuple<string, double>> GenerateClassUsageData(KolossiumLadder ladder)
        {
            List<Tuple<string, double>> usageData =
            [
                .. from className in CLASS_NAMES
                let percentage = GetPercentageOfClass(ladder, className)
                where percentage > 0
                select new Tuple<string, double>(className, percentage),
            ];
            usageData = usageData.OrderBy(s => s.Item2).ToList();
            return usageData;
        }

        private static string GenerateRankList(KolossiumLadder ladder)
        {
            var content = "## Name | Class | Rating | Win Rate %\n";
            for (int ladderIndex = 0; ladderIndex < ladder.Rankings.Count; ladderIndex++)
            {
                if (ladderIndex == 50) break;

                KolossiumRanking playerData = ladder.Rankings[ladderIndex];

                string playerInfo = "* ";
                if (ladderIndex == 0) playerInfo += "🥇 ";
                else if (ladderIndex == 1) playerInfo += "🥈 ";
                else if (ladderIndex == 2) playerInfo += "🥉 ";
                else playerInfo += $"[{ladderIndex + 1}] ";

                playerInfo += $"**{playerData.Name}** | {playerData.Class} | {playerData.Rating} | {playerData.Winrate}";

                if (ladderIndex != 0) content += "\n";
                content += playerInfo;
            }

            return content;
        }

        private static async Task DeleteOldMessages(DiscordSocketClient client, ITextChannel textChannel)
        {
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
        }

        public static float GetPercentageOfClass(KolossiumLadder ladder, string className)
        {
            var total = ladder.Rankings.Count;
            if (total == 0) return 0;
            var matching = ladder.Rankings.Count(s => s.Class == className);
            if (matching == 0)
            {
                return 0;
            }

            return (float)((float)total / (float)total * (float)matching);
        }

    }
}
