using Microsoft.Win32;
using Naad.Settings;

namespace Naad.Tests.Settings;

// Runs against a throwaway key under HKCU\Software\NaadTests, never the real Run key, and
// deletes it afterwards.
public sealed class StartupEntryTests : IDisposable
{
    private const string ExePath = @"C:\Program Files\Naad Test\Naad.exe";
    private readonly string _testKey = $@"Software\NaadTests\{Guid.NewGuid():N}";

    private string RunKey => _testKey + @"\Run";

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_testKey, throwOnMissingSubKey: false);

    [Fact]
    public void Command_PathWithSpaces_IsQuoted()
    {
        Assert.Equal("\"C:\\Program Files\\Naad Test\\Naad.exe\"", Entry().Command);
    }

    [Fact]
    public void IsEnabled_NoKey_IsFalse()
    {
        Assert.False(Entry().IsEnabled());
    }

    [Fact]
    public void Set_EnabledThenDisabled_RoundTrips()
    {
        var entry = Entry();

        Assert.Null(entry.Set(true));
        Assert.True(entry.IsEnabled());
        Assert.Equal(entry.Command, ReadValue());

        Assert.Null(entry.Set(false));
        Assert.False(entry.IsEnabled());
        Assert.Null(ReadValue());
    }

    [Fact]
    public void Set_DisabledWithoutEntry_Succeeds()
    {
        Assert.Null(Entry().Set(false));
    }

    [Fact]
    public void IsEnabled_EntryForAnotherCopy_IsFalseUntilSaved()
    {
        new StartupEntry(Registry.CurrentUser, RunKey, @"D:\old place\Naad.exe").Set(true);
        var entry = Entry();

        Assert.False(entry.IsEnabled());
        entry.Set(true);
        Assert.True(entry.IsEnabled());
    }

    [Fact]
    public void Set_KeepsOtherProgramsEntries()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            key.SetValue("OtherApp", "other.exe");
        }

        Entry().Set(true);
        Entry().Set(false);

        using var run = Registry.CurrentUser.OpenSubKey(RunKey)!;
        Assert.Equal("other.exe", run.GetValue("OtherApp"));
    }

    private StartupEntry Entry() => new(Registry.CurrentUser, RunKey, ExePath);

    private object? ReadValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(StartupEntry.ValueName);
    }
}
