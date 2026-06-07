namespace InCleanHome.IamService.Infrastructure.Tokens;

public class TokenSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "incleanhome";
    public string Audience { get; set; } = "incleanhome-api";
    public int ExpirationDays { get; set; } = 7;
}
