using System.Windows;
using System.Windows.Controls;
using Naad.Context;
using Naad.Dictation;
using Naad.History;
using Naad.Hotkey;
using Naad.Providers;
using Naad.Settings;
using Naad.Tray;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextBox = System.Windows.Controls.TextBox;

namespace Naad.Window;

/// <summary>
/// The guide's steps for the AI key, shortcuts, Edit Mode and the overlay, and the test
/// dictation that shows its progress. Never shows or logs what the test dictation said.
/// </summary>
internal sealed partial class SetupWizard
{
    private const string CaptureItem = "Other keys...";
    private const string CapturePrompt = "Press the keys, then let go. Esc cancels.";
    private const string Listening = "Listening...";
    private const string Transcribing = "Transcribing...";

    private ShortcutRole? _capturing;
    private Action? _refillShortcuts;
    private TextBlock? _tryStatus;

    /// <summary>The hook finished a capture this guide started. Call on the UI thread.</summary>
    public void ShowCapture(CaptureStep step)
    {
        if (_capturing is not { } role)
        {
            return;
        }

        _capturing = null;
        if (step.Chord is { } chord)
        {
            ChooseShortcut(role, chord);
        }
        else if (step.Status == CaptureStatus.Rejected)
        {
            _shortcutNote.Text = "That key can't be part of a shortcut. Try another.";
        }

        _refillShortcuts?.Invoke();
    }

    // Asks the service whether the typed key works, and says so in the note.
    private Button CheckKey(Func<string?> key, Func<(CloudService Service, Uri BaseUrl)> target, TextBlock note)
    {
        var check = new Button { Content = "Check key", HorizontalAlignment = HorizontalAlignment.Left };
        check.Click += async (_, _) =>
        {
            if (key() is not { } typed)
            {
                note.Text = "Paste a key first.";
                return;
            }

            check.IsEnabled = false;
            note.Text = "Checking...";
            try
            {
                var (service, baseUrl) = target();
                var result = await _actions.TestKey(service, typed, baseUrl, _closing.Token);
                note.Text = SettingsInput.KeyTestMessage(result);
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested)
            {
                // The guide closed during the check.
            }
            finally
            {
                check.IsEnabled = true;
            }
        };
        return check;
    }

    private StackPanel AiKey()
    {
        var services = CloudServices.ForLanguageModel;
        var service = Picker(
            [.. services.Select(s => CloudServices.For(s).Name)],
            Math.Max(0, IndexOf(services, _settings.LanguageModelService)),
            index => _settings = _settings with { LanguageModelService = services[index] });
        var key = new PasswordBox { Margin = new Thickness(0, 0, 0, 12) };
        var note = Muted("");
        var check = CheckKey(
            () => _aiKey, () => (_settings.LanguageModelService, _settings.LanguageModelBaseUrl), note);
        // A key typed earlier isn't shown again; the note says one will be saved.
        if (_aiKey is not null)
        {
            note.Text = "A key is entered and will be saved. Paste another to replace it.";
        }

        key.PasswordChanged += (_, _) =>
        {
            var typed = key.Password.Trim();
            _aiKey = SecretStore.IsValidKey(typed) ? typed : null;
            note.Text = typed.Length > 0 && _aiKey is null ? SettingsInput.KeyError(typed) ?? "" : "";
        };
        return Page(
            "An AI key (optional)",
            "Skip this to clean up on this PC. To use a cloud model for AI cleanup and context instead, choose " +
            "a provider and paste its key, then check it.",
            service,
            key,
            check,
            note);
    }

    private StackPanel ShortcutsStep()
    {
        var rows = new StackPanel();
        void Refill()
        {
            rows.Children.Clear();
            // Meeting notes are set up in Settings, beside the rest of meetings.
            foreach (var role in Shortcuts.Roles.Where(r => r != ShortcutRole.Meeting))
            {
                rows.Children.Add(new TextBlock { Text = Shortcuts.Name(role), Margin = new Thickness(0, 0, 0, 4) });
                rows.Children.Add(ShortcutPicker(role));
            }
        }

        _refillShortcuts = Refill;
        _shortcutNote.Text = "";
        Refill();
        return Page(
            "Your shortcuts",
            "Hold to talk records while held; the toggle starts and stops hands-free. Paste again pastes your " +
            "last dictation. Choose Off for any you don't want, but keep one way to dictate.",
            rows,
            _shortcutNote);
    }

    // Off, the role's presets, its custom chord, and "Other keys..." to capture a new one.
    private ComboBox ShortcutPicker(ShortcutRole role)
    {
        var current = Shortcuts.Get(_settings, role);
        List<Chord?> choices = [null, .. Shortcuts.Presets(role)];
        foreach (var other in new[] { Shortcuts.Custom(_settings, role), current })
        {
            if (other is not null && !choices.Contains(other))
            {
                choices.Add(other);
            }
        }

        var list = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var choice in choices)
        {
            list.Items.Add(choice is null ? "Off" : Shortcuts.Presets(role).Contains(choice)
                ? choice.Describe()
                : "Custom: " + choice.Describe());
        }

        list.Items.Add(_capturing == role ? CapturePrompt : CaptureItem);
        list.SelectedIndex = _capturing == role ? choices.Count : choices.IndexOf(current);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0 || _capturing == role)
            {
                return;
            }

            if (list.SelectedIndex < choices.Count)
            {
                ChooseShortcut(role, choices[list.SelectedIndex]);
            }
            else
            {
                BeginCapture(role);
            }

            // Rebuilt after the selection settles, so the list never changes under its own event.
            _ = Dispatcher.BeginInvoke(() => _refillShortcuts?.Invoke());
        };
        return list;
    }

    private void ChooseShortcut(ShortcutRole role, Chord? chord)
    {
        var (next, error) = Shortcuts.Choose(_settings, role, chord);
        _shortcutNote.Text = error ?? "";
        if (next is not null)
        {
            _settings = next;
        }
    }

    private void BeginCapture(ShortcutRole role)
    {
        CancelCapture();
        if (!_actions.BeginCapture())
        {
            _shortcutNote.Text = "Finish the dictation first, then try again.";
            return;
        }

        _capturing = role;
    }

    private void CancelCapture()
    {
        if (_capturing is not null)
        {
            _capturing = null;
            _actions.CancelCapture();
        }
    }

    private StackPanel EditModeStep()
    {
        var mode = Picker(
            ["Off: speaking over selected text replaces it", "Automatic: with text selected, what you say edits it",
                "Manual: edit only while holding the Edit Mode key with the shortcut"],
            (int)_settings.EditMode,
            index => _settings = _settings with { EditMode = (EditMode)index });
        var note = Muted("");
        var key = Picker(
            ["Edit Mode key: Shift", "Edit Mode key: Ctrl"],
            (int)_settings.EditModeKey,
            index =>
            {
                var next = _settings with { EditModeKey = (EditModeKey)index };
                note.Text = Shortcuts.Problem(next) ?? "";
                if (note.Text.Length == 0)
                {
                    _settings = next;
                }
            });
        return Page(
            "Edit Mode",
            "Select text, then dictate an instruction such as \"make this shorter\", and the AI rewrites the " +
            "selection. It needs AI cleanup.",
            mode,
            key,
            note);
    }

    private StackPanel OverlayStep() => Page(
        "While you speak",
        "A small overlay shows that Naad is listening. Choose how much room it takes.",
        Picker(
            ["Pill: voice bars and words such as \"Transcribing\"", "Compact: a small capsule of voice bars"],
            (int)_settings.OverlayStyle,
            index => _settings = _settings with { OverlayStyle = (OverlayStyle)index }));

    private StackPanel TryIt()
    {
        var box = new TextBox
        {
            Height = 120,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Margin = new Thickness(0, 0, 0, 12),
        };
        _tryStatus = Muted("");
        box.Loaded += (_, _) => box.Focus();
        var (shortcut, handsFree) = Shortcuts.Primary(_settings);
        var how = handsFree
            ? $"Click in the box, press {shortcut.Describe()}, say a sentence, and press it again."
            : $"Click in the box, hold {shortcut.Describe()}, say a sentence, and let go.";
        return Page("Try it", how + " The first dictation can take a minute while models load.", box, _tryStatus);
    }

    // The test dictation's progress, from the controller; only while the Try it step is shown.
    private void ShowTryState(DictationState state)
    {
        if (_tryStatus is null)
        {
            return;
        }

        // Back to idle with no result: too short, cancelled, or nothing heard at all.
        _tryStatus.Text = state switch
        {
            DictationState.Recording => Listening,
            DictationState.Idle when _tryStatus.Text is Listening or Transcribing =>
                "Nothing came through. Try again, and speak a little longer.",
            DictationState.Idle => _tryStatus.Text,
            _ => Transcribing,
        };
    }

    private void ShowTryResult(DictationReport report)
    {
        if (_tryStatus is not null)
        {
            _tryStatus.Text = report.Outcome switch
            {
                HistoryOutcome.Pasted or HistoryOutcome.Kept => "It works. Your words arrived.",
                HistoryOutcome.Empty => NothingHeard,
                _ => "That didn't work. The notification from Naad says why.",
            };
        }
    }

    private void ShowNothingHeard()
    {
        if (_tryStatus is not null)
        {
            _tryStatus.Text = NothingHeard;
        }
    }

    // Closed before the last step: the shortcuts in use when the guide opened come back, so a
    // half-made choice never leaves the keyboard in a state the user didn't confirm. A field changed
    // in Settings meanwhile (no longer the guide's value) is left alone.
    private void RestoreShortcuts()
    {
        var now = _latest();
        var old = _opened;
        var saved = _settings;
        T Back<T>(T current, T guide, T before) =>
            EqualityComparer<T>.Default.Equals(current, guide) ? before : current;
        var restored = now with
        {
            HoldShortcut = Back(now.HoldShortcut, saved.HoldShortcut, old.HoldShortcut),
            ToggleShortcut = Back(now.ToggleShortcut, saved.ToggleShortcut, old.ToggleShortcut),
            PasteAgainShortcut = Back(now.PasteAgainShortcut, saved.PasteAgainShortcut, old.PasteAgainShortcut),
            CustomHoldShortcut = Back(now.CustomHoldShortcut, saved.CustomHoldShortcut, old.CustomHoldShortcut),
            CustomToggleShortcut = Back(
                now.CustomToggleShortcut, saved.CustomToggleShortcut, old.CustomToggleShortcut),
            CustomPasteAgainShortcut = Back(
                now.CustomPasteAgainShortcut, saved.CustomPasteAgainShortcut, old.CustomPasteAgainShortcut),
            EditMode = Back(now.EditMode, saved.EditMode, old.EditMode),
            EditModeKey = Back(now.EditModeKey, saved.EditModeKey, old.EditModeKey),
        };
        if (restored != now)
        {
            _ = _actions.Save(new(restored, new Dictionary<CloudService, string>(), _startAtLogin));
        }
    }
}
