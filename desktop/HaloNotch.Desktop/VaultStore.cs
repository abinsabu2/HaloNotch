using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using static HaloNotch.Loc;
#if WINDOWS
using HaloNotch.Win;
#endif

namespace HaloNotch;

public sealed class VaultEntry : Observable
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Label { get; init; } = "";
    public string Secret { get; init; } = "";
    bool revealed, copied;
    public bool Revealed { get => revealed; set { if (Set(ref revealed, value)) Changed(nameof(Shown)); } }
    public bool Copied { get => copied; set => Set(ref copied, value); }
    public string Shown => revealed ? Secret : new string('•', Math.Min(Secret.Length, 12));
}

/// Personal clipboard for secrets, unlocked with a PIN or password. Same design as the Mac app:
/// entries AES-GCM encrypted with a key derived from the passcode (PBKDF2-SHA256, 600k iterations),
/// stored in %APPDATA%\HaloNotch\vault.json; the key exists in memory only while unlocked.
/// ponytail: a stolen vault file can be brute-forced offline (PBKDF2 slows each guess). Upgrade: Windows Hello / DPAPI-wrapped key.
public sealed class VaultStore : Observable
{
    public const int MinPasscode = 6, Iterations = 600_000;
    static readonly TimeSpan AutoLock = TimeSpan.FromMinutes(5), ClipboardClear = TimeSpan.FromSeconds(30);

    public enum Phase { Setup, Locked, Unlocked }

    record VaultFile(int Version, string Salt, int Iterations, string Sealed);
    record Entry(Guid Id, string Label, string Secret);

    static string FilePath => Path.Combine(Prefs.Dir, "vault.json");

    Phase phase = File.Exists(FilePath) ? Phase.Locked : Phase.Setup;
    byte[]? key;
    byte[] salt = [];
    int fileIterations = Iterations, failures;
    DateTime retryAt = DateTime.MinValue;
    IDisposable? lockTimer;
    (string Text, Action OnMoved)? pendingMove;
    string? error;
    bool busy;

    public List<VaultEntry> Entries { get; private set; } = [];
    public string Passcode { get; set; } = "";
    public string Confirm { get; set; } = "";
    public string DraftLabel { get; set; } = "";
    public string DraftSecret { get; set; } = "";

    public bool IsSetup => phase == Phase.Setup;
    public bool IsLocked => phase == Phase.Locked;
    public bool IsUnlocked => phase == Phase.Unlocked;
    public bool Busy => busy;
    public string? Error => error;
    public bool HasError => error != null;
    public bool IsEmpty => Entries.Count == 0;
    public bool HasPendingMove => pendingMove != null;

    // MARK: Lock / unlock

    public async void Create()
    {
        if (phase != Phase.Setup || busy) return;
        if (Passcode.Length < MinPasscode) { Fail(LF("Use at least %d characters", MinPasscode)); return; }
        if (Passcode != Confirm) { Fail(L("The two entries don't match")); return; }
        var newSalt = RandomNumberGenerator.GetBytes(16);
        busy = true; ChangedAll();
        var derived = await Task.Run(() => Derive(Passcode, newSalt, Iterations));
        busy = false;
        key = derived; salt = newSalt; fileIterations = Iterations;
        Entries = [];
        if (!Save()) { key = null; ChangedAll(); return; }
        DidUnlock();
    }

    public async void Unlock()
    {
        if (phase != Phase.Locked || busy || Passcode.Length == 0) return;
        var wait = (retryAt - DateTime.Now).TotalSeconds;
        if (wait > 0) { Fail(LF("Too many attempts – try again in %d s", (int)Math.Ceiling(wait))); return; }
        var pass = Passcode;
        VaultFile? file = null;
        try { file = JsonSerializer.Deserialize<VaultFile>(File.ReadAllText(FilePath)); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        busy = true; ChangedAll();
        List<Entry>? opened = null;
        byte[]? derived = null;
        if (file != null)
        {
            var fileSalt = Convert.FromBase64String(file.Salt);
            derived = await Task.Run(() => Derive(pass, fileSalt, Math.Min(file.Iterations, 10_000_000)));
            opened = Open(Convert.FromBase64String(file.Sealed), derived);
        }
        busy = false;
        if (file == null || derived == null || opened == null)
        {
            failures++;
            Passcode = "";
            // 3 free tries, then 5 s, 10 s, 20 s … up to 5 min between attempts.
            if (failures >= 3)
            {
                var delay = Math.Min(5 * Math.Pow(2, failures - 3), 300);
                retryAt = DateTime.Now.AddSeconds(delay);
                Fail(LF("Too many attempts – try again in %d s", (int)delay));
            }
            else Fail(L("Wrong PIN or password"));
            return;
        }
        key = derived;
        salt = Convert.FromBase64String(file.Salt);
        fileIterations = file.Iterations;
        Entries = opened.Select(e => new VaultEntry { Id = e.Id, Label = e.Label, Secret = e.Secret }).ToList();
        DidUnlock();
    }

    public void Lock()
    {
        if (phase != Phase.Unlocked) return;
        lockTimer?.Dispose();
        if (key != null) CryptographicOperations.ZeroMemory(key);
        key = null;
        Entries = [];
        DraftLabel = DraftSecret = "";
        pendingMove = null;                       // the history item stays where it was
        phase = Phase.Locked;
        ChangedAll();
    }

    /// Forgotten passcode: deletes the vault file and every secret in it.
    public void Erase()
    {
        try { File.Delete(FilePath); } catch (IOException) { }
        Lock();
        failures = 0;
        retryAt = DateTime.MinValue;
        ClearInputs();
        phase = Phase.Setup;
        ChangedAll();
    }

    void DidUnlock()
    {
        failures = 0;
        retryAt = DateTime.MinValue;
        ClearInputs();
        if (pendingMove is { } move) DraftSecret = move.Text;
        phase = Phase.Unlocked;
        Touch();
        ChangedAll();
    }

    void ClearInputs() { Passcode = Confirm = ""; error = null; }
    void Fail(string message) { error = message; ChangedAll(); }

    /// Restarts the auto-lock countdown (every interaction).
    void Touch()
    {
        lockTimer?.Dispose();
        lockTimer = DispatcherTimer.RunOnce(Lock, AutoLock);
    }

    // MARK: Entries

    /// "Move to Vault" from the clipboard history: prefills the secret (after unlock if needed);
    /// `onMoved` removes it from the history once it is saved here.
    public void MoveIn(string text, Action onMoved)
    {
        pendingMove = (text, onMoved);
        if (phase == Phase.Unlocked) { DraftSecret = text; Touch(); }
        ChangedAll();
    }

    public void Add()
    {
        if (phase != Phase.Unlocked || DraftSecret.Length == 0) return;
        var label = DraftLabel.Trim();
        var previous = Entries;
        var secret = DraftSecret;
        Entries = [new VaultEntry { Label = label.Length == 0 ? L("Secret") : label, Secret = secret }, .. Entries];
        if (Save())
        {
            DraftLabel = DraftSecret = "";
            if (pendingMove is { } move && move.Text == secret) move.OnMoved();
            pendingMove = null;
        }
        else Entries = previous;
        Touch();
        ChangedAll();
    }

    public void Remove(VaultEntry entry)
    {
        var previous = Entries;
        Entries = Entries.Where(e => e != entry).ToList();
        if (!Save()) Entries = previous;
        Touch();
        ChangedAll();
    }

    public void ToggleReveal(VaultEntry entry) { entry.Revealed = !entry.Revealed; Touch(); }

    /// Copies the secret marked "don't record" (HaloNotch's history, Win+V and cloud clipboard skip it)
    /// and clears the clipboard after 30 s unless something else was copied meanwhile.
    public void Copy(VaultEntry entry)
    {
#if WINDOWS
        ClipboardWin.WriteText(entry.Secret, secret: true);
        var sequence = ClipboardWin.Sequence;
        DispatcherTimer.RunOnce(() => { if (ClipboardWin.Sequence == sequence) ClipboardWin.Clear(); }, ClipboardClear);
#endif
        entry.Copied = true;
        DispatcherTimer.RunOnce(() => entry.Copied = false, TimeSpan.FromSeconds(1.5));
        Touch();
    }

    // MARK: Storage & crypto

    bool Save()
    {
        if (key == null) return false;
        try
        {
            var plain = JsonSerializer.SerializeToUtf8Bytes(Entries.Select(e => new Entry(e.Id, e.Label, e.Secret)).ToList());
            var file = new VaultFile(1, Convert.ToBase64String(salt), fileIterations, Convert.ToBase64String(Seal(plain, key)));
            Directory.CreateDirectory(Prefs.Dir);
            Prefs.WriteAtomic(FilePath, JsonSerializer.Serialize(file));
            error = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
        {
            Debug.WriteLine($"HaloNotch: vault save failed – {e.Message}");
            error = L("Could not save the vault");
            return false;
        }
    }

    static byte[] Derive(string passcode, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passcode), salt, iterations, HashAlgorithmName.SHA256, 32);

    /// AES-GCM combined: nonce (12) + ciphertext + tag (16) – same layout as CryptoKit's `combined`.
    static byte[] Seal(byte[] plain, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return [.. nonce, .. cipher, .. tag];
    }

    static List<Entry>? Open(byte[] sealedBytes, byte[] key)
    {
        if (sealedBytes.Length < 28) return null;
        var plain = new byte[sealedBytes.Length - 28];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(sealedBytes[..12], sealedBytes[12..^16], sealedBytes[^16..], plain);   // wrong key fails the tag
            return JsonSerializer.Deserialize<List<Entry>>(plain);
        }
        catch (Exception e) when (e is CryptographicException or JsonException) { return null; }
    }

#if DEBUG
    /// Round trip with the right passcode opens, a wrong one does not.
    public static void SelfCheck()
    {
        var s = RandomNumberGenerator.GetBytes(16);
        var plain = JsonSerializer.SerializeToUtf8Bytes(new List<Entry> { new(Guid.NewGuid(), "a", "s3cret") });
        var sealedBytes = Seal(plain, Derive("123456", s, 1000));
        Debug.Assert(Open(sealedBytes, Derive("123456", s, 1000))?[0].Secret == "s3cret");
        Debug.Assert(Open(sealedBytes, Derive("123457", s, 1000)) == null);
    }
#endif
}
