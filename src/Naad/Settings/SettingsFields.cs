using System.Text.Json;
using System.Text.Json.Nodes;

namespace Naad.Settings;

/// <summary>
/// Reads one field of settings.json each. A missing field gives its fallback; a field of the wrong
/// type or an invalid value gives the fallback too, and sets <c>repaired</c> so the file is backed
/// up and rewritten. Pure.
/// </summary>
internal static class SettingsFields
{
    // Absent means the default. A name of one of the enum's members, in any casing, selects it;
    // anything else falls back to the default and marks the settings repaired. Names only:
    // Enum.TryParse would also take "1" or "Cloud, Local".
    public static TEnum ReadName<TEnum>(JsonObject root, string name, TEnum fallback, ref bool repaired)
        where TEnum : struct, Enum
    {
        if (TryReadString(root, name, ref repaired) is not { } text)
        {
            return fallback;
        }

        if (Array.Find(Enum.GetNames<TEnum>(), member =>
                string.Equals(member, text, StringComparison.OrdinalIgnoreCase)) is { } match)
        {
            return Enum.Parse<TEnum>(match);
        }

        repaired = true;
        return fallback;
    }

    // Absent means the default; a URL that TryParseBaseUrl refuses falls back to it and marks
    // the settings repaired.
    public static Uri ReadUrl(JsonObject root, string name, Uri fallback, ref bool repaired)
    {
        if (TryReadString(root, name, ref repaired) is not { } text)
        {
            return fallback;
        }

        if (AppSettings.TryParseBaseUrl(text, out var url))
        {
            return url;
        }

        repaired = true;
        return fallback;
    }

    // Absent means the default; a whole number outside IsValidTimeout falls back to it.
    public static int ReadTimeout(JsonObject root, string name, int fallback, ref bool repaired)
    {
        if (!root.TryGetPropertyValue(name, out var node))
        {
            return fallback;
        }

        if (node is JsonValue value && value.TryGetValue(out int seconds) && AppSettings.IsValidTimeout(seconds))
        {
            return seconds;
        }

        repaired = true;
        return fallback;
    }

    // Absent means the default; an id that IsValidModel refuses falls back to it.
    public static string ReadModel(JsonObject root, string name, string fallback, ref bool repaired)
    {
        if (TryReadString(root, name, ref repaired) is not { } text)
        {
            return fallback;
        }

        if (AppSettings.IsValidModel(text))
        {
            return text;
        }

        repaired = true;
        return fallback;
    }

    // Absent means the default; anything but true or false falls back to it and marks the
    // settings repaired.
    public static bool ReadBool(JsonObject root, string name, bool fallback, ref bool repaired)
    {
        if (!root.TryGetPropertyValue(name, out var node))
        {
            return fallback;
        }

        if (node?.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
        {
            return node.GetValue<bool>();
        }

        repaired = true;
        return fallback;
    }

    // Absent, null and "" all mean unset. Any other value must pass isValid; if it doesn't, the
    // setting is unset and the settings are marked repaired.
    public static string? ReadOptional(JsonObject root, string name, Func<string?, bool> isValid, ref bool repaired)
    {
        if (!root.TryGetPropertyValue(name, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue(out string? text) && (text.Length == 0 || isValid(text)))
        {
            return text.Length == 0 ? null : text;
        }

        repaired = true;
        return null;
    }

    // Null when the field is absent, or present but not a string (which marks it repaired).
    public static string? TryReadString(JsonObject root, string name, ref bool repaired)
    {
        if (!root.TryGetPropertyValue(name, out var node))
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue(out string? text))
        {
            return text;
        }

        repaired = true;
        return null;
    }
}
