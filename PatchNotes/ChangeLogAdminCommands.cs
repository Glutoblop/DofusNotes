using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

namespace DofusNotes.PatchNotes
{
    [RequireOwner]
    public class ChangeLogAdminCommands : InteractionModuleBase<InteractionContext>
    {
        private IServiceProvider _Services;

        public ChangeLogAdminCommands(IServiceProvider services)
        {
            _Services = services;
        }

        [RequireOwner]
        [SlashCommand("trigger", "Trigger the bot to query the change log changes.", runMode: RunMode.Async)]
        public async Task TriggerChangelogQuery()
        {
            await DeferAsync(true);

            await _Services.GetRequiredService<ChangeLogChecker>().TriggerNow();

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = "Triggered";
            });
        }
    }
}
