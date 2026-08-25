using GuildSimulator.Game.Data;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Guild static state")]
public class MasterDataDistributionTests
{
    [Fact]
    public void CanonicalMasterDataPassesValidation()
    {
        string canonicalData = Path.Combine(FindRepoRoot(), "GuildSimulator.Game", "Data");

        var errors = MasterValidator.Validate(MasterLoader.Load(canonicalData));

        Assert.Empty(errors);
    }

    [Fact]
    public void RuntimeMasterDataCopyMatchesCanonicalFiles()
    {
        string repoRoot = FindRepoRoot();
        string canonicalData = Path.Combine(repoRoot, "GuildSimulator.Game", "Data");
        var distributionCopies = new Dictionary<string, string>
        {
            ["test runtime"] = Path.Combine(AppContext.BaseDirectory, "Data"),
        };
        string[] canonicalFiles = JsonFileNames(canonicalData);

        foreach ((string label, string distributionData) in distributionCopies)
        {
            Assert.True(Directory.Exists(distributionData),
                $"{label} master-data directory is missing: {distributionData}");
            Assert.Equal(canonicalFiles, JsonFileNames(distributionData));

            foreach (string fileName in canonicalFiles)
            {
                string copyPath = Path.Combine(distributionData, fileName);
                byte[] canonical = File.ReadAllBytes(Path.Combine(canonicalData, fileName));
                Assert.True(canonical.SequenceEqual(File.ReadAllBytes(copyPath)),
                    $"{label} master data is stale: {copyPath}");
            }
        }
    }

    [Fact]
    public void MasterDataCopyPoliciesDoNotTrustDestinationTimestamps()
    {
        string repoRoot = FindRepoRoot();

        AssertLinkedJsonCopyPolicy(
            Path.Combine(repoRoot, "GuildSimulator.Cli", "GuildSimulator.Cli.csproj"),
            copyToPublish: true);
        AssertLinkedJsonCopyPolicy(
            Path.Combine(repoRoot, "GuildSimulator.Balance", "GuildSimulator.Balance.csproj"),
            copyToPublish: true);
        AssertLinkedJsonCopyPolicy(
            Path.Combine(repoRoot, "GuildSimulator.Tests", "GuildSimulator.Tests.csproj"),
            copyToPublish: false);

        XDocument web = XDocument.Load(Path.Combine(
            repoRoot, "GuildSimulator.Web", "GuildSimulator.Web.csproj"));
        XElement target = Assert.Single(web.Descendants("Target"),
            element => (string?)element.Attribute("Name") == "CopyMasterData");
        string[] beforeTargets = ((string?)target.Attribute("BeforeTargets") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Contains("BeforeBuild", beforeTargets);
        Assert.Contains("BeforeRebuild", beforeTargets);
        Assert.Contains("ComputeFilesToPublish", beforeTargets);
        Assert.Null(target.Attribute("Condition"));

        XElement copy = Assert.Single(target.Elements("Copy"));
        Assert.Equal("false", (string?)copy.Attribute("SkipUnchangedFiles"));
        Assert.Equal("@(MasterDataJson)", (string?)copy.Attribute("SourceFiles"));
        Assert.Equal(@"$(MSBuildProjectDirectory)\wwwroot\Data",
            (string?)copy.Attribute("DestinationFolder"));

        string deployWorkflow = File.ReadAllText(Path.Combine(
            repoRoot, ".github", "workflows", "deploy-web.yml"));
        Assert.Contains("cd GuildSimulator.Game/Data", deployWorkflow);
        Assert.Contains("cd publish/wwwroot/Data", deployWorkflow);
        Assert.Contains("-name '*.json'", deployWorkflow);
        Assert.Contains("sha256sum", deployWorkflow);
        Assert.Contains("diff -u", deployWorkflow);
        Assert.DoesNotContain(
            "diff -qr GuildSimulator.Game/Data publish/wwwroot/Data",
            deployWorkflow);
    }

    static string FindRepoRoot()
    {
        string? embeddedRoot = typeof(MasterDataDistributionTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "AIRPG.RepositoryRoot")?.Value;
        if (!string.IsNullOrWhiteSpace(embeddedRoot)
            && File.Exists(Path.Combine(embeddedRoot, "GuildSimulator.sln")))
            return Path.GetFullPath(embeddedRoot);

        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GuildSimulator.sln")))
                directory = directory.Parent;
            if (directory != null) return directory.FullName;
        }

        throw new InvalidOperationException(
            $"Could not find GuildSimulator.sln from {Directory.GetCurrentDirectory()} or {AppContext.BaseDirectory}");
    }

    static string[] JsonFileNames(string directory) =>
        Directory.GetFiles(directory, "*.json")
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    static void AssertLinkedJsonCopyPolicy(string projectPath, bool copyToPublish)
    {
        XDocument project = XDocument.Load(projectPath);
        XElement item = Assert.Single(project.Descendants("None"), element =>
            ((string?)element.Attribute("Include"))?.EndsWith(
                @"GuildSimulator.Game\Data\*.json", StringComparison.Ordinal) == true);

        Assert.Equal("Always", item.Element("CopyToOutputDirectory")?.Value);
        if (copyToPublish)
            Assert.Equal("Always", item.Element("CopyToPublishDirectory")?.Value);
        else
            Assert.Null(item.Element("CopyToPublishDirectory"));
    }
}
