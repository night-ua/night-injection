using NightInjection.Core.Services;
using NightInjection.Core.Validation;

namespace NightInjection.Tests;

public sealed class ValidationAndRulesTests
{
    [Theory]
    [InlineData("220")]
    [InlineData(" 570 ")]
    [InlineData("18446744073709551615")]
    public void AppId_AcceptsPositiveAsciiNumbers(string value) => Assert.True(AppIdValidator.IsValid(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("١٢٣")]
    [InlineData("12a")]
    [InlineData("18446744073709551616")]
    public void AppId_RejectsUnsafeOrNonNumericValues(string? value) => Assert.False(AppIdValidator.IsValid(value));

    [Fact]
    public void SpinRule_KeepsOnlyAddAppIdLinesAndSignature()
    {
        const string source = "addappid(220)\nsetManifestid(220,1)\nprint('x')";
        var result = RepositoryContentRules.TransformLua(source, RepositoryRule.SpinAddAppIdOnly);
        Assert.Contains("addappid(220)", result, StringComparison.Ordinal);
        Assert.DoesNotContain("setManifestid", result, StringComparison.Ordinal);
        Assert.DoesNotContain("print", result, StringComparison.Ordinal);
        Assert.EndsWith(RepositoryContentRules.SignatureLine, result, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveManifestRule_PreservesOtherLuaLines()
    {
        const string source = "addappid(10)\nsetManifestid(10,20)\nreturn true";
        var result = RepositoryContentRules.TransformLua(source, RepositoryRule.RemoveManifestId);
        Assert.Contains("addappid(10)", result, StringComparison.Ordinal);
        Assert.Contains("return true", result, StringComparison.Ordinal);
        Assert.DoesNotContain("setManifestid", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryResolution_MatchesLegacyPriorityGroups()
    {
        Assert.Equal(RepositoryRule.ProjectLightning, RepositoryContentRules.Resolve("x/ProjectLightningManifests"));
        Assert.Equal(RepositoryRule.SpinAddAppIdOnly, RepositoryContentRules.Resolve("x/SPIN0ZAi/y"));
        Assert.Equal(RepositoryRule.RemoveManifestId, RepositoryContentRules.Resolve("x/SteamAutoCracks/y"));
        Assert.Equal(RepositoryRule.Unsupported, RepositoryContentRules.Resolve("example/unknown"));
    }
}
