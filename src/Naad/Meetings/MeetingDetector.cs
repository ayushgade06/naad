namespace Naad.Meetings;

/// <summary>
/// Decides when to offer meeting notes: a call app or browser that has held the microphone for a
/// few seconds, once per call. Pure: it is given each snapshot of microphone use and the time.
/// Naad never records from this alone; the user must click the offer. One caller at a time.
/// </summary>
internal sealed class MeetingDetector
{
    // A microphone held this long is a call, not a voice message or a quick test.
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

    /// <summary>Call apps, and browsers for calls on the web, by their short names.</summary>
    public static readonly IReadOnlySet<string> KnownApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ms-teams", "msteams", "teams", "zoom", "slack", "discord", "webex", "ciscowebexstart", "ciscocollabhost",
        "whatsapp", "whatsappdesktop", "skype", "skypeapp", "signal", "telegram", "telegramdesktop",
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc",
    };

    private readonly Dictionary<string, TimeSpan> _since = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _handled = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Takes the latest snapshot and returns the app to offer notes for, or null. Each call gets
    /// one offer, until the app lets go of the microphone; none while Naad already records.
    /// </summary>
    /// <param name="uses">Every app's microphone use now.</param>
    /// <param name="now">The time, from any steady clock.</param>
    /// <param name="busy">Naad is recording or already offering, so every call now counts as handled.</param>
    /// <param name="ignored">Apps the user never wants an offer for; "Chrome.exe" means "chrome".</param>
    public string? Next(IEnumerable<MicrophoneUse> uses, TimeSpan now, bool busy, IReadOnlyCollection<string> ignored)
    {
        var active = uses
            .Where(u => u.InUse && KnownApps.Contains(u.App)
                && !ignored.Any(i => MicrophoneUsage.AppName(i, packaged: false) == u.App))
            .Select(u => u.App)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // An app that let go ends its call: the next one gets a new offer.
        _handled.IntersectWith(active);
        foreach (var gone in _since.Keys.Where(app => !active.Contains(app)).ToList())
        {
            _since.Remove(gone);
        }

        foreach (var app in active)
        {
            _since.TryAdd(app, now);
        }

        if (busy)
        {
            _handled.UnionWith(active);
            return null;
        }

        var ready = active
            .Where(app => !_handled.Contains(app) && now - _since[app] >= Settle)
            .Order(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (ready is not null)
        {
            _handled.Add(ready);
        }

        return ready;
    }
}
