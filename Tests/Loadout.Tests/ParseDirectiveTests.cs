using Loadout.Services;
using Loadout.ViewModels;

namespace Loadout.Tests;

public class ParseDirectiveTests
{
    [Theory]
    [InlineData("!OJO: partida clasificatoria", "!")]
    [InlineData("#build apuro", "#")]
    [InlineData("▲ llevar a top", "▲")]
    [InlineData(">meta", "▲")]
    [InlineData("/comunicación", "/")]
    [InlineData("frase normal", "·")]
    public void ParseDirective_MapsPrefixToIcon(string text, string expected)
    {
        var (icon, _) = MainViewModel.ParseDirective(text);
        Assert.Equal(expected, icon);
    }

    [Theory]
    [InlineData("!x", false)]
    [InlineData("", false)]
    public void ParseDirective_DoesNotThrow(string text, bool throwExpected)
    {
        if (throwExpected)
        {
            Assert.ThrowsAny<Exception>(() => MainViewModel.ParseDirective(text));
            return;
        }
        var (icon, hex) = MainViewModel.ParseDirective(text);
        Assert.False(string.IsNullOrWhiteSpace(icon));
        Assert.False(string.IsNullOrWhiteSpace(hex));
    }
}