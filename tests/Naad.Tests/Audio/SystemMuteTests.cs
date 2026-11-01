using Naad.Audio;

namespace Naad.Tests.Audio;

[Collection("Log")]
public sealed class SystemMuteTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"naad-mute-{Guid.NewGuid():N}.json");
    private readonly FakeOutput _output = new();

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void MuteThenRestore_PlayingSpeakers_MutesAndPutsThemBack()
    {
        var mute = new SystemMute(_output, _path);

        mute.Mute();
        var during = _output.Muted;
        mute.Restore();

        Assert.True(during);
        Assert.False(_output.Muted);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Restore_SpeakersAlreadyMuted_LeavesThemMuted()
    {
        _output.Muted = true;
        var mute = new SystemMute(_output, _path);

        mute.Mute();
        mute.Restore();

        Assert.True(_output.Muted);
        Assert.Equal(0, _output.Changes);
    }

    [Fact]
    public void Restore_UserUnmutedMeanwhile_TouchesNothing()
    {
        var mute = new SystemMute(_output, _path);
        mute.Mute();
        _output.Muted = false;

        mute.Restore();

        Assert.Equal(1, _output.Changes);
    }

    [Fact]
    public void Restore_AfterACrash_UnmutesWhatTheLastRunMuted()
    {
        new SystemMute(_output, _path).Mute();

        // A new run, as after Naad was killed mid-dictation.
        new SystemMute(_output, _path).Restore();

        Assert.False(_output.Muted);
    }

    [Fact]
    public void Restore_SpeakersUnpluggedMeanwhile_KeepsTheNoteForLater()
    {
        var mute = new SystemMute(_output, _path);
        mute.Mute();
        _output.Present = false;

        mute.Restore();
        _output.Present = true;
        mute.Restore();

        Assert.False(_output.Muted);
    }

    [Fact]
    public void Mute_Twice_RemembersTheFirstOnly()
    {
        var mute = new SystemMute(_output, _path);

        mute.Mute();
        mute.Mute();
        mute.Restore();

        Assert.False(_output.Muted);
    }

    [Fact]
    public void Mute_WindowsRefuses_RemembersNothing()
    {
        _output.Refuse = true;

        new SystemMute(_output, _path).Mute();

        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Mute_NoSpeakers_DoesNothing()
    {
        _output.Present = false;

        new SystemMute(_output, _path).Mute();

        Assert.False(File.Exists(_path));
    }

    [Theory]
    [InlineData(false, 0.5f, false)]
    [InlineData(true, 0.5f, true)]
    [InlineData(false, 0.05f, true)]
    [InlineData(false, 0.10f, false)]
    public void LooksSilent_State_WarnsWhenMutedOrVeryLow(bool muted, float volume, bool expected) =>
        Assert.Equal(expected, SystemMute.LooksSilent(new OutputState("synthetic-id", muted, volume)));

    [Fact]
    public void LooksSilent_NoDevice_DoesNotWarn() => Assert.False(SystemMute.LooksSilent(null));

    private sealed class FakeOutput : IOutputDevice
    {
        private const string Id = "synthetic-speakers";

        public bool Muted { get; set; }

        public bool Present { get; set; } = true;

        public bool Refuse { get; set; }

        public int Changes { get; private set; }

        public OutputState? Default() => Present ? new(Id, Muted, 0.5f) : null;

        public OutputState? Find(string id) => Present && id == Id ? new(Id, Muted, 0.5f) : null;

        public bool SetMuted(string id, bool muted)
        {
            if (Refuse)
            {
                return false;
            }

            Changes++;
            Muted = muted;
            return true;
        }
    }
}
