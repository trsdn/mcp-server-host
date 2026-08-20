using HostMcp.Core.Models;
using Xunit;

namespace HostMcp.Core.Tests;

public class MnemonicParserTests
{
    [Fact]
    public void Parse_MixedTextAndMnemonics_ProducesOrderedTokens()
    {
        var keys = MnemonicParser.Parse("USER01[tab]SECRET[enter]");

        Assert.Collection(
            keys,
            k => Assert.Equal(Keystroke.Text("USER01"), k),
            k => Assert.Equal(Keystroke.Mnemonic("tab"), k),
            k => Assert.Equal(Keystroke.Text("SECRET"), k),
            k => Assert.Equal(Keystroke.Mnemonic("enter"), k));
    }

    [Fact]
    public void Parse_EmptyString_ReturnsNoTokens() => Assert.Empty(MnemonicParser.Parse(string.Empty));

    [Theory]
    [InlineData("[ENTER]", "enter")]
    [InlineData("[Enter]", "enter")]
    [InlineData("[pf3]", "pf3")]
    [InlineData("[f3]", "pf3")]
    [InlineData("[PF24]", "pf24")]
    [InlineData("[pa1]", "pa1")]
    [InlineData("[clear]", "clear")]
    [InlineData("[backtab]", "backtab")]
    [InlineData("[eraseeof]", "eraseeof")]
    public void Parse_MnemonicAliases_NormalizeToCanonicalName(string input, string expected)
    {
        var key = Assert.Single(MnemonicParser.Parse(input));
        Assert.Equal(KeystrokeKind.Mnemonic, key.Kind);
        Assert.Equal(expected, key.Value);
    }

    [Fact]
    public void Parse_DoubleBracket_YieldsLiteralBracket()
    {
        var key = Assert.Single(MnemonicParser.Parse("[["));
        Assert.Equal(Keystroke.Text("["), key);
    }

    [Fact]
    public void Parse_LiteralBracketInsideText_IsMergedWithSurroundingText()
    {
        var keys = MnemonicParser.Parse("A[[B");
        Assert.Equal([Keystroke.Text("A[B")], keys);
    }

    [Fact]
    public void Parse_UnknownMnemonic_Throws()
    {
        var ex = Assert.Throws<FormatException>(() => MnemonicParser.Parse("[nope]"));
        Assert.Contains("nope", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UnterminatedMnemonic_Throws()
        => Assert.Throws<FormatException>(() => MnemonicParser.Parse("LOGON[enter"));

    [Theory]
    [InlineData("enter", true)]
    [InlineData("clear", true)]
    [InlineData("pf1", true)]
    [InlineData("pf24", true)]
    [InlineData("pa1", true)]
    [InlineData("tab", false)]
    [InlineData("home", false)]
    [InlineData("left", false)]
    public void IsAidKey_ClassifiesSubmittingKeys(string mnemonic, bool expected)
        => Assert.Equal(expected, MnemonicParser.IsAidKey(mnemonic));

    [Fact]
    public void ToCanonicalString_RoundTripsThroughParse()
    {
        const string Input = "USER01[tab]SECRET[enter]";
        var canonical = MnemonicParser.ToCanonicalString(MnemonicParser.Parse(Input));

        Assert.Equal(Input, canonical);
        Assert.Equal(MnemonicParser.Parse(Input), MnemonicParser.Parse(canonical));
    }

    [Fact]
    public void SupportedMnemonics_CoverTheDocumentedSurface()
    {
        var supported = MnemonicParser.SupportedMnemonics;

        Assert.Contains("enter", supported);
        Assert.Contains("clear", supported);
        Assert.Contains("pa1", supported);
        foreach (var pf in Enumerable.Range(1, 24))
        {
            Assert.Contains($"pf{pf}", supported);
        }
    }
}
