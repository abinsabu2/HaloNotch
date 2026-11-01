using Avalonia.Controls;
using Avalonia.Interactivity;
using static HaloNotch.Loc;

namespace HaloNotch.Views;

/// Reminders tab: upcoming reminders (soonest first), ✓ marks one done. New ones are added in Settings → Calendar.
public partial class RemindersView : UserControl
{
    public static string DoneText => L("Done");
    public event Action? OpenSettings;

    public RemindersView()
    {
        InitializeComponent();
        DataContext = Hub.Reminders;
        AddButton.Content = L("Add reminder");
        EmptyText.Text = L("No reminders");
    }

    void OnAdd(object? s, RoutedEventArgs e) => OpenSettings?.Invoke();
    void OnDone(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is Reminder r) Hub.Reminders.Remove(r); }
}
