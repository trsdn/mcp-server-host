using HostMcp.ComInterop.Ehllapi;
using HostMcp.ComInterop.Pcomm;
using HostMcp.Core.Models;
using Xunit;

namespace HostMcp.ComInterop.Tests;

/// <summary>
/// Provider tests that must pass on a machine with no emulator installed, which is exactly the
/// situation in CI. They assert the "no emulator" behaviour rather than real host interaction.
/// </summary>
public class ProviderTests
{
    [Fact]
    public void CreateDefault_OrdersProvidersForAutoDetection()
    {
        var providers = TerminalProviderFactory.CreateDefault();

        Assert.Equal(["pcomm", "ehllapi"], providers.Select(p => p.Name));
        Assert.True(providers[0].Priority < providers[1].Priority);
    }

    [Fact]
    public void CreateDefault_WithFake_PutsTheFakeLast()
    {
        var providers = TerminalProviderFactory.CreateDefault(includeFake: true);

        Assert.Equal("fake", providers[^1].Name);
        Assert.Equal(int.MaxValue, providers[^1].Priority);
    }

    [Fact]
    public async Task Probing_NeverThrows_WhenNoEmulatorIsInstalled()
    {
        foreach (var provider in TerminalProviderFactory.CreateDefault())
        {
            var availability = await provider.IsAvailableAsync();

            // On a build agent both probes fail; on a developer machine with PCOMM one may
            // succeed. Either way the probe must answer instead of throwing.
            if (!availability.IsAvailable)
            {
                Assert.False(string.IsNullOrWhiteSpace(availability.Reason));
            }
        }
    }

    [Fact]
    public void PcommProvider_ExposesStableMetadata()
    {
        var provider = new PcommTerminalProvider();

        Assert.Equal("pcomm", provider.Name);
        Assert.Contains("Personal Communications", provider.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void EhllapiProvider_DescribesThePresentationSpaceItAssumes()
    {
        var provider = new EhllapiTerminalProvider(43, 132);

        Assert.Equal("ehllapi", provider.Name);
        Assert.Contains("43x132", provider.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EhllapiProvider_ListsTheConventionalShortNames()
    {
        var sessions = await new EhllapiTerminalProvider().ListSessionsAsync();

        Assert.Equal(26, sessions.Count);
        Assert.Equal("A", sessions[0].Name);
        Assert.Equal("Z", sessions[^1].Name);
    }

    [Fact]
    public void ComObject_TryCreate_ReturnsNullForAnUnregisteredProgId()
        => Assert.Null(ComObject.TryCreate("HostMcp.NotARealProgId.Ever"));

    [Fact]
    public void PcommProgIds_MatchTheDocumentedAutomationObjects()
    {
        Assert.Equal("PCOMM.autECLSession", PcommProgIds.Session);
        Assert.Equal("PCOMM.autECLPS", PcommProgIds.PresentationSpace);
        Assert.Equal("PCOMM.autECLOIA", PcommProgIds.Oia);
        Assert.Equal("PCOMM.autECLConnList", PcommProgIds.ConnectionList);
        Assert.Equal("PCOMM.autECLScreenDesc", PcommProgIds.ScreenDescription);
        Assert.Equal("PCOMM.autECLFieldList", PcommProgIds.FieldList);
        Assert.Equal("PCOMM.autECLWinMetrics", PcommProgIds.WindowMetrics);
    }

    [Fact]
    public void EhllapiFunctionNumbers_MatchTheIbmSpecification()
    {
        Assert.Equal(1, (int)EhllapiFunction.ConnectPresentationSpace);
        Assert.Equal(2, (int)EhllapiFunction.DisconnectPresentationSpace);
        Assert.Equal(3, (int)EhllapiFunction.SendKey);
        Assert.Equal(4, (int)EhllapiFunction.Wait);
        Assert.Equal(5, (int)EhllapiFunction.CopyPresentationSpace);
        Assert.Equal(6, (int)EhllapiFunction.SearchPresentationSpace);
        Assert.Equal(7, (int)EhllapiFunction.QueryCursorLocation);
        Assert.Equal(8, (int)EhllapiFunction.CopyPresentationSpaceToString);
        Assert.Equal(15, (int)EhllapiFunction.CopyStringToPresentationSpace);
        Assert.Equal(40, (int)EhllapiFunction.SetCursor);
    }
}

public class EhllapiMnemonicsTests
{
    [Theory]
    [InlineData("[enter]", "@E")]
    [InlineData("[tab]", "@T")]
    [InlineData("[backtab]", "@B")]
    [InlineData("[home]", "@0")]
    [InlineData("[clear]", "@C")]
    [InlineData("[pf1]", "@1")]
    [InlineData("[pf9]", "@9")]
    [InlineData("[pf10]", "@a")]
    [InlineData("[pf24]", "@o")]
    [InlineData("[pa1]", "@x")]
    [InlineData("[pa2]", "@y")]
    [InlineData("[pa3]", "@z")]
    [InlineData("[eraseeof]", "@F")]
    [InlineData("[left]", "@L")]
    [InlineData("[right]", "@Z")]
    [InlineData("[up]", "@U")]
    [InlineData("[down]", "@V")]
    public void Encode_MapsMnemonicsToTheIbmEscapeSequences(string input, string expected)
        => Assert.Equal(expected, EhllapiMnemonics.Encode(MnemonicParser.Parse(input)));

    [Fact]
    public void Encode_PassesLiteralTextThrough()
        => Assert.Equal("USER01", EhllapiMnemonics.Encode(MnemonicParser.Parse("USER01")));

    [Fact]
    public void Encode_DoublesLiteralAtSigns()
        => Assert.Equal("A@@B", EhllapiMnemonics.Encode([Keystroke.Text("A@B")]));

    [Fact]
    public void Encode_CombinesTextAndMnemonics()
        => Assert.Equal(
            "USER01@TSECRET@E",
            EhllapiMnemonics.Encode(MnemonicParser.Parse("USER01[tab]SECRET[enter]")));

    [Fact]
    public void EveryParserMnemonicHasAnEhllapiEncoding()
    {
        var missing = MnemonicParser.SupportedMnemonics
            .Where(m => !EhllapiMnemonics.Supported.Contains(m))
            .ToList();

        Assert.Empty(missing);
    }
}
