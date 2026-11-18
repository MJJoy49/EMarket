namespace EMarket.Shared.Constants;

/// <summary>Field formats from SRS 4.7.</summary>
public static class ValidationPatterns
{
    public const string Phone = @"^01[3-9]\d{8}$";
    public const string Name = @"^[\p{L} .'\-]{2,100}$";
    public const string Password = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,64}$";
}
