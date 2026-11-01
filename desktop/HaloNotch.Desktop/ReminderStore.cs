using System.Text.Json.Serialization;
using Avalonia.Threading;
using static HaloNotch.Loc;

namespace HaloNotch;

public sealed record Reminder(string Title, DateTime Due)
{
    [JsonIgnore]
    public string DueText => (Due.Date == DateTime.Today ? L("Today") : Due.Date == DateTime.Today.AddDays(1) ? L("Tomorrow")
                              : Fmt.ShortDate(Due)) + " · " + Fmt.Time(Due);
}

/// Reminders created in Settings → Calendar, stored in settings.json. Windows offers no shared reminder store to
/// unpackaged apps (the Mac app reads Apple Reminders). The notch opens when one is due – also on launch for
/// reminders missed while HaloNotch was closed. ponytail: 15 s check, no repeat / snooze.
public sealed class ReminderStore : Observable
{
    const string Key = "reminders.items";
    DispatcherTimer? timer;

    public List<Reminder> Items => Prefs.Get(Key, new List<Reminder>());
    public bool NoItems => Items.Count == 0;

    /// The due reminder shown in the notch (one at a time).
    public Reminder? Current { get; private set; }

    public void Start()
    {
        Check();
        timer = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, (_, _) => Check());
        timer.Start();
    }

    public void Add(string title, DateTime due)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        Save([.. Items, new Reminder(title.Trim(), due)]);
        Check();
    }

    public void Remove(Reminder reminder)
    {
        Save(Items.Where(r => r != reminder).ToList());
        if (Current == reminder) { Current = null; Changed(nameof(Current)); Check(); }
    }

    /// Done: removes the shown reminder; the next due one (if any) follows.
    public void Complete()
    {
        if (Current is { } r) Remove(r);
    }

    void Check()
    {
        if (Current != null) return;
        Current = Items.FirstOrDefault(r => r.Due <= DateTime.Now);
        if (Current != null) Changed(nameof(Current));
    }

    void Save(List<Reminder> items)
    {
        Prefs.Set(Key, items.OrderBy(r => r.Due).ToList());
        Changed(nameof(Items));
        Changed(nameof(NoItems));
    }
}
