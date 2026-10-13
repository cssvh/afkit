using System.Globalization;
using System.Text.Json;
using System.Windows.Markup;

namespace AFKit.Services;

/// <summary>
/// UI localization (pt-BR, English, Spanish). The language is chosen at startup (saved in afkit.json) and
/// changing it restarts the app, so every string can be resolved once, when it is needed.
/// Translations live next to each other in <c>Strings.*.cs</c>: <c>Add("key", en, ptBR, es)</c>.
/// </summary>
public static class Loc
{
    public const string PtBr = "pt-BR";
    public const string English = "en";
    public const string Spanish = "es";

    public static readonly (string Code, string Name)[] Languages =
    {
        (PtBr, "Português (Brasil)"),
        (English, "English"),
        (Spanish, "Español")
    };

    private static readonly Dictionary<string, (string En, string Pt, string Es)> Table = new();

    public static string Language { get; private set; } = English;

    static Loc() => Strings.RegisterAll();

    /// <summary>Picks the saved language, or the Windows UI language when nothing was saved yet.</summary>
    public static void Initialize(string? saved)
    {
        Language = Normalize(saved) ?? Normalize(CultureInfo.CurrentUICulture.Name) ?? English;
    }

    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        code = code.Trim();
        if (code.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return PtBr;
        if (code.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return Spanish;
        if (code.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return English;
        return null;
    }

    /// <summary>Reads only the saved language from afkit.json (the store is not loaded yet at this point).</summary>
    public static string? PeekSavedLanguage(string dataFile)
    {
        try
        {
            if (!File.Exists(dataFile)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(dataFile));
            return doc.RootElement.TryGetProperty("Language", out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }

    public static void Add(string key, string en, string pt, string es) => Table[key] = (en, pt, es);

    public static string T(string key)
    {
        if (!Table.TryGetValue(key, out var v)) return key;
        var text = Language switch { PtBr => v.Pt, Spanish => v.Es, _ => v.En };
        return string.IsNullOrEmpty(text) ? v.En : text;
    }

    public static string T(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>
    /// JavaScript that defines <c>window.__afkitText</c> with every "Js.*" text in the current language.
    /// It is prepended to the scripts injected into pages, so they can show localized text.
    /// </summary>
    public static string ScriptPrelude()
    {
        var texts = Table.Keys.Where(k => k.StartsWith("Js.", StringComparison.Ordinal)).ToDictionary(k => k[3..], T);
        return "window.__afkitText=" + JsonSerializer.Serialize(texts) + ";\n";
    }
}

/// <summary>XAML: <c>Text="{loc:T Main.NewGroup}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public TExtension() { }
    public TExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
