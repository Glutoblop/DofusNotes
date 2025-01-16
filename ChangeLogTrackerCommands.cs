using Discord;
using Discord.Interactions;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using ChangeLogTracker.Core.Interfaces;
using ChangeLogTracker.Data;

namespace ChangeLogTracker
{
    public class ChangeLogTrackerCommands : InteractionModuleBase<InteractionContext>
    {
        private IServiceProvider _Services;

        public ChangeLogTrackerCommands(IServiceProvider services)
        {
            _Services = services;
        }

        [SlashCommand("set_changelog_role", "Set the role to notifiy when change logs are posted.", runMode: RunMode.Async)]
        public async Task SetChangelogRole(IRole role)
        {
            await DeferAsync(true);

            var db = _Services.GetRequiredService<IDatabase>();
            var notifyRole = new NotifyRole();
            notifyRole.RoleId = role.Id;

            var content = $"{role.Mention} will now be notified on change log notifications messages";

            await db.PutAsync<NotifyRole>($"NotifyRole/{Context.Guild.Id}", notifyRole);

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = content;
            });
        }

        [SlashCommand("set_changelog_channel", "Channel to post notifications about change logs go.", runMode: RunMode.Async)]
        public async Task SetDofusServerHostedChannel(ITextChannel textChannel)
        {
            await DeferAsync(true);

            var db = _Services.GetRequiredService<IDatabase>();
            var hostedChannel = new HostedChannel();
            hostedChannel.ChannelId = textChannel.Id;

            var content = $"{textChannel.Mention} will now host the change log notifications messages";

            await db.PutAsync<HostedChannel>($"HostedChannel/{Context.Guild.Id}", hostedChannel);

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = content;
            });
        }
    }
}
