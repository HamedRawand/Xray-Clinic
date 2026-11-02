using NasimXray.Shared;
using System.Globalization;
using NasimXray.Client.Helpers;
using NasimXray.Shared.Dtos.Billing;
using NasimXray.Shared.Dtos.ExamOrders;
using NasimXray.Shared.Dtos.Patients;
using NasimXray.Shared.Dtos.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace NasimXray.Client.Services;

public static class PdfService
{
    private const string Accent = "#0E7490";
    private const string BoxHeaderBg = "#E4E4E4";
    private const string PatientBg = "#EBEBEB";
    private const string DarkText = "#111111";
    private const string GrayText = "#555555";
    private const string BoxBorder = "#999999";

    private static readonly PersianCalendar Pc = new();

    public static byte[] GenerateReportPdf(ReportDto report, ExamOrderDto order, PatientDto patient)
    {
        var isDari = AppSession.Language == AppLanguage.Dari;

        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontFamily("Calibri").FontSize(11).FontColor(DarkText));
                if (isDari) page.ContentFromRightToLeft();

                page.Header().Element(ComposeHeader);
                page.Content().Element(c => ComposeBody(c, report, order, patient, isDari));
                page.Footer().Element(c => ComposeFooter(c, report, isDari));
            });
        });

        return document.GeneratePdf();
    }

    public static void GenerateAndOpen(ReportDto report, ExamOrderDto order, PatientDto patient)
    {
        var bytes = GenerateReportPdf(report, order, patient);
        var fileName = $"Report_{patient.PatientCode}_{order.Id}_{DateTime.Now:yyyyMMddHHmmss}.pdf";
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);
        System.IO.File.WriteAllBytes(path, bytes);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    public static byte[] GenerateReceiptPdf(InvoiceDto invoice)
    {
        var isDari = AppSession.Language == AppLanguage.Dari;

        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontFamily("Calibri").FontSize(11).FontColor(DarkText));
                if (isDari) page.ContentFromRightToLeft();

                page.Header().Element(ComposeHeader);
                page.Content().Element(c => ComposeReceiptBody(c, invoice, isDari));
            });
        });

        return document.GeneratePdf();
    }

    public static void GenerateAndOpenReceipt(InvoiceDto invoice)
    {
        var bytes = GenerateReceiptPdf(invoice);
        var fileName = $"Receipt_{invoice.PatientCode}_{invoice.Id}_{DateTime.Now:yyyyMMddHHmmss}.pdf";
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);
        System.IO.File.WriteAllBytes(path, bytes);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static void ComposeReceiptBody(IContainer container, InvoiceDto invoice, bool isDari)
    {
        container.PaddingTop(12).Column(col =>
        {
            col.Spacing(10);

            col.Item().Text(L(isDari, "Payment Receipt", "رسید پرداخت")).FontSize(14).Bold().FontColor(Accent);

            col.Item().Background(PatientBg).Padding(10).Column(c =>
            {
                c.Item().Text($"{L(isDari, "Invoice #", "فاکتور شماره ")}{invoice.Id}").Bold();
                c.Item().Text($"{invoice.PatientName} ({invoice.PatientCode})").FontSize(10);
                c.Item().Text($"{L(isDari, "Date", "تاریخ")}: {FormatDate(invoice.CreatedAt)}").FontSize(9).FontColor(GrayText);
            });

            col.Item().Border(1).BorderColor(BoxBorder).Column(c =>
            {
                c.Item().Background(BoxHeaderBg).Padding(6).Text(L(isDari, "Exam Orders", "معاینات")).Bold().FontSize(11);
                c.Item().Padding(8).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);
                        cols.RelativeColumn();
                    });
                    foreach (var line in invoice.Lines)
                    {
                        table.Cell().PaddingVertical(2).Text(string.IsNullOrEmpty(line.ExamNumber) ? line.ExamTypeName : $"{line.ExamNumber}  {line.ExamTypeName}").FontSize(10);
                        table.Cell().PaddingVertical(2).Text(Currency.Format(line.ListPrice)).FontSize(10);
                        if (line.DiscountAmount > 0)
                        {
                            table.Cell().PaddingVertical(1).PaddingLeft(10)
                                .Text($"{L(isDari, "Discount", "تخفیف")}: {line.DiscountReason}").FontSize(9).FontColor(GrayText);
                            table.Cell().PaddingVertical(1).Text("-" + Currency.Format(line.DiscountAmount)).FontSize(9).FontColor(GrayText);
                        }
                    }
                });
            });

            col.Item().Border(1).BorderColor(BoxBorder).Column(c =>
            {
                c.Item().Background(BoxHeaderBg).Padding(6).Text(L(isDari, "Payments", "پرداخت‌ها")).Bold().FontSize(11);
                c.Item().Padding(8).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);
                        cols.RelativeColumn();
                    });
                    foreach (var payment in invoice.Payments)
                    {
                        table.Cell().PaddingVertical(2).Text($"{FormatDate(payment.Timestamp)} — {payment.Method}").FontSize(9);
                        table.Cell().PaddingVertical(2).Text(Currency.Format(payment.Amount)).FontSize(10).Bold();
                    }
                });
            });

            col.Item().Row(row =>
            {
                row.RelativeItem().Text($"{L(isDari, "Total", "مجموع")}: {Currency.Format(invoice.Total)}").FontSize(11).Bold();
                row.RelativeItem().AlignRight().Text($"{L(isDari, "Balance", "باقیمانده")}: {Currency.Format(invoice.Balance)}")
                    .FontSize(11).Bold().FontColor(invoice.Balance > 0 ? "#DC2626" : "#16A34A");
            });
        });
    }

    // Letterhead from the clinic's own profile (Clinic Settings): optional logo, English and Dari names side by side,
    // the tagline (e.g. the services offered) and the contact line.
    private static void ComposeHeader(IContainer container)
    {
        var profile = ClinicInfoService.Profile;
        var logo = ClinicInfoService.LogoBytes;
        var english = profile?.NameEnglish;
        var dari = profile?.NameDari;
        if (string.IsNullOrWhiteSpace(english) && string.IsNullOrWhiteSpace(dari)) english = ClinicInfoService.Title;

        var contact = string.Join("  ·  ", new[] { profile?.Address, profile?.Phone, profile?.Email }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                if (logo is { Length: > 0 })
                    row.ConstantItem(60).PaddingRight(10).Height(48).Image(logo).FitArea();

                row.RelativeItem().Row(names =>
                {
                    if (!string.IsNullOrWhiteSpace(english))
                        names.RelativeItem().AlignLeft().AlignMiddle().Text(english)
                            .FontSize(16).Bold().FontColor(Accent);
                    if (!string.IsNullOrWhiteSpace(dari))
                        names.RelativeItem().AlignRight().AlignMiddle().Text(dari)
                            .FontSize(16).Bold().FontColor(Accent).DirectionFromRightToLeft();
                });
            });
            if (!string.IsNullOrWhiteSpace(profile?.Tagline))
                col.Item().PaddingTop(2).Text(profile!.Tagline).FontSize(9).FontColor(GrayText);
            if (contact.Length > 0)
                col.Item().PaddingTop(1).Text(contact).FontSize(8).FontColor(GrayText);
            col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Accent);
        });
    }

    private static void ComposeBody(IContainer container, ReportDto report, ExamOrderDto order, PatientDto patient, bool isDari)
    {
        container.PaddingTop(12).Column(col =>
        {
            col.Item().Element(c => ComposePatientBar(c, order, patient, isDari));
            col.Spacing(10);

            col.Item().Element(c => ComposeLabelledBox(c, L(isDari, "Findings", "یافته‌ها"), report.Findings));

            if (report.Measurements.Count > 0)
                col.Item().Element(c => ComposeMeasurements(c, report, isDari));

            col.Item().Element(c => ComposeLabelledBox(c, L(isDari, "Impression", "نتیجه‌گیری"), report.Impression));

            if (!report.IsFinalized)
            {
                col.Item().PaddingTop(6).Background("#FEF9C3").Padding(8)
                    .Text(L(isDari, "DRAFT — not yet finalized", "پیش‌نویس — هنوز نهایی نشده است"))
                    .FontColor("#92400E").Bold().FontSize(10);
            }
        });
    }

    private static void ComposePatientBar(IContainer container, ExamOrderDto order, PatientDto patient, bool isDari)
    {
        container.Background(PatientBg).Padding(10).Row(row =>
        {
            row.RelativeItem(2).Column(c =>
            {
                c.Item().Text(patient.FullName).Bold().FontSize(13);
                if (!string.IsNullOrEmpty(patient.PatientCode))
                    c.Item().Text($"ID: {patient.PatientCode}").FontSize(9).FontColor(GrayText);
            });
            row.RelativeItem().Column(c =>
            {
                c.Item().Text($"{L(isDari, "Sex", "جنسیت")}: {TranslateSex(patient.Sex, isDari)}").FontSize(10);
                if (patient.DateOfBirth.HasValue)
                    c.Item().Text($"{L(isDari, "DOB", "تولد")}: {FormatDate(patient.DateOfBirth.Value)}").FontSize(10);
            });
            row.RelativeItem().AlignRight().Column(c =>
            {
                c.Item().Text($"{L(isDari, "Exam", "معاینه")}: {order.ExamTypeName}").FontSize(10).Bold();
                c.Item().Text($"{L(isDari, "Date", "تاریخ")}: {FormatDate(order.CreatedAt)}").FontSize(9).FontColor(GrayText);
                if (!string.IsNullOrEmpty(order.ReferringPhysicianName))
                    c.Item().Text($"{L(isDari, "Ref.", "رجعت")}: {order.ReferringPhysicianName}").FontSize(9).FontColor(GrayText);
            });
        });
    }

    private static void ComposeLabelledBox(IContainer container, string title, string? content)
    {
        container.Border(1).BorderColor(BoxBorder).Column(col =>
        {
            col.Item().Background(BoxHeaderBg).Padding(6).Text(title).Bold().FontSize(11).FontColor(DarkText);
            col.Item().Padding(8).Text(string.IsNullOrWhiteSpace(content) ? "—" : content).FontSize(10.5f);
        });
    }

    private static void ComposeMeasurements(IContainer container, ReportDto report, bool isDari)
    {
        container.Border(1).BorderColor(BoxBorder).Column(col =>
        {
            col.Item().Background(BoxHeaderBg).Padding(6)
                .Text(L(isDari, "Measurements", "اندازه‌گیری‌ها")).Bold().FontSize(11).FontColor(DarkText);

            col.Item().Padding(8).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);
                    c.RelativeColumn();
                });

                foreach (var m in report.Measurements)
                {
                    var value = string.IsNullOrWhiteSpace(m.Value) ? "—" : m.Value;
                    var withUnit = m.Unit is null ? value : $"{value} {m.Unit}";
                    table.Cell().PaddingVertical(2).Text(m.Label).FontSize(10);
                    table.Cell().PaddingVertical(2).Text(withUnit).FontSize(10).Bold();
                }
            });
        });
    }

    private static void ComposeFooter(IContainer container, ReportDto report, bool isDari)
    {
        container.PaddingTop(10).Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(BoxBorder);
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span($"{L(isDari, "Reported by", "راپور توسط")}: ").FontSize(9).FontColor(GrayText);
                    text.Span(report.ReportedByName).FontSize(9).Bold();
                });
                if (report.IsFinalized && report.FinalizedAt.HasValue)
                {
                    row.RelativeItem().AlignRight().Text(
                        $"{L(isDari, "Finalized", "نهایی شده")}: {FormatDate(report.FinalizedAt.Value)}")
                        .FontSize(9).FontColor(GrayText);
                }
            });
        });
    }

    private static string L(bool isDari, string en, string dari) => isDari ? dari : en;

    private static string TranslateSex(string? sex, bool isDari)
    {
        if (string.IsNullOrEmpty(sex)) return "—";
        return sex switch
        {
            "Male" => isDari ? "مذکر" : "Male",
            "Female" => isDari ? "مؤنث" : "Female",
            _ => sex,
        };
    }

    private static string FormatDate(DateTime dt)
    {
        if (AppSession.CalendarType == CalendarType.Shamsi)
        {
            int y = Pc.GetYear(dt), m = Pc.GetMonth(dt), d = Pc.GetDayOfMonth(dt);
            return $"{y:0000}/{m:00}/{d:00}";
        }
        return dt.ToString("dd/MM/yyyy");
    }
}
