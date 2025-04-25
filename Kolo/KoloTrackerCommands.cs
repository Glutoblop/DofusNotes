using ChangeLogTracker.Core.Interfaces;
using ChangeLogTracker.Data;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

namespace DofusNotes.PatchNotes
{
    [RequireOwner]
    public class KoloTrackerCommands : InteractionModuleBase<InteractionContext>
    {
        private IServiceProvider _Services;

        public KoloTrackerCommands(IServiceProvider services)
        {
            _Services = services;
        }

        [RequireOwner]
        [SlashCommand("set_kolo_channel", "Channel to host information about current Kolo ladders.", runMode: RunMode.Async)]
        public async Task SetKoloLadderChanenl(ITextChannel textChannel)
        {
            await DeferAsync(true);

            var db = _Services.GetRequiredService<IDatabase>();
            var hostedChannel = new HostedChannel();
            hostedChannel.ChannelId = textChannel.Id;

            var content = $"{textChannel.Mention} will now host the Kolo Leaderboards";

            await db.PutAsync($"KoloHostedChannel/{Context.Guild.Id}", hostedChannel);

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = content;
            });
        }
    }
}
