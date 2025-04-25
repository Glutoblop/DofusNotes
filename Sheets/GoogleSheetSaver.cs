using DofusNotes.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using System.Text;

namespace DofusNotes.Sheets
{
    public class GoogleSheetSaver
    {
        private IServiceProvider _Services;
        private string _SpreadSheetId;
        private SheetsService _Sheets;

        public GoogleSheetSaver(IServiceProvider services)
        {
            _Services = services;
        }

        public void SetAuth(string serviceAccount, string sheetId)
        {
            _SpreadSheetId = sheetId;

            byte[] serviceAccountBytes = Encoding.ASCII.GetBytes(serviceAccount);
            GoogleCredential credential = GoogleCredential.FromServiceAccountCredential(ServiceAccountCredential.FromServiceAccountData(new MemoryStream(serviceAccountBytes)));
            _Sheets = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Kolossium Leaderboards"
            });
        }

        public async Task PushDataToSheetAsync(KolossiumLadder ladder)
        {
            var dateStr = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy_MM_dd");
            var sheetName = $"{ladder.GetPlaylistParam()} {dateStr}";

            var spreadsheet = _Sheets.Spreadsheets.Get(_SpreadSheetId).Execute();
            bool sheetExists = spreadsheet.Sheets.Any(s => s.Properties.Title == sheetName);

            if (!sheetExists)
            {
                // Add the sheet
                var addSheetRequest = new Request
                {
                    AddSheet = new AddSheetRequest
                    {
                        Properties = new SheetProperties { Title = sheetName }
                    }
                };

                var batchUpdateRequest = new BatchUpdateSpreadsheetRequest
                {
                    Requests = new List<Request> { addSheetRequest }
                };

                _Sheets.Spreadsheets.BatchUpdate(batchUpdateRequest, _SpreadSheetId).Execute();
            }

            await _Sheets.Spreadsheets.Values.Clear(new ClearValuesRequest(), _SpreadSheetId, $"{sheetName}!A1:E100").ExecuteAsync();

            // Prepare data to push
            var range = $"{sheetName}!A1"; // top-left cell
            var valueRange = new ValueRange
            {
                Values = new List<IList<object>>
                {
                    new List<object> { "Place", "Name", "Class", "Rating", "Win Rate %", "Server" },
                }
            };

            for (int i = 0; i < ladder.Rankings.Count; i++)
            {
                KolossiumRanking? ranking = ladder.Rankings[i];
                valueRange.Values.Add(new List<object>()
                {
                    ranking.Rank,
                    ranking.Name, 
                    ranking.Class, 
                    ranking.Rating,
                    ranking.Winrate,
                    ranking.Server,
                });
            }

            // Push data
            var updateRequest = _Sheets.Spreadsheets.Values.Update(valueRange, _SpreadSheetId, range);
            updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
            await updateRequest.ExecuteAsync();
        }
    }
}
