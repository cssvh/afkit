using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AFKit.Models;
using Microsoft.Web.WebView2.Core;

namespace AFKit.Services;

/// <summary>"Expected" error while installing an extension (the message is ready to show to the user).</summary>
public sealed class ExtensionException : Exception
{
    public ExtensionException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Extension already unpacked on disk, ready to be loaded by WebView2.</summary>
public sealed record ExtensionPackage(string Folder, string ExtensionId, string Name, string Version, string? OptionsPage);

/// <summary>
/// Downloads (Chrome Web Store), unpacks (.crx/.zip/folder) and prepares extensions in %LOCALAPPDATA%\AFKit\Extensions\{id}\{version}.
/// WebView2 loads the "unpacked" extension from that folder, so it has to keep existing.
/// </summary>
public sealed class ExtensionService
{
    private const string StoreDownloadUrl =
        "https://clients2.google.com/service/update2/crx?response=redirect&prodversion=131.0.0.0&acceptformat=crx2,crx3&x=id%3D{0}%26uc";

    private static readonly Regex IdRegex = new(@"\b[a-p]{32}\b", RegexOptions.Compiled);

    private static readonly JsonDocumentOptions ManifestJson = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public string ExtensionsRoot { get; }

    public ExtensionService(ProfileStore store)
    {
        ExtensionsRoot = Path.Combine(store.RootFolder, "Extensions");
    }

    // ================================================================ source: store

    /// <summary>Extracts the ID (32 letters from a to p) from a Chrome Web Store link, or accepts the ID itself.</summary>
    public static string? ParseStoreId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var matches = IdRegex.Matches(input.Trim().ToLowerInvariant());
        // In a link the ID comes after the extension's "slug": keep the last occurrence.
        return matches.Count == 0 ? null : matches[^1].Value;
    }

    public async Task<ExtensionPackage> DownloadFromStoreAsync(string extensionId, CancellationToken cancel = default)
    {
        byte[] data;
        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

            using var response = await http.GetAsync(string.Format(StoreDownloadUrl, extensionId), cancel);
            if (response.StatusCode == HttpStatusCode.NoContent)
                throw new ExtensionException(Loc.T("ExtSvc.StoreEmpty"));
            if (!response.IsSuccessStatusCode)
                throw new ExtensionException(Loc.T("ExtSvc.StoreError", (int)response.StatusCode, response.ReasonPhrase ?? ""));
            data = await response.Content.ReadAsByteArrayAsync(cancel);
        }
        catch (ExtensionException) { throw; }
        catch (TaskCanceledException ex) when (!cancel.IsCancellationRequested)
        {
            throw new ExtensionException(Loc.T("ExtSvc.Timeout"), ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ExtensionException(Loc.T("ExtSvc.NetworkError", ex.Message), ex);
        }

        if (data.Length == 0)
            throw new ExtensionException(Loc.T("ExtSvc.StoreEmptyFile"));
        if (!IsCrx(data) && !IsZip(data))
            throw new ExtensionException(Loc.T("ExtSvc.NotAnExtensionFile"));

        return await Task.Run(() => Unpack(data, extensionId), cancel);
    }

    // ================================================================ source: local files

    /// <summary>A .crx file (CRX2 or CRX3) or a .zip.</summary>
    public Task<ExtensionPackage> FromFileAsync(string path) => Task.Run(() =>
    {
        byte[] data;
        try { data = File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ExtensionException(Loc.T("ExtSvc.ReadFileError", ex.Message), ex);
        }
        if (!IsCrx(data) && !IsZip(data))
            throw new ExtensionException(Loc.T("ExtSvc.InvalidFile"));
        return Unpack(data, expectedId: null);
    });

    /// <summary>Unpacked folder (with manifest.json). It is copied into AFKit's extensions folder.</summary>
    public Task<ExtensionPackage> FromFolderAsync(string folder) => Task.Run(() =>
    {
        var source = Path.GetFullPath(folder);
        if (!File.Exists(Path.Combine(source, "manifest.json")))
            throw new ExtensionException(Loc.T("ExtSvc.NoManifestInFolder"));

        var temp = NewTempFolder();
        try
        {
            CopyFolder(source, temp);
            // Without a "key" in the manifest, the ID is computed from the source path (like Chrome does for folders).
            return Finish(temp, publicKey: null, fallbackId: IdFromBytes(Encoding.UTF8.GetBytes(source.ToLowerInvariant())), reuseExisting: false);
        }
        catch
        {
            TryDeleteFolder(temp);
            throw;
        }
    });

    // ================================================================ CRX / ZIP

    private static bool IsCrx(byte[] d) => d.Length > 16 && d[0] == 'C' && d[1] == 'r' && d[2] == '2' && d[3] == '4';
    private static bool IsZip(byte[] d) => d.Length > 4 && d[0] == 'P' && d[1] == 'K' && d[2] == 3 && d[3] == 4;

    private ExtensionPackage Unpack(byte[] data, string? expectedId)
    {
        var (zipStart, publicKey) = ReadCrxHeader(data);
        var temp = NewTempFolder();
        try
        {
            ExtractZip(data, zipStart, temp);
            var fallback = expectedId ?? IdFromBytes(SHA256.HashData(data));
            return Finish(temp, publicKey, fallback);
        }
        catch
        {
            TryDeleteFolder(temp);
            throw;
        }
    }

    /// <summary>Where the ZIP starts inside the file and, if present, the public key (which defines the extension ID).</summary>
    private static (int zipStart, byte[]? publicKey) ReadCrxHeader(byte[] data)
    {
        if (!IsCrx(data)) return (0, null);

        var version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4));
        try
        {
            switch (version)
            {
                case 3:
                {
                    // "Cr24" + version + header size (protobuf CrxFileHeader) + header + ZIP
                    var headerLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4)));
                    var start = checked(12 + headerLength);
                    if (start > data.Length) throw new ExtensionException(Loc.T("ExtSvc.CrxCorruptHeader"));
                    byte[]? key = null;
                    try { key = FindCrx3PublicKey(data, 12, headerLength); }
                    catch (Exception) { /* odd header: carry on without the key (the ID changes, but the extension works) */ }
                    return (start, key);
                }
                case 2:
                {
                    // "Cr24" + version + key size + signature size + key + signature + ZIP
                    var keyLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4)));
                    var sigLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12, 4)));
                    var start = checked(16 + keyLength + sigLength);
                    if (start > data.Length) throw new ExtensionException(Loc.T("ExtSvc.CrxCorruptHeader"));
                    return (start, data.AsSpan(16, keyLength).ToArray());
                }
                default:
                    throw new ExtensionException(Loc.T("ExtSvc.CrxVersionUnsupported", version));
            }
        }
        catch (OverflowException ex)
        {
            throw new ExtensionException(Loc.T("ExtSvc.CrxCorrupt"), ex);
        }
    }

    /// <summary>
    /// Reads the CrxFileHeader (protobuf): field 2/3 = proofs with a public key (field 1 of each),
    /// field 10000 = signed_header_data, whose field 1 is the crx_id (16 bytes). The right key is the one that produces that crx_id.
    /// </summary>
    private static byte[]? FindCrx3PublicKey(byte[] data, int offset, int length)
    {
        var keys = new List<byte[]>();
        byte[]? crxId = null;
        foreach (var (field, value) in ReadLengthDelimitedFields(data, offset, length))
        {
            if (field is 2 or 3)
            {
                foreach (var (inner, innerValue) in ReadLengthDelimitedFields(value, 0, value.Length))
                    if (inner == 1) keys.Add(innerValue);
            }
            else if (field == 10000)
            {
                foreach (var (inner, innerValue) in ReadLengthDelimitedFields(value, 0, value.Length))
                    if (inner == 1) crxId = innerValue;
            }
        }
        if (crxId != null)
        {
            foreach (var key in keys)
                if (SHA256.HashData(key).Take(16).SequenceEqual(crxId)) return key;
        }
        return keys.FirstOrDefault();
    }

    /// <summary>"Length-delimited" fields (type 2) of a protobuf message; other types are skipped.</summary>
    private static List<(int field, byte[] value)> ReadLengthDelimitedFields(byte[] data, int offset, int length)
    {
        var result = new List<(int, byte[])>();
        var pos = offset;
        var end = offset + length;
        while (pos < end)
        {
            var tag = ReadVarint(data, ref pos, end);
            var field = (int)(tag >> 3);
            switch (tag & 7)
            {
                case 0: ReadVarint(data, ref pos, end); break;
                case 1: pos += 8; break;
                case 5: pos += 4; break;
                case 2:
                    var size = checked((int)ReadVarint(data, ref pos, end));
                    if (size < 0 || pos + size > end) throw new InvalidDataException("invalid protobuf");
                    result.Add((field, data.AsSpan(pos, size).ToArray()));
                    pos += size;
                    break;
                default: throw new InvalidDataException("invalid protobuf");
            }
        }
        return result;
    }

    private static ulong ReadVarint(byte[] data, ref int pos, int end)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (pos >= end) throw new InvalidDataException("invalid protobuf");
            var b = data[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
        }
        throw new InvalidDataException("invalid protobuf");
    }

    /// <summary>Chrome-style ID: first 16 bytes of the SHA-256, each nibble becomes a letter from 'a' to 'p'.</summary>
    private static string IdFromBytes(byte[] bytesToHash)
    {
        var hash = SHA256.HashData(bytesToHash);
        var sb = new StringBuilder(32);
        for (var i = 0; i < 16; i++)
        {
            sb.Append((char)('a' + (hash[i] >> 4)));
            sb.Append((char)('a' + (hash[i] & 0xF)));
        }
        return sb.ToString();
    }

    private static void ExtractZip(byte[] data, int start, string targetFolder)
    {
        var root = Path.GetFullPath(targetFolder);
        try
        {
            using var stream = new MemoryStream(data, start, data.Length - start, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.Length == 0) continue;
                var target = Path.GetFullPath(Path.Combine(root, name));
                // Protection against "zip slip" (entries trying to write outside the extension folder).
                if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (name.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }
        catch (InvalidDataException ex)
        {
            throw new ExtensionException(Loc.T("ExtSvc.PackageCorrupt", ex.Message), ex);
        }
    }

    // ================================================================ manifest and final folder

    /// <summary>Validates the manifest, writes the key (to keep the store ID), cleans _metadata and moves everything to the final folder.</summary>
    private ExtensionPackage Finish(string temp, byte[]? publicKey, string fallbackId, bool reuseExisting = true)
    {
        var baseFolder = temp;
        if (!File.Exists(Path.Combine(baseFolder, "manifest.json")))
        {
            // A hand-made .zip often has an outer folder: accept it if it is the only one.
            var dirs = Directory.GetDirectories(temp);
            if (dirs.Length == 1 && Directory.GetFiles(temp).Length == 0 && File.Exists(Path.Combine(dirs[0], "manifest.json")))
                baseFolder = dirs[0];
            else
                throw new ExtensionException(Loc.T("ExtSvc.NoManifest"));
        }

        var manifestPath = Path.Combine(baseFolder, "manifest.json");
        JsonObject manifest;
        try
        {
            manifest = JsonNode.Parse(File.ReadAllText(manifestPath), null, ManifestJson) as JsonObject
                       ?? throw new ExtensionException(Loc.T("ExtSvc.ManifestInvalid"));
        }
        catch (JsonException ex)
        {
            throw new ExtensionException(Loc.T("ExtSvc.ManifestInvalidDetail", ex.Message), ex);
        }

        var rawName = GetString(manifest, "name");
        var version = GetString(manifest, "version");
        if (string.IsNullOrWhiteSpace(rawName)) throw new ExtensionException(Loc.T("ExtSvc.ManifestMissingField", "name"));
        if (string.IsNullOrWhiteSpace(version)) throw new ExtensionException(Loc.T("ExtSvc.ManifestMissingField", "version"));
        if (manifest["manifest_version"] == null) throw new ExtensionException(Loc.T("ExtSvc.ManifestMissingField", "manifest_version"));

        // With the public key in the manifest, WebView2 gives the extension the same ID as the store (some depend on it).
        var existingKey = GetString(manifest, "key");
        string id;
        if (!string.IsNullOrEmpty(existingKey) && TryBase64(existingKey) is { } keyBytes)
        {
            id = IdFromBytes(keyBytes);
        }
        else if (publicKey is { Length: > 0 })
        {
            manifest["key"] = Convert.ToBase64String(publicKey);
            File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            id = IdFromBytes(publicKey);
        }
        else
        {
            id = fallbackId;
        }

        // _metadata is only for the store's signature check and gets in the way of "unpacked" loading.
        TryDeleteFolder(Path.Combine(baseFolder, "_metadata"));

        var name = ResolveMessage(baseFolder, manifest, rawName!);
        var options = ReadOptionsPage(manifest);
        var safeVersion = MakeSafe(version!);
        var final = Path.Combine(ExtensionsRoot, id, safeVersion);

        if (reuseExisting && File.Exists(Path.Combine(final, "manifest.json")))
        {
            // Same extension and version already unpacked (maybe in use by another profile): reuse it.
            TryDeleteFolder(temp);
            return new ExtensionPackage(final, id, name, version!, options);
        }

        TryDeleteFolder(final);
        Directory.CreateDirectory(Path.GetDirectoryName(final)!);
        Directory.Move(baseFolder, final);
        if (baseFolder != temp) TryDeleteFolder(temp);
        return new ExtensionPackage(final, id, name, version!, options);
    }

    /// <summary>Reads name/version/options from the manifest of an installed folder (for the dialog list).</summary>
    public static (string? name, string? version, string? optionsPage) ReadInfo(string folder)
    {
        try
        {
            var path = Path.Combine(folder, "manifest.json");
            if (!File.Exists(path)) return (null, null, null);
            if (JsonNode.Parse(File.ReadAllText(path), null, ManifestJson) is not JsonObject manifest) return (null, null, null);
            var raw = GetString(manifest, "name");
            return (raw == null ? null : ResolveMessage(folder, manifest, raw), GetString(manifest, "version"), ReadOptionsPage(manifest));
        }
        catch (Exception) { return (null, null, null); }
    }

    private static string? ReadOptionsPage(JsonObject manifest)
    {
        if (manifest["options_ui"] is JsonObject ui && GetString(ui, "page") is { Length: > 0 } page) return page.TrimStart('/');
        return GetString(manifest, "options_page") is { Length: > 0 } legacy ? legacy.TrimStart('/') : null;
    }

    /// <summary>Resolves "__MSG_xxx__" through _locales (UI language first, then default_locale, en, and whatever exists).</summary>
    private static string ResolveMessage(string folder, JsonObject manifest, string value)
    {
        if (!value.StartsWith("__MSG_", StringComparison.Ordinal) || !value.EndsWith("__", StringComparison.Ordinal) || value.Length <= 8)
            return value;
        var key = value[6..^2];

        var localesRoot = Path.Combine(folder, "_locales");
        if (!Directory.Exists(localesRoot)) return key;

        var candidates = new List<string>();
        if (Loc.Language == Loc.PtBr) candidates.AddRange(new[] { "pt_BR", "pt" });
        else if (Loc.Language == Loc.Spanish) candidates.AddRange(new[] { "es", "es_419", "es_ES" });
        if (GetString(manifest, "default_locale") is { Length: > 0 } def) candidates.Add(def);
        candidates.AddRange(new[] { "en", "en_US", "pt_BR", "pt" });
        try { candidates.AddRange(Directory.GetDirectories(localesRoot).Select(d => Path.GetFileName(d))); }
        catch (Exception) { }

        foreach (var locale in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var file = Path.Combine(localesRoot, locale, "messages.json");
            if (!File.Exists(file)) continue;
            try
            {
                if (JsonNode.Parse(File.ReadAllText(file), null, ManifestJson) is not JsonObject messages) continue;
                // Message keys are case-insensitive.
                foreach (var pair in messages)
                {
                    if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
                    if (pair.Value is JsonObject entry && GetString(entry, "message") is { Length: > 0 } message)
                        return message;
                }
            }
            catch (Exception) { /* malformed messages.json: try the next language */ }
        }
        return key;
    }

    private static string? GetString(JsonObject obj, string property)
    {
        try
        {
            return obj[property] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        }
        catch (Exception) { return null; }
    }

    private static byte[]? TryBase64(string value)
    {
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { return null; }
    }

    private static string MakeSafe(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
        return safe.Length == 0 ? "0" : safe;
    }

    private string NewTempFolder()
    {
        var temp = Path.Combine(ExtensionsRoot, ".tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        return temp;
    }

    private static void CopyFolder(string source, string target)
    {
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            // Version-control folders are not part of the extension.
            if (relative.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            var dest = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    public static void TryDeleteFolder(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (Exception) { /* locked file: leave it for later */ }
    }

    /// <summary>Deletes the unpacked folder if no other profile uses it (and only if it really is in AFKit's extensions folder).</summary>
    public void DeleteFolderIfUnused(string folder, IEnumerable<Profile> profiles)
    {
        if (string.IsNullOrEmpty(folder)) return;
        var full = Path.GetFullPath(folder);
        var root = Path.GetFullPath(ExtensionsRoot) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
        if (profiles.SelectMany(p => p.Extensions).Any(r =>
                !string.IsNullOrEmpty(r.SourceFolder) &&
                string.Equals(Path.GetFullPath(r.SourceFolder), full, StringComparison.OrdinalIgnoreCase)))
            return;
        TryDeleteFolder(full);
        // Remove the ID folder if it became empty.
        try
        {
            var parent = Path.GetDirectoryName(full);
            if (parent != null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                Directory.Delete(parent);
        }
        catch (Exception) { }
    }

    // ================================================================ sync with the browser

    /// <summary>
    /// Makes the profile's browser match AFKit's record: removes the ones pending removal, installs the missing ones
    /// (from SourceFolder), turns them on/off as recorded and adopts extensions the browser has but AFKit did not know about.
    /// Returns true if anything changed (worth reloading the tabs so content scripts get in).
    /// </summary>
    public static async Task<bool> SyncAsync(CoreWebView2Profile browser, Profile profile)
    {
        var changed = false;
        var installed = (await browser.GetBrowserExtensionsAsync()).ToList();

        foreach (var id in profile.RemovedExtensionIds.ToList())
        {
            var ext = installed.FirstOrDefault(e => e.Id == id);
            if (ext != null && profile.Extensions.All(r => r.Id != id))
            {
                try { await ext.RemoveAsync(); installed.Remove(ext); changed = true; }
                catch (Exception) { continue; /* try again next time */ }
            }
            profile.RemovedExtensionIds.Remove(id);
        }

        foreach (var record in profile.Extensions)
        {
            record.LastError = null;
            var ext = FindInstalled(installed, record);

            if (ext == null && !string.IsNullOrEmpty(record.SourceFolder) &&
                File.Exists(Path.Combine(record.SourceFolder, "manifest.json")))
            {
                try
                {
                    ext = await browser.AddBrowserExtensionAsync(record.SourceFolder);
                    installed.Add(ext);
                    changed = true;
                }
                catch (Exception ex)
                {
                    record.LastError = Loc.T("ExtSvc.BrowserLoadFailed", ex.Message);
                    continue;
                }
            }
            if (ext == null)
            {
                if (string.IsNullOrEmpty(record.SourceFolder) || !Directory.Exists(record.SourceFolder))
                    record.LastError = Loc.T("ExtSvc.FolderMissing");
                continue;
            }

            record.Id = ext.Id;
            if (!string.IsNullOrEmpty(ext.Name) && !ext.Name.StartsWith("__MSG_", StringComparison.Ordinal)) record.Name = ext.Name;
            if (ext.IsEnabled != record.Enabled)
            {
                try { await ext.EnableAsync(record.Enabled); changed = true; }
                catch (Exception ex) { record.LastError = Loc.T(record.Enabled ? "ExtSvc.EnableFailed" : "ExtSvc.DisableFailed", ex.Message); }
            }
        }

        // Extensions the browser has but AFKit did not record (e.g. profile imported from an old backup).
        foreach (var ext in installed.Where(e => profile.Extensions.All(r => r.Id != e.Id)).ToList())
        {
            profile.Extensions.Add(new ExtensionRecord
            {
                Id = ext.Id,
                StoreId = ext.Id,
                Name = ext.Name,
                Enabled = ext.IsEnabled
            });
        }
        return changed;
    }

    public static CoreWebView2BrowserExtension? FindInstalled(IEnumerable<CoreWebView2BrowserExtension> installed, ExtensionRecord record)
    {
        var list = installed as IList<CoreWebView2BrowserExtension> ?? installed.ToList();
        if (!string.IsNullOrEmpty(record.Id))
        {
            var byId = list.FirstOrDefault(e => e.Id == record.Id);
            if (byId != null) return byId;
        }
        // Record without the WebView2 ID (not saved yet): with the key in the manifest, the ID is the same as the store's.
        return string.IsNullOrEmpty(record.StoreId) ? null : list.FirstOrDefault(e => e.Id == record.StoreId);
    }
}
