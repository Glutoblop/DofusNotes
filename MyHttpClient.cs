namespace DofusNotes
{
    public class MyHttpClient
    {
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
