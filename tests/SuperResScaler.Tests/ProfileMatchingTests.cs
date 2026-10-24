using SuperResScaler.Config;
using SuperResScaler.Native;

namespace SuperResScaler.Tests;

public class ProfileMatchingTests
{
    [Theory]
    [InlineData("game.exe", "game.exe", true)]
    [InlineData("game", "GAME.EXE", true)]                 // case and ".exe" do not matter
    [InlineData("game.exe", "game2.exe", false)]           // no wildcard: the whole name
    [InlineData("game*.exe", "game_dx11.exe", true)]       // wildcards, as BackForceFeeder
    [InlineData("game?.exe", "game2.exe", true)]
    [InlineData("game?.exe", "game10.exe", false)]
    [InlineData("other.exe|game.exe", "game.exe", true)]   // alternatives
    [InlineData(" other.exe | game.exe ", "game.exe", true)]
    [InlineData(@"C:\Games\Daytona\game.exe", "game.exe", true)] // a full path is reduced to its file name
    [InlineData("", "game.exe", false)]                    // a profile needs a process
    public void ProcessFilter(string filter, string process, bool expected)
        => Assert.Equal(expected, WindowMatcher.MatchesProcess(filter, process));

    [Theory]
    [InlineData(null, "Anything", true)]
    [InlineData("", "Anything", true)]
    [InlineData("Daytona", "SEGA Daytona USA 2", true)]    // plain text: contained, any case
    [InlineData("daytona", "SEGA DAYTONA USA 2", true)]
    [InlineData("Daytona*", "SEGA Daytona USA 2", false)]  // wildcards: the whole title
    [InlineData("*Daytona*", "SEGA Daytona USA 2", true)]
    [InlineData("Scud Race|Daytona", "Daytona 2", true)]
    [InlineData("Scud Race", "Daytona 2", false)]
    public void TitleFilter(string? filter, string title, bool expected)
        => Assert.Equal(expected, WindowMatcher.MatchesTitle(filter, title));

    private static WindowInfo Window(string title, string process) => new(new IntPtr(1), title, "Class", process, 1);

    [Fact]
    public void ProfileMatchesProcessAndTitle()
    {
        var profile = new Profile { ProcessName = "supermodel.exe", WindowTitle = "Scud Race" };
        Assert.True(profile.Matches(Window("Supermodel - Scud Race", "Supermodel.exe")));
        Assert.False(profile.Matches(Window("Supermodel - Daytona 2", "Supermodel.exe")));
        Assert.False(profile.Matches(Window("Scud Race", "mame.exe")));
    }

    [Fact]
    public void MostSpecificProfileWins()
    {
        var any = new Profile { Name = "any", ProcessName = "supermodel*" };
        var exact = new Profile { Name = "exact", ProcessName = "supermodel.exe" };
        var titled = new Profile { Name = "titled", ProcessName = "supermodel.exe", WindowTitle = "Scud" };
        var settings = new AppSettings { Profiles = [any, exact, titled] };

        Assert.Same(titled, settings.FindProfile(Window("Supermodel - Scud Race", "supermodel.exe")));
        Assert.Same(exact, settings.FindProfile(Window("Supermodel - Daytona 2", "supermodel.exe")));
        Assert.Same(any, settings.FindProfile(Window("Supermodel", "supermodel64.exe")));
        Assert.Null(settings.FindProfile(Window("MAME", "mame.exe")));
        Assert.Same(settings.Defaults, settings.ResolveOptions(Window("MAME", "mame.exe")));
    }

    [Fact]
    public void NewProfilesScaleAutomatically()
    {
        var profile = new Profile();
        Assert.True(profile.AutoScale);
        Assert.Equal(1, profile.AutoScaleDelay);
    }

    [Fact]
    public void CloneCopiesEverything()
    {
        var profile = new Profile
        {
            Name = "Daytona", ProcessName = "game.exe", WindowTitle = "Daytona", AutoScale = false, AutoScaleDelay = 3,
            Shader = "Nearest", AspectRatio = "4:3_crop", CropBarrel = true, Resolution = "1920x240@60",
        };
        var copy = profile.Clone();
        Assert.NotSame(profile, copy);
        Assert.Equal(("Daytona", "game.exe", "Daytona", false, 3.0), (copy.Name, copy.ProcessName, copy.WindowTitle, copy.AutoScale, copy.AutoScaleDelay));
        Assert.Equal(("Nearest", "4:3_crop", true, "1920x240@60"), (copy.Shader, copy.AspectRatio, copy.CropBarrel, copy.Resolution));
    }
}
