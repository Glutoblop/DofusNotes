namespace ChangeLogTracker.Data
{
    public class GuildProfessionData
    {
        public string GuildName;
        public string DofusServer;
        public string GuildURL;

        public List<PlayerProfessionData> Players;
    }

    public class PlayerProfessionData
    {
        public string CharacterURL;

        public string CharacterName;
        public string DofusServer;
        public List<ProfessionData> Professions;

        public DateTime TimeStamp;
    }

    public class ProfessionData
    {
        public string Profession;
        public int Level;
    }
}
