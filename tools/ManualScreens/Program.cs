using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Auth;
using NasimXray.Shared.Dtos.Billing;
using NasimXray.Shared.Dtos.Clinic;
using NasimXray.Shared.Dtos.ExamOrders;
using NasimXray.Shared.Enums;

// Renders the manual's screenshots from the real client windows. Everything shown is made-up sample data.
internal static class Program
{
    private static string _outDir = "";

    [STAThread]
    private static int Main(string[] args)
    {
        _outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "images");
        Directory.CreateDirectory(_outDir);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var res = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "NasimXray.Client", "Resources")) + Path.DirectorySeparatorChar;
        foreach (var f in new[] { "Colors.xaml", "Styles.xaml", "Strings.en.xaml" })
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(File.OpenRead(res + f)));
        var asm = typeof(ApiClient).Assembly;
        foreach (var name in new[]
        {
            "DateToCalendarConverter", "BoolToVisibilityConverter", "InverseBoolToVisibilityConverter", "StatusToLocalizedConverter",
            "StatusVisibilityConverter", "StatusToBadgeKindConverter", "NullToVisibilityConverter", "InvoiceStatusToLocalizedConverter",
            "FileSizeConverter", "MoneyConverter",
        })
            app.Resources[name] = Activator.CreateInstance(asm.GetType($"NasimXray.Client.Converters.{name}")!)!;
        app.DispatcherUnhandledException += (_, e) => { Console.WriteLine("UNHANDLED: " + e.Exception.Message); e.Handled = true; };

        SessionService.Start(new LoginResponse
        {
            FullName = "Administrator", RoleDisplayName = "Admin", Role = UserRole.Admin,
            Permissions = Enum.GetValues<Permission>().ToList(),
        });

        // a clinic identity for the screenshots (the real one comes from the server)
        var svc = typeof(ClinicInfoService);
        svc.GetProperty("Profile")!.SetValue(null, new ClinicProfileDto { NameEnglish = "Example Diagnostic Clinic", NameDari = "کلینیک تشخیصی نمونه", IsConfigured = true });
        svc.GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);

        // 1. sign-in
        Shot(new NasimXray.Client.Views.LoginWindow(), "01_login.png", cropTo: w => (FrameworkElement)((Grid)w.Content).Children[0]);

        // 2. server address window with the certificate to confirm
        Shot(new NasimXray.Client.Views.ServerSettingsWindow("https://192.168.1.10:5443/", null), "02_server_address.png",
            w =>
            {
                ((Border)w.FindName("CertPanel")).Visibility = Visibility.Visible;
                ((TextBlock)w.FindName("ThumbprintText")).Text = "3A 9F C2 7E 04 B1 D8 55 6C 21 E9 70 AA 13 5B F4 88 0D 62 CE";
            }, cropTo: w => (FrameworkElement)w.Content);

        // 3. main window (sidebar overview)
        Shot(new NasimXray.Client.MainWindow(new ApiClient("http://localhost:1")), "03_main.png", w =>
        {
            // the real window loads this PC's cached clinic name; show the sample identity instead
            svc.GetProperty("Profile")!.SetValue(null, new ClinicProfileDto { NameEnglish = "Example Diagnostic Clinic", IsConfigured = true });
            svc.GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        });

        // 4. clinic settings
        Shot(Host(new NasimXray.Client.Views.ClinicSettingsView(new ApiClient("http://localhost:1")), 980, 900), "04_clinic_settings.png",
            w =>
            {
                var v = (FrameworkElement)((Window)w).Content;
                ((TextBox)v.FindName("NameEnglishBox")).Text = "Example Diagnostic Clinic";
                ((TextBox)v.FindName("NameDariBox")).Text = "کلینیک تشخیصی نمونه";
                ((TextBox)v.FindName("TaglineBox")).Text = "X-ray · Ultrasound · Echocardiography";
                ((TextBox)v.FindName("AddressBox")).Text = "Main Street, Kabul";
                ((TextBox)v.FindName("PhoneBox")).Text = "+93 700 000 000";
                ((TextBox)v.FindName("EmailBox")).Text = "info@example-clinic.org";
            });

        // 5. exams register
        var exams = new NasimXray.Client.Views.ExamsView(new ApiClient("http://localhost:1"));
        Shot(Host(exams, 1000, 560), "05_exams.png", w =>
        {
            var g = (DataGrid)exams.FindName("Grid");
            g.ItemsSource = new List<ExamOrderDto>
            {
                new() { ExamNumber = "E-000041", PatientName = "Ahmad Karimi", PatientCode = "P-00239", ExamTypeName = "Chest X-ray", Modality = "X-ray", Price = 500m, CreatedAt = DateTime.UtcNow.AddMinutes(-50), Status = ExamOrderStatus.Pending, InvoiceStatus = InvoiceStatus.Paid, ReferringPhysicianName = "Dr. Bahar" },
                new() { ExamNumber = "E-000042", PatientName = "Sara Mohammadi", PatientCode = "P-00240", ExamTypeName = "Abdominal ultrasound", Modality = "Ultrasound", Price = 800m, CreatedAt = DateTime.UtcNow.AddMinutes(-35), Status = ExamOrderStatus.InProgress, InvoiceStatus = InvoiceStatus.PartiallyPaid, ReferringPhysicianName = "Dr. Naseri" },
                new() { ExamNumber = "E-000043", PatientName = "Ali Rahimi", PatientCode = "P-00241", ExamTypeName = "Echocardiography", Modality = "Echo", Price = 1500m, CreatedAt = DateTime.UtcNow.AddMinutes(-20), Status = ExamOrderStatus.Completed, InvoiceStatus = InvoiceStatus.Paid, ReferringPhysicianName = "Dr. Bahar" },
                new() { ExamNumber = "E-000044", PatientName = "Mina Azizi", PatientCode = "P-00242", ExamTypeName = "Chest X-ray", Modality = "X-ray", Price = 500m, DiscountAmount = 500m, DiscountReason = "Charity", CreatedAt = DateTime.UtcNow.AddMinutes(-5), Status = ExamOrderStatus.Reported, InvoiceStatus = InvoiceStatus.Paid },
            };
        });

        // 6. invoices with totals
        var invoices = new NasimXray.Client.Views.InvoicesView(new ApiClient("http://localhost:1"));
        Shot(Host(invoices, 1000, 500), "06_invoices.png", w =>
        {
            ((DataGrid)invoices.FindName("Grid")).ItemsSource = new List<InvoiceDto>
            {
                new() { Id = 1, PatientName = "Ahmad Karimi", Total = 500m, AmountPaid = 500m, Balance = 0, Status = InvoiceStatus.Paid, CreatedAt = DateTime.UtcNow },
                new() { Id = 2, PatientName = "Sara Mohammadi", Total = 800m, AmountPaid = 300m, Balance = 500m, Status = InvoiceStatus.PartiallyPaid, CreatedAt = DateTime.UtcNow },
                new() { Id = 3, PatientName = "Ali Rahimi", Total = 1500m, AmountPaid = 1500m, Balance = 0, Status = InvoiceStatus.Paid, CreatedAt = DateTime.UtcNow },
            };
            ((TextBlock)invoices.FindName("TotalsCaption")).Text = "Totals for all 3 matching invoices";
            ((TextBlock)invoices.FindName("SumTotalText")).Text = NasimXray.Shared.Currency.Format(2800m);
            ((TextBlock)invoices.FindName("SumPaidText")).Text = NasimXray.Shared.Currency.Format(2300m);
            var bal = (TextBlock)invoices.FindName("SumBalanceText");
            bal.Text = NasimXray.Shared.Currency.Format(500m);
            bal.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        });

        // 7. new check-in
        Shot(new NasimXray.Client.Views.CheckInWindow(new ApiClient("http://localhost:1")), "07_checkin.png",
            w => { w.SizeToContent = SizeToContent.Manual; w.Height = 560; });

        Console.WriteLine("screenshots written to " + _outDir);
        return 0;
    }

    private static Window Host(UIElement content, double width, double height) =>
        new Window { Content = content, Width = width, Height = height };

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

    private static void Shot(Window w, string file, Action<Window>? prepare = null, Func<Window, FrameworkElement>? cropTo = null)
    {
        w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = -12000; w.Top = 0; w.ShowInTaskbar = false; w.ShowActivated = false;
        w.Show(); Pump(); w.UpdateLayout(); Pump();
        prepare?.Invoke(w);
        Pump(); w.UpdateLayout(); Pump();

        const double scale = 2; // 192 dpi: sharp when printed
        var bmp = new RenderTargetBitmap((int)(w.ActualWidth * scale), (int)(w.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(w);

        BitmapSource result = bmp;
        if (cropTo is not null)
        {
            // dialogs: keep just the card (plus a small margin) instead of the empty window around it
            var element = cropTo(w);
            var box = element.TransformToAncestor(w).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            const double pad = 12;
            var x0 = (int)Math.Max(0, (box.Left - pad) * scale); var y0 = (int)Math.Max(0, (box.Top - pad) * scale);
            var x1 = (int)Math.Min(bmp.PixelWidth, (box.Right + pad) * scale); var y1 = (int)Math.Min(bmp.PixelHeight, (box.Bottom + pad) * scale);
            result = new CroppedBitmap(bmp, new Int32Rect(x0, y0, x1 - x0, y1 - y0));
        }

        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(result));
        using (var fs = File.Create(Path.Combine(_outDir, file))) enc.Save(fs);
        Console.WriteLine($"  {file}  {result.PixelWidth}x{result.PixelHeight}");
        w.Close();
    }
}
