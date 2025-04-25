namespace DofusNotes.Data
{
    public class KolossiumLadder
    {
        public enum EKolossiumPlaylist
        {
            Ones,
            Twos,
            Threes
        }

        public static string GetPlaylistParam(EKolossiumPlaylist playlist)
        {
            if(playlist == EKolossiumPlaylist.Ones) return "1V1";
            if(playlist == EKolossiumPlaylist.Twos) return "2V2";
            return "3V3";
        }

        public EKolossiumPlaylist LadderType;
        public string GetPlaylistParam() => GetPlaylistParam(LadderType);
        public List<KolossiumRanking> Rankings;

        public static string GetDatabaseUrl(EKolossiumPlaylist playlist)
        {
            var nowDateOnly = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy_MM_dd");
            return $"Ladder/{nowDateOnly}/{GetPlaylistParam(playlist)}";
        }
    }

    public class KolossiumRanking
    {
        public int Rank { get; set; }
        public string Name { get; set; }
        public string Class { get; set; }
        public string Server { get; set; }
        public int Level { get; set; }
        public int Rating { get; set; }
        public string Winrate { get; set; }
    }

}
