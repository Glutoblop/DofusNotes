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
        [SlashCommand("kolo_push", "Push a single yyyy/MM/dd data to google sheets", runMode: RunMode.Async)]
        public async Task PushKolo(string dateString)
        {
            await DeferAsync(true);

            if(!DateOnly.TryParseExact(dateString, "yyyy/MM/dd", out DateOnly targetDate))
            {
                await ModifyOriginalResponseAsync(properties =>
                {
                    properties.Content = "Date format needs to be yyyy/MM/dd";
                });
                return;
            }

            if(!await _Services.GetRequiredService<KoloCheckerService>().PushDayDataToSheets(targetDate))
            {
                await ModifyOriginalResponseAsync(properties =>
                {
                    properties.Content = "Failed :(";
                });
                return;
            }

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = "Pushed!";
            });
        }

        [RequireOwner]
        [SlashCommand("kolo_push_all", "Triggers all kolo data to be pushed to google sheets.", runMode: RunMode.Async)]
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
