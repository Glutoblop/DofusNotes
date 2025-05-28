using Newtonsoft.Json;

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
            if (playlist == EKolossiumPlaylist.Ones) return "1V1";
            if (playlist == EKolossiumPlaylist.Twos) return "2V2";
            return "3V3";
        }

        public EKolossiumPlaylist LadderType;
        public string GetPlaylistParam() => GetPlaylistParam(LadderType);
        public List<KolossiumRanking> Rankings;

        public static string GetDatabaseUrl(DateOnly dateTime, EKolossiumPlaylist playlist, int breed = -1)
        {
            var nowDateOnly = dateTime.ToString("yyyy_MM_dd");
            return $"Ladder/{nowDateOnly}/{GetPlaylistParam(playlist)}/{breed}";
        }

        public static EKolossiumPlaylist ToKoloPlaylist(string key)
        {
            switch (key.ToLowerInvariant())
            {
                case "1v1": return EKolossiumPlaylist.Ones;
                case "2v2": return EKolossiumPlaylist.Twos;
            }
            return EKolossiumPlaylist.Threes;
        }
    }

    public class KolossiumRanking
    {
        public int Rank { get; set; } = 0;

        public int GlobalRank { get; set; } = 0;
        public int ClassRank { get; set; } = 0;


        public string Name { get; set; }
        public string Class { get; set; }
        public string Server { get; set; }
        public int Level { get; set; }
        public int Rating { get; set; }
        public string Winrate { get; set; }

        [JsonConverter(typeof(DateOnlyJsonConverter))]
        public DateOnly DayStamp { get; set; }
        public string Playlist { get; set; }
    }

}
