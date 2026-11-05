using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using Naad.Window;

namespace Naad.Tests.Window;

public sealed class EscapeKeyTests
{
    [Fact]
    public void Closes_TypingInATextBox_KeepsTheWindowOpen() =>
        OnSta(() => Assert.False(EscapeKey.Closes(new TextBox())));

    [Fact]
    public void Closes_PasswordBox_KeepsTheWindowOpen() =>
        OnSta(() => Assert.False(EscapeKey.Closes(new PasswordBox())));

    [Fact]
    public void Closes_EditableComboBox_KeepsTheWindowOpen() =>
        OnSta(() => Assert.False(EscapeKey.Closes(new ComboBox { IsEditable = true })));

    [Fact]
    public void Closes_DropDownList_Closes() => OnSta(() => Assert.True(EscapeKey.Closes(new ComboBox())));

    [Fact]
    public void Closes_Button_Closes() => OnSta(() => Assert.True(EscapeKey.Closes(new Button())));

    [Fact]
    public void Closes_NothingFocused_Closes() => Assert.True(EscapeKey.Closes(null));

    // WPF controls must be made on a single-threaded apartment thread.
    private static void OnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
