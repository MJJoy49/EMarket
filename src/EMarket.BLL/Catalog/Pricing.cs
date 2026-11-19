namespace EMarket.BLL.Catalog;

public static class Pricing
{
    /// <summary>Price after the product discount, in Tk with 2 decimals.</summary>
    public static decimal Final(decimal price, decimal discountPercent) =>
        Math.Round(price * (100 - discountPercent) / 100, 2, MidpointRounding.AwayFromZero);

    /// <summary>The form asks for a discount price; the table stores a percent (SRS 6.3).</summary>
    public static decimal PercentFrom(decimal price, decimal? discountPrice) =>
        discountPrice is { } d ? Math.Round((price - d) / price * 100, 2, MidpointRounding.AwayFromZero) : 0;

    public static string MediaUrl(string? path) => path is null ? "" : $"/api/v1/media/{path}";
}
