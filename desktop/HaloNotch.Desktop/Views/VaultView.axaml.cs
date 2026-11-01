using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using static HaloNotch.Loc;

namespace HaloNotch.Views;

/// Clipboard → Vault: create / unlock / list secrets.
public partial class VaultView : UserControl
{
    VaultStore Vault => Hub.Vault;
    bool confirmErase;

    public VaultView()
    {
        InitializeComponent();
        DataContext = Hub.Vault;
        LabelBox.PlaceholderText = L("Label");
        SecretBox.PlaceholderText = L("Secret");
        ConfirmBox.PlaceholderText = L("Repeat PIN or password");
        AddButton.Content = L("Add");
        LockButton.Content = L("Lock");
        EmptyText.Text = L("No secrets yet");
        MoveHint.Text = L("Name it and press ↩ to move it out of the clipboard history.");
        Footnote.Text = L("Copied secrets are cleared from the clipboard after 30 s. The vault locks after 5 min.");
        Vault.PropertyChanged += (_, _) => UpdateTexts();
        UpdateTexts();
    }

    void UpdateTexts()
    {
        Intro.Text = Vault.IsSetup ? L("Create a PIN or password for your vault. Secrets are encrypted and stay on this PC.")
                                   : L("The clipboard item moves here once the vault is open.");
        Intro.IsVisible = Vault.IsSetup || Vault.HasPendingMove;
        PassBox.PlaceholderText = Vault.IsSetup ? LF("PIN or password (%d+ characters)", VaultStore.MinPasscode) : L("PIN or password");
        MainButton.Content = Vault.IsSetup ? L("Create vault") : L("Unlock");
        EraseText.Text = confirmErase ? L("Erase all secrets? This cannot be undone.") : L("Forgot it? Erase vault");
    }

    void OnMain(object? sender, RoutedEventArgs e) { if (Vault.IsSetup) Vault.Create(); else Vault.Unlock(); }
    void OnPassKey(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter) OnMain(sender, e); }
    void OnAdd(object? sender, RoutedEventArgs e) => Vault.Add();
    void OnAddKey(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter) Vault.Add(); }
    void OnLock(object? sender, RoutedEventArgs e) => Vault.Lock();

    void OnErase(object? sender, RoutedEventArgs e)
    {
        if (confirmErase) { confirmErase = false; Vault.Erase(); }
        else confirmErase = true;
        UpdateTexts();
    }

    static VaultEntry? Entry(object? sender) => (sender as Control)?.DataContext as VaultEntry;
    void OnReveal(object? sender, RoutedEventArgs e) { if (Entry(sender) is { } x) Vault.ToggleReveal(x); }
    void OnCopy(object? sender, RoutedEventArgs e) { if (Entry(sender) is { } x) Vault.Copy(x); }
    void OnRemove(object? sender, RoutedEventArgs e) { if (Entry(sender) is { } x) Vault.Remove(x); }
}
