using System.Text.RegularExpressions;
using EMarket.Shared.Constants;

namespace EMarket.BLL.Identity;

/// <summary>NFR-18: 8 to 64 characters, upper, lower, digit, and not a very common password.</summary>
public static partial class PasswordPolicy
{
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password1", "Password12", "Password123", "Password1234", "Passw0rd", "Passw0rd1", "P@ssw0rd", "P@ssw0rd1", "P@ssword1", "Pa55word",
        "Pa55w0rd", "Qwerty123", "Qwerty1234", "Qwerty12", "Qwertyuiop1", "Abc12345", "Abcd1234", "Abcde123", "Abcdef12", "Abc123456",
        "Aa123456", "Aa12345678", "Aa112233", "Admin123", "Admin1234", "Admin12345", "Administrator1", "Welcome1", "Welcome12", "Welcome123",
        "Letmein1", "Letmein12", "Letmein123", "Monkey123", "Dragon123", "Dragon12", "Master123", "Master12", "Sunshine1", "Sunshine12",
        "Princess1", "Princess12", "Football1", "Football12", "Baseball1", "Baseball12", "Iloveyou1", "Iloveyou12", "Trustno1", "Trustno12",
        "Superman1", "Superman12", "Batman123", "Michael1", "Michael12", "Jennifer1", "Jordan123", "Shadow123", "Shadow12", "Charlie1",
        "Charlie12", "Ashley123", "Daniel123", "Andrew123", "Joshua123", "Hello123", "Hello1234", "Freedom1", "Whatever1", "Computer1",
        "Computer12", "Internet1", "Samsung1", "Samsung12", "Google123", "Facebook1", "Change123", "Changeme1", "Changeme12", "Test1234",
        "Test12345", "Testing1", "Testing123", "User1234", "User12345", "Login123", "Login1234", "Secret123", "Secret12", "Summer2024",
        "Summer2025", "Summer2026", "Winter2024", "Winter2025", "Winter2026", "Spring2026", "Bangladesh1", "Bangladesh71", "Dhaka1234", "Emarket1",
    };

    public static bool IsValid(string password) =>
        PasswordFormat().IsMatch(password) && !Common.Contains(password);

    [GeneratedRegex(ValidationPatterns.Password)]
    private static partial Regex PasswordFormat();
}
