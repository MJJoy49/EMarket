using System.Xml.Linq;
using Shouldly;

namespace EMarket.Tests.Architecture;

/// <summary>NFR-68: the layer rules of SRS 3.2 are checked by a test, so a forbidden reference fails the build.</summary>
public sealed class LayerRulesTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EMarket.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("EMarket.slnx was not found above the test folder.");
    }

    private static IReadOnlyList<string> References(string project) =>
        XDocument.Load(Path.Combine(Root, "src", project, $"{project}.csproj")).Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension((string)r.Attribute("Include")!)).Order().ToList();

    private static IEnumerable<string> Sources(string project, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root, "src", project), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Project_references_follow_the_architecture() // Web uses Shared; Api uses BLL and Shared; BLL uses DAL, Domain and Shared; DAL uses Domain
    {
        References("EMarket.Web").ShouldBe(["EMarket.Shared"]);
        References("EMarket.Api").ShouldBe(["EMarket.BLL", "EMarket.Shared"]);
        References("EMarket.BLL").ShouldBe(["EMarket.DAL", "EMarket.Domain", "EMarket.Shared"]);
        References("EMarket.DAL").ShouldBe(["EMarket.Domain"]);
        References("EMarket.Domain").ShouldBeEmpty();
        References("EMarket.Shared").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("EMarket.Web", "EMarket.BLL")]
    [InlineData("EMarket.Web", "EMarket.DAL")]
    [InlineData("EMarket.Web", "EMarket.Domain")]
    [InlineData("EMarket.Api", "EMarket.DAL")]
    public void The_web_and_api_code_never_uses_a_layer_below_its_own(string project, string forbidden)
    {
        var offenders = Sources(project, "*.cs").Concat(Sources(project, "*.cshtml"))
            .Where(f => File.ReadAllText(f).Contains(forbidden + ".") || File.ReadAllText(f).Contains(forbidden + ";"))
            .Select(f => Path.GetRelativePath(Root, f)).ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Every_settings_and_launch_file_is_valid_json()
    {
        var files = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.json", SearchOption.AllDirectories)
            .Where(f => (Path.GetFileName(f).StartsWith("appsettings") || Path.GetFileName(f) == "launchSettings.json")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
        foreach (var file in files)
        {
            Should.NotThrow(() => System.Text.Json.JsonDocument.Parse(File.ReadAllText(file), new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip }), file);
        }
    }

    [Fact]
    public void No_secret_is_kept_in_the_settings_files()
    {
        var settings = Directory.EnumerateFiles(Path.Combine(Root, "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
        foreach (var file in settings)
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("Password=", Case.Insensitive, $"{file} holds a password"); // NFR-42
            text.ShouldNotMatch("\"(Key|StorePassword|Secret)\"\\s*:\\s*\"[^\"]+\"");
        }
    }

    [Fact]
    public void The_ledger_has_no_update_or_delete_in_the_code()
    {
        var offenders = Sources("EMarket.BLL", "*.cs").Concat(Sources("EMarket.Api", "*.cs"))
            .Where(f => File.ReadAllText(f) is var t && (t.Contains("LedgerEntries.Remove") || t.Contains("LedgerEntries.Update") || t.Contains("LedgerEntries.ExecuteDelete") || t.Contains("LedgerEntries.ExecuteUpdate")))
            .ToList();

        offenders.ShouldBeEmpty(); // FR-24.6: ledger lines are only added
    }
}
