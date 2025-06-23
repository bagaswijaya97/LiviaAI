using System.Globalization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace LiviaAI.Helpers
{
    public class GoogleSheetsLogger
    {
        private readonly SheetsService _service;
        private readonly string _spreadsheetId;

        public GoogleSheetsLogger(string credentialPath, string spreadsheetId)
        {
            var credential = GoogleCredential
                .FromFile(credentialPath)
                .CreateScoped(SheetsService.Scope.Spreadsheets);

            _service = new SheetsService(
                new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "LiviaAI Logger",
                }
            );

            _spreadsheetId = spreadsheetId;
        }

        public async Task LogAsync(
            string type,
            string prompt,
            string html,
            int personaToken,
            int inputTokenText,
            int inputTokenImage,
            int outputToken,
            int totalToken,
            double fileSize,
            string model
        )
        {
            string sheetName = "Phase 2";
            string range = $"{sheetName}!A:L";
            string headerRange = $"{sheetName}!A1:L1";

            var spreadsheet = await _service.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
            var sheet = spreadsheet.Sheets.FirstOrDefault(s =>
                s.Properties.Title.Equals(sheetName, StringComparison.OrdinalIgnoreCase)
            );

            int sheetId;

            if (sheet == null)
            {
                // Create new sheet
                await _service
                    .Spreadsheets.BatchUpdate(
                        new BatchUpdateSpreadsheetRequest
                        {
                            Requests = new List<Request>
                            {
                                new Request
                                {
                                    AddSheet = new AddSheetRequest
                                    {
                                        Properties = new SheetProperties { Title = sheetName },
                                    },
                                },
                            },
                        },
                        _spreadsheetId
                    )
                    .ExecuteAsync();

                // Update headers
                var headers = new ValueRange
                {
                    Values = new List<IList<object>>
                    {
                        new List<object>
                        {
                            "Date",
                            "Time",
                            "Type",
                            "Prompt",
                            "HTML Response",
                            "Persona Token",
                            "Input Text Token",
                            "Input Image Token",
                            "Output Token",
                            "Total Tokens",
                            "File Size (MB)",
                            "Model",
                        },
                    },
                };

                var updateRequest = _service.Spreadsheets.Values.Update(
                    headers,
                    _spreadsheetId,
                    headerRange
                );
                updateRequest.ValueInputOption = SpreadsheetsResource
                    .ValuesResource
                    .UpdateRequest
                    .ValueInputOptionEnum
                    .RAW;
                await updateRequest.ExecuteAsync();

                // Get sheet ID after creation
                sheet = (
                    await _service.Spreadsheets.Get(_spreadsheetId).ExecuteAsync()
                ).Sheets.First(s => s.Properties.Title == sheetName);

                sheetId = sheet.Properties.SheetId ?? 0;

                // Format sheet
                var formatRequest = new BatchUpdateSpreadsheetRequest
                {
                    Requests = new List<Request>
                    {
                        new Request
                        {
                            RepeatCell = new RepeatCellRequest
                            {
                                Range = new GridRange
                                {
                                    SheetId = sheetId,
                                    StartRowIndex = 0,
                                    EndRowIndex = 1,
                                },
                                Cell = new CellData
                                {
                                    UserEnteredFormat = new CellFormat
                                    {
                                        TextFormat = new TextFormat
                                        {
                                            Bold = true,
                                            ForegroundColor = new Color
                                            {
                                                Red = 0,
                                                Green = 0,
                                                Blue = 0,
                                            },
                                        },
                                        HorizontalAlignment = "CENTER",
                                    },
                                },
                                Fields = "userEnteredFormat(textFormat,horizontalAlignment)",
                            },
                        },
                        new Request
                        {
                            RepeatCell = new RepeatCellRequest
                            {
                                Range = new GridRange
                                {
                                    SheetId = sheetId,
                                    StartColumnIndex = 0,
                                    EndColumnIndex = 10,
                                },
                                Cell = new CellData
                                {
                                    UserEnteredFormat = new CellFormat { WrapStrategy = "CLIP" },
                                },
                                Fields = "userEnteredFormat.wrapStrategy",
                            },
                        },
                        new Request
                        {
                            UpdateSheetProperties = new UpdateSheetPropertiesRequest
                            {
                                Properties = new SheetProperties
                                {
                                    SheetId = sheetId,
                                    GridProperties = new GridProperties { FrozenRowCount = 1 },
                                },
                                Fields = "gridProperties.frozenRowCount",
                            },
                        },
                        new Request
                        {
                            UpdateBorders = new UpdateBordersRequest
                            {
                                Range = new GridRange
                                {
                                    SheetId = sheetId,
                                    StartRowIndex = 0,
                                    EndRowIndex = 1,
                                    StartColumnIndex = 0,
                                    EndColumnIndex = 10,
                                },
                                Top = MakeSolidBorder(),
                            },
                        },
                        new Request
                        {
                            SetBasicFilter = new SetBasicFilterRequest
                            {
                                Filter = new BasicFilter
                                {
                                    Range = new GridRange
                                    {
                                        SheetId = sheetId,
                                        StartRowIndex = 0,
                                        EndRowIndex = 1,
                                        StartColumnIndex = 0,
                                        EndColumnIndex = 12,
                                    },
                                },
                            },
                        },
                        new Request
                        {
                            SortRange = new SortRangeRequest
                            {
                                Range = new GridRange
                                {
                                    SheetId = sheetId,
                                    StartRowIndex = 1,
                                    StartColumnIndex = 0,
                                    EndColumnIndex = 12,
                                },
                                SortSpecs = new List<SortSpec>
                                {
                                    new SortSpec { DimensionIndex = 0, SortOrder = "DESCENDING" }, // Date
                                    new SortSpec { DimensionIndex = 1, SortOrder = "DESCENDING" }, // Time
                                },
                            },
                        },
                        new Request
                        {
                            UpdateDimensionProperties = new UpdateDimensionPropertiesRequest
                            {
                                Range = new DimensionRange
                                {
                                    SheetId = sheetId,
                                    Dimension = "COLUMNS",
                                    StartIndex = 0,
                                    EndIndex = 10,
                                },
                                Properties = new DimensionProperties { PixelSize = 140 },
                                Fields = "pixelSize",
                            },
                        },
                    },
                };

                await _service
                    .Spreadsheets.BatchUpdate(formatRequest, _spreadsheetId)
                    .ExecuteAsync();
            }
            else
            {
                sheetId = sheet.Properties.SheetId ?? 0;
                
                // Check if headers exist
                var headerCheck = await _service.Spreadsheets.Values.Get(_spreadsheetId, headerRange).ExecuteAsync();
                if (headerCheck.Values == null || headerCheck.Values.Count == 0 || headerCheck.Values[0].Count == 0)
                {
                    // Add headers if they don't exist
                    var headers = new ValueRange
                    {
                        Values = new List<IList<object>>
                        {
                            new List<object>
                            {
                                "Date",
                                "Time",
                                "Type",
                                "Prompt",
                                "HTML Response",
                                "Persona Token",
                                "Input Text Token",
                                "Input Image Token",
                                "Output Token",
                                "Total Tokens",
                                "File Size (MB)",
                                "Model",
                            },
                        },
                    };

                    var updateRequest = _service.Spreadsheets.Values.Update(
                        headers,
                        _spreadsheetId,
                        headerRange
                    );
                    updateRequest.ValueInputOption = SpreadsheetsResource
                        .ValuesResource
                        .UpdateRequest
                        .ValueInputOptionEnum
                        .RAW;
                    await updateRequest.ExecuteAsync();
                }
            }

            // Append data
            var values = new ValueRange
            {
                Values = new List<IList<object>>
                {
                    new List<object>
                    {
                        DateTime.Now.ToString("yyyy-MM-dd"),
                        DateTime.Now.ToString("HH:mm:ss"),
                        type,
                        prompt,
                        html,
                        personaToken,
                        inputTokenText,
                        inputTokenImage,
                        outputToken,
                        totalToken,
                        fileSize,
                        model,
                    },
                },
            };

            var appendRequest = _service.Spreadsheets.Values.Append(values, _spreadsheetId, range);
            appendRequest.ValueInputOption = SpreadsheetsResource
                .ValuesResource
                .AppendRequest
                .ValueInputOptionEnum
                .RAW;
            await appendRequest.ExecuteAsync();

            // Force sorting after appending
            var sortRequest = new BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Request>
                {
                    new Request
                    {
                        SortRange = new SortRangeRequest
                        {
                            Range = new GridRange
                            {
                                SheetId = sheetId,
                                StartRowIndex = 1,
                                StartColumnIndex = 0,
                                EndColumnIndex = 12,
                            },
                            SortSpecs = new List<SortSpec>
                            {
                                new SortSpec { DimensionIndex = 0, SortOrder = "DESCENDING" }, // Date
                                new SortSpec { DimensionIndex = 1, SortOrder = "DESCENDING" }, // Time
                            },
                        },
                    },
                },
            };

            await _service.Spreadsheets.BatchUpdate(sortRequest, _spreadsheetId).ExecuteAsync();
        }

        private static Border MakeSolidBorder()
        {
            return new Border
            {
                Style = "SOLID",
                Width = 1,
                ColorStyle = new ColorStyle
                {
                    RgbColor = new Color
                    {
                        Red = 0,
                        Green = 0,
                        Blue = 0,
                    },
                },
            };
        }
    }
}
