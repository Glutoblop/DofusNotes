using Discord;
using Discord.Interactions;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using ChangeLogTracker.Core.Interfaces;
using ChangeLogTracker.Data;

namespace ChangeLogTracker
{
    public class RequestProfessionsCommand : InteractionModuleBase<InteractionContext>
    {
        private IServiceProvider _Services;

        public RequestProfessionsCommand(IServiceProvider services)
        {
            _Services = services;
        }

        [SlashCommand("set_profession_channel", "Channel where the messages about player professions are hosted, for a particular dofus server.", runMode: RunMode.Async)]
        public async Task SetDofusServerHostedChannel(ITextChannel textChannel, string dofusServer)
        {
            await SetHostedChannel(textChannel, dofusServer);
        }

        [SlashCommand("set_guild_profession_channel", "Channel where the messages about player professions are hosted, for a particular guild only.", runMode: RunMode.Async)]
        public async Task SetGuildHostedChannel(ITextChannel textChannel, string guildUrl)
        {
            await SetHostedChannel(textChannel, "", guildUrl);
        }

        private async Task SetHostedChannel(ITextChannel textChannel,
            string dofusServer,
            string guildUrl = null)
        {
            await DeferAsync(true);

            var db = _Services.GetRequiredService<IDatabase>();
            var hostedChannel = new HostedChannel();
            hostedChannel.GuildId = Context.Guild.Id;
            hostedChannel.ChannelId = textChannel.Id;
            hostedChannel.DofusServer = dofusServer;
            hostedChannel.DofusServer = hostedChannel.DofusServer.Replace(" ", "");
            hostedChannel.DofusServer = hostedChannel.DofusServer.ToLowerInvariant();

            hostedChannel.GuildURL = guildUrl;

            var content = $"{textChannel.Mention} will now host the profession messages for Dofus Server: `{dofusServer}`";

            if (!string.IsNullOrEmpty(guildUrl))
            {
                var finder = _Services.GetRequiredService<ProfessionFinder>();
                var guildData = await finder.FindGuild(guildUrl, false);
                if (guildData == null || guildData?.Players?.Count == 0)
                {
                    await ModifyOriginalResponseAsync(properties =>
                    {
                        properties.Content = $"❌ Failed to find Guild Page, cannot confirm channel for guild.";
                    });
                    return;
                }

                hostedChannel.DofusServer = guildData.DofusServer;

                content = $"{textChannel.Mention} will now track the professions for Guild: `{guildData.GuildName}` on Dofus Server: `{dofusServer}`";
            }

            await db.PutAsync<HostedChannel>($"HostedChannel/{Context.Guild.Id}/{textChannel.Id}", hostedChannel);

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = content;
            });

            if (!string.IsNullOrEmpty(guildUrl))
            {
                var finder = _Services.GetRequiredService<ProfessionFinder>();
                await finder.TriggerImmediately();
            }
        }

        
        [SlashCommand("track_player", "Track this players profession progress", runMode: RunMode.Async)]
        public async Task AddPlayer(string characterPageUrl)
        {
            await DeferAsync(true);

            var finder = _Services.GetRequiredService<ProfessionFinder>();
            var success = await finder.TrackPlayer(characterPageUrl);

            if (!success)
            {
                await ModifyOriginalResponseAsync(properties =>
                {
                    properties.Content = $"❌ Tracking player failed.";
                });
                return;
            }

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = $"✅ Tracking player.";
            });

        }

        [SlashCommand("remove_player", "Remove player from being invidiually tracked.", runMode: RunMode.Async)]
        public async Task RemovePlayer(string characterPageUrl)
        {
            await DeferAsync(true);

            var db = _Services.GetRequiredService<IDatabase>();

            var finder = _Services.GetRequiredService<ProfessionFinder>();
            var player = await finder.FindPlayer(characterPageUrl);

            if (player == null)
            {
                await ModifyOriginalResponseAsync(properties =>
                {
                    properties.Content = $"❌ Player removal failed.";
                });
                return;
            }

            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(characterPageUrl);
            var encoded = System.Convert.ToBase64String(plainTextBytes);
            await db.DeleteAsync($"Player/{encoded}");

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = $"✅ Player removed.";
            });
        }


        [SlashCommand("track_guild", "Add this guild to track all its members professions.", runMode: RunMode.Async)]
        public async Task AddGuild(string guildPageUrl)
        {
            await DeferAsync(true);

            var db = _Services.GetRequiredService<IDatabase>();

            var finder = _Services.GetRequiredService<ProfessionFinder>();
            var success = await finder.TrackGuild(guildPageUrl);

            if (!success)
            {
                await ModifyOriginalResponseAsync(properties =>
                {
                    properties.Content = $"❌ Tracking guild failed.";
                });
                return;
            }

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = $"✅ Tracking guild.";
            });

        }

        [SlashCommand("remove_guild", "Remove guild from being tracked.", runMode: RunMode.Async)]
        public async Task RemoveGuild(string guildPageUrl)
        {
            await DeferAsync(true);

            var db = _Services.GetRequiredService<IDatabase>();

            var finder = _Services.GetRequiredService<ProfessionFinder>();
            var guild = await finder.FindGuild(guildPageUrl, false);

            if (guild != null)
            {
                await ModifyOriginalResponseAsync(properties =>
                {
                    properties.Content = $"❌ Guild removal failed.";
                });
                return;
            }

            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(guildPageUrl);
            var encoded = System.Convert.ToBase64String(plainTextBytes);
            await db.DeleteAsync($"Guild/{encoded}");

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = $"✅ Guild removed.";
            });
        }

    }
}
