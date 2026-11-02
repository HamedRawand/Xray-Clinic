using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

// Builds "Diagnostic Clinic Manager - Installation and Configuration Guide" as a PDF.
//   dotnet run --project tools/ManualBuilder -- <output.pdf> <version> <images folder>
QuestPDF.Settings.License = LicenseType.Community;

var output = Path.GetFullPath(args.Length > 0 ? args[0] : "manual.pdf");
var version = args.Length > 1 ? args[1] : "1.1.3";
var images = Path.GetFullPath(args.Length > 2 ? args[2] : Path.Combine("tools", "ManualBuilder", "images"));
var serverVersion = args.Length > 4 ? args[4] : "1.1.2"; // the server has its own version number; it can differ from the client's
Directory.CreateDirectory(Path.GetDirectoryName(output)!);

var toc = new List<(string Title, string Anchor)>();

var document = Document.Create(container =>
{
    // ------------------------------------------------------------------------------------------------ cover
    container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(0);
        page.Content().Column(col =>
        {
            col.Item().Height(300).Background(Style.Sidebar).Padding(50).AlignBottom().Column(c =>
            {
                var logo = Path.Combine(images, "appicon.png");
                if (File.Exists(logo))
                    c.Item().PaddingBottom(18).Width(76).Height(76).Background(Colors.White).Padding(8).Image(logo).FitArea();
                c.Item().Text("Diagnostic Clinic Manager").FontSize(38).Bold().FontColor(Colors.White);
                c.Item().PaddingTop(6).Text("Installation and Configuration Guide").FontSize(20).FontColor("#BFE3E6");
            });
            col.Item().Padding(50).Column(c =>
            {
                c.Item().Text("For the clinic administrator").FontSize(14).SemiBold().FontColor(Style.Dark);
                c.Item().PaddingTop(8).Text("How to set up the server PC, connect every clinic PC, prepare the clinic's own details and keep the system " +
                    "running: backups, updates and troubleshooting.").FontSize(12).FontColor(Style.Muted).LineHeight(1.4f);
                c.Item().PaddingTop(40).Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.ConstantColumn(110); cd.RelativeColumn(); });
                    void Row(string k, string v)
                    {
                        t.Cell().PaddingVertical(4).Text(k).FontSize(11).FontColor(Style.Muted);
                        t.Cell().PaddingVertical(4).Text(v).FontSize(11).SemiBold().FontColor(Style.Dark);
                    }
                    Row("Version", version);
                    Row("Date", DateTime.Now.ToString("d MMMM yyyy"));
                    Row("Applies to", "Client " + version + " and Server " + serverVersion + " (and newer 1.1.x releases)");
                    Row("Platform", "Windows 10 / 11 (64-bit)");
                });
                c.Item().PaddingTop(60).Border(1).BorderColor(Style.Line).Background(Style.Soft).Padding(14)
                    .DefaultTextStyle(x => x.FontSize(10.5f).FontColor(Style.Dark)).Text(t =>
                {
                    t.Span("Keep this guide with the server PC. ").SemiBold();
                    t.Span("It lists where the passwords, backups and certificate live, and what to do when something goes wrong.");
                });
            });
        });
    });

    // ------------------------------------------------------------------------------------------------ body
    container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.MarginHorizontal(52);
        page.MarginTop(48);
        page.MarginBottom(44);
        page.DefaultTextStyle(x => x.FontSize(10.5f).FontColor(Style.Dark).LineHeight(1.35f));

        page.Header().PaddingBottom(8).BorderBottom(1).BorderColor(Style.Line).Row(r =>
        {
            r.RelativeItem().Text("Diagnostic Clinic Manager - Installation and Configuration Guide").FontSize(8.5f).FontColor(Style.Muted);
            r.AutoItem().Text("Version " + version).FontSize(8.5f).FontColor(Style.Muted);
        });
        page.Footer().AlignCenter().Text(t =>
        {
            t.DefaultTextStyle(x => x.FontSize(8.5f).FontColor(Style.Muted));
            t.Span("Page "); t.CurrentPageNumber(); t.Span(" of "); t.TotalPages();
        });

        page.Content().Column(col =>
        {
            col.Spacing(6);

            // ---- contents (filled with the section list below)
            col.Item().Text("Contents").FontSize(22).Bold().FontColor(Style.Teal);
            col.Item().PaddingTop(4).Column(tocCol =>
            {
                foreach (var entry in Sections.All)
                {
                    tocCol.Item().PaddingVertical(3).Row(r =>
                    {
                        r.RelativeItem().SectionLink(entry.Anchor).Text(entry.Title).FontSize(11.5f);
                        r.AutoItem().DefaultTextStyle(x => x.FontSize(11.5f).FontColor(Style.Muted)).Text(t => t.BeginPageNumberOfSection(entry.Anchor));
                    });
                }
            });
            col.Item().PaddingTop(14).Element(e => Doc.Note(e, "How to read this guide",
                "Steps are numbered and meant to be followed in order. Text in **bold** is something you click or type exactly as written. " +
                "Grey boxes in `this style` are commands or file names. Amber boxes warn about things that can cause trouble; teal boxes give helpful background.",
                "info"));

            Sections.Render(col, images, version, serverVersion);
        });
    });
});

document.GeneratePdf(output);
Console.WriteLine("Manual written: " + output + "  (" + new FileInfo(output).Length / 1024 + " KB)");

// optional 4th argument: a folder to receive one preview PNG per page (for checking the layout)
if (args.Length > 3)
{
    var previewDir = Path.GetFullPath(args[3]);
    Directory.CreateDirectory(previewDir);
    var n = 0;
    foreach (var png in document.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 80 }))
        File.WriteAllBytes(Path.Combine(previewDir, $"page_{++n:00}.png"), png);
    Console.WriteLine("Preview pages: " + n);
}

// ==================================================================================================== styles
static class Style
{
    public const string Teal = "#0E7490";
    public const string Sidebar = "#0E3B43";
    public const string Dark = "#0F172A";
    public const string Muted = "#64748B";
    public const string Line = "#CBD5E1";
    public const string Soft = "#F1F5F9";
    public const string Warn = "#B45309";
    public const string WarnBg = "#FEF3C7";
    public const string InfoBg = "#E0F2F7";
    public const string Danger = "#B91C1C";
    public const string DangerBg = "#FEE2E2";
}

// ==================================================================================================== building blocks
static class Doc
{
    static readonly Regex Markup = new(@"(\*\*.+?\*\*|\*[^*]+?\*|`.+?`)", RegexOptions.Compiled);

    // **bold**, *italic* and `code` inside running text
    public static void Rich(TextDescriptor t, string text, float size = 10.5f)
    {
        foreach (var part in Markup.Split(text))
        {
            if (part.Length == 0) continue;
            if (part.StartsWith("**")) t.Span(part[2..^2]).SemiBold();
            else if (part.Length > 2 && part.StartsWith('*')) t.Span(part[1..^1]).Italic();
            else if (part.StartsWith('`')) t.Span(part[1..^1]).FontFamily("Consolas").FontSize(size - 1.3f).BackgroundColor(Style.Soft);
            else t.Span(part);
        }
    }

    public static void H1(ColumnDescriptor col, string title, string anchor, string number)
    {
        col.Item().PageBreak();
        col.Item().Section(anchor).PaddingBottom(2).DefaultTextStyle(x => x.FontSize(22).Bold().FontColor(Style.Teal)).Text(t =>
        {
            t.Span(number + "  ").FontColor(Style.Muted);
            t.Span(title);
        });
        col.Item().PaddingBottom(6).LineHorizontal(2).LineColor(Style.Teal);
    }

    // Items inside a block are kept together: if they do not fit in the space left on the page, the whole block starts
    // on the next page.
    public static void Block(ColumnDescriptor col, Action<ColumnDescriptor> content) =>
        col.Item().ShowEntire().Column(content);

    // A heading is remembered and drawn together with the FIRST item that follows it (paragraph, list, table, picture ...),
    // as one unbreakable group - so a heading is never left alone at the bottom of a page.
    static string? _pendingHeading;

    public static void H2(ColumnDescriptor col, string title, float unused = 0) => _pendingHeading = title;

    static void FirstItem(ColumnDescriptor col, Action<IContainer> build)
    {
        if (_pendingHeading is null) { build(col.Item()); return; }

        var title = _pendingHeading;
        _pendingHeading = null;
        col.Item().ShowEntire().Column(c =>
        {
            c.Item().PaddingTop(10).PaddingBottom(4).Text(title).FontSize(14).Bold().FontColor(Style.Dark);
            build(c.Item());
        });
    }

    public static void P(ColumnDescriptor col, string text) =>
        FirstItem(col, e => e.Text(t => Rich(t, text)));

    public static void Bullets(ColumnDescriptor col, params string[] items)
    {
        foreach (var item in items)
            FirstItem(col, e => e.Row(r =>
            {
                r.ConstantItem(16).PaddingTop(1).Text("\u2022").FontColor(Style.Teal).Bold();
                r.RelativeItem().Text(t => Rich(t, item));
            }));
    }

    // tick-box list for the acceptance checklist
    public static void Checklist(ColumnDescriptor col, params string[] items)
    {
        foreach (var item in items)
            FirstItem(col, e => e.ShowEntire().Row(r =>
            {
                r.ConstantItem(22).PaddingTop(2).Width(11).Height(11).Border(1.2f).BorderColor(Style.Teal);
                r.RelativeItem().Text(t => Rich(t, item));
            }));
    }

    public static void Steps(ColumnDescriptor col, params string[] items)
    {
        var n = 1;
        foreach (var item in items)
        {
            var number = n++;
            FirstItem(col, e => e.ShowEntire().Row(r =>
            {
                r.ConstantItem(26).PaddingTop(1).Width(18).Height(18).Background(Style.Teal)
                    .AlignCenter().AlignMiddle().Text(number.ToString()).FontSize(9.5f).Bold().FontColor(Colors.White);
                r.RelativeItem().PaddingLeft(4).Text(t => Rich(t, item));
            }));
        }
    }

    public static void Note(IContainer e, string title, string text, string kind)
    {
        var (bg, bar) = kind switch
        {
            "warn" => (Style.WarnBg, Style.Warn),
            "danger" => (Style.DangerBg, Style.Danger),
            _ => (Style.InfoBg, Style.Teal),
        };
        e.ShowEntire().Background(bg).BorderLeft(4).BorderColor(bar).Padding(10).Column(c =>
        {
            c.Item().Text(title).Bold().FontColor(bar).FontSize(10.5f);
            c.Item().PaddingTop(2).Text(t => Rich(t, text, 10));
        });
    }

    public static void Note(ColumnDescriptor col, string title, string text, string kind = "info") =>
        FirstItem(col, e => Note(e, title, text, kind));

    public static void Code(ColumnDescriptor col, params string[] lines) =>
        FirstItem(col, e => e.ShowEntire().Background("#0F172A").Padding(10).Column(c =>
        {
            foreach (var l in lines)
                c.Item().Text(l).FontFamily("Consolas").FontSize(9).FontColor("#E2E8F0");
        }));

    public static void Table(ColumnDescriptor col, string[] headers, string[][] rows, float[]? widths = null) =>
        FirstItem(col, e => e.ShowEntire().Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                for (var i = 0; i < headers.Length; i++)
                    if (widths is null) cd.RelativeColumn(); else cd.RelativeColumn(widths[i]);
            });
            t.Header(h =>
            {
                foreach (var header in headers)
                    h.Cell().Background(Style.Sidebar).PaddingVertical(5).PaddingHorizontal(7)
                        .Text(header).FontColor(Colors.White).SemiBold().FontSize(10);
            });
            foreach (var row in rows)
                foreach (var cell in row)
                    t.Cell().BorderBottom(1).BorderColor(Style.Line).PaddingVertical(5).PaddingHorizontal(7)
                        .DefaultTextStyle(x => x.FontSize(10)).Text(tx => Rich(tx, cell, 10));
        }));

    public static void Picture(ColumnDescriptor col, string folder, string file, string caption, float widthPercent = 100)
    {
        var path = Path.Combine(folder, file);
        if (!File.Exists(path)) { Note(col, "Missing screenshot", file, "danger"); return; }
        FirstItem(col, e => e.ShowEntire().PaddingVertical(6).Column(c =>
        {
            c.Item().AlignCenter().Width(widthPercent * 4.9f, Unit.Point).Border(1).BorderColor(Style.Line).Image(path).FitWidth();
            c.Item().PaddingTop(3).AlignCenter().Text(caption).FontSize(9).Italic().FontColor(Style.Muted);
        }));
    }
}

// ==================================================================================================== the content
static class Sections
{
    public static readonly (string Title, string Anchor)[] All =
    {
        ("1   How the system works", "s1"),
        ("2   Before you start", "s2"),
        ("3   Install the server PC", "s3"),
        ("4   First sign-in and clinic set-up", "s4"),
        ("5   Connect the other PCs", "s5"),
        ("6   Daily use at a glance", "s6"),
        ("7   Backups and restore", "s7"),
        ("8   Updates", "s8"),
        ("9   Troubleshooting", "s9"),
        ("10  Reference", "s10"),
    };

    public static void Render(ColumnDescriptor col, string img, string version, string serverVersion)
    {
        // ===================================================================================== 1
        Doc.H1(col, "How the system works", "s1", "1");
        Doc.P(col, "Diagnostic Clinic Manager runs on **one server PC** and any number of **client PCs** that are on the same network. " +
            "Everyone works with the same patients, exams, reports and invoices at the same time.");

        col.Item().PaddingVertical(6).Table(t =>
        {
            t.ColumnsDefinition(cd => { cd.RelativeColumn(1.4f); cd.ConstantColumn(34); cd.RelativeColumn(1); });
            t.Cell().Background(Style.Sidebar).Padding(12).Column(c =>
            {
                c.Item().Text("SERVER PC").Bold().FontColor("#5EEAD4").FontSize(10);
                c.Item().PaddingTop(4).Text("Database (PostgreSQL)").FontColor(Colors.White);
                c.Item().Text("Server service (Windows service)").FontColor(Colors.White);
                c.Item().Text("Backups, certificate, settings").FontColor(Colors.White);
            });
            t.Cell().AlignCenter().AlignMiddle().Text("\u2194").FontSize(20).FontColor(Style.Teal);
            t.Cell().Background(Style.Soft).Border(1).BorderColor(Style.Line).Padding(12).Column(c =>
            {
                c.Item().Text("CLIENT PCs").Bold().FontColor(Style.Teal).FontSize(10);
                c.Item().PaddingTop(4).Text("Reception");
                c.Item().Text("Technician");
                c.Item().Text("Radiologist");
                c.Item().Text("Administrator");
            });
        });

        Doc.Bullets(col,
            "**One installation per clinic.** Each clinic has its own server PC and its own data. The clinic's name, logo and contact details are entered once in the program (Clinic Settings) and appear everywhere, including printed reports and receipts.",
            "**The server PC also works as a normal client** if you wish: install the client program on it too.",
            "**Secure connection.** All traffic between PCs is encrypted (HTTPS). Each PC remembers the server's certificate on first connection and refuses any other, so nobody can pretend to be your server.",
            "**No internet is needed for daily work.** The internet is only used during installation and to check for new versions.",
            "**The server must be running.** There is no offline mode: if the server PC is switched off, no PC can work. Keep it powered on during clinic hours.");

        Doc.H2(col, "What you will install");
        Doc.Table(col, new[] { "Program", "Where", "File name" }, new[]
        {
            new[] { "**Server** (database + service)", "Server PC only", "`DiagnosticClinicManager_Server_Setup_v" + serverVersion + ".exe`" },
            new[] { "**Client** (the program people use)", "Every PC that uses the system", "`DiagnosticClinicManager_Setup_v" + version + ".exe`" },
        }, new[] { 2f, 1.5f, 3.6f });
        Doc.P(col, "Both are available on the project's release page: **github.com/HamedRawand/Xray-Clinic/releases** (look for the newest *Server* and *Client* releases).");

        // ===================================================================================== 2
        Doc.H1(col, "Before you start", "s2", "2");
        Doc.H2(col, "What you need");
        Doc.Table(col, new[] { "", "Server PC", "Client PCs" }, new[]
        {
            new[] { "**Windows**", "Windows 10 or 11, 64-bit (or Windows Server 2016 and newer)", "Windows 10 or 11, 64-bit. Windows 7/8 and 32-bit PCs are not supported by this version." },
            new[] { "**Account**", "An administrator account for the installation", "Any account" },
            new[] { "**Internet**", "Needed once, to download PostgreSQL (about 370 MB)", "Needed to download the installer and to check for updates" },
            new[] { "**Disk space**", "At least 3 GB free, plus room for backups", "About 500 MB" },
            new[] { "**Power**", "Always on during clinic hours. Set Windows sleep to **Never**.", "-" },
            new[] { "**Network**", "Wired to the router if possible. Give it a **fixed address** (see below).", "Same network as the server (not a separate guest Wi-Fi)" },
        }, new[] { 1.1f, 3f, 3f });

        Doc.H2(col, "Prepare");
        Doc.Steps(col,
            "**Choose the server PC** and give it a fixed address: in the router, reserve an IP address for this PC (DHCP reservation), or set a static IP in Windows. Otherwise the address may change and the other PCs will lose the connection.",
            "**Set the correct date, time and time zone** on every PC.",
            "**Collect the clinic's details** you will enter later: clinic name (English and Dari), a short line for printouts (for example the services offered), address, phone, e-mail, and optionally a logo (PNG or JPEG, up to 300 KB).",
            "**Collect the price list**: each exam the clinic offers (for example chest X-ray, abdominal ultrasound), its modality and its price in AFN.",
            "**List the people** who will use the system and their roles (reception, technician, radiologist, administrator).");

        Doc.Note(col, "Windows may show a warning when you start an installer",
            "The installers are not code-signed, so Windows SmartScreen may say \"Windows protected your PC\". Click **More info**, then **Run anyway**. " +
            "Antivirus software may also scan the new files for a few minutes the first time; this is normal.", "warn");

        // ===================================================================================== 3
        Doc.H1(col, "Install the server PC", "s3", "3");
        Doc.P(col, "Do this once, on the PC that will act as the server. It takes about 10 to 15 minutes, most of it waiting.");

        Doc.H2(col, "Step by step");
        Doc.Steps(col,
            "**Write down the PC's name and IP address.** Open the Start menu, type `cmd` and press Enter. Type `hostname` and note the result (for example `CLINIC-SRV`). Then type `ipconfig` and note the **IPv4 Address** (for example `192.168.1.10`).",
            "**Download and start the server installer**, `DiagnosticClinicManager_Server_Setup_v" + serverVersion + ".exe`. Approve the Windows administrator prompt and click through the wizard to the end.",
            "**A setup window opens and works through these steps automatically:** it installs the database program (PostgreSQL 17) if it is missing, creates the clinic database, generates all passwords and the security certificate, registers the server as a Windows service, opens the network port 5443 for the local network only, starts the server and checks that it answers.",
            "**Wait.** The step *Starting the server* can sit quietly for **a few minutes** while antivirus checks the new files. Do not close the window.",
            "**Wait for \"Server is running.\"** The window then shows the address to use, the certificate fingerprint and the first administrator password. **Write these down** (see the table below).",
            "**Save the passwords file.** Copy `C:\\ProgramData\\DiagnosticClinicManager\\INSTALL-SECRETS.txt` to a USB stick (or print it), keep it somewhere safe that is **not** this PC, then delete it from the PC.",
            "**Check that the server answers.** In a web browser on the server PC open `https://localhost:5443/api/system/clinic` and accept the certificate warning. You should see a short line of text that includes `\"nameEnglish\":\"My Clinic\"`. This link is only for checking in a browser - it is not the address you enter in the program.");

        Doc.H2(col, "Write these down");
        Doc.Table(col, new[] { "Item", "Your value", "Why it matters" }, new[]
        {
            new[] { "Server computer name", "", "Used by the other PCs to find the server" },
            new[] { "Server IP address", "", "An alternative to the name" },
            new[] { "Certificate fingerprint", "", "Compare it on every PC before clicking Trust" },
            new[] { "First administrator password", "", "You must change it at the first sign-in" },
            new[] { "**Backup encryption password**", "", "**Without it, backups can never be restored**" },
        }, new[] { 2.2f, 2.4f, 3f });

        Doc.Note(col, "Keep the backup password safe",
            "The backup encryption password is generated once and is not stored anywhere you can recover it from if the PC is lost. Keep a copy off the server PC (a printed copy in a safe, or a password manager).", "danger");

        Doc.H2(col, "How to confirm the service is installed");
        Doc.P(col, "Open **Services** (Start menu, type `services.msc`). The entry **Diagnostic Clinic Manager Server** should be **Running**, with start type **Automatic (Delayed Start)**.");

        Doc.H2(col, "If something does not work");
        Doc.Bullets(col,
            "**The database program could not be installed.** Install PostgreSQL 17 by hand from postgresql.org (choose a password for the user `postgres` and keep the other defaults), then run the setup again from an administrator PowerShell: `& \"C:\\Program Files\\DiagnosticClinicManager\\Server\\scripts\\Install-Server.ps1\"`. It asks for that password.",
            "**The service could not start.** On some PCs Windows does not allow the standard low-privilege service account to use the certificate. In that case the setup switches the service to the Windows System account by itself and tells you so. Nothing else is needed.",
            "**Log files** are in `C:\\ProgramData\\DiagnosticClinicManager\\Logs`. Send the newest one to your support person if you need help.");

        // ===================================================================================== 4
        Doc.H1(col, "First sign-in and clinic set-up", "s4", "4");
        Doc.P(col, "Do this once, as the administrator, on the server PC (or on any client PC after section 5).");

        Doc.H2(col, "4.1  Install the client and connect to the server");
        Doc.Steps(col,
            "Download and run `DiagnosticClinicManager_Setup_v" + version + ".exe` and click through the wizard.",
            "Start the program. On the very first start the **Clinic Server Address** window opens by itself.",
            "Type the server address. On the server PC itself you can use `https://localhost:5443/`. Click **Test Connection**.",
            "The window shows the server's **certificate fingerprint** (see the picture). **Compare it character by character with the fingerprint you wrote down in section 3.** If it is the same, click **Trust This Certificate**.",
            "Click **Test Connection** again - it should say *Connected successfully* - then click **Save**.");
        Doc.Picture(col, img, "02_server_address.png", "The Clinic Server Address window while the certificate is being confirmed", 62);
        Doc.Note(col, "Type only the server, nothing after the port number",
            "The address is always `https://` + computer name or IP + `:5443/` and **nothing else**, for example `https://192.168.1.10:5443/`. " +
            "If you paste a longer link, the program removes the extra part for you, but never enter the link you used to check the server in the browser.");

        Doc.H2(col, "4.2  Sign in and change the password");
        Doc.Steps(col,
            "On the sign-in screen enter the user name **admin** and the first administrator password from section 3.",
            "You are asked to choose a **new password**. Choose a strong one and write it down in a safe place.");
        Doc.Picture(col, img, "01_login.png", "The sign-in screen. The server address, the language button and the version are at the bottom.", 46);

        Doc.Block(col, c =>
        {
            Doc.H2(c, "4.3  Enter the clinic's details", 0);
            Doc.P(c, "Open **Clinic Settings** in the sidebar. Until you save this once, a yellow banner reminds you to do it.");
            Doc.Picture(c, img, "04_clinic_settings.png", "Clinic Settings: the clinic's name, contact details and logo", 72);
        });
        Doc.Bullets(col,
            "**Clinic name (English)** is required. The **Dari name** is shown when the program runs in Dari.",
            "**Line under the name on printouts** is for example the services you offer.",
            "**Address, phone, e-mail and logo** appear on the top of every printed report and receipt.",
            "Click **Save**. The name changes in the title bar, on the sign-in screen and on every other PC within a couple of minutes.");

        Doc.H2(col, "4.4  Prepare the exam list and referring doctors");
        Doc.Steps(col,
            "Open **Exam Types** and click **+ New Exam Type** for every exam the clinic performs. Enter the **name**, the **modality** (X-ray, ultrasound, echo, ...) and the **price in AFN**. Exams with measurements (for example echocardiography) can have measurement fields added.",
            "Open **Referring Physicians** and add the doctors who send patients to the clinic.");
        Doc.Note(col, "The price is fixed when an exam is booked",
            "Changing a price later does not change exams that were already booked. This keeps old invoices correct.");

        Doc.H2(col, "4.5  Create the users");
        Doc.P(col, "Open **Users** and click **+ New User**. Create one user for each person and choose a role. Each person signs in with their own user name, so the audit log shows who did what.");
        Doc.Table(col, new[] { "Role", "What the person can do" }, new[]
        {
            new[] { "**Admin**", "Everything, including users, roles, clinic settings, backups and the audit log." },
            new[] { "**Receptionist**", "Register patients, check patients in, create invoices, record payments, give discounts (with a reason), manage referring physicians." },
            new[] { "**Technician**", "See patients and exams, start and complete exams, view reports." },
            new[] { "**Radiologist**", "See patients and exams, write, finalize and amend reports." },
        }, new[] { 1.4f, 5f });
        Doc.P(col, "If a person needs a different combination of rights, create a **Custom Role** (sidebar: **Custom Roles**) and assign it to the user.");

        Doc.H2(col, "4.6  Language and calendar");
        Doc.P(col, "The small icons in the row at the bottom of the sidebar switch between **English and Dari** (the globe) and between the **Gregorian and Solar Hijri (Shamsi)** calendars (the calendar). Hold the mouse over an icon to see its name. The choice is remembered on each PC. To change your own password, use the key icon in the same row.");

        // ===================================================================================== 5
        Doc.H1(col, "Connect the other PCs", "s5", "5");
        Doc.P(col, "Repeat these steps on every client PC.");
        Doc.Steps(col,
            "**Check that the PC can reach the server.** In a web browser open `https://<server name or IP>:5443/api/system/clinic`, accept the certificate warning, and look for the line of text with the clinic name. If the name does not work but the IP address does, use the IP address from now on. If neither works, see *Troubleshooting*.",
            "Install the client program (`DiagnosticClinicManager_Setup_v" + version + ".exe`).",
            "On the first start the **Clinic Server Address** window opens. Enter `https://<server name or IP>:5443/` - **only that**.",
            "Click **Test Connection**, **compare the fingerprint** with the one from the server, click **Trust This Certificate**, click **Test Connection** once more, then **Save**.",
            "Sign in with the user that was created for this person.");
        Doc.Note(col, "Why the certificate must be trusted once on each PC",
            "The first time a PC connects, it cannot know that the certificate really belongs to your server. By comparing the fingerprint yourself you confirm it. " +
            "From then on the PC accepts only this exact certificate. If the server is ever reinstalled with a new certificate, every PC must repeat the trust step.", "info");
        Doc.P(col, "You can change the server address later with the **Server Settings** button on the sign-in screen.");

        // ===================================================================================== 6
        Doc.H1(col, "Daily use at a glance", "s6", "6");
        Doc.P(col, "This is a short tour of the screens. The sidebar on the left shows only what the signed-in person is allowed to use.");
        Doc.Picture(col, img, "03_main.png", "The main window: your name and the menu in the sidebar, and a row of small icons, Sign Out and the version at its bottom", 92);

        Doc.Table(col, new[] { "Sidebar item", "What it is for" }, new[]
        {
            new[] { "**Dashboard**", "Today's numbers in icon cards, the work queues (click one to open it), money collected today with a payment-method bar (for users who may see billing), exams by type and by status - and, for administrators, a reminder card with the date of the last backup." },
            new[] { "**+ New Check-in**", "Register a patient's visit: find or create the patient, choose the exams, create the invoice and take the payment - all in one window." },
            new[] { "**Exams**", "The exams register with queues, filters and the workflow buttons." },
            new[] { "**Statistics**", "Counts and income for a chosen period; can be exported to Excel." },
            new[] { "**Patients**", "Search and open patients; see exam count and last visit." },
            new[] { "**Invoices**", "All invoices with totals; record payments; export to Excel." },
            new[] { "**Exam Types, Referring Physicians**", "The clinic's price list and referring doctors." },
            new[] { "**Users, Custom Roles, Audit Log**", "Administration (administrators only by default)." },
            new[] { "**Backup & Restore, Clinic Settings**", "Backups and the clinic's own details (administrators)." },
            new[] { "**Icons at the bottom**", "Calendar (Gregorian / Shamsi), globe (English / Dari), key (change password), book (this manual), circular arrow (check for updates). Hold the mouse over an icon to see its name." },
            new[] { "**Sign Out**", "Ends your session. Always sign out when you leave the PC." },
        }, new[] { 2.2f, 5f });

        Doc.Block(col, c =>
        {
            Doc.H2(c, "6.1  Checking a patient in (reception)", 0);
            Doc.Picture(c, img, "07_checkin.png", "New Check-in: patient, exams, billing", 66);
        });
        Doc.Steps(col,
            "Click **+ New Check-in**. Search for the patient by name, patient ID or phone number, or click **+ New patient**. If a patient with the same name or phone number already exists, a yellow warning shows them so you do not create a duplicate.",
            "Under **Exams**, find and add each exam and choose the **referring physician**.",
            "Under **Billing & payment** leave **Create an invoice** ticked, enter the **amount received now** (or click **Full amount**) and the payment method. The balance is shown.",
            "To give a **discount or a free exam**, change the exam's **Charge**: lower than the list price is a discount, 0 (or the **Free** option) is a free exam. Then enter the **Discount reason**. Everything is recorded in the audit log.",
            "Click **Check in**. The exams now appear in the technician's queue.");

        Doc.Block(col, c =>
        {
            Doc.H2(c, "6.2  The exams register (technician and radiologist)", 0);
            Doc.Picture(c, img, "05_exams.png", "The exams register with its queues and the workflow buttons", 92);
        });
        Doc.Table(col, new[] { "Status", "Meaning", "Next step" }, new[]
        {
            new[] { "**Pending**", "Booked, waiting for the technician", "Technician clicks **Start**" },
            new[] { "**In Progress**", "The exam is being performed", "Technician clicks **Complete**" },
            new[] { "**Completed**", "Done, waiting for the report", "Radiologist opens **Report** from the **...** menu" },
            new[] { "**Reported**", "Report finalized", "-" },
            new[] { "**Cancelled**", "Cancelled with a reason", "-" },
        }, new[] { 1.4f, 3f, 3f });
        Doc.Bullets(col,
            "The **queue buttons** at the top (*Waiting for technician*, *In progress*, *Waiting for report*, *Needs billing*, *Unpaid*) show how many exams are waiting and filter the list with one click.",
            "The **...** button at the end of each row opens more actions: **Report**, **Patient**, **Discount** and **Cancel exam** (a reason is required). Double-clicking a row opens the patient.",
            "In the report window: **Save Draft**, **Finalize** (the report becomes read-only), **Amend** (needs a reason; the original is kept in the history) and **Print / Export PDF**.");

        Doc.Block(col, c =>
        {
            Doc.H2(c, "6.3  Invoices and payments", 0);
            Doc.Picture(c, img, "06_invoices.png", "Invoices with the totals of everything that matches the filters", 92);
        });
        Doc.Bullets(col,
            "All amounts are shown in **AFN**.",
            "The totals bar shows **Total, Paid and Balance** for *all* invoices matching the filters, not only the current page.",
            "Open an invoice to **record a payment** or print a receipt. Use **Export to Excel** to save the list.");

        Doc.H2(col, "6.4  Good to know");
        Doc.Bullets(col,
            "**Automatic lock.** After 10 minutes without mouse or keyboard use the program locks; the user enters their password to continue.",
            "**Audit log.** Every important action (sign-ins, discounts, report changes, backups, settings) is recorded with who and when - see **Audit Log**.",
            "**Printouts** show the clinic's name, logo and contact details from Clinic Settings.",
            "**This guide is inside the program.** Click the **book icon** in the row of small icons at the bottom of the sidebar to open it as a PDF. On the server PC a copy is also kept in the server's program folder (`docs`).");

        // ===================================================================================== 7
        Doc.H1(col, "Backups and restore", "s7", "7");
        Doc.P(col, "Backups protect the clinic's data against disk failure, theft, mistakes and viruses. They are **encrypted** with the backup password from the installation.");

        Doc.H2(col, "7.1  Automatic and manual backups");
        Doc.Bullets(col,
            "The server makes a backup **every night at about 02:30** (Kabul time) and keeps the **newest 14**.",
            "To make one at any time: sidebar **Backup & Restore**, then **Create Backup Now** - or click **Back up now** on the **Dashboard**.",
            "**The Dashboard reminds you.** Administrators see a card at the top with the date of the last backup and how many days ago it was: green when it is from today or yesterday, amber after two days, red after a week or when none exists yet.",
            "Backups are stored on the server PC in `C:\\ProgramData\\DiagnosticClinicManager\\Backups`.");
        Doc.Note(col, "A backup on the same disk is not enough",
            "If that disk fails, the backups are lost with it. **Copy the Backups folder to an external disk or USB stick regularly** (at least weekly), and keep one copy outside the clinic.", "warn");

        Doc.H2(col, "7.2  Restoring a backup");
        Doc.Steps(col,
            "Make sure nobody else is working (a restore **replaces all current data** with the data from the backup).",
            "Open **Backup & Restore**, choose the backup in the list and click **Restore**.",
            "Enter the **backup encryption password** when asked and confirm.",
            "After the restore, everyone should sign out and back in.");
        Doc.Note(col, "Try a restore before you need one",
            "Do a restore test on a spare PC, or at least confirm once that you have the backup password and can open the Backup & Restore screen. A backup that cannot be restored is worth nothing.", "info");

        Doc.H2(col, "7.3  Disaster recovery in short");
        Doc.Steps(col,
            "Install a server PC again (section 3).",
            "Copy your latest backup file (`.nxb`) into `C:\\ProgramData\\DiagnosticClinicManager\\Backups` on the new server.",
            "On a client PC open **Backup & Restore**, restore it with the backup password.",
            "Every client PC must trust the new server's certificate again (section 5).");

        // ===================================================================================== 8
        Doc.H1(col, "Updates", "s8", "8");
        Doc.H2(col, "8.1  Client PCs");
        Doc.Bullets(col,
            "When the program starts it checks for a newer version (at most once a day). If there is one, an **Update available** window offers to download and install it.",
            "To check at any time click the **circular-arrow icon** (*Check for updates*) in the row of small icons at the bottom of the sidebar. The program tells you whether you are up to date.",
            "The installed version is shown at the very bottom of the sidebar.");

        Doc.H2(col, "8.2  The server PC");
        Doc.Steps(col,
            "Download the newer `DiagnosticClinicManager_Server_Setup_vX.Y.Z.exe` from the release page.",
            "Run it **on top of the existing installation**. Your database, passwords and certificate are kept. The setup first takes a **safety backup** (`Backups\\pre-upgrade-<time>.dump`) and then starts the new version; the database is upgraded automatically.",
            "Then update the client PCs.");
        Doc.Note(col, "Update the server first, then the clients",
            "A newer client may need the newer server. Do the update outside clinic hours and make a manual backup first.", "warn");

        // ===================================================================================== 9
        Doc.H1(col, "Troubleshooting", "s9", "9");
        Doc.Table(col, new[] { "Problem", "What to do" }, new[]
        {
            new[] { "The client cannot connect", "Check both PCs are on the same network (not a guest Wi-Fi). In a browser on the client open `https://<server>:5443/api/system/clinic`. If it fails, use the server's IP address instead of its name; then check the server PC is on and the service is running." },
            new[] { "Works by IP address but not by name", "Use the IP address in the Clinic Server Address window." },
            new[] { "Still blocked", "On the server, open Windows Defender Firewall > Advanced settings > Inbound Rules and look for **Diagnostic Clinic Manager Server (HTTPS 5443)**. It allows the local network only." },
            new[] { "Sign-in fails right after saving the address", "Open **Server Settings** and make sure the address is only `https://<server>:5443/` with nothing after the port." },
            new[] { "The fingerprints are different", "Do **not** click Trust. Check that you typed the right server, then compare again with the fingerprint from the server." },
            new[] { "\"Account locked\"", "After 5 wrong passwords in a row an account is locked until an administrator unlocks it: open **Users**, choose the user and reset the password (this unlocks the account)." },
            new[] { "Everyone gets errors after the server PC was restarted", "Open **Services** on the server and check **Diagnostic Clinic Manager Server** is Running; start it if not. The first start after a restart can take a minute." },
            new[] { "Forgot the admin password", "Another administrator can reset it in **Users**. If there is none, contact your support person." },
            new[] { "Lost the first-run passwords", "They stay in `C:\\ProgramData\\DiagnosticClinicManager\\server.settings.json` until the first successful start; re-running the server setup recreates the passwords file from it." },
            new[] { "No update window appears", "The automatic check runs once a day. Click the circular-arrow icon (*Check for updates*) at the bottom of the sidebar to check now. If it says it cannot reach the update service, check the internet connection." },
        }, new[] { 2.4f, 5.6f });

        Doc.H2(col, "Where to look for details");
        Doc.Table(col, new[] { "On the", "Log files" }, new[]
        {
            new[] { "Server PC", "`C:\\ProgramData\\DiagnosticClinicManager\\Logs`" },
            new[] { "Client PC", "`%LocalAppData%\\DiagnosticClinicManager\\Logs`" },
            new[] { "Windows", "Event Viewer > Windows Logs > Application (errors from \".NET Runtime\" or \"Application Error\")" },
        }, new[] { 1.4f, 5f });

        // ===================================================================================== 10
        Doc.H1(col, "Reference", "s10", "10");
        Doc.H2(col, "Names, folders and ports");
        Doc.Table(col, new[] { "Item", "Value" }, new[]
        {
            new[] { "Server service", "**Diagnostic Clinic Manager Server** (`DiagnosticClinicManagerServer`), automatic (delayed) start, runs after PostgreSQL" },
            new[] { "Server program", "`C:\\Program Files\\DiagnosticClinicManager\\Server`" },
            new[] { "Server data (private)", "`C:\\ProgramData\\DiagnosticClinicManager` - settings and passwords (`server.settings.json`), certificate (`certs`), backups (`Backups`), logs (`Logs`). Only administrators and the service can read it." },
            new[] { "Database", "PostgreSQL 17, database `clinic`, its own login `clinic_app` (password generated, stored in the settings file)" },
            new[] { "Network port", "**TCP 5443** (HTTPS), opened to the local network only. Port 5080 (plain HTTP) is not opened to other PCs." },
            new[] { "Client program", "`C:\\Program Files\\DiagnosticClinicManager` (installed per PC); settings in `%LocalAppData%\\DiagnosticClinicManager`" },
            new[] { "Certificate", "Self-signed, valid 5 years from installation. Replace it with the setup option `-ReissueCertificate`; every PC must then trust it again." },
            new[] { "Time zone", "Asia/Kabul by default (day boundaries for reports and statistics)" },
        }, new[] { 1.8f, 5.2f });

        Doc.H2(col, "Useful commands (administrator PowerShell on the server)");
        Doc.Code(col,
            "# is the server running?",
            "Get-Service DiagnosticClinicManagerServer",
            "",
            "# restart it",
            "Restart-Service DiagnosticClinicManagerServer",
            "",
            "# run the setup again (upgrade / repair)",
            "& \"C:\\Program Files\\DiagnosticClinicManager\\Server\\scripts\\Install-Server.ps1\"");

        Doc.H2(col, "Removing the program");
        Doc.P(col, "Use **Settings > Apps** to uninstall the server or the client. Uninstalling the server **does not delete** the database, backups, certificate or settings, so patient data cannot be lost by accident. " +
            "To wipe a test PC completely, also uninstall PostgreSQL and delete `C:\\ProgramData\\DiagnosticClinicManager`.");

        Doc.H2(col, "Security checklist");
        Doc.Bullets(col,
            "Change the first administrator password immediately and give every person their own account.",
            "Keep the passwords file and the backup password off the server PC; delete `INSTALL-SECRETS.txt` from the server.",
            "Never forward port 5443 from the internet to the server. For remote support use a VPN on the router plus Windows Remote Desktop.",
            "Copy backups to an external disk regularly and keep one copy outside the clinic.",
            "Install Windows updates on the server PC, and keep the program up to date.");

        Doc.H2(col, "Final acceptance checklist");
        Doc.Checklist(col,
            "The server PC has a fixed address and never sleeps.",
            "Setup finished with \"Server is running\"; the passwords are saved off the PC and the file is deleted.",
            "The admin password is changed; Clinic Settings, exam types, doctors and users are entered.",
            "Every client PC connects (certificate trusted) and each person can sign in.",
            "A backup was made, copied to an external disk, and the backup password is stored safely.",
            "The server PC was restarted once: the service came back by itself and the clients reconnected.");
    }
}
