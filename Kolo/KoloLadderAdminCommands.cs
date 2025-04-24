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
        [SlashCommand("add_kolo_data", "Assigns this channel as the channel to show kolo info in", runMode: RunMode.Async)]
        public async Task SetKoloChannel()
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
