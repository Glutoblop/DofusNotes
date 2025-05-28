using ChangeLogTracker.Core.Interfaces;
using DofusNotes.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.DependencyInjection;
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

        public async Task PushDataToSheetAsync(DateOnly dateOnly, List<KolossiumLadder> ladders)
        {
            var dateStr = dateOnly.ToString("yyyy/MM/dd");
            var sheetName = $"{dateStr}";

            var db = _Services.GetRequiredService<IDatabase>();

            var pushedKey = dateOnly.ToString("yyyy_MM_dd");
            var pushedSheet = await db.GetAsync<PushedStamp>($"Pushed/Sheet/{pushedKey}");
            if (pushedSheet != null)
            {
#if !DEBUG
                Console.WriteLine($"Already pushed the sheet");
                return;
#endif
            }
            await db.PutAsync<PushedStamp>($"Pushed/Sheet/{pushedKey}", new() { Pushed = dateOnly });

            List<KolossiumRanking> combinedRankings = ladders.SelectMany(s => s.Rankings).ToList();

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

            await _Sheets.Spreadsheets.Values.Clear(new ClearValuesRequest(), _SpreadSheetId, $"{sheetName}!A1:X100").ExecuteAsync();

            // Prepare data to push
            var range = $"{sheetName}!A1"; // top-left cell
            var valueRange = new ValueRange
            {
                Values = new List<IList<object>>
                {
                    new List<object> { "Name", "Class", "Global Rating", "Class Rating", "Win Rate %", "Server", "Playlist", "Rating", "Level", "Date" },
                }
            };

            for (int i = 0; i < combinedRankings.Count; i++)
            {
                KolossiumRanking? ranking = combinedRankings[i];
                valueRange.Values.Add(new List<object>()
                {
                    ranking.Name,
                    ranking.Class,
                    ranking.GlobalRank,
                    ranking.ClassRank,
                    ranking.Winrate,
                    ranking.Server,
                    ranking.Playlist,
                    ranking.Rating,
                    ranking.Level,
                    ranking.DayStamp.ToString("yyyy/MM/dd")
                });
            }

            // Push data
            var updateRequest = _Sheets.Spreadsheets.Values.Update(valueRange, _SpreadSheetId, range);
            updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
            await updateRequest.ExecuteAsync();

            spreadsheet = _Sheets.Spreadsheets.Get(_SpreadSheetId).Execute();
            var sheet = spreadsheet.Sheets.FirstOrDefault(s => s.Properties.Title == sheetName);
            int sheetId = (int)sheet.Properties.SheetId;

            var formatRequest = new Request
            {
                RepeatCell = new RepeatCellRequest
                {
                    Range = new GridRange
                    {
                        SheetId = sheetId,
                        StartRowIndex = 1,
                        StartColumnIndex = 4,  
                        EndColumnIndex = 5                                               
                    },
                    Cell = new CellData
                    {
                        UserEnteredFormat = new CellFormat
                        {
                            NumberFormat = new NumberFormat
                            {
                                Type = "PERCENT",
                                Pattern = "0.00%" // or "0%" if you want whole numbers
                            }
                        }
                    },
                    Fields = "userEnteredFormat.numberFormat"
                }
            };

            var batchUpdate = new BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Request> { formatRequest }
            };

            _Sheets.Spreadsheets.BatchUpdate(batchUpdate, _SpreadSheetId).Execute();
        }
    }
}
