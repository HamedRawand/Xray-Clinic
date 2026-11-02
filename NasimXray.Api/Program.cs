using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NasimXray.Api.Data;
using NasimXray.Api.Services;
using NasimXray.Shared.Enums;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Calling ConfigureKestrel with any explicit Listen call replaces --urls/ASPNETCORE_URLS entirely
// (confirmed via Kestrel's own startup warning) rather than adding to it — so once HTTPS needs
// explicit configuration, HTTP must be configured explicitly here too, not left to --urls.
var httpsSettings = builder.Configuration.GetSection(HttpsSettings.SectionName).Get<HttpsSettings>() ?? new HttpsSettings();
var httpPort = builder.Configuration.GetValue("Http:Port", 5080);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(httpPort);

    if (httpsSettings.Enabled)
    {
        if (string.IsNullOrWhiteSpace(httpsSettings.PfxPath) || !File.Exists(httpsSettings.PfxPath))
            throw new InvalidOperationException($"Https:Enabled is true but Https:PfxPath ('{httpsSettings.PfxPath}') was not found.");

        options.ListenAnyIP(httpsSettings.Port, listenOptions =>
            listenOptions.UseHttps(httpsSettings.PfxPath, httpsSettings.PfxPassword));
    }
});

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(NasimXray.Shared.AppFolders.Resolve(Environment.SpecialFolder.LocalApplicationData), "Logs", "api_.log"),
        rollingInterval: RollingInterval.Day)
    .Enrich.FromLogContext()
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Clinic API", Version = "v1" });
    var bearerScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter a valid JWT access token.",
    };
    c.AddSecurityDefinition("Bearer", bearerScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, [] },
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuditService>();

builder.Services.Configure<ClinicSettings>(builder.Configuration.GetSection(ClinicSettings.SectionName));
builder.Services.AddSingleton<ClinicClock>();

builder.Services.Configure<BackupSettings>(builder.Configuration.GetSection(BackupSettings.SectionName));
builder.Services.AddScoped<BackupService>();
builder.Services.AddHostedService<BackupSchedulerService>();

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");

if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
    throw new InvalidOperationException(
        "Jwt:Secret is not configured. Set it via user-secrets (dev) or the Jwt__Secret environment variable (production) — never commit a real secret to appsettings.json.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwtSettings.Secret)),
            // Microsoft.IdentityModel's default ClockSkew is 5 minutes — far more generous than
            // this app needs, and it means Jwt:AccessTokenMinutes doesn't reflect the token's
            // real accepted lifetime. 30s tolerates ordinary clock drift between the server and
            // clinic PCs without silently extending the token's effective life by 5x.
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Enum.GetValues<Permission>())
    {
        options.AddPolicy(permission.ToString(), policy =>
            policy.Requirements.Add(new PermissionRequirement(permission)));
    }
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
// Deliberately NOT calling app.UseHttpsRedirection() — until every clinic PC has gone through
// the client's trust-on-first-use certificate pinning flow (ServerSettingsWindow), a silent
// HTTP→HTTPS redirect would break them: their ApiClient only wires up the pinned-thumbprint
// validation callback when explicitly configured for an https:// base URL, so a client still
// pointed at http:// that gets redirected hits a generic, unfixable TLS trust failure instead of
// the intended "trust this certificate" prompt. HTTP and HTTPS are both available; each clinic
// PC opts into HTTPS explicitly via Server Settings when ready, not all-at-once via a redirect.
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/api/system/version", () => new
{
    version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
}).AllowAnonymous();

app.Run();
