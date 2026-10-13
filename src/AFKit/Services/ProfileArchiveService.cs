using System.IO.Compression;
using System.Text.Json;
using AFKit.Models;

namespace AFKit.Services;

/// <summary>
/// Exports/imports a profile as .afkitprofile (zip): profile.json + a data/ folder with the browser data.
/// The profile's session must be closed before exporting.
/// </summary>
public sealed class ProfileArchiveService
{
    public const string Extension = ".afkitprofile";
    private const string ManifestName = "profile.json";
    private const string DataPrefix = "data/";

    // Folders that only hold disposable cache; left out so the backup does not get huge.
    private static readonly string[] SkippedFolders =
    {
        "Cache", "Code Cache", "GPUCache", "GrShaderCache", "ShaderCache", "DawnCache",
        "DawnGraphiteCache", "DawnWebGPUCache", "Crashpad", "component_crx_cache", "Service Worker\\CacheStorage"
    };

    private readonly ProfileStore _store;

    public ProfileArchiveService(ProfileStore store) => _store = store;

    public async Task<int> ExportAsync(Profile profile, string zipPath, IProgress<string>? progress = null)
    {
        var skipped = 0;
        await Task.Run(() =>
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            var manifest = zip.CreateEntry(ManifestName);
            using (var writer = new StreamWriter(manifest.Open()))
                writer.Write(JsonSerializer.Serialize(profile, ProfileStore.JsonOptions));

            var root = _store.GetUserDataFolder(profile);
            if (!Directory.Exists(root)) return;

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                if (IsSkipped(relative)) continue;
                progress?.Report(relative);
                try
                {
                    var entry = zip.CreateEntry(DataPrefix + relative.Replace('\\', '/'), CompressionLevel.Optimal);
                    entry.LastWriteTime = File.GetLastWriteTime(file);
                    using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var dst = entry.Open();
                    src.CopyTo(dst);
                }
                catch (IOException) { skipped++; }          // locked file (e.g. lockfile)
                catch (UnauthorizedAccessException) { skipped++; }
            }
        });
        return skipped;
    }

    public async Task<Profile> ImportAsync(string zipPath)
    {
        return await Task.Run(() =>
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var manifest = zip.GetEntry(ManifestName)
                ?? throw new InvalidDataException(Loc.T("Archive.InvalidFile"));

            Profile profile;
            using (var reader = new StreamReader(manifest.Open()))
                profile = JsonSerializer.Deserialize<Profile>(reader.ReadToEnd(), ProfileStore.JsonOptions)
                          ?? throw new InvalidDataException(Loc.T("Archive.EmptyManifest"));

            profile.Id = Guid.NewGuid().ToString("N");
            profile.CreatedAt = DateTime.Now;
            if (profile.GroupId != null && _store.Groups.All(g => g.Id != profile.GroupId))
                profile.GroupId = null;
            if (_store.Profiles.Any(p => p.Name.Equals(profile.Name, StringComparison.CurrentCultureIgnoreCase)))
                profile.Name += Loc.T("Archive.ImportedSuffix");

            var root = Path.GetFullPath(_store.GetUserDataFolder(profile));
            Directory.CreateDirectory(root);
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith(DataPrefix, StringComparison.Ordinal) || entry.FullName.EndsWith('/'))
                    continue;
                var target = Path.GetFullPath(Path.Combine(root, entry.FullName[DataPrefix.Length..]));
                // Protection against "zip slip" (entries trying to write outside the profile folder).
                if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
            return profile;
        });
    }

    private static bool IsSkipped(string relative)
    {
        foreach (var folder in SkippedFolders)
        {
            if (relative.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase) ||
                relative.Contains("\\" + folder + "\\", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return relative.EndsWith("lockfile", StringComparison.OrdinalIgnoreCase);
    }
}
