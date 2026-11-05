namespace Naad.Window;

/// <summary>The Home page's greeting. Pure. The name is shown only, never logged.</summary>
internal static class Greeting
{
    /// <summary>
    /// The first name in a Windows display name: "Ada Lovelace" and "Lovelace, Ada" both give "Ada".
    /// Null for none, or for an account name such as "DOMAIN\ada" or an email address.
    /// </summary>
    public static string? FirstName(string? displayName)
    {
        var name = displayName?.Trim() ?? "";
        if (name.Length == 0 || name.Contains('\\') || name.Contains('@'))
        {
            return null;
        }

        var comma = name.IndexOf(',');
        var given = comma >= 0 ? name[(comma + 1)..].Trim() : name;
        var first = given.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrEmpty(first) ? null : first;
    }

    /// <summary>The heading: by name when one is known, followed on the page by the shortcut's keycap.</summary>
    public static string Heading(string? firstName) =>
        firstName is null ? "Welcome back" : $"Hey {firstName}, get back into it with";
}
