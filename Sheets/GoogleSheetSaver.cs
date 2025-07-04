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

        public async Task PushDataToSheetAsync(DateOnly dateOnly, List<KolossiumLadder> ladders, bool pivotUpdate, bool force = false)
        {
            var dateStr = dateOnly.ToString("yyyy/MM/dd");
            var sheetName = $"{dateStr}";

            var db = _Services.GetRequiredService<IDatabase>();

            var pushedKey = dateOnly.ToString("yyyy_MM_dd");
            var pushedSheet = await db.GetAsync<PushedStamp>($"Pushed/Sheet/{pushedKey}");
            if (pushedSheet != null && !force)
            {
#if !DEBUG
                Console.WriteLine($"Already pushed the sheet");
                return;
#endif
            }
            await db.PutAsync<PushedStamp>($"Pushed/Sheet/{pushedKey}", new() { Pushed = dateOnly });

            List<KolossiumRanking> combinedRankings = ladders.SelectMany(s => s.Rankings).ToList();

            var spreadsheet = _Sheets.Spreadsheets.Get(_SpreadSheetId).Execute();

            {
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

                    double winRate = double.Parse(ranking.Winrate.Replace("%", "")) / 100.0;

                    valueRange.Values.Add(new List<object>()
                {
                    ranking.Name,
                    ranking.Class,
                    ranking.GlobalRank,
                    ranking.ClassRank,
                    winRate,
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
            }

            // --------------------------------------------------------------
            // -------------- UPDATE PERCENT FORMAT

            {
                Console.WriteLine("Updating Percentage Format for Win Rate");

                spreadsheet = await _Sheets.Spreadsheets.Get(_SpreadSheetId).ExecuteAsync();
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

                await _Sheets.Spreadsheets.BatchUpdate(batchUpdate, _SpreadSheetId).ExecuteAsync();

            }
            // ----------------------------------------------------------------------
            // ------------ UPDATE PIVOT TABLES

            if(pivotUpdate)
            {
                Console.WriteLine("Updating Pivot Tables");

                try
                {
                    // Step 1: Load full spreadsheet with grid data
                    var spreadsheetRequest = _Sheets.Spreadsheets.Get(_SpreadSheetId);
                    spreadsheetRequest.IncludeGridData = true;
                    spreadsheetRequest.Ranges = new List<string>
                    {
                        "'Full Detail Top 100'!A1",
                        "'TOP Class per 100'!A1",
                        "'TOP 100 ALL CLASSES'!A1",
                        $"'{dateStr}'!A1"
                    };
                    spreadsheet = await spreadsheetRequest.ExecuteAsync();

                    // Step 2: Get sheet ID of the new data sheet (e.g. "2025/05/29")
                    var newDataSheet = spreadsheet.Sheets.FirstOrDefault(s => s.Properties.Title == dateStr);
                    int newDataSheetId = (int)newDataSheet.Properties.SheetId;

                    // Step 3: Sheets that contain pivot tables
                    var pivotSheetNames = new[] { "Full Detail Top 100", "TOP Class per 100", "TOP 100 ALL CLASSES" };
                    var updateRequests = new List<Request>();

                    foreach (var name in pivotSheetNames)
                    {
                        var sheet = spreadsheet.Sheets.FirstOrDefault(s => s.Properties.Title == name);
                        if (sheet == null)
                        {
                            Console.WriteLine($"Warning: Sheet \"{name}\" not found, skipping.");
                            continue;
                        }

                        int sheetId = (int)sheet.Properties.SheetId;
                        var pivotCell = sheet.Data
                            .SelectMany(d => d.RowData ?? new List<RowData>())
                            .SelectMany(r => r.Values ?? new List<CellData>())
                            .FirstOrDefault(c => c?.PivotTable != null);

                        if (pivotCell == null)
                        {
                            Console.WriteLine($"Warning: No pivot table found on \"{name}\".");
                            continue;
                        }

                        // Update the source of the pivot table
                        pivotCell.PivotTable.Source.SheetId = newDataSheetId;
                        pivotCell.PivotTable.Source.StartRowIndex = 0;
                        pivotCell.PivotTable.Source.EndRowIndex = 6000;

                        var updateRequest = new Request
                        {
                            UpdateCells = new UpdateCellsRequest
                            {
                                Start = new GridCoordinate
                                {
                                    SheetId = sheetId,
                                    RowIndex = 0,
                                    ColumnIndex = 0
                                },
                                Rows = new List<RowData>
                    {
                        new RowData
                        {
                            Values = new List<CellData> { new CellData { PivotTable = pivotCell.PivotTable } }
                        }
                    },
                                Fields = "pivotTable.source"
                            }
                        };

                        updateRequests.Add(updateRequest);
                    }

                    // Step 4: Execute batch update
                    if (updateRequests.Count > 0)
                    {
                        var batchRequest = new BatchUpdateSpreadsheetRequest { Requests = updateRequests };
                        await _Sheets.Spreadsheets.BatchUpdate(batchRequest, _SpreadSheetId).ExecuteAsync();
                        Console.WriteLine($"Pivot tables updated to new date source: '{dateStr}'!A1");
                    }
                    else
                    {
                        Console.WriteLine("No pivot tables were updated.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Something in the Pivot Table update went wrong: {ex.Message}");
                    return;
                }
            }
            else
            {
                Console.WriteLine("Pivot Table update ignored.");
            }

            Console.WriteLine("Completed sheet operations.");
        }
    }
}
