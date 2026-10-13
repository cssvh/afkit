using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AFKit.Models;

namespace AFKit.Services;

/// <summary>
/// Persists profiles and groups in %LOCALAPPDATA%\AFKit\afkit.json.
/// Each profile has its own browser data folder in %LOCALAPPDATA%\AFKit\Profiles\{id}.
/// </summary>
public sealed class ProfileStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Data folder: %LOCALAPPDATA%\AFKit, or the folder in the AFKIT_DATA_DIR environment variable (portable use, tests).</summary>
    public static string DefaultRoot =>
        Environment.GetEnvironmentVariable("AFKIT_DATA_DIR") is { Length: > 0 } custom ? custom : Path.Combine(LocalAppData, "AFKit");
    public static string DefaultDataFile => Path.Combine(DefaultRoot, "afkit.json");

    public string RootFolder { get; }
    public string ProfilesFolder => Path.Combine(RootFolder, "Profiles");
    private string DataFile => Path.Combine(RootFolder, "afkit.json");

    public ObservableCollection<ProfileGroup> Groups { get; } = new();
    public ObservableCollection<Profile> Profiles { get; } = new();
    public string? LastProfileId { get; set; }
    /// <summary>UI language code (pt-BR, en or es); null = follow Windows.</summary>
    public string? Language { get; set; }
    public bool GridMode { get; set; }
    public bool MuteAll { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool AutoRecover { get; set; } = true;
    public double GridColumnRatio { get; set; } = 0.5;
    public double GridRowRatio { get; set; } = 0.5;
    public bool TrayHintShown { get; set; }
    public bool SidebarCollapsed { get; set; }
    public bool TaskbarEnabled { get; set; }
    // V2: values of the old single window — only preserved in afkit.json; each profile now keeps its own
    // (Profile.TaskbarOverlay / Profile.TaskbarBounds).
    public bool TaskbarOverlay { get; set; } = true;
    public double[]? TaskbarBounds { get; set; }

    public const int MaxPinned = 4;
    /// <summary>Order of the grid cells.</summary>
    public List<string> PinnedIds { get; } = new();

    public List<Profile> PinnedProfiles() =>
        PinnedIds.Select(id => Profiles.FirstOrDefault(p => p.Id == id)).OfType<Profile>().ToList();

    public bool TogglePin(Profile profile)
    {
        if (PinnedIds.Remove(profile.Id)) { profile.IsPinned = false; return true; }
        if (PinnedIds.Count >= MaxPinned) return false;
        PinnedIds.Add(profile.Id);
        profile.IsPinned = true;
        return true;
    }

    public ProfileStore(string? rootFolder = null)
    {
        RootFolder = rootFolder ?? DefaultRoot;
        if (rootFolder == null) MigrateLegacyData();
        Directory.CreateDirectory(ProfilesFolder);
    }

    /// <summary>The app used to be called Deck: moves the old data folder (profiles, logins, extensions) to the new name.</summary>
    public static void MigrateLegacyData()
    {
        try
        {
            if (Environment.GetEnvironmentVariable("AFKIT_DATA_DIR") is { Length: > 0 }) return; // custom folder: nothing to migrate
            var oldRoot = Path.Combine(LocalAppData, "Deck");
            if (!Directory.Exists(oldRoot)) return;
            if (Directory.Exists(DefaultRoot))
            {
                // An earlier run may have left an empty AFKit folder behind (e.g. while the old Deck was still open,
                // so the move failed). Only a folder with real data blocks the migration.
                if (HasUserData(DefaultRoot)) return;
                Directory.Delete(DefaultRoot, recursive: true);
            }
            Directory.Move(oldRoot, DefaultRoot);
            var oldFile = Path.Combine(DefaultRoot, "deck.json");
            if (File.Exists(oldFile)) File.Move(oldFile, DefaultDataFile);
        }
        catch { /* no migration (e.g. old Deck still open): AFKit starts empty and the old folder stays untouched */ }
    }

    /// <summary>Does the data folder hold anything worth keeping (profiles or profile browser data)?</summary>
    private static bool HasUserData(string root)
    {
        try
        {
            var profiles = Path.Combine(root, "Profiles");
            if (Directory.Exists(profiles) && Directory.EnumerateFileSystemEntries(profiles).Any()) return true;
            var file = Path.Combine(root, "afkit.json");
            if (!File.Exists(file)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            return doc.RootElement.TryGetProperty("Profiles", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0;
        }
        catch { return true; } // unreadable: be conservative and keep it
    }

    public string GetUserDataFolder(Profile profile) => Path.Combine(ProfilesFolder, profile.Id);

    public void Load()
    {
        Groups.Clear();
        Profiles.Clear();
        if (!File.Exists(DataFile)) return;

        try
        {
            var data = JsonSerializer.Deserialize<AFKitData>(File.ReadAllText(DataFile), JsonOptions) ?? new AFKitData();
            foreach (var g in data.Groups) Groups.Add(g);
            foreach (var p in data.Profiles) Profiles.Add(p);
            LastProfileId = data.LastProfileId;
            Language = data.Language;
            GridMode = data.GridMode;
            MuteAll = data.MuteAll;
            CloseToTray = data.CloseToTray;
            AutoRecover = data.AutoRecover;
            GridColumnRatio = Math.Clamp(data.GridColumnRatio, 0.15, 0.85);
            GridRowRatio = Math.Clamp(data.GridRowRatio, 0.15, 0.85);
            TrayHintShown = data.TrayHintShown;
            SidebarCollapsed = data.SidebarCollapsed;
            TaskbarEnabled = data.TaskbarEnabled;
            TaskbarOverlay = data.TaskbarOverlay;
            TaskbarBounds = data.TaskbarBounds is { Length: 4 } b ? b : null;
            PinnedIds.Clear();
            foreach (var id in data.PinnedIds.Distinct().Take(MaxPinned))
            {
                var p = Profiles.FirstOrDefault(x => x.Id == id);
                if (p == null) continue;
                p.IsPinned = true;
                PinnedIds.Add(id);
            }
        }
        catch (Exception ex)
        {
            // Keep a copy of the corrupt file so no data is lost.
            var backup = DataFile + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(DataFile, backup, overwrite: true);
            throw new InvalidDataException(Loc.T("Store.ReadError", DataFile, backup), ex);
        }
    }

    public void Save()
    {
        var data = new AFKitData
        {
            Language = Language,
            Groups = Groups.ToList(),
            Profiles = Profiles.ToList(),
            LastProfileId = LastProfileId,
            PinnedIds = PinnedIds.Where(id => Profiles.Any(p => p.Id == id)).ToList(),
            GridMode = GridMode,
            MuteAll = MuteAll,
            CloseToTray = CloseToTray,
            AutoRecover = AutoRecover,
            GridColumnRatio = GridColumnRatio,
            GridRowRatio = GridRowRatio,
            TrayHintShown = TrayHintShown,
            SidebarCollapsed = SidebarCollapsed,
            TaskbarEnabled = TaskbarEnabled,
            TaskbarOverlay = TaskbarOverlay,
            TaskbarBounds = TaskbarBounds
        };
        var tmp = DataFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, JsonOptions));
        File.Move(tmp, DataFile, overwrite: true);
    }

    public IEnumerable<Profile> ProfilesInGroup(string? groupId) =>
        Profiles.Where(p => p.GroupId == groupId)
            .OrderBy(p => p.Order)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>Next free position at the end of a group.</summary>
    public int NextOrder(string? groupId) =>
        Profiles.Where(p => p.GroupId == groupId).Select(p => p.Order).DefaultIfEmpty(-1).Max() + 1;

    /// <summary>
    /// Moves the profile to the given group at <paramref name="index"/> (relative to the group's list without it)
    /// and renumbers the group's order.
    /// </summary>
    public void MoveProfile(Profile profile, string? groupId, int index)
    {
        var list = ProfilesInGroup(groupId).Where(p => p != profile).ToList();
        index = Math.Clamp(index, 0, list.Count);
        list.Insert(index, profile);
        profile.GroupId = groupId;
        for (var i = 0; i < list.Count; i++) list[i].Order = i;
    }

    /// <summary>Deletes the data folder with a few retries (the browser process may take a while to release the files).</summary>
    public async Task DeleteUserDataAsync(Profile profile)
    {
        var folder = GetUserDataFolder(profile);
        for (var attempt = 0; attempt < 10 && Directory.Exists(folder); attempt++)
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException) { await Task.Delay(500); }
            catch (UnauthorizedAccessException) { await Task.Delay(500); }
        }
    }
}
