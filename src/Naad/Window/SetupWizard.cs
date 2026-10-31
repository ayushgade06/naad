using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Naad.Audio;
using Naad.Cleanup;
using Naad.LanguageModel;
using Naad.Native;
using Naad.Providers;
using Naad.Settings;
using Naad.Transcription;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using ProgressBar = System.Windows.Controls.ProgressBar;
using TextBox = System.Windows.Controls.TextBox;

namespace Naad.Window;

/// <summary>
/// The first-run guide: microphone, speech model, AI cleanup, an optional cloud key, vocabulary,
/// starting at sign-in, then a real dictation into a box. Settings are saved before the test, so
/// the test uses them. Closing it part-way changes nothing; it can be run again from the tray.
/// Create and use it on the UI thread.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "A WPF window isn't disposable, and a handler still running may read the token after " +
        "the window closes, so the source is only cancelled.")]
internal sealed class SetupWizard : System.Windows.Window
{
    // A short check: long enough to say a sentence and watch the bar move.
    private static readonly TimeSpan MicrophoneTest = TimeSpan.FromSeconds(5);

    private readonly SettingsWindowActions _actions;
    private readonly Palette _palette;
    private readonly List<Func<UIElement>> _steps = [];
    private readonly ContentControl _page = new();
    private readonly Button _back = new() { Content = "Back", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _next = new() { Content = "Next", MinWidth = 90, IsDefault = true };
    private readonly CancellationTokenSource _closing = new();
    private readonly IReadOnlyList<string> _microphones;
    private readonly Func<AppSettings> _latest;
    private AppSettings _settings;
    private bool _startAtLogin;
    private string? _newKey;
    private int _step;

    /// <param name="current">The settings in use, read again when saving.</param>
    /// <param name="microphones">The connected recording devices.</param>
    /// <param name="startsAtLogin">Whether Naad starts at sign-in now.</param>
    /// <param name="actions">Downloads and saving, as in Settings.</param>
    /// <param name="palette">The colours.</param>
    /// <param name="dark">Whether the title bar should be dark.</param>
    public SetupWizard(
        Func<AppSettings> current,
        IReadOnlyList<string> microphones,
        bool startsAtLogin,
        SettingsWindowActions actions,
        Palette palette,
        bool dark)
    {
        _latest = current;
        _settings = current();
        _microphones = microphones;
        _startAtLogin = startsAtLogin;
        _actions = actions;
        _palette = palette;
        Title = "Set up Naad";
        Icon = MainWindow.AppIcon();
        Width = 560;
        Height = 560;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = MainWindow.Brush(palette.Background);
        Foreground = MainWindow.Brush(palette.Text);
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 14;
        _steps.AddRange([Welcome, Microphone, Speech, Cleanup, Cloud, Vocabulary, SignIn, TryIt, Done]);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(24),
        };
        buttons.Children.Add(_back);
        buttons.Children.Add(_next);
        var layout = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        _page.Margin = new Thickness(32, 32, 32, 0);
        layout.Children.Add(_page);
        Content = layout;

        _back.Click += (_, _) => Go(_step - 1);
        _next.Click += (_, _) => Go(_step + 1);
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
            }
        };
        Closed += (_, _) => _closing.Cancel();
        SourceInitialized += (_, _) => Dwmapi.SetDarkTitleBar(new WindowInteropHelper(this).Handle, dark);
        Go(0);
    }

    private void Go(int step)
    {
        if (step >= _steps.Count)
        {
            Close();
            return;
        }

        // Saved before the test, so the dictation into the box uses the choices just made.
        if (step < _steps.Count && _steps[step] == (Func<UIElement>)TryIt && step > _step && Save() is { } problem)
        {
            System.Windows.MessageBox.Show(this, problem, "Naad");
            return;
        }

        _step = Math.Max(0, step);
        _back.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        _next.Content = _step == _steps.Count - 1 ? "Finish" : "Next";
        _page.Content = _steps[_step]();
    }

    private string? Save()
    {
        var keys = new Dictionary<CloudService, string>();
        if (_newKey is { Length: > 0 } key)
        {
            keys[_settings.TranscriptionService] = key;
        }

        // Only the guide's own choices go on top of the settings in use now, so a change saved in
        // Settings while the guide was open survives.
        var s = _latest() with
        {
            Microphone = _settings.Microphone,
            TranscriptionProvider = _settings.TranscriptionProvider,
            LocalModel = _settings.LocalModel,
            Cleanup = _settings.Cleanup,
            LocalLanguageModel = _settings.LocalLanguageModel,
            TranscriptionService = _settings.TranscriptionService,
            Vocabulary = _settings.Vocabulary,
        };
        return _actions.Save(new(s, keys, _startAtLogin));
    }

    private StackPanel Welcome() => Page(
        "Welcome to Naad",
        "Hold a key, speak, and let go: your words are typed into whatever app you are using. This guide " +
        "takes about two minutes. Everything works offline, on this PC, unless you choose a cloud service.");

    private StackPanel Microphone()
    {
        var list = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
        list.Items.Add("Windows default");
        foreach (var name in _microphones)
        {
            list.Items.Add(name);
        }

        list.SelectedIndex = _settings.Microphone is { } chosen ? Math.Max(0, IndexOf(_microphones, chosen) + 1) : 0;
        list.SelectionChanged += (_, _) => _settings = _settings with
        {
            Microphone = list.SelectedIndex == 0 ? null : _microphones[list.SelectedIndex - 1],
        };
        var bar = new ProgressBar { Maximum = 1, Height = 12, Margin = new Thickness(0, 12, 0, 0) };
        var test = new Button { Content = "Test microphone", HorizontalAlignment = HorizontalAlignment.Left };
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            try
            {
                await TestMicrophoneAsync(level => bar.Value = level);
            }
            finally
            {
                test.IsEnabled = true;
                bar.Value = 0;
            }
        };
        return Page(
            "Your microphone",
            "Choose the microphone to dictate with, then test it: speak, and the bar should move.",
            list,
            test,
            bar);
    }

    // A separate recorder for five seconds: dictation keeps its own. Nothing is kept or sent.
    private async Task TestMicrophoneAsync(Action<double> show)
    {
        using var recorder = new AudioRecorder();
        recorder.UseMicrophone(_settings.Microphone);
        var meter = new LevelMeter();
        var ui = SynchronizationContext.Current!;
        recorder.LevelMeasured += (rms, duration) => ui.Post(_ => show(meter.Add(rms, duration)), null);
        if (await Task.Run(recorder.Start) is { } failure)
        {
            System.Windows.MessageBox.Show(this, Tray.TrayIcon.MicrophoneErrorMessage(failure.Error), "Naad");
            return;
        }

        try
        {
            await Task.Delay(MicrophoneTest, _closing.Token);
        }
        catch (OperationCanceledException)
        {
            // The guide closed during the test.
        }

        await recorder.StopAsync();
    }

    private StackPanel Speech()
    {
        var local = Choice("On this PC with Whisper: free, private, offline", _settings.TranscriptionProvider ==
            TranscriptionProvider.Local, on => _settings = _settings with
            {
                TranscriptionProvider = on ? TranscriptionProvider.Local : TranscriptionProvider.Cloud,
            });
        var model = Picker(
            ["Base: fast on any PC (148 MB)", "Large v3 Turbo: more accurate, needs a graphics card (574 MB)"],
            (int)_settings.LocalModel,
            index => _settings = _settings with { LocalModel = (WhisperModel)index });
        return Page(
            "Speech to text",
            "Naad turns your speech into text on this PC by default. Untick it to use a cloud service, set up in " +
            "the next steps.",
            local,
            model,
            Download(() => LocalModel.For(_settings.LocalModel)));
    }

    private StackPanel Cleanup()
    {
        var level = Picker(
            ["Off: paste exactly what was heard", "Basic: remove um and uh, add capitals and a full stop",
                "AI: also fix grammar, punctuation and self-corrections"],
            (int)_settings.Cleanup,
            index => _settings = _settings with { Cleanup = (CleanupLevel)index });
        var model = Picker(
            ["Small: 1.1 GB, fast on any PC", "Large: 2.5 GB, more accurate, best with a graphics card"],
            (int)_settings.LocalLanguageModel,
            index => _settings = _settings with { LocalLanguageModel = (LocalLanguageModelChoice)index });
        return Page(
            "Clean up what you say",
            "AI cleanup runs a small language model on this PC. If it is slow or fails, your words are pasted " +
            "as said.",
            level,
            model,
            Download(() => LanguageModelFiles.For(_settings.LocalLanguageModel)));
    }

    private StackPanel Cloud()
    {
        var services = CloudServices.ForTranscription;
        var service = Picker(
            [.. services.Select(s => CloudServices.For(s).Name)],
            Math.Max(0, IndexOf(services, _settings.TranscriptionService)),
            index => _settings = _settings with { TranscriptionService = services[index] });
        var key = new PasswordBox { Margin = new Thickness(0, 0, 0, 12) };
        var note = Muted("");
        key.PasswordChanged += (_, _) =>
        {
            var typed = key.Password.Trim();
            _newKey = SecretStore.IsValidKey(typed) ? typed : null;
            note.Text = typed.Length > 0 && _newKey is null ? SettingsInput.KeyError(typed) ?? "" : "";
        };
        return Page(
            "A cloud service (optional)",
            "Skip this to stay offline. To transcribe in the cloud instead, choose a service and paste its key; " +
            "Settings has the rest, including keys for AI.",
            service,
            key,
            note);
    }

    private StackPanel Vocabulary()
    {
        var box = new TextBox { Text = _settings.Vocabulary ?? "", Margin = new Thickness(0, 0, 0, 12) };
        var note = Muted("");
        box.TextChanged += (_, _) =>
        {
            var terms = box.Text.Trim();
            var valid = terms.Length == 0 || AppSettings.IsValidVocabulary(terms);
            note.Text = valid ? "" : "Keep it to one line of up to 500 characters; the last valid list is kept.";
            if (valid)
            {
                _settings = _settings with { Vocabulary = terms.Length > 0 ? terms : null };
            }
        };
        return Page(
            "Your words",
            "Names and terms you use, separated by commas, such as people, products and places. They are " +
            "spelled right when you say them.",
            box,
            note);
    }

    private StackPanel SignIn()
    {
        var box = new CheckBox { Content = "Start Naad when I sign in", IsChecked = _startAtLogin };
        box.Click += (_, _) => _startAtLogin = box.IsChecked == true;
        return Page(
            "Always ready", "Naad waits in the notification area, using almost nothing until you dictate.", box);
    }

    private StackPanel TryIt()
    {
        var box = new TextBox
        {
            Height = 120,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var result = Muted("");
        box.TextChanged += (_, _) => result.Text = box.Text.Length > 0 ? "It works. Your words arrived." : "";
        box.Loaded += (_, _) => box.Focus();
        var how = _settings.HandsFree
            ? $"Click in the box, press {_settings.DictationShortcut.Describe()}, say a sentence, and press it again."
            : $"Click in the box, hold {_settings.DictationShortcut.Describe()}, say a sentence, and let go.";
        return Page("Try it", how + " The first dictation can take a minute while models load.", box, result);
    }

    private StackPanel Done() => Page(
        "You're all set",
        "Naad is in the notification area. Open it from there for history, your dictionary and settings, or " +
        "to run this guide again.");

    // A download button for whichever model the step has chosen, with its progress.
    private StackPanel Download(Func<LocalModel> model)
    {
        var status = Muted("");
        var button = new Button { Content = "Download", HorizontalAlignment = HorizontalAlignment.Left };
        void Show() => status.Text = model().IsReady() ? "Downloaded." : $"Needs a one-time download of " +
            $"{model().Size / 1_000_000} MB.";
        Show();
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            var progress = new Progress<double>(fraction => status.Text = $"Downloading... {fraction:P0}");
            try
            {
                var result = await _actions.DownloadModel(model(), progress, _closing.Token);
                status.Text = SettingsInput.ModelDownloadMessage(result);
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested)
            {
                // The guide closed during the download; nothing partial is kept.
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(button);
        status.Margin = new Thickness(12, 4, 0, 0);
        row.Children.Add(status);
        return row;
    }

    private static int IndexOf<T>(IReadOnlyList<T> items, T item)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(items[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    private static CheckBox Choice(string text, bool on, Action<bool> changed)
    {
        var box = new CheckBox { Content = text, IsChecked = on, Margin = new Thickness(0, 0, 0, 12) };
        box.Click += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    private static ComboBox Picker(IReadOnlyList<string> items, int selected, Action<int> changed)
    {
        var list = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var item in items)
        {
            list.Items.Add(item);
        }

        list.SelectedIndex = selected;
        list.SelectionChanged += (_, _) => changed(list.SelectedIndex);
        return list;
    }

    private TextBlock Muted(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = MainWindow.Brush(_palette.Muted),
    };

    private static StackPanel Page(string title, string text, params UIElement[] controls)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        });
        stack.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });
        foreach (var control in controls)
        {
            stack.Children.Add(control);
        }

        return stack;
    }
}
