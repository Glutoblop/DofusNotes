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

            Console.WriteLine($"OnTick Processing..");

            var dateTime = DateOnly.FromDateTime(DateTime.UtcNow);

            var allLadders = await GetKoloLaddersFromDate(dateTime);
            if (allLadders != null)
            {
                List<KolossiumLadder> globalLadders = allLadders[0];
                List<KolossiumLadder> combinedLadders = allLadders[1];

                //Update the Discord charts using the global ladders (only for now)
                await UpdateLadderChannelsGraphs(dateTime, globalLadders);

                Console.WriteLine($"Pushing data to Sheets..");
                var googleSheet = _Services.GetRequiredService<GoogleSheetSaver>();
                await googleSheet.PushDataToSheetAsync(dateTime, combinedLadders, true);
            }

            _IsProcessing = false;

            Console.WriteLine($"Completed!");
        }

        private async Task<List<List<KolossiumLadder>>> GetKoloLaddersFromDate(DateOnly dateTime, bool useCache = true)
        {
            var db = _Services.GetRequiredService<IDatabase>();

            List<KolossiumLadder> globalLadders = new();
            List<KolossiumLadder> combinedLadders = new();

            var playLists = Enum.GetValues(typeof(KolossiumLadder.EKolossiumPlaylist)).OfType<KolossiumLadder.EKolossiumPlaylist>().ToList();
            foreach (KolossiumLadder.EKolossiumPlaylist playlist in playLists)
            {
                //Get the generic top 100
                KolossiumLadder globalLadder = await GetKolossiumLadderAsync(dateTime, playlist, -1, useCache);
                if (globalLadder == null)
                {
                    return null;
                }
                globalLadders.Add(globalLadder);

                List<KolossiumRanking> combinedRankings = new();
                foreach (var globalRank in globalLadder.Rankings)
                {
                    globalRank.GlobalRank = globalRank.Rank;
                    combinedRankings.Add(globalRank);
                }

                //Get the top 100 for each class
                for (int i = 1; i <= 20; i++)
                {
                    //Class 19 is used as the common spells class so no kolo leaderboard
                    if (i == 19) continue;

                    KolossiumLadder classLadder = await GetKolossiumLadderAsync(dateTime, playlist, i, useCache);
                    if (classLadder == null)
                    {
                        return null;
                    }

                    foreach (var classRank in classLadder.Rankings)
                    {
                        //Only merge the data if the character name AND server are the same
                        var combinedIndex = combinedRankings.FindIndex(s => s.Name == classRank.Name && s.Server == classRank.Server);
                        if (combinedIndex != -1)
                        {
                            combinedRankings[combinedIndex].ClassRank = classRank.Rank;
                        }
                        else
                        {
                            classRank.ClassRank = classRank.Rank;
                            combinedRankings.Add(classRank);
                        }
                    };
                }

                combinedLadders.Add(new KolossiumLadder()
                {
                    LadderType = playlist,
                    Rankings = combinedRankings
                });
            }

            return [globalLadders, combinedLadders];
        }

        public async Task<KolossiumLadder> GetKolossiumLadderAsync(DateOnly dateOnly, KolossiumLadder.EKolossiumPlaylist playlist, int breed, bool useCache = true)
        {
            string playlistType = KolossiumLadder.GetPlaylistParam(playlist);
            var url = $"https://www.dofus.com/en/mmorpg/community/rankings/kolossium?type={playlistType}&breeds={breed}";

            var dbPath = KolossiumLadder.GetDatabaseUrl(dateOnly, playlist, breed);

            Console.WriteLine($"Requesting {playlistType} filtered by {breed}");

            var db = _Services.GetRequiredService<IDatabase>();

            KolossiumLadder ladder = await db.GetAsync<KolossiumLadder>(dbPath);
            if (ladder != null && useCache)
            {
                return ladder;
            }

            if (DateOnly.FromDateTime(DateTime.UtcNow) != dateOnly)
            {
                Console.WriteLine($"You can only fetch the data from none-cache if its todays data.");
                return null;
            }

            //Just wait an amount of time to make sure not to hit any lockouts from the website.
            await Task.Delay(2340);

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
                            Winrate = cells[6].InnerText.Trim(),

                            DayStamp = dateOnly,
                            Playlist = playlistType
                        };
                        ladder.Rankings.Add(ranking);
                    }
                }

                await db.PutAsync<KolossiumLadder>(dbPath, ladder);

            }
            catch (Exception e)
            {
                Console.WriteLine($"Parsing Error for Kolo {url}: {e.Message}");
            }

            return ladder;
        }

        public async Task UpdateLadderChannelsGraphs(DateOnly dateOnly, List<KolossiumLadder> ladders)
        {
            Console.WriteLine($"Updating Channels with Ladder Data...");

            var client = _Services.GetRequiredService<DiscordSocketClient>();
            var db = _Services.GetRequiredService<IDatabase>();
            var logger = _Services.GetRequiredService<ILogger>();

            var pushedKey = dateOnly.ToString("yyyy_MM_dd");
            var pushedGraphs = await db.GetAsync<PushedStamp>($"Pushed/Graphs/{pushedKey}");
            if (pushedGraphs != null)
            {
                Console.WriteLine($"Already pushed the graphs");
                return;
            }
            await db.PutAsync<PushedStamp>($"Pushed/Graphs/{pushedKey}", new() { Pushed = dateOnly });

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

                    logger.Log($"Fetching channel..");
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

                    logger.Log($"Sending Messages to {guild.Name}..");

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

                        var msg = await textChannel.SendMessageAsync($"# 🏆 {ladder.GetPlaylistParam()} Kolossium Leaderboard 🏆\n`{dateOnly:yyyy/MM/dd}`",
                        embed: embedBuilder.Build());

                        await UpdateWithUsageChart(ladder.GetPlaylistParam(), values, labels, msg);
                    }
                }
                catch (Exception ex)
                {
                    logger.Log($"UpdateLadderChannels Error: {ex}");
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

        private async Task DeleteOldMessages(DiscordSocketClient client, ITextChannel textChannel)
        {
            var logger = _Services.GetRequiredService<ILogger>();
            logger.Log($"DeleteOldMessages ..");

            try
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
            catch (Exception ex)
            {
                logger.Log($"DeleteOldMessages Error: {ex}");
            }
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

        public async Task PushAllDataToSheets(Action<string> onMsg = null)
        {
            var db = _Services.GetRequiredService<IDatabase>();

            DateOnly? earliestTime = null;

            await db.GetAllAsync<KolossiumLadder>("Ladder", (path, data) =>
            {
                string[] pathParts = path.Split('/');
                string date = pathParts[1];
                string mode = pathParts[2];

                DateOnly dayStamp = DateOnly.ParseExact(date, "yyyy_MM_dd");
                if (earliestTime == null)
                {
                    earliestTime = dayStamp;
                }
                else if (dayStamp < earliestTime)
                {
                    earliestTime = dayStamp;
                }

                return false;
            });

            DateOnly dateTime = earliestTime.Value;
            DateOnly nowDate = DateOnly.FromDateTime(DateTime.Now);

            while (dateTime <= nowDate)
            {
                var allLadders = await GetKoloLaddersFromDate(dateTime);
                if (allLadders != null)
                {
                    List<KolossiumLadder> combinedLadders = allLadders[1];

                    var googleSheet = _Services.GetRequiredService<GoogleSheetSaver>();
                    await googleSheet.PushDataToSheetAsync(dateTime, combinedLadders, dateTime == nowDate);

                    onMsg?.Invoke($"Updated {dateTime}");
                }
                dateTime = dateTime.AddDays(1);
            }
        }
    }
}
