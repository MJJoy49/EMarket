namespace EMarket.Shared.Constants;

/// <summary>System messages MSG01 to MSG40 (SRS 4.10). Text in braces is filled in at run time.</summary>
public static class Messages
{
    public const string RegistrationDone = "Registration done. Please check your email to verify your account.";
    public const string EmailTaken = "This email is already registered.";
    public const string Required = "This field is required.";
    public const string Invalid = "This value is not valid. Please check it.";
    public const string WrongLogin = "Email or password is wrong.";
    public const string Locked = "Too many failed tries. This account is locked for {0} minutes.";
    public const string Blocked = "This account is blocked. Please contact support.";
    public const string BadLink = "This link is not valid or has expired.";
    public const string WrongOldPassword = "The old password is wrong.";
    public const string ResetSent = "If this email is registered, a reset link has been sent.";
    public const string Saved = "Saved successfully.";
    public const string AddedToCart = "Added to your cart.";
    public const string NotEnoughStock = "Not enough stock for {0}. Only {1} left.";
    public const string BadCoupon = "This coupon is not valid.";
    public const string CouponApplied = "Coupon applied. You saved Tk {0}.";
    public const string OrderPlaced = "Your order has been placed. Order number: {0}.";
    public const string PaymentFailed = "Payment failed. You can try again.";
    public const string PaymentCancelled = "Payment was cancelled. You can pay again before the order expires.";
    public const string OrderCancelled = "Your order has been cancelled.";
    public const string CannotCancel = "This order can no longer be cancelled.";
    public const string ReviewPosted = "Thank you. Your review was posted.";
    public const string ReturnSent = "Your return request was sent.";
    public const string ReturnWindowOver = "The return period for this order is over.";
    public const string PayoutSent = "Your payout request was sent.";
    public const string OverBalance = "The amount is more than your available balance.";
    public const string ApplicationSent = "Your seller application was submitted. We will review it.";
    public const string ApplicationResult = "Your seller application was {0}.";
    public const string ProductSubmitted = "Your product was submitted for approval.";
    public const string NotAllowed = "You are not allowed to do this.";
    public const string NotFound = "We could not find what you asked for.";
    public const string ServiceDown = "The service is not available now. Please try again soon.";
    public const string TooManyRequests = "Too many requests. Please wait and try again.";
    public const string VerifyEmailFirst = "Please verify your email first.";
    public const string TooManyLinks = "Too many requests for a new link. Try again in an hour.";
    public const string OwnProduct = "You cannot buy a product from your own shop.";
    public const string AlreadyDone = "This request was already done. Showing the first result.";
    public const string BadFile = "The file is not valid. Use a JPG or PNG up to 2 MB.";
    public const string LowCredit = "Your store credit is not enough.";
    public const string ShopUnavailable = "This shop is not available now.";
    public const string Conflict = "This cannot be done now because the data changed or a rule does not allow it.";
}
