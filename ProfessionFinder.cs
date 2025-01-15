using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using Microsoft.Extensions.DependencyInjection;
using ChangeLogTracker.Core.Interfaces;
using ChangeLogTracker.Data;
using ChangeLogTracker.Timer;
using System.Text.RegularExpressions;

namespace ChangeLogTracker
{
    public class ProfessionFinder
    {
        /// <summary>Show the top n amount of people in each profession.</summary>
        public static int TOP_SHOW_COUNT = 7;

        /// <summary>How old the cache can be before it needs refreshing.</summary>
        public static TimeSpan PLAYER_CACHE_DIRTY_TIME = TimeSpan.FromMinutes(30);

        /// <summary>How often it will attempt to update the Player Data and Discord messages.</summary>
        public static TimeSpan TICK_INTERVAL_TIMESPAN = TimeSpan.FromMinutes(60);

        private static Dictionary<string, string> IconUrls = new Dictionary<string, string>()
        {
            {"Alchemist","https://nokazu.com/images/almanax/metiers/alchimiste.png" },
            {"Artificer","https://nokazu.com/images/almanax/metiers/faconneur.png" },
            {"Carver","https://nokazu.com/images/almanax/metiers/sculpteur.png" },
            {"Carvmagus","https://nokazu.com/images/almanax/sculptemage.png" },
            {"Costumagus","https://nokazu.com/images/almanax/costumage.png" },
            {"Craftmagus","https://nokazu.com/images/almanax/facomage.png" },
            {"Farmer","https://nokazu.com/images/almanax/metiers/paysan.png" },
            {"Fisherman","https://nokazu.com/images/almanax/metiers/pecheur.png" },
            {"Hunter","https://nokazu.com/images/almanax/metiers/chasseur.png" },
            {"Handyman","https://nokazu.com/images/almanax/metiers/bricoleur.png" },
            {"Jeweller","https://nokazu.com/images/almanax/metiers/bijoutier.png" },
            {"Jewelmagus","https://nokazu.com/images/almanax/joaillomage.png" },
            {"Lumberjack","https://nokazu.com/images/almanax/metiers/bucheron.png" },
            {"Miner","https://nokazu.com/images/almanax/metiers/mineur.png" },
            {"Smith","https://nokazu.com/images/almanax/metiers/forgeron.png" },
            {"Shoemaker","https://nokazu.com/images/almanax/metiers/cordonnier.png" },
            {"Smithmagus","https://nokazu.com/images/almanax/forgemage.png" },
            {"Shoemagus","https://nokazu.com/images/almanax/cordomage.png" },
            {"Tailor","https://nokazu.com/images/almanax/metiers/tailleur.png" },
        };

        private IServiceProvider _Services;

        private BackgroundTask _Ticker;
        private bool _IsProcessing = false;

        private class ProfPlayerData
        {
            public string PlayerName;
            public string ProfessionName;
            public int ProfessionLevel;
            public DateTime LastFetched;
        }

        public ProfessionFinder(IServiceProvider services)
        {
            _Services = services;
        }

        public void Start()
        {
            _Ticker = new BackgroundTask(TICK_INTERVAL_TIMESPAN, OnTick, TimeSpan.FromSeconds(30));
            _Ticker.Start();
        }

        public async Task TriggerImmediately()
        {
            await OnTick();
        }

        private async Task OnTick()
        {
            if (_IsProcessing) return;
            _IsProcessing = true;

            try
            {
                Console.WriteLine($"OnTick Started");

                var db = _Services.GetRequiredService<IDatabase>();
                var client = _Services.GetRequiredService<DiscordSocketClient>();

                var now = DateTime.UtcNow;

                var finder = _Services.GetRequiredService<ProfessionFinder>();

                //Update the cache of players using the tracked guilds.
                List<GuildProfessionData> guildData = new();
                await db.GetAllAsync<GuildProfessionData>($"Guild", (path, data) =>
                {
                    if (data == null) return false;
                    guildData.Add(data);
                    return false;
                });

                Console.WriteLine($"Preparing guild data before player parsing..");
                foreach (var guild in guildData)
                {
                    await finder.FindGuild(guild.GuildURL, true);
                }
                Console.WriteLine($"Guild prepartion complete.");

                //Find all the tracked players
                //Collate a Dictionary of <string,PlayerProfessionData> key'd by DofusServer
                Dictionary<string, List<PlayerProfessionData>> serverPlayers = new();
                await db.GetAllAsync<PlayerProfessionData>($"Player", (path, data) =>
                {
                    if (data == null) return false;
                    if (!serverPlayers.ContainsKey(data.DofusServer))
                    {
                        serverPlayers.Add(data.DofusServer, new());
                    }
                    serverPlayers[data.DofusServer].Add(data);
                    return false;
                });

                Console.WriteLine($"Updating ({client.Guilds.Count}) Discords ...");

                //Do this for each Discord the bot is a part of (won't work if this is a 100+ discord server.
                foreach (var guild in client.Guilds)
                {
                    //Check HostedChannels in this guild to see if one exists for the key in this dict
                    //<dofusServer,HostedChannel>
                    List<ulong> hostedChannelIds = new();

                    await db.GetAllAsync<HostedChannel>($"HostedChannel/{guild.Id}", (path, data) =>
                     {
                         if (data == null) return false;
                         hostedChannelIds.Add(data.ChannelId);
                         return false;
                     });

                    foreach (var channelId in hostedChannelIds)
                    {
                        Console.WriteLine($"Find [{guild.Name}] channel <{channelId}>");

                        ITextChannel textChannel = await (guild as IGuild).GetTextChannelAsync(channelId);
                        if (textChannel == null) continue;

                        HostedChannel hostedChannelData = await db.GetAsync<HostedChannel>($"HostedChannel/{guild.Id}/{textChannel.Id}");
                        if (hostedChannelData.ProfessionMessages == null) hostedChannelData.ProfessionMessages = new();

                        if (!string.IsNullOrEmpty(hostedChannelData.GuildURL))
                        {
                            //If this isn't empty, then repopulate the player data available to only be from the guild players.
                            serverPlayers = new();
                            var specificGuildData = await finder.FindGuild(hostedChannelData.GuildURL, true);
                            if (specificGuildData == null)
                            {
                                //Error here, don't continue updating this channel otherwise it will update incorrectly.
                                continue;
                            }

                            foreach (var sgd in specificGuildData.Players)
                            {
                                if (!serverPlayers.ContainsKey(sgd.DofusServer))
                                {
                                    serverPlayers.Add(sgd.DofusServer, new());
                                }
                                serverPlayers[sgd.DofusServer].Add(sgd);
                            }
                        }

                        List<PlayerProfessionData> playerProfessions = serverPlayers[hostedChannelData.DofusServer];

                        //Collate the data into a <string,List<PlayerProfessionData>> key'd by ProfessionsName alphabetical
                        //Sort this profession list by lvl

                        Dictionary<string, List<ProfPlayerData>> sortedByProfession = new();
                        foreach (var player in playerProfessions)
                        {
                            foreach (var prof in player.Professions)
                            {
                                if (!sortedByProfession.ContainsKey(prof.Profession)) sortedByProfession.Add(prof.Profession, new List<ProfPlayerData>());

                                sortedByProfession[prof.Profession].Add(new ProfPlayerData()
                                {
                                    PlayerName = player.CharacterName,
                                    LastFetched = player.TimeStamp,
                                    ProfessionLevel = prof.Level,
                                    ProfessionName = prof.Profession
                                });
                            }
                        }

                        Dictionary<string, List<ProfPlayerData>> finalMessageData = new();

                        var sortedKeys = sortedByProfession.Keys.OrderBy(s => (int)s[0]).ToList();
                        //Loop through all this dofus servers professions
                        foreach (var profKey in sortedKeys)
                        {
                            List<ProfPlayerData> profs = sortedByProfession[profKey];
                            profs = profs.OrderByDescending(s => s.ProfessionLevel).ToList();

                            finalMessageData[profKey] = new();

                            for (int i = 0; i < profs.Count && i < TOP_SHOW_COUNT; i++)
                            {
                                finalMessageData[profKey].Add(profs[i]);
                            }
                        }

                        var stamp = await db.GetAsync<TimeStamp>($"LastUpdated");
                        DateTime? timestamp = stamp?.Date;

                        //All hosted channels will have a message (with embed) per profession
                        //Find a message for this profession in the channel (if it doesnt exist then make one)
                        //Edit the message with the new data of the top 10 people with professions
                        //Including relative timestamp for how old the data is for that person (taken from web)
                        //The message should include; Dofus Server Name as the Title. eg # Talkasha

                        Console.WriteLine($"Updating Profession Messages for {guild.Name}::{textChannel.Name}..");

                        foreach (KeyValuePair<string, List<ProfPlayerData>> msgData in finalMessageData)
                        {
                            try
                            {
                                Console.WriteLine($"Updating {msgData.Key} message..");

                                RestUserMessage msg = null;
                                if (hostedChannelData.ProfessionMessages?.ContainsKey(msgData.Key) ?? false)
                                {
                                    msg = await textChannel.GetMessageAsync(hostedChannelData.ProfessionMessages[msgData.Key]) as RestUserMessage;
                                }

                                if (msg == null)
                                {
                                    msg = await textChannel.SendMessageAsync($"# {msgData.Key}") as RestUserMessage;

                                    hostedChannelData.ProfessionMessages.Add(msgData.Key, msg.Id);
                                    await db.PutAsync($"HostedChannel/{guild.Id}/{textChannel.Id}", hostedChannelData);
                                }

                                var embedBuilder = new EmbedBuilder();
                                embedBuilder.Title = $"{msgData.Key}";
                                string relativeTime = timestamp != null ? $"<t:{((DateTimeOffset)timestamp).ToUnixTimeSeconds()}:R>" : "`Never`";
                                embedBuilder.Description = $"Last updated {relativeTime}";

                                embedBuilder.ThumbnailUrl = IconUrls[msgData.Key];
                                embedBuilder.ImageUrl = "";

                                for (int i = 0; i < msgData.Value.Count; i++)
                                {
                                    var data = msgData.Value[i];
                                    embedBuilder.AddField($"{data.PlayerName}", $"Level {data.ProfessionLevel}");
                                }

                                await msg.ModifyAsync(properties =>
                                {
                                    properties.Content = $"";
                                    properties.Embed = embedBuilder.Build();
                                });
                            }
                            catch
                            {

                            }
                            finally
                            {
                                Console.WriteLine($"Update {msgData.Key} message complete!");
                            }
                        }

                    }

                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"OnTick Error: {e}");
            }
            finally
            {
                Console.WriteLine($"### OnTick completed!");
                _IsProcessing = false;
            }
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

        public async Task<bool> TrackPlayer(string characterPageUrl)
        {
            var db = _Services.GetRequiredService<IDatabase>();
            var playerData = await FindPlayer(characterPageUrl);
            if (playerData == null)
            {
                return false;
            }

            await db.PutAsync<PlayerProfessionData>($"Player/{Base64Encode(characterPageUrl)}", new()
            {
                CharacterURL = characterPageUrl
            });

            return true;
        }

        public async Task<bool> TrackGuild(string guildPageUrl)
        {
            var guildData = await FindGuild(guildPageUrl, false);
            if (guildData == null || guildData.Players.Count == 0)
            {
                return false;
            }

            var db = _Services.GetRequiredService<IDatabase>();
            await db.PutAsync<GuildProfessionData>($"Guild/{Base64Encode(guildPageUrl)}", new()
            {
                GuildURL = guildPageUrl
            });
            return true;
        }

        public async Task<GuildProfessionData> FindGuild(string guildPageUrl, bool fetchPlayers)
        {
            await AwaitPollingDelay();

            var web = new HtmlWeb();
            var doc = await web.LoadFromWebAsync(guildPageUrl);

            var data = new GuildProfessionData();
            data.GuildURL = guildPageUrl;
            try
            {
                var dofusServerNode = doc.DocumentNode.QuerySelector(".ak-directories-server-name");
                data.DofusServer = dofusServerNode.InnerText;
                data.DofusServer = data.DofusServer.Replace(" ", "");
                data.DofusServer = data.DofusServer.ToLowerInvariant();

                var guildNameNode = doc.DocumentNode.QuerySelector(".ak-return-link");
                var guildName = guildNameNode.InnerText;

                data.GuildName = "";
                bool startFound = false;
                int spaceCount = 0;
                for (int i = 0; i < guildName.Length; i++)
                {
                    var c = guildName[i];

                    if (c == ' ')
                    {
                        spaceCount++;
                    }
                    else
                    {
                        spaceCount = 0;
                    }

                    if (!startFound && Char.IsLetter(c))
                    {
                        startFound = true;
                        data.GuildName += c;
                    }
                    else if (startFound)
                    {
                        if (spaceCount == 2)
                        {
                            data.GuildName = data.GuildName.Remove(data.GuildName.Length - 2, 2);
                            break;
                        }
                        data.GuildName += c;
                    }
                }

                List<PlayerProfessionData> players = new();

                var allMemberNodes = doc.DocumentNode.QuerySelectorAll(".tr_class").ToList();

                Console.WriteLine($"Parsing guild {data.GuildName} and {allMemberNodes.Count} members");

                foreach (var memberNode in allMemberNodes)
                {
                    //Find the breed-icon (class icon) element, then get its parent.. and the child (alongside the breed icon) is called "a" for the name.
                    var child = memberNode.QuerySelector(".ak-breed-icon");
                    var parent = child.ParentNode;
                    var nameNode = parent.ChildNodes[1];
                    var characterName = nameNode.InnerText;

                    var characterUrl = $"https://www.dofus.com{nameNode.Attributes[0].Value}";

                    if (fetchPlayers)
                    {
                        PlayerProfessionData pData = await FindPlayer(characterUrl);
                        if (pData != null)
                        {
                            players.Add(pData);
                        }
                    }
                    else
                    {
                        PlayerProfessionData pData = new PlayerProfessionData();
                        pData.CharacterURL = characterUrl;
                        pData.CharacterName = characterName;
                        players.Add(pData);
                    }
                }

                data.Players = players;

                Console.WriteLine($"Guild [{data.GuildName}] player parse complete.");

                var db = _Services.GetRequiredService<IDatabase>();
                await db.PutAsync($"Guild/{Base64Encode(guildPageUrl)}", data);

                return data;
            }
            catch (Exception ex)
            {

            }

            return null;
        }

        public async Task<PlayerProfessionData> FindPlayer(string characterPageUrl)
        {
            var db = _Services.GetRequiredService<IDatabase>();
            PlayerProfessionData playerData = await db.GetAsync<PlayerProfessionData>($"Player/{Base64Encode(characterPageUrl)}");
            if (playerData != null)
            {
                var dif = DateTime.UtcNow - playerData.TimeStamp;
                if (dif.TotalSeconds <= PLAYER_CACHE_DIRTY_TIME.TotalSeconds)
                {
                    return playerData;
                }
            }

            await AwaitPollingDelay();

            var web = new HtmlWeb();
            var doc = await web.LoadFromWebAsync(characterPageUrl);

            PlayerProfessionData ppData = new();
            List<ProfessionData> professionData = new();

            try
            {
                var charNameNode = doc.DocumentNode.QuerySelector(".ak-return-link");
                var charName = charNameNode.InnerText;
                charName = charName.Replace(" ", "");
                charName = charName.Replace("\n", "");
                ppData.CharacterName = charName;

                Console.WriteLine($"Parsing {charName} ...");

                var serverNameNode = doc.DocumentNode.QuerySelector(".ak-directories-server-name");
                var serverName = serverNameNode.InnerHtml;
                ppData.DofusServer = serverName;
                ppData.DofusServer = ppData.DofusServer.Replace(" ", "");
                ppData.DofusServer = ppData.DofusServer.ToLowerInvariant();

                // Find containing div with selector
                var containingDiv = doc.DocumentNode.QuerySelectorAll(".ak-infos-jobs div.ak-lists-paginable div.ak-list-element div.ak-content").ToList();

                // Loop through each one

                foreach (var div in containingDiv)
                {
                    var title = div.QuerySelector(".ak-title").InnerText;
                    title = title.Replace(" ", "");
                    title = title.Replace("\n", "");

                    var levelStr = div.QuerySelector(".ak-text").InnerText;
                    levelStr = Regex.Replace(levelStr, @"[^\d]", "");
                    int level = 0;
                    int.TryParse(levelStr, out level);

                    if (level == 0) continue;

                    professionData.Add(new ProfessionData
                    {
                        Profession = title,
                        Level = level,
                    });

                }

                ppData.Professions = professionData;
                ppData.TimeStamp = DateTime.Now;

                await db.PutAsync<PlayerProfessionData>($"Player/{Base64Encode(characterPageUrl)}", ppData);

                await db.PutAsync<TimeStamp>($"LastUpdated", new TimeStamp() { Date = DateTime.UtcNow });

                return ppData;
            }
            catch (Exception ex)
            {

            }

            return null;
        }

    }
}
