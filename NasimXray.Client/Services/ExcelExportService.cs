using NasimXray.Shared;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using ClosedXML.Excel;
using Microsoft.Win32;
using NasimXray.Client.Helpers;
using NasimXray.Shared.Dtos.Billing;
using NasimXray.Shared.Dtos.ExamOrders;
using NasimXray.Shared.Dtos.Stats;

namespace NasimXray.Client.Services;

// Builds .xlsx files for the exams register and the statistics report. Text follows the active
// language and calendar (dates are written as text in the calendar the user is looking at);
// numbers and money are real numeric cells so Excel can sum and chart them.
public static class ExcelExportService
{
    private static readonly string MoneyFormat = $"#,##0.00 \"{Currency.Code}\"";

    private static string Res(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    // Returns the chosen path, or null if the user cancelled.
    public static string? PromptPath(Window? owner, string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = Res("Stats.Export"),
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = suggestedName + ".xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx",
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public static void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    // ── Exams register ────────────────────────────────────────────────────

    public static void SaveExamRegister(string path, IReadOnlyList<ExamOrderDto> rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(SheetName(Res("Exams.Title")));
        ApplyDirection(ws);

        WriteTable(ws, 1,
            [Res("Exams.Number"), Res("Exams.DateTime"), Res("Exams.Patient"), Res("Exams.PatientId"), Res("Exams.ExamType"),
             Res("Exams.Modality"), Res("Exams.Referring"), Res("Exams.Status"), Res("Exams.PerformedBy"),
             Res("Exams.Billing"), Res("Exams.CancelReason"), Res("Exams.ListPrice"), Res("Exams.Discount"),
             Res("Exams.Charge"), Res("Exams.DiscountReason"), Res("Exams.DiscountedBy")],
            rows.Select(r => new object?[]
            {
                r.ExamNumber,
                CalendarFormat.Format(r.CreatedAt, "datetime"),
                r.PatientName,
                r.PatientCode,
                r.ExamTypeName,
                r.Modality,
                r.ReferringPhysicianName,
                Res($"Status.{r.Status}"),
                r.PerformedByName,
                r.InvoiceStatus is { } s ? Res($"InvoiceStatus.{s}") : Res("Exams.NotInvoiced"),
                r.CancelReason,
                r.Price,
                r.DiscountAmount,
                r.NetPrice,
                r.DiscountReason,
                r.DiscountedByName,
            }));
        ws.Range(2, 12, rows.Count + 1, 14).Style.NumberFormat.Format = MoneyFormat;

        ws.SheetView.FreezeRows(1);
        workbook.SaveAs(path);
    }

    // ── Invoices ──────────────────────────────────────────────────────────

    // Sheet 1: one row per invoice with a totals row; sheet 2: every individual payment (for cash reconciliation).
    public static void SaveInvoices(string path, IReadOnlyList<InvoiceDto> invoices)
    {
        using var workbook = new XLWorkbook();

        var sheet = workbook.Worksheets.Add(SheetName(Res("Invoices.Title")));
        ApplyDirection(sheet);
        WriteTable(sheet, 1,
            [Res("Invoices.Number"), Res("Invoices.Date"), Res("Invoices.Patient"), Res("Exams.PatientId"), Res("Stats.Col.Exams"),
             Res("Invoices.ListTotal"), Res("Exams.Discount"), Res("Invoices.Total"), Res("Invoices.Paid"), Res("Invoices.Balance"), Res("Invoices.Status")],
            invoices.Select(i => new object?[]
            {
                i.Id,
                CalendarFormat.Format(i.CreatedAt, "datetime"),
                i.PatientName,
                i.PatientCode,
                string.Join("; ", i.Lines.Select(l => string.IsNullOrEmpty(l.ExamNumber) ? l.ExamTypeName : $"{l.ExamNumber} {l.ExamTypeName}")),
                i.Lines.Sum(l => l.ListPrice),
                i.Lines.Sum(l => l.DiscountAmount),
                i.Total,
                i.AmountPaid,
                i.Balance,
                Res($"InvoiceStatus.{i.Status}"),
            }));

        var last = invoices.Count + 1;
        var totalRow = last + 1;
        sheet.Cell(totalRow, 1).Value = Res("Stats.Col.Total");
        // Static values (not formulas) so previewers that don't recalculate still show the totals.
        sheet.Cell(totalRow, 6).Value = invoices.Sum(i => i.Lines.Sum(l => l.ListPrice));
        sheet.Cell(totalRow, 7).Value = invoices.Sum(i => i.Lines.Sum(l => l.DiscountAmount));
        sheet.Cell(totalRow, 8).Value = invoices.Sum(i => i.Total);
        sheet.Cell(totalRow, 9).Value = invoices.Sum(i => i.AmountPaid);
        sheet.Cell(totalRow, 10).Value = invoices.Sum(i => i.Balance);
        var totals = sheet.Range(totalRow, 1, totalRow, 11);
        totals.Style.Font.Bold = true;
        totals.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        totals.Style.Fill.BackgroundColor = XLColor.FromHtml("#E9F1F4");
        sheet.Range(2, 6, totalRow, 10).Style.NumberFormat.Format = MoneyFormat;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, Math.Min(totalRow, 200), 8, 60);

        var payments = workbook.Worksheets.Add(SheetName(Res("InvoiceDetail.Payments")));
        ApplyDirection(payments);
        var rows = invoices
            .SelectMany(i => i.Payments.Select(p => (Invoice: i, Payment: p)))
            .OrderByDescending(x => x.Payment.Timestamp)
            .ToList();
        WriteTable(payments, 1,
            [Res("Invoices.Number"), Res("Invoices.Date"), Res("InvoiceDetail.Method"), Res("RecordPayment.Amount"), Res("InvoiceDetail.ReceivedBy"),
             Res("Invoices.Patient"), Res("Exams.PatientId")],
            rows.Select(x => new object?[]
            {
                x.Invoice.Id,
                CalendarFormat.Format(x.Payment.Timestamp, "datetime"),
                Res($"PaymentMethod.{x.Payment.Method}"),
                x.Payment.Amount,
                x.Payment.ReceivedByName,
                x.Invoice.PatientName,
                x.Invoice.PatientCode,
            }));
        var payTotalRow = rows.Count + 2;
        payments.Cell(payTotalRow, 1).Value = Res("Stats.Col.Total");
        payments.Cell(payTotalRow, 4).Value = rows.Sum(x => x.Payment.Amount);
        var payTotals = payments.Range(payTotalRow, 1, payTotalRow, 7);
        payTotals.Style.Font.Bold = true;
        payTotals.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        payTotals.Style.Fill.BackgroundColor = XLColor.FromHtml("#E9F1F4");
        payments.Range(2, 4, payTotalRow, 4).Style.NumberFormat.Format = MoneyFormat;
        payments.SheetView.FreezeRows(1);

        workbook.SaveAs(path);
    }

    // ── Statistics report ─────────────────────────────────────────────────

    public static void SaveStatsReport(string path, StatsReportDto report)
    {
        using var workbook = new XLWorkbook();
        var money = report.Money is not null;

        // Summary
        var summary = workbook.Worksheets.Add(SheetName(Res("Stats.Summary")));
        ApplyDirection(summary);
        var summaryRows = new List<object?[]>
        {
            new object?[] { Res("Stats.Period"), $"{CalendarFormat.Format(report.From)}  →  {CalendarFormat.Format(report.To)}" },
            new object?[] { Res("Stats.ExamsTotal"), report.TotalExams },
            new object?[] { Res("Stats.Patients"), report.DistinctPatients },
            new object?[] { Res("Stats.NewPatients"), report.NewPatients },
            new object?[] { Res("Stats.Cancelled"), report.CancelledExams },
        };
        var moneyRows = new List<int>();
        if (report.Money is { } m)
        {
            moneyRows.AddRange(Enumerable.Range(summaryRows.Count, 7)); // Billed .. Discounts
            summaryRows.Add([Res("Stats.Billed"), m.Billed]);
            summaryRows.Add([Res("Stats.NotInvoiced"), m.NotInvoiced]);
            summaryRows.Add([Res("Stats.Collected"), m.Collected]);
            summaryRows.Add([Res("PaymentMethod.Cash"), m.Cash]);
            summaryRows.Add([Res("PaymentMethod.Card"), m.Card]);
            summaryRows.Add([Res("PaymentMethod.Other"), m.Other]);
            summaryRows.Add([Res("Stats.Discounts"), m.Discounts]);
            summaryRows.Add([Res("Stats.DiscountedExams"), m.DiscountedExams]);
            summaryRows.Add([Res("Stats.FreeExams"), m.FreeExams]);
        }
        summaryRows.Add([string.Empty, string.Empty]);
        summaryRows.Add([Res("Stats.Note"), string.Empty]);
        WriteTable(summary, 1, [Res("Stats.Item"), Res("Stats.Value")], summaryRows);
        foreach (var index in moneyRows) summary.Cell(index + 2, 2).Style.NumberFormat.Format = MoneyFormat;

        // Breakdowns
        AddBreakdown(workbook, Res("Stats.ByType"), Res("Exams.ExamType"), report.ByExamType, null, money);
        AddBreakdown(workbook, Res("Stats.ByModality"), Res("Exams.Modality"), report.ByModality, Res("Stats.NoModality"), money);
        AddBreakdown(workbook, Res("Stats.ByDoctor"), Res("Exams.Referring"), report.ByReferringPhysician, Res("Stats.NoDoctor"), money);

        // By day
        var days = workbook.Worksheets.Add(SheetName(Res("Stats.ByDay")));
        ApplyDirection(days);
        var dayHeaders = new List<string> { Res("Stats.Col.Date"), Res("Stats.Col.Exams"), Res("Stats.Col.Patients") };
        if (money) dayHeaders.Add(Res("Stats.Col.Collected"));
        WriteTable(days, 1, dayHeaders, report.ByDay.Select(d =>
        {
            var row = new List<object?> { CalendarFormat.Format(d.Date), d.Exams, d.Patients };
            if (money) row.Add(d.Collected ?? 0m);
            return row.ToArray();
        }));
        if (money) days.Column(4).Style.NumberFormat.Format = MoneyFormat;

        // Cash by user
        if (report.CashByUser is { } cash)
        {
            var sheet = workbook.Worksheets.Add(SheetName(Res("Stats.CashByUser")));
            ApplyDirection(sheet);
            WriteTable(sheet, 1,
                [Res("Stats.Col.User"), Res("Stats.Col.Payments"), Res("PaymentMethod.Cash"), Res("PaymentMethod.Card"),
                 Res("PaymentMethod.Other"), Res("Stats.Col.Total")],
                cash.Select(u => new object?[] { u.UserName, u.Payments, u.Cash, u.Card, u.Other, u.Total }));
            sheet.Range(2, 3, cash.Count + 1, 6).Style.NumberFormat.Format = MoneyFormat;
        }

        // Who gave discounts
        if (report.DiscountsByUser is { Count: > 0 } discounts)
        {
            var sheet = workbook.Worksheets.Add(SheetName(Res("Stats.DiscountsByUser")));
            ApplyDirection(sheet);
            WriteTable(sheet, 1,
                [Res("Stats.Col.User"), Res("Stats.Col.Exams"), Res("Stats.Col.FreeExams"), Res("Stats.Col.Discount")],
                discounts.Select(u => new object?[] { u.UserName, u.Exams, u.FreeExams, u.Amount }));
            sheet.Column(4).Style.NumberFormat.Format = MoneyFormat;
        }

        workbook.SaveAs(path);
    }

    private static void AddBreakdown(XLWorkbook workbook, string title, string labelHeader,
        IEnumerable<StatsRowDto> rows, string? emptyLabel, bool money)
    {
        var ws = workbook.Worksheets.Add(SheetName(title));
        ApplyDirection(ws);

        var headers = new List<string> { labelHeader, Res("Stats.Col.Exams"), Res("Stats.Col.Patients") };
        if (money) { headers.Add(Res("Stats.Col.Revenue")); headers.Add(Res("Stats.Col.Discount")); }

        var list = rows.ToList();
        WriteTable(ws, 1, headers, list.Select(r =>
        {
            var row = new List<object?> { r.Label ?? emptyLabel ?? string.Empty, r.Exams, r.Patients };
            if (money) { row.Add(r.Revenue ?? 0m); row.Add(r.Discount ?? 0m); }
            return row.ToArray();
        }));
        if (money) { ws.Column(4).Style.NumberFormat.Format = MoneyFormat; ws.Column(5).Style.NumberFormat.Format = MoneyFormat; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void WriteTable(IXLWorksheet ws, int startRow, IReadOnlyList<string> headers, IEnumerable<object?[]> rows)
    {
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(startRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E9F1F4");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        var r = startRow + 1;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c])
                {
                    case null: break;
                    case int i: cell.Value = i; break;
                    case decimal d: cell.Value = d; break;
                    default: cell.Value = Convert.ToString(row[c], CultureInfo.InvariantCulture) ?? string.Empty; break;
                }
            }
            r++;
        }

        ws.Columns().AdjustToContents(1, Math.Min(r, 200), 8, 60);
    }

    // Right-to-left sheets for Dari so columns read from the right, like the app.
    private static void ApplyDirection(IXLWorksheet ws) =>
        ws.RightToLeft = AppSession.Language == AppLanguage.Dari;

    // Excel sheet names: max 31 chars, none of [ ] : * ? / \
    private static string SheetName(string name)
    {
        var cleaned = new string(name.Where(ch => "[]:*?/\\".IndexOf(ch) < 0).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "Sheet";
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }
}
