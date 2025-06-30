namespace DofusNotes
{
    public class MyHttpClient
    {
        private static HttpClient _ComplexClient;

        public static HttpClient Complex
        {
            get
            {
                if(_ComplexClient == null)
                {
                    _ComplexClient = new HttpClient();
                    _ComplexClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                                                                  "AppleWebKit/537.36 (KHTML, like Gecko) " +
                                                                  "Chrome/117.0.0.0 Safari/537.36");
                    _ComplexClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml");
                    _ComplexClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
                }
                return _ComplexClient;
            }
        }

        private static HttpClient _SimpleClient;

        public static HttpClient Simple
        {
            get
            {
                if (_SimpleClient == null)
                {
                    _SimpleClient = new HttpClient();
                }
                return _SimpleClient;
            }
        }
    }
}
