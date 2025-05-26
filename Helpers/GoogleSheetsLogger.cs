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
            double fileSize
        )
        {
            string sheetName = "Phase 2";
            string range = $"{sheetName}!A:K"; // Updated range to include 'File Size'
            string headerRange = $"{sheetName}!A1:K1";

            var spreadsheet = await _service.Spreadsheets.Get(_spreadsheetId).ExecuteAsync();
            var sheetExists = spreadsheet.Sheets.Any(s =>
                s.Properties.Title.Equals(sheetName, StringComparison.OrdinalIgnoreCase)
            );

            if (!sheetExists)
            {
                // Add new sheet
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

                // Column headers
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
                            "File Size (MB)", // New column
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

                var sheetId =
                    (await _service.Spreadsheets.Get(_spreadsheetId).ExecuteAsync())
                        .Sheets.First(s => s.Properties.Title == sheetName)
                        .Properties.SheetId ?? 0;

                var formatRequest = new BatchUpdateSpreadsheetRequest
                {
                    Requests = new List<Request>
                    {
                        // Format header: bold, center, black text color
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
                        // Wrap strategy = CLIP
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
                        // Freeze header row
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
                        // Border table
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
                                // Bottom = MakeSolidBorder(),
                                // Left = MakeSolidBorder(),
                                // Right = MakeSolidBorder(),
                                // InnerHorizontal = MakeSolidBorder(),
                                // InnerVertical = MakeSolidBorder(),
                            },
                        },
                        // Filter like Table1
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
                                        EndColumnIndex = 11,
                                    },
                                },
                            },
                        },
                        // Set all columns to 150px
                        new Request
                        {
                            UpdateDimensionProperties = new UpdateDimensionPropertiesRequest
                            {
                                Range = new DimensionRange
                                {
                                    SheetId = sheetId,
                                    Dimension = "COLUMNS",
                                    StartIndex = 0,
                                    EndIndex = 11,
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

            // Add log data to the next row
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
                        fileSize, // New data
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
