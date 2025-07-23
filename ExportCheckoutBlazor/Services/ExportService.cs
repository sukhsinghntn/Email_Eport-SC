using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Collections.Generic;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;

namespace NDAProcesses.Services
{
    public class ExportService
    {
        private readonly IConfiguration _config;
        private readonly string _basePath;
        private readonly string _logFile;

        public ExportService(IConfiguration config, IWebHostEnvironment env)
        {
            _config = config;
            _basePath = env.ContentRootPath;

            var logDir = Path.Combine(_basePath, "Logs");
            Directory.CreateDirectory(logDir);
            _logFile = Path.Combine(logDir, "ExportCheckoutReport.log");
        }

        private void Log(string msg)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {msg}{Environment.NewLine}";
            File.AppendAllText(_logFile, line);
        }

        /// <summary>
        /// Runs the export/email process.
        /// </summary>
        public string RunExport(int daysBack, List<string> toAddresses, List<ColumnDef> selectedCols = null)
        {
            try
            {
                Log($"Starting export for last {daysBack} days.");

                // 1) Determine columns
                var defaultSqlCols = new List<string>
                { "id", "QRCODE", "[DESC]", "QTY", "BIN", "DEPT", "USER_NAME", "checkout_time" };
                var defaultColLabels = new List<string>
                { "ID", "QR Code", "Description", "Quantity", "Loc", "Department", "User Name", "Checkout Time" };

                var sqlCols = selectedCols?.Any() == true
                    ? selectedCols.Select(c => c.NameSql).ToList()
                    : defaultSqlCols;
                var headerLabels = selectedCols?.Any() == true
                    ? selectedCols.Select(c => c.Label).ToList()
                    : defaultColLabels;

                var colList = string.Join(", ", sqlCols);
                Log($"Building SQL for columns: {colList}");

                // 2) Pull data
                var connStr = _config.GetConnectionString("ToolCribDb")
                              ?? "Server=10.201.50.70\\NDASHAFTDB;Database=NDA2_ToolCrib_DB;User Id=sa;Password=NDA_Admin;TrustServerCertificate=True;";
                var sql = $@"
SELECT {colList}
FROM dbo.Checkout_Hist
WHERE checkout_time >= DATEADD(day, -{daysBack}, GETDATE())
ORDER BY checkout_time";

                var dt = new DataTable();
                using (var conn = new SqlConnection(connStr))
                using (var cmd = new SqlCommand(sql, conn))
                {
                    conn.Open();
                    using var rdr = cmd.ExecuteReader();
                    dt.Load(rdr);
                }
                Log($"Retrieved {dt.Rows.Count} rows with {dt.Columns.Count} columns.");

                // 2a) Compute summary
                var rows = dt.Rows.Cast<DataRow>();
                var earliest = rows.Min(r => Convert.ToDateTime(r["checkout_time"]));
                var latest = rows.Max(r => Convert.ToDateTime(r["checkout_time"]));
                var totalQty = rows.Sum(r => {
                    var s = r["QTY"]?.ToString();
                    return int.TryParse(s, out var q) ? q : 0;
                });
                var uniqueUsers = rows
                    .Select(r => r["USER_NAME"]?.ToString())
                    .Where(u => !string.IsNullOrEmpty(u))
                    .Distinct().Count();
                var uniqueItems = rows
                    .Select(r => r["QRCODE"]?.ToString())
                    .Where(i => !string.IsNullOrEmpty(i))
                    .Distinct().Count();

                // 3) Build Excel
                var exportDir = Path.Combine(_basePath, "Exports");
                Directory.CreateDirectory(exportDir);
                var fileName = $"CheckoutHist_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                var filePath = Path.Combine(exportDir, fileName);

                using (var wb = new XLWorkbook())
                {
                    var startDate = DateTime.Now.Date.AddDays(-daysBack);
                    var endDate = DateTime.Now.Date;
                    var sheetName = $"{startDate:MM-dd}_to_{endDate:MM-dd}";
                    var ws = wb.Worksheets.Add(dt, sheetName);

                    for (int i = 0; i < headerLabels.Count; i++)
                        ws.Cell(1, i + 1).Value = headerLabels[i];

                    ws.Columns().AdjustToContents();
                    wb.SaveAs(filePath);
                }
                Log($"Excel saved: {filePath}");

                // 4) Email
                var smtpHost = _config["Email:IP"] ?? "10.201.50.5";
                var smtpPort = int.TryParse(_config["Email:Port"], out var p) ? p : 25;
                var fromAddr = _config["Email:From"] ?? throw new Exception("Email:From missing");
                var tos = toAddresses?.Any() == true ? toAddresses : new List<string> { _config["Email:BugReportEmailTo"] };

                Log($"Sending email to: {string.Join(", ", tos)}");

                var reportStart = DateTime.Now.Date.AddDays(-daysBack);
                var reportEnd = DateTime.Now.Date;
                var rowCount = dt.Rows.Count;

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress(fromAddr);
                    foreach (var addr in tos)
                        mail.To.Add(addr.Trim());

                    mail.Subject = $"Tool Crib Checkout Report: {reportStart:MMM d, yyyy} – {reportEnd:MMM d, yyyy}";
                    mail.IsBodyHtml = true;

                    mail.Body = $@"
                    <html>
                      <body style=""font-family: Segoe UI, sans-serif; color: #333; line-height: 1.4;"">
                        <h2 style=""color: #0055A4; margin-bottom: 0.5em;"">🧰 Tool Crib Checkout Report</h2>
                        <p>Hi Team,</p>
                        <p>Here is your automated checkout report for <strong>{reportStart:MMMM d, yyyy}</strong> through <strong>{reportEnd:MMMM d, yyyy}</strong>.</p>
                        <ul>
                          <li><strong>Total Records:</strong> {rowCount:N0}</li>
                        </ul>
                        <p>The full details are attached as an Excel file: <em>{Path.GetFileName(filePath)}</em>.</p>
                        <hr style=""border: none; border-top: 1px solid #ddd; margin: 1.5em 0;""/>
                        <p style=""font-size: 0.85em; color: #666;"">
                          This is an automated message from the Tool Crib system.<br/>
                          <a href=""https://teams.microsoft.com/l/chat/0/0?users=ssingh@ntnanderson.com""
                             target=""_blank""
                             style=""display: inline-block; padding: 8px 12px; background: #0055A4; color: #fff; text-decoration: none; border-radius: 4px;"">
                            Chat with IT in Teams
                          </a>
                        </p>
                      </body>
                    </html>";

                    mail.Attachments.Add(new Attachment(filePath));
                    using var smtp = new SmtpClient(smtpHost, smtpPort) { EnableSsl = false };
                    smtp.Send(mail);
                    Log("Email sent.");
                }

                // 5) Cleanup
                File.Delete(filePath);
                Log($"Deleted file: {filePath}");
                Log("Export completed successfully.");

                return $"Success: {dt.Rows.Count} rows sent to {string.Join(", ", tos)}.";
            }
            catch (Exception ex)
            {
                Log($"ERROR: {ex.Message}\n{ex.StackTrace}");
                return $"Error: {ex.Message}";
            }
        }
    }

    public class ColumnDef
    {
        public string NameSql { get; set; }
        public string Label { get; set; }
        public bool IsSelected { get; set; }
    }
}
