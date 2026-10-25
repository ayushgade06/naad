using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Key = System.Windows.Forms.Keys;

namespace Naad.Hotkey;

/// <summary>
/// Keys held together as a shortcut, such as Right Ctrl or Left Ctrl+Space. Left and right
/// modifiers are different keys. Key order doesn't matter: two chords with the same keys
/// are equal. In settings a chord is written as key names joined by "+", e.g. "RightCtrl".
/// </summary>
[JsonConverter(typeof(TextConverter))]
internal sealed record Chord
{
    // Many keyboards can't report more keys held at once, and the tray tooltip that
    // describes the chord has a 127-character limit.
    private const int MaxKeys = 4;

    // Low-level keyboard hooks report sided modifiers (RControlKey), never the generic
    // ones (ControlKey), and never mouse buttons. A chord using them could never match.
    private static readonly Key[] Unmatchable =
    [
        Key.ShiftKey, Key.ControlKey, Key.Menu,
        Key.LButton, Key.RButton, Key.MButton, Key.XButton1, Key.XButton2,
    ];

    // The Keys enum names modifiers unhelpfully (RControlKey, RMenu); every other key
    // uses its enum name, such as Space, F9 or A.
    private static readonly (Key Key, string Name, string Display)[] Modifiers =
    [
        (Key.LControlKey, "LeftCtrl", "Left Ctrl"),
        (Key.RControlKey, "RightCtrl", "Right Ctrl"),
        (Key.LShiftKey, "LeftShift", "Left Shift"),
        (Key.RShiftKey, "RightShift", "Right Shift"),
        (Key.LMenu, "LeftAlt", "Left Alt"),
        (Key.RMenu, "RightAlt", "Right Alt"),
        (Key.LWin, "LeftWin", "Left Windows"),
        (Key.RWin, "RightWin", "Right Windows"),
    ];

    private Chord(IReadOnlyList<Key> keys) => Keys = keys;

    /// <summary>The default dictation shortcut.</summary>
    public static Chord RightCtrl { get; } = new([Key.RControlKey]);

    /// <summary>One to four distinct keys, in the order they were written.</summary>
    public IReadOnlyList<Key> Keys { get; }

    /// <summary>
    /// Parses the settings form, e.g. "RightCtrl" or "LeftCtrl+Space". Names ignore case.
    /// Fails for unknown names, repeated keys, more than four keys, generic modifiers
    /// (ControlKey) and mouse buttons.
    /// </summary>
    public static bool TryParse(string text, [NotNullWhen(true)] out Chord? chord)
    {
        chord = null;
        var keys = new List<Key>();
        foreach (var name in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (!TryParseKey(name, out var key) || keys.Contains(key))
            {
                return false;
            }

            keys.Add(key);
        }

        if (keys.Count > MaxKeys)
        {
            return false;
        }

        chord = new(keys);
        return true;
    }

    public bool Contains(Key key) => Keys.Contains(key);

    /// <summary>Ctrl, Shift, Alt or Windows, on either side.</summary>
    public static bool IsModifier(Key key) => Array.Exists(Modifiers, m => m.Key == key);

    /// <summary>The chord for people, e.g. "Right Ctrl" or "Left Ctrl+Space".</summary>
    public string Describe() => string.Join('+', Keys.Select(k => NameOf(k, display: true)));

    /// <summary>The settings form, which <see cref="TryParse"/> reads back.</summary>
    public override string ToString() => string.Join('+', Keys.Select(k => NameOf(k, display: false)));

    public bool Equals(Chord? other) =>
        other is not null && Keys.Count == other.Keys.Count && Keys.All(other.Contains);

    // XOR ignores order, matching Equals; the keys in a chord are distinct.
    public override int GetHashCode() => Keys.Aggregate(0, (hash, key) => hash ^ (int)key);

    private static string NameOf(Key key, bool display)
    {
        var i = Array.FindIndex(Modifiers, m => m.Key == key);
        return i < 0 ? key.ToString() : display ? Modifiers[i].Display : Modifiers[i].Name;
    }

    private static bool TryParseKey(string name, out Key key)
    {
        foreach (var modifier in Modifiers)
        {
            if (string.Equals(modifier.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                key = modifier.Key;
                return true;
            }
        }

        // Enum.TryParse also accepts numbers ("65") and flag lists ("A, Control"), so only
        // a plain name is passed to it, and modifier flags above the key-code range are refused.
        key = Key.None;
        return name.Length > 0 && char.IsAsciiLetter(name[0]) && name.All(char.IsAsciiLetterOrDigit)
            && Enum.TryParse(name, ignoreCase: true, out key)
            && key is > Key.None and <= Key.OemClear
            && Enum.IsDefined(key)
            && !Unmatchable.Contains(key);
    }

    /// <summary>Stores a chord in JSON as its settings form.</summary>
    internal sealed class TextConverter : JsonConverter<Chord>
    {
        public override Chord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && TryParse(reader.GetString()!, out var chord)
                ? chord
                : throw new JsonException("Not a valid chord.");

        public override void Write(Utf8JsonWriter writer, Chord value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
