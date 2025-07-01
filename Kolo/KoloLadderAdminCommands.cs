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
        public async Task TriggerKolo(bool force = false)
        {
            await DeferAsync(true);

            await _Services.GetRequiredService<KoloCheckerService>().TriggerNow(!force);

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = "Triggered";
            });
        }

        [RequireOwner]
        [SlashCommand("kolo_push", "Triggers all kolo data to be pushed to google sheets.", runMode: RunMode.Async)]
        public async Task PushKolo()
        {
            await DeferAsync(true);

            await _Services.GetRequiredService<KoloCheckerService>().PushAllDataToSheets(async s =>
            {
                await ModifyOriginalResponseAsync(properties =>
                {
                    properties.Content = s;
                });
            });

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = "Pushed";
            });
        }
    }
}
