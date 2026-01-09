using AI;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using Microsoft.Graph;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AI
{
    /// <summary>
    /// PUBLIC DEMO VERSION
    /// - Removes tenant/site paths, database names, and other environment-specific identifiers.
    /// - Uses configuration keys that should be mapped in each environment (local/dev/prod).
    /// - Avoids returning internal URLs to callers; returns a non-sensitive reference instead.
    /// - Avoids logging raw document text (may contain PII/contract details).
    /// </summary>
    public class ProcessCityAgenda
    {
        // SharePoint/Graph helper for document operations (provided elsewhere in your project)
        private readonly SharePointAPI spApi;

        // Azure OpenAI helper for extracting structured data (provided elsewhere in your project)
        private readonly AzureOpenAIApi azureOpenAIApi;

        // Logging helper (provided elsewhere in your project)
        private readonly LogFile logFile;

        // SQL connection string used by SaveToSqlAsync
        private readonly string sqlConnectionString;

        // Environment/config-based identifiers
        private readonly string locationId;
        private readonly string siteID;
        private readonly string libraryName;

        private ProcessCityAgenda(
            LogFile log,
            SharePointAPI spApi,
            AzureOpenAIApi azureOpenAIApi,
            string siteID,
            string libraryName,
            string sqlConnectionString,
            string locationId)
        {
            this.logFile = log;
            this.spApi = spApi;
            this.azureOpenAIApi = azureOpenAIApi;
            this.siteID = siteID;
            this.libraryName = libraryName;
            this.sqlConnectionString = sqlConnectionString;
            this.locationId = locationId;
        }

        /// <summary>
        /// Create a fully initialized processor using config keys.
        /// Required appSettings keys (example):
        /// - DB_SOURCE, DB_USER, DB_PASSWORD, DB_NAME
        /// - SP_TENANT_DOMAIN (e.g., "contoso.sharepoint.com")
        /// - SP_SITE_PATH_<LOCATION_ID> (e.g., "/sites/DemoSite")
        /// - SP_LIBRARY_<LOCATION_ID> (e.g., "Documents")
        /// </summary>
        public static async Task<ProcessCityAgenda> CreateAsync(LogFile log, string locationId)
        {
            var spApi = new SharePointAPI(log);
            var azureOpenAIApi = new AzureOpenAIApi(
                log,
                "You are a helpful assistant that extracts structured project data from city council documents.",
                Convert.ToDecimal(0.2)
            );

            // Build SQL connection string from app settings (do not hard-code environment identifiers)
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
            {
                DataSource = ConfigurationManager.AppSettings["DB_SOURCE"],
                UserID = ConfigurationManager.AppSettings["DB_USER"],
                Password = ConfigurationManager.AppSettings["DB_PASSWORD"],
                InitialCatalog = ConfigurationManager.AppSettings["DB_NAME"]
            };

            // Resolve SharePoint site + library from config (no tenant/site names in code)
            var tenantDomain = ConfigurationManager.AppSettings["SP_TENANT_DOMAIN"];
            var sitePathKey = $"SP_SITE_PATH_{locationId}";
            var libraryKey = $"SP_LIBRARY_{locationId}";

            var sitePath = ConfigurationManager.AppSettings[sitePathKey];
            var libraryName = ConfigurationManager.AppSettings[libraryKey];

            if (string.IsNullOrWhiteSpace(tenantDomain) || string.IsNullOrWhiteSpace(sitePath) || string.IsNullOrWhiteSpace(libraryName))
                throw new ArgumentException($"Missing SharePoint configuration. Expected keys: SP_TENANT_DOMAIN, {sitePathKey}, {libraryKey}");

            // SiteID resolved at runtime for the configured path
            var siteID = await spApi.GetSiteIdByPathAsync(sitePath, tenantDomain);

            return new ProcessCityAgenda(log, spApi, azureOpenAIApi, siteID, libraryName, builder.ConnectionString, locationId);
        }

        /// <summary>
        /// Main entry: process one SharePoint file by its item ID.
        /// </summary>
        public async Task<ProcessCityAgendaResponse> ProcessAgendaAsync(int sharePointItemId)
        {
            logFile.MyLogFile($"Starting City Agenda processing for SharePoint item ID {sharePointItemId}");

            try
            {
                // STEP 1: Locate the SharePoint document
                var documents = await spApi.GetDocumentAsync(siteID, libraryName, sharePointItemId.ToString());
                if (documents == null || !documents.Any())
                {
                    return new ProcessCityAgendaResponse
                    {
                        Success = false,
                        Message = $"No documents found for sharePointItemId {sharePointItemId}."
                    };
                }

                // Use the first document found using the provided item ID
                var driveItem = documents.First().DriveItem;
                string fileName = driveItem.Name;
                string cityName = ExtractCityNameFromFileName(fileName);

                // Avoid logging internal IDs/URLs in demo output
                string driveId = driveItem.ParentReference?.DriveId;
                string driveItemId = driveItem.Id;

                Stream fileStream = await spApi.GetFileStreamByDriveItemIdAsync(driveId, driveItemId);
                if (fileStream == null)
                {
                    return new ProcessCityAgendaResponse
                    {
                        Success = false,
                        Message = $"Could not get stream for file {fileName}"
                    };
                }

                // STEP 2: Extract text (true-text PDFs)
                string fileText = await ExtractTextFromPdfAsync(fileStream);
                fileStream.Dispose();

                if (string.IsNullOrEmpty(fileText))
                {
                    return new ProcessCityAgendaResponse
                    {
                        Success = false,
                        Message = $"Failed to extract text from file {fileName}"
                    };
                }

                // IMPORTANT: fileText may contain sensitive information.
                // Do NOT log fileText. Optionally, apply redaction before sending to an LLM.
                string redactedText = RedactPotentialPII(fileText);

                // STEP 2b: Use LLM to structure project data from the text
                var projects = await ExtractProjectDataAsync(redactedText);
                if (projects == null || !projects.Any())
                {
                    return new ProcessCityAgendaResponse
                    {
                        Success = false,
                        Message = "Failed to extract project data from document text"
                    };
                }

                // Set Region and Discipline based on locationId using config (demo-friendly)
                string region = ConfigurationManager.AppSettings[$"REGION_{locationId}"] ?? "";
                string discipline = ConfigurationManager.AppSettings[$"DISCIPLINE_{locationId}"] ?? "";

                foreach (var project in projects)
                {
                    project.Region = region;
                    project.Discipline = discipline;
                }

                // STEP 3: Persist results to SQL
                // Demo note: consider disabling DB writes in public demos, or writing to a demo database.
                bool saveSuccess = await SaveToSqlAsync(cityName, projects, driveItemId /* non-sensitive reference */);
                if (!saveSuccess)
                {
                    return new ProcessCityAgendaResponse
                    {
                        Success = false,
                        Message = "Failed to save project data to database"
                    };
                }

                // STEP 4: Return response (do not return internal URLs)
                return new ProcessCityAgendaResponse
                {
                    Success = true,
                    Message = "Successfully processed city agenda file",
                    Projects = projects,
                    City = cityName,
                    FileReference = driveItemId
                };
            }
            catch (Exception ex)
            {
                // Avoid leaking stack traces or environment info in demo responses
                logFile.MyLogFile($"Error processing agenda: {ex.Message}");
                return new ProcessCityAgendaResponse
                {
                    Success = false,
                    Message = "Error processing agenda."
                };
            }
        }

        // Pull the city name before the first underscore (e.g., "Allen_10-15-2025.pdf" -> "Allen")
        private string ExtractCityNameFromFileName(string fileName)
        {
            var match = Regex.Match(fileName, @"^([^_]+)_");
            if (match.Success && match.Groups.Count > 1)
                return match.Groups[1].Value.Trim();

            int underscoreIndex = fileName.IndexOf('_');
            if (underscoreIndex > 0)
                return fileName.Substring(0, underscoreIndex).Trim();

            return string.Empty;
        }

        /// <summary>
        /// Extract all text from a PDF (true-text).
        /// NOTE: OCR fallback omitted in public demo version.
        /// </summary>
        private async Task<string> ExtractTextFromPdfAsync(Stream pdfStream)
        {
            try
            {
                // Copy the original input stream once so we can create fresh streams for each consumer.
                byte[] pdfBytes;
                using (var buffer = new MemoryStream())
                {
                    await pdfStream.CopyToAsync(buffer);
                    pdfBytes = buffer.ToArray();
                }

                var extractedText = new StringBuilder();

                using (var reader = new PdfReader(pdfBytes))
                {
                    for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
                    {
                        var strategy = new SimpleTextExtractionStrategy();
                        string pageText = PdfTextExtractor.GetTextFromPage(reader, pageNum, strategy);
                        if (!string.IsNullOrWhiteSpace(pageText))
                            extractedText.AppendLine(pageText);
                    }
                }

                return extractedText.ToString();
            }
            catch (Exception ex)
            {
                logFile.MyLogFile($"Error extracting text from PDF: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Basic redaction for demo safety. Expand as needed.
        /// </summary>
        private string RedactPotentialPII(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Email addresses
            text = Regex.Replace(text, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", "[REDACTED_EMAIL]", RegexOptions.IgnoreCase);

            // US phone-like patterns
            text = Regex.Replace(text, @"(\+?1[\s\-\.]?)?(\(?\d{3}\)?[\s\-\.]?)\d{3}[\s\-\.]?\d{4}", "[REDACTED_PHONE]");

            // SSN-like patterns
            text = Regex.Replace(text, @"\b\d{3}-\d{2}-\d{4}\b", "[REDACTED_SSN]");

            return text;
        }

        // LLM prompt + parse into strongly-typed list
        private async Task<List<ProjectData>> ExtractProjectDataAsync(string documentText)
        {
            try
            {
                // Keep prompts generic (no company, tenant, or client/vendor-specific names)
                string prompt = $@"
You are a helpful assistant that extracts structured civil/transportation project data from city council documents.

Extract ONLY projects that involve professional engineering design or review services and appear UNDER the CONSENT AGENDA or CONSENT RESOLUTION sections ONLY.
Do NOT extract projects under REGULAR AGENDA or REGULAR AGENDA ITEMS.

Include items that mention professional engineering services, design services, review services, authorizations to execute amendments/agreements, or resolutions/ordinances for engineering services.

Exclude items that mention construction/change orders, materials testing, geotechnical services, landscape architecture, real estate/leasing, purchase orders/bids, procurement of materials/equipment, or non-engineering vendors.

For each qualifying project, extract:
- Date (YYYY-MM-DD)
- Consultant (company name)
- Amount (numeric value only; if absent, use 0)
- Project Name (official title)
- Project Category (array of one or more values)

Allowed categories:
['Bridge or Structure','Drainage','Roadway','Traffic','Utility','Replacement','Asset Management','Railroad',
 'Drainage/Stormwater Management','Water Line','Street Improvement','Lighting or Signals','Utility Coordination',
 'Wastewater Sewer','Wastewater Treatment Plant','Traffic Report','Permit (Railroad)','Permit (Wastewater)','Permit (Other)']

Respond in this JSON array format ONLY:
[
  {{
    ""date"": ""YYYY-MM-DD"",
    ""consultant"": ""Consultant Name"",
    ""amount"": 123456.78,
    ""project_name"": ""Project Title"",
    ""category"": [""Roadway"", ""Traffic""]
  }}
]

Here is the text:
{documentText}
";

                var response = await azureOpenAIApi.GetCompletionAsync(prompt, 2000);
                if (!response.Success)
                {
                    logFile.MyLogFile($"LLM error: {response.ErrorMessage}");
                    return null;
                }

                string jsonContent = response.Content.Trim();

                // If the model wrapped the JSON, extract the array
                if (!(jsonContent.StartsWith("[") && jsonContent.EndsWith("]")))
                {
                    var arrayMatch = Regex.Match(jsonContent, @"\[(.|\s)*\]", RegexOptions.Singleline);
                    if (arrayMatch.Success)
                    {
                        jsonContent = arrayMatch.Value;
                    }
                    else
                    {
                        logFile.MyLogFile("Failed to extract JSON array from LLM response.");
                        return null;
                    }
                }

                var projectDataList = JsonConvert.DeserializeObject<List<ProjectData>>(jsonContent);
                return projectDataList;
            }
            catch (Exception ex)
            {
                logFile.MyLogFile($"Error extracting project data: {ex.Message}");
                return null;
            }
        }

        // Persist extracted rows to SQL
        private async Task<bool> SaveToSqlAsync(string city, List<ProjectData> projects, string fileReference)
        {
            try
            {
                using (var connection = new SqlConnection(sqlConnectionString))
                {
                    await connection.OpenAsync();
                    int totalRows = 0;

                    foreach (var project in projects)
                    {
                        // Demo-friendly table name (configure if needed)
                        string query = @"
INSERT INTO dbo.AgendaInsights
(City, Project_Name, Consultant, Amount, Date, File_Reference, Category, Region, Discipline)
VALUES
(@City, @Project_Name, @Consultant, @Amount, @Date, @File_Reference, @Category, @Region, @Discipline)";

                        using (var command = new SqlCommand(query, connection))
                        {
                            command.Parameters.AddWithValue("@City", city);
                            command.Parameters.AddWithValue("@Project_Name", project.ProjectName);
                            command.Parameters.AddWithValue("@Consultant", project.Consultant);
                            command.Parameters.AddWithValue("@Amount", project.Amount);
                            command.Parameters.AddWithValue("@Date", DateTime.Parse(project.Date));
                            command.Parameters.AddWithValue("@File_Reference", fileReference);

                            string categoryString = project.Category != null ? string.Join(", ", project.Category) : "";
                            command.Parameters.AddWithValue("@Category", categoryString);
                            command.Parameters.AddWithValue("@Region", project.Region ?? "");
                            command.Parameters.AddWithValue("@Discipline", project.Discipline ?? "");

                            int rowsAffected = await command.ExecuteNonQueryAsync();
                            totalRows += rowsAffected;
                        }
                    }

                    return totalRows == projects.Count;
                }
            }
            catch (Exception ex)
            {
                logFile.MyLogFile($"Database error: {ex.Message}");
                return false;
            }
        }
    }

    // Data model for one extracted project row
    public class ProjectData
    {
        [JsonProperty("date")]
        public string Date { get; set; }

        [JsonProperty("consultant")]
        public string Consultant { get; set; }

        [JsonProperty("amount")]
        public decimal Amount { get; set; }

        [JsonProperty("project_name")]
        public string ProjectName { get; set; }

        [JsonProperty("category")]
        public List<string> Category { get; set; }

        public string Region { get; set; }
        public string Discipline { get; set; }
    }

    // Response wrapper returned to callers
    public class ProcessCityAgendaResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<ProjectData> Projects { get; set; }
        public string City { get; set; }

        // Public demo: avoid returning internal SharePoint URLs
        public string FileReference { get; set; }
    }
}
