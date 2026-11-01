using Naad.Insertion;
using Naad.Live;

namespace Naad.Tests.Live;

[Collection("Log")]
public sealed class LiveTyperTests
{
    private static readonly WindowTarget Target = new(0x1001, 10, 100);
    private readonly List<string> _typed = [];
    private InsertionDecision _decision = InsertionDecision.Insert;
    private bool _keyDown;
    private TypingResult _result = TypingResult.Typed;

    [Fact]
    public void Update_Allowed_TypesOnlyTheNewWords()
    {
        var typer = Typer(allowed: true);

        typer.Update("synthetic one.");
        typer.Update("synthetic one. synthetic two.");

        Assert.Equal(["synthetic one.", " synthetic two."], _typed);
    }

    [Fact]
    public void Update_NotAllowedYet_WaitsThenTypesEverything()
    {
        var typer = Typer(allowed: false);

        typer.Update("synthetic one.");
        Assert.Empty(_typed);
        typer.Allow(true);

        Assert.Equal(["synthetic one."], _typed);
    }

    [Fact]
    public void Allow_False_NeverTypes()
    {
        var typer = Typer(allowed: false);
        typer.Allow(false);

        typer.Update("synthetic one.");

        Assert.Empty(_typed);
        Assert.False(typer.Finish("synthetic one."));
    }

    [Fact]
    public void Update_KeyHeld_WaitsUntilFinish()
    {
        var typer = Typer(allowed: true);
        _keyDown = true;

        typer.Update("synthetic one.");
        Assert.Empty(_typed);
        _keyDown = false;

        Assert.True(typer.Finish("synthetic one. synthetic two."));
        Assert.Equal(["synthetic one. synthetic two."], _typed);
    }

    [Fact]
    public void Update_WordsRevised_StopsAndFinishKeepsTheText()
    {
        var typer = Typer(allowed: true);

        typer.Update("synthetic one.");
        typer.Update("synthetic won.");

        Assert.True(typer.Stopped);
        Assert.False(typer.Finish("synthetic won."));
        Assert.Equal(["synthetic one."], _typed);
    }

    [Fact]
    public void Update_FocusMoved_StopsTyping()
    {
        var typer = Typer(allowed: true);
        typer.Update("synthetic one.");
        _decision = InsertionDecision.FocusChanged;

        typer.Update("synthetic one. synthetic two.");

        Assert.True(typer.Stopped);
        Assert.Equal(["synthetic one."], _typed);
    }

    [Fact]
    public void Update_TypingPartlyFailed_Stops()
    {
        var typer = Typer(allowed: true);
        _result = TypingResult.Partial;

        typer.Update("synthetic one.");

        Assert.True(typer.Stopped);
        Assert.Equal("", typer.Typed);
    }

    [Fact]
    public void Finish_FinalDoesNotContinueTheTypedText_ReturnsFalse()
    {
        var typer = Typer(allowed: true);
        typer.Update("synthetic one.");

        Assert.False(typer.Finish("different synthetic text."));
        Assert.Equal(["synthetic one."], _typed);
    }

    [Fact]
    public void Update_AfterFinish_IsIgnored()
    {
        var typer = Typer(allowed: true);
        Assert.True(typer.Finish("synthetic one. synthetic two."));

        typer.Update("synthetic one.");

        Assert.False(typer.Stopped);
        Assert.Single(_typed);
    }

    private LiveTyper Typer(bool allowed)
    {
        var typer = new LiveTyper(Target, _ => _decision, () => _keyDown, text =>
        {
            if (_result == TypingResult.Typed)
            {
                _typed.Add(text);
            }

            return _result;
        });
        if (allowed)
        {
            typer.Allow(true);
        }

        return typer;
    }
}
