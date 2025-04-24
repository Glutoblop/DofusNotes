using Discord.Interactions;
using DofusNotes.PatchNotes;
using Microsoft.Extensions.DependencyInjection;

namespace DofusNotes.Kolo
{
    [RequireOwner]
    public class KoloLadderAdminCommands : InteractionModuleBase<InteractionContext>
    {
        private IServiceProvider _Services;

        public KoloLadderAdminCommands(IServiceProvider services)
        {
            _Services = services;
        }

        [RequireOwner]
        [SlashCommand("kolo_trigger", "Triggers kolo leaderboard tick", runMode: RunMode.Async)]
        public async Task TriggerKolo()
        {
            await DeferAsync(true);

            await _Services.GetRequiredService<KoloCheckerService>().TriggerNow();

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = "Triggered";
            });
        }
    }
}
