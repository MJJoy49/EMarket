namespace EMarket.BLL.Orders;

public enum Actor
{
    Buyer,
    Seller,
    Admin,
    System,
}

/// <summary>The allowed order status changes of SRS 4.8 (BR-13, BR-14). Anything else is refused with 409.</summary>
public static class OrderRules
{
    public const string AwaitingPayment = "AwaitingPayment";
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string Processing = "Processing";
    public const string Shipped = "Shipped";
    public const string Delivered = "Delivered";
    public const string Cancelled = "Cancelled";

    public static readonly string[] Open = [AwaitingPayment, Pending, Confirmed, Processing, Shipped];

    private static readonly Dictionary<(string From, string To), Actor[]> Moves = new()
    {
        [(AwaitingPayment, Pending)] = [Actor.System],
        [(AwaitingPayment, Cancelled)] = [Actor.Buyer, Actor.Admin, Actor.System],
        [(Pending, Confirmed)] = [Actor.Seller],
        [(Pending, Cancelled)] = [Actor.Buyer, Actor.Seller, Actor.Admin, Actor.System],
        [(Confirmed, Processing)] = [Actor.Seller],
        [(Confirmed, Cancelled)] = [Actor.Buyer, Actor.Seller, Actor.Admin],
        [(Processing, Shipped)] = [Actor.Seller],
        [(Processing, Cancelled)] = [Actor.Seller, Actor.Admin],
        [(Shipped, Delivered)] = [Actor.Seller, Actor.Admin],
    };

    public static bool CanMove(string from, string to, Actor actor) =>
        Moves.TryGetValue((from, to), out var actors) && actors.Contains(actor);

    public static IReadOnlyList<string> NextFor(string from, Actor actor) =>
        Moves.Where(m => m.Key.From == from && m.Value.Contains(actor)).Select(m => m.Key.To).ToList();
}
