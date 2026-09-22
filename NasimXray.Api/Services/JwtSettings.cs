namespace NasimXray.Api.Services;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "NasimXrayClinic";
    public string Audience { get; set; } = "NasimXrayClinicClient";
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 14;
}
