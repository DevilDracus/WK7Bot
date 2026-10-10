using System.IO;
using System.Text.RegularExpressions;
using WK7Bot.Core.Utilities;
using Xunit;

namespace WK7Bot.Tests;

/// <summary>
/// Guards the add-on version flowing into the reported version: Home Assistant shows the value from
/// <see cref="AppInformation"/>, and it must always agree with the <c>version:</c> in
/// <c>WK7Bot/config.yaml</c> (the add-on image tag), instead of silently regressing to 1.0.0.
/// </summary>
public class AppInformationTests
{
    [Fact]
    public void Version_MatchesAddOnVersionInConfigYaml()
    {
        var expected = ReadConfigYamlVersion();
        Assert.False(string.IsNullOrWhiteSpace(expected), "version: could not be read from WK7Bot/config.yaml");

        Assert.Equal(expected, AppInformation.Version);
    }

    [Fact]
    public void Version_IsNotTheAssemblyDefault()
    {
        // The SDK default when no version is configured anywhere; a regression to this value means
        // config.yaml stopped reaching the build.
        Assert.NotEqual("1.0.0", AppInformation.Version);
    }

    /// <summary>
    /// Locates WK7Bot/config.yaml from the test output directory.
    /// </summary>
    private static string ReadConfigYamlVersion()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "WK7Bot", "config.yaml");
                if (File.Exists(candidate))
                {
                    var match = Regex.Match(
                        File.ReadAllText(candidate),
                        @"(?m)^version:\s*""?([^""\s]+)""?");
                    return match.Success ? match.Groups[1].Value : string.Empty;
                }
            }
        }

        return string.Empty;
    }
}
