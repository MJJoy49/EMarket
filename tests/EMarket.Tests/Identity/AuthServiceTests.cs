using EMarket.BLL.Common;
using EMarket.BLL.Identity;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Shouldly;

namespace EMarket.Tests.Identity;

public sealed class AuthServiceTests
{
    private const string GoodPassword = "Tiger7Lamp";
    private readonly FakeClock clock = new();
    private readonly AppDbContext db;
    private readonly AuthService auth;

    public AuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        db = new AppDbContext(options);
        db.AspNetRoles.Add(new AspNetRole { Id = Guid.NewGuid(), Name = Roles.Buyer, NormalizedName = "BUYER" });
        db.SaveChanges();
        var cache = new MemoryCache(new MemoryCacheOptions());
        auth = new AuthService(db, new PasswordHasher<User>(), new FakeIssuer(), new SettingsReader(db, cache), clock, cache);
    }

    private static RegisterRequest Registration(string email = "karim@example.com") => new()
    {
        FullName = "Karim Uddin",
        Email = email,
        Phone = "01712345678",
        Password = GoodPassword,
        ConfirmPassword = GoodPassword,
        AcceptTerms = true,
    };

    private static LoginRequest Login(string password = GoodPassword) => new() { Email = "karim@example.com", Password = password };

    [Theory]
    [InlineData("abc12345", false)]    // no uppercase (FR-1.3)
    [InlineData("Password1", false)]   // common (NFR-18)
    [InlineData("Ab1", false)]         // too short
    [InlineData("Tiger7Lamp", true)]
    public void Password_policy_follows_the_srs(string password, bool expected) =>
        PasswordPolicy.IsValid(password).ShouldBe(expected);

    [Fact]
    public async Task Register_gives_the_buyer_role_and_stores_only_a_hash()
    {
        await auth.RegisterAsync(Registration());

        var user = await db.Users.Include(u => u.Roles).SingleAsync();
        user.Roles.Single().Name.ShouldBe(Roles.Buyer);
        user.PasswordHash.ShouldNotContain(GoodPassword);
        (await db.Notifications.SingleAsync()).Title.ShouldBe("Welcome to EMarket");
    }

    [Fact]
    public async Task Register_refuses_the_same_email_in_capitals()
    {
        await auth.RegisterAsync(Registration());

        var error = await Should.ThrowAsync<AppException>(() => auth.RegisterAsync(Registration("KARIM@EXAMPLE.COM")));

        error.Message.ShouldBe(Messages.EmailTaken);
        (await db.Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Sixth_login_is_refused_even_with_the_right_password_until_the_lock_ends()
    {
        await auth.RegisterAsync(Registration());
        for (var i = 0; i < 5; i++)
        {
            await Should.ThrowAsync<AppException>(() => auth.LoginAsync(Login("Wrong7Pass"), null));
        }

        var locked = await Should.ThrowAsync<AppException>(() => auth.LoginAsync(Login(), null));
        locked.Message.ShouldBe(string.Format(Messages.Locked, 15));

        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        (await auth.LoginAsync(Login(), null)).AccessToken.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Wrong_email_and_wrong_password_show_the_same_message()
    {
        await auth.RegisterAsync(Registration());

        var wrongPassword = await Should.ThrowAsync<AppException>(() => auth.LoginAsync(Login("Wrong7Pass"), null));
        var wrongEmail = await Should.ThrowAsync<AppException>(
            () => auth.LoginAsync(new LoginRequest { Email = "nobody@example.com", Password = GoodPassword }, null));

        wrongEmail.Message.ShouldBe(wrongPassword.Message);
    }

    [Fact]
    public async Task Refresh_rotates_and_replaying_an_old_token_ends_every_session()
    {
        await auth.RegisterAsync(Registration());
        var first = await auth.LoginAsync(Login(), null);
        var second = await auth.RefreshAsync(first.RefreshToken, null);

        await Should.ThrowAsync<AppException>(() => auth.RefreshAsync(first.RefreshToken, null));

        var afterReplay = await Should.ThrowAsync<AppException>(() => auth.RefreshAsync(second.RefreshToken, null));
        afterReplay.StatusCode.ShouldBe(401);
    }

    [Fact]
    public async Task Logout_makes_the_refresh_token_stop_working()
    {
        await auth.RegisterAsync(Registration());
        var tokens = await auth.LoginAsync(Login(), null);

        await auth.LogoutAsync(tokens.RefreshToken);

        (await Should.ThrowAsync<AppException>(() => auth.RefreshAsync(tokens.RefreshToken, null))).StatusCode.ShouldBe(401);
    }

    [Fact]
    public async Task Fourth_resend_in_one_hour_is_refused()
    {
        await auth.RegisterAsync(Registration());
        var user = await db.Users.SingleAsync();
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        for (var i = 0; i < 3; i++)
        {
            await auth.ResendVerificationAsync(user.Id);
        }

        var error = await Should.ThrowAsync<AppException>(() => auth.ResendVerificationAsync(user.Id));

        error.Message.ShouldBe(Messages.TooManyLinks);
    }

    [Fact]
    public async Task Blocked_user_cannot_log_in()
    {
        await auth.RegisterAsync(Registration());
        (await db.Users.SingleAsync()).Status = "Blocked";
        await db.SaveChangesAsync();

        (await Should.ThrowAsync<AppException>(() => auth.LoginAsync(Login(), null))).Message.ShouldBe(Messages.Blocked);
    }

    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;
    }

    private sealed class FakeIssuer : IAccessTokenIssuer
    {
        public AccessToken Issue(User user, IReadOnlyList<string> roles) => new("access-token", DateTime.UtcNow.AddMinutes(15));
    }
}
