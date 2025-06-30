using ChangeLogTracker.Core.Interfaces;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DofusNotes.Data;
using DofusNotes.LiveStatus;
using FuzzySharp;
using Microsoft.Extensions.DependencyInjection;

namespace DofusNotes.Kolo
{
    [RequireOwner]
    public class LiveStatusAdminCommands : InteractionModuleBase<InteractionContext>
    {
        private IServiceProvider _Services;

        public LiveStatusAdminCommands(IServiceProvider services)
        {
            _Services = services;
        }

        [RequireOwner]
        [SlashCommand("status_trigger", "Triggers live status tick", runMode: RunMode.Async)]
        public async Task TriggerKolo()
        {
            await DeferAsync(true);

            await _Services.GetRequiredService<LiveStatusService>().TriggerNow();

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = "Triggered";
            });
        }

        [AutocompleteCommand("dofus_server", "status_config_add", runMode: RunMode.Async)]
        public async Task AddDofusServer()
        {
            var interaction = (SocketAutocompleteInteraction)Context.Interaction;
            string input = interaction.Data.Current.Value?.ToString() ?? "";

            List<AutocompleteResult> results = new();

            if (string.IsNullOrEmpty(input))
            {
                results = LiveStatusData.ServerNames
                .Take(25)
                .Select(s => new AutocompleteResult(s, s)).ToList();
            }
            else
            {

                results = LiveStatusData.ServerNames
                    .Select(s => new
                    {
                        Name = s,
                        Score = Fuzz.PartialRatio(input, s)
                    })
                    .Where(s => s.Score > 70)
                    .OrderByDescending(s => s.Score)
                    .Take(25)
                    .Select(s => new AutocompleteResult(s.Name, s.Name)).ToList();
            }

            await interaction.RespondAsync(results);
        }

        [RequireOwner]
        [SlashCommand("status_config_add", "Add a dofus server to be mentioned by the role.", runMode: RunMode.Async)]
        public async Task AddConfig(
            [Summary("dofus_server", description: "The name of the Dofus Server.")][Autocomplete] string dofusServer,
            IRole role)
        {
            await DeferAsync(true);

            if (!LiveStatusData.ServerNames.Contains(dofusServer))
            {
                await ModifyOriginalResponseAsync(properties =>
                {
                    properties.Content = $"{dofusServer} not included in Dofus Servers";
                });
                return;
            }

            var db = _Services.GetRequiredService<IDatabase>();
            var config = await db.GetAsync<LiveStatusData>($"LiveStatusConfig/{Context.Interaction.GuildId}");
            if (config == null) config = new();
            config.ChannelId = Context.Interaction.ChannelId.Value;

            if (config.DofusUpMentionRole == null) config.DofusUpMentionRole = new();
            config.DofusUpMentionRole.TryAdd(dofusServer, role.Id);

            await db.PutAsync($"LiveStatusConfig/{Context.Interaction.GuildId}", config);

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = $"<#{Context.Interaction.ChannelId}> will mention {role.Mention} when {dofusServer} goes live.";
            });
        }

        [AutocompleteCommand("dofus_server", "status_config_remove", runMode: RunMode.Async)]
        public async Task RemoveDofusServer()
        {
            var interaction = (SocketAutocompleteInteraction)Context.Interaction;
            string input = interaction.Data.Current.Value?.ToString() ?? "";

            List<AutocompleteResult> results = new();

            var db = _Services.GetRequiredService<IDatabase>();
            var config = await db.GetAsync<LiveStatusData>($"LiveStatusConfig/{Context.Interaction.GuildId}");
            List<string> serverNames = new();
            if (config != null && config.DofusUpMentionRole != null)
            {
                serverNames = config.DofusUpMentionRole.Keys.ToList();
            }

            if (string.IsNullOrEmpty(input))
            {
                results = serverNames
                .Take(25)
                .Select(s => new AutocompleteResult(s, s)).ToList();
            }
            else
            {

                results = serverNames
                    .Select(s => new
                    {
                        Name = s,
                        Score = Fuzz.PartialRatio(input, s)
                    })
                    .Where(s => s.Score > 70)
                    .OrderByDescending(s => s.Score)
                    .Take(25)
                    .Select(s => new AutocompleteResult(s.Name, s.Name)).ToList();
            }

            await interaction.RespondAsync(results);
        }

        [RequireOwner]
        [SlashCommand("status_config_remove", "Remove an available dofus server thats been assigned to this channel.", runMode: RunMode.Async)]
        public async Task RemoveConfigs([Summary("dofus_server", description: "The name of the Dofus Server.")][Autocomplete] string dofusServer)
        {
            await DeferAsync(true);

            var db = _Services.GetRequiredService<IDatabase>();
            var config = await db.GetAsync<LiveStatusData>($"LiveStatusConfig/{Context.Interaction.GuildId}");
            if (config == null || config.DofusUpMentionRole == null)
            {
                await ModifyOriginalResponseAsync(properties =>
                {
                    properties.Content = $"No config setup for <#{Context.Interaction.ChannelId}>";
                });
                return;
            }

            if (!config.DofusUpMentionRole.Keys.Contains(dofusServer))
            {
                await ModifyOriginalResponseAsync(properties =>
                {
                    properties.Content = $"{dofusServer} not included in Dofus Servers";
                });
                return;
            }

            if (config.DofusUpMentionRole.ContainsKey(dofusServer))
            {
                config.DofusUpMentionRole.Remove(dofusServer);
            }
            await db.PutAsync($"LiveStatusConfig/{Context.Interaction.GuildId}", config);

            await ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = $"<#{Context.Interaction.ChannelId}> had all live status configs removed.";
            });
        }
    }
}
