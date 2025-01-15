namespace ChangeLogTracker.Data
{
    public class HostedChannel
    {
        public ulong GuildId;
        public ulong ChannelId;
        public string DofusServer;

        public Dictionary<string, ulong> ProfessionMessages = new();

        /// <summary>If this isn't empty, this Discord Channel is for only updating for this specific guild.</summary>
        public string GuildURL;
    }
}
