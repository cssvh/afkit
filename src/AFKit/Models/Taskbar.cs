using System.Globalization;
using System.Windows.Media;
using AFKit.Services;

namespace AFKit.Models;

/// <summary>V2: a Pokémon as the page reader reported it.</summary>
public sealed class PokemonInfo
{
    public string Name { get; init; } = "";
    public int? Level { get; init; }
    public long? Hp { get; init; }
    public long? MaxHp { get; init; }
    /// <summary>The Pokémon's experience (yours only; the enemy has none).</summary>
    public long? Xp { get; init; }
    public long? XpMax { get; init; }
    public bool Shiny { get; init; }
    /// <summary>Sprite key (e.g. "me:Weavile", "lt:60149"); the frames arrive in a separate message.</summary>
    public string? SpriteKey { get; init; }

    public double HpRatio => Hp is { } hp && MaxHp is { } max && max > 0 ? Math.Clamp((double)hp / max, 0, 1) : 1;
    /// <summary>0..1, or null when the game did not show the experience.</summary>
    public double? XpRatio => Xp is { } xp && XpMax is { } max && max > 0 ? Math.Clamp((double)xp / max, 0, 1) : null;
}

/// <summary>V2: what is happening in the game in a tab.</summary>
public sealed class GameSnapshot
{
    public PokemonInfo? Me { get; init; }
    /// <summary>Enemy your Pokémon is attacking (null = out of combat).</summary>
    public PokemonInfo? Foe { get; init; }
}

/// <summary>V2: session summary as the page shows it (already formatted texts, e.g. "32:17:18", "12.980.326").</summary>
public sealed record SessionSummary(string Time, string Xp, string Gold, string? Profit);

/// <summary>V2: a capture seal (P2, IV, Q, N…) with the colors the page used.</summary>
public sealed class CaptureSeal
{
    public string Text { get; init; } = "";
    public Brush Background { get; init; } = CssColor.SealBackground;
    public Brush Foreground { get; init; } = CssColor.White;
}

/// <summary>V2: a capture row (record or recent), ready for the battle window's strip.</summary>
public sealed class CaptureRow
{
    public string Name { get; init; } = "";
    /// <summary>"Lv 100" (as the page shows it).</summary>
    public string Level { get; init; } = "";
    /// <summary>"10:44".</summary>
    public string Time { get; init; } = "";
    public bool Shiny { get; init; }
    public ImageSource? Portrait { get; init; }
    public bool HasPortrait => Portrait != null;
    /// <summary>Left border, in the grade's color.</summary>
    public Brush BorderBrush { get; init; } = CssColor.DefaultBorder;
    public IReadOnlyList<CaptureSeal> Seals { get; init; } = Array.Empty<CaptureSeal>();
    public bool HasSeals => Seals.Count > 0;
    /// <summary>Digest of the content (to tell whether the row changed without comparing field by field).</summary>
    public string Signature { get; init; } = "";
}

/// <summary>V2: session record and latest captures of a tab.</summary>
public sealed class CapturesInfo
{
    public CapturesInfo(CaptureRow? record, IReadOnlyList<CaptureRow> recent)
    {
        Record = record;
        Recent = recent;
        Signature = (record?.Signature ?? "-") + "\u001e" + string.Join("\u001e", recent.Select(r => r.Signature));
    }

    public CaptureRow? Record { get; }
    public IReadOnlyList<CaptureRow> Recent { get; }
    public string Signature { get; }
}

/// <summary>V2: CSS colors the page sends ("rgb(r, g, b)", "rgba(r, g, b, a)", also "#rrggbb").</summary>
public static class CssColor
{
    public static readonly Brush White = Frozen(Colors.White);
    /// <summary>Left border of a capture without a grade.</summary>
    public static readonly Brush DefaultBorder = Frozen(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF));
    /// <summary>Background of a seal without a color.</summary>
    public static readonly Brush SealBackground = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));

    public static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Brush for the CSS color, or <paramref name="fallback"/> if it is null or invalid.</summary>
    public static Brush BrushOr(string? css, Brush fallback) => Parse(css) is { } c ? Frozen(c) : fallback;

    /// <summary>Reads "rgb(…)", "rgba(…)" (with commas or spaces, "/ a" and percentages) or "#rgb"/"#rrggbb"/"#rrggbbaa".</summary>
    public static Color? Parse(string? css)
    {
        if (string.IsNullOrWhiteSpace(css) || css.Length > 64) return null;
        var s = css.Trim().ToLowerInvariant();

        if (s.StartsWith('#'))
        {
            var hex = s[1..];
            if (hex.Length == 3) hex = string.Concat(hex.Select(ch => new string(ch, 2)));
            if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                return null;
            return hex.Length == 6
                ? Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v)
                : Color.FromArgb((byte)v, (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8));
        }

        if (!s.StartsWith("rgb", StringComparison.Ordinal)) return null;
        var open = s.IndexOf('(');
        var close = s.LastIndexOf(')');
        if (open < 0 || close <= open) return null;
        var parts = s[(open + 1)..close].Replace(',', ' ').Replace('/', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (3 or 4)) return null;

        var rgb = new byte[3];
        for (var i = 0; i < 3; i++)
        {
            if (Number(parts[i], 255) is not { } n) return null;
            rgb[i] = (byte)Math.Round(Math.Clamp(n, 0, 255));
        }
        byte a = 255;
        if (parts.Length == 4)
        {
            if (Number(parts[3], 1) is not { } alpha) return null;
            a = (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255);
        }
        return Color.FromArgb(a, rgb[0], rgb[1], rgb[2]);
    }

    /// <summary>CSS number; "50%" becomes the fraction of <paramref name="full"/>.</summary>
    private static double? Number(string text, double full)
    {
        var pct = text.EndsWith('%');
        if (pct) text = text[..^1];
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || double.IsNaN(n)) return null;
        return pct ? n / 100 * full : n;
    }
}

/// <summary>V2: one cell of the battle window (one profile).</summary>
public sealed class TaskbarCell : Observable
{
    public string ProfileId { get; init; } = "";

    private string _meText = "";
    private double _meHp = 1;
    private ImageSource? _meFrame;
    private bool _hasFoe;
    private string _foeText = "";
    private double _foeHp = 1;
    private ImageSource? _foeFrame;

    public string MeText { get => _meText; set => Set(ref _meText, value); }
    public double MeHp { get => _meHp; set { if (Set(ref _meHp, value)) Raise(nameof(MeHpBrush)); } }
    public ImageSource? MeFrame { get => _meFrame; set => Set(ref _meFrame, value); }

    public bool HasFoe { get => _hasFoe; set => Set(ref _hasFoe, value); }
    public string FoeText { get => _foeText; set => Set(ref _foeText, value); }
    public double FoeHp { get => _foeHp; set { if (Set(ref _foeHp, value)) Raise(nameof(FoeHpBrush)); } }
    public ImageSource? FoeFrame { get => _foeFrame; set => Set(ref _foeFrame, value); }

    // ----- Game Boy Advance style visual (Controls/BattleCell) -----
    private string _meName = "";
    private string _meLevel = "";
    private bool _meShiny;
    private string _meHpText = "";
    private double _meExp;
    private bool _hasExp;
    private string _foeName = "";
    private string _foeLevel = "";
    private bool _foeShiny;

    /// <summary>Name in UPPERCASE, like the GBA games.</summary>
    public string MeName { get => _meName; set => Set(ref _meName, value); }
    /// <summary>"Lv17" (empty if the level did not arrive).</summary>
    public string MeLevel { get => _meLevel; set => Set(ref _meLevel, value); }
    public bool MeShiny { get => _meShiny; set => Set(ref _meShiny, value); }
    /// <summary>"8534/14304" under the HP bar.</summary>
    public string MeHpText { get => _meHpText; set => Set(ref _meHpText, value); }
    /// <summary>EXP bar, 0..1.</summary>
    public double MeExp { get => _meExp; set => Set(ref _meExp, value); }
    public bool HasExp { get => _hasExp; set => Set(ref _hasExp, value); }
    public Brush MeHpBrush => HpBrush(MeHp);

    public string FoeName { get => _foeName; set => Set(ref _foeName, value); }
    public string FoeLevel { get => _foeLevel; set => Set(ref _foeLevel, value); }
    public bool FoeShiny { get => _foeShiny; set => Set(ref _foeShiny, value); }
    public Brush FoeHpBrush => HpBrush(FoeHp);

    // GBA HP bar colors: green above 50%, yellow from 20% to 50%, red below 20%.
    private static readonly Brush HpGreen = Frozen(0x58, 0xD0, 0x80);
    private static readonly Brush HpYellow = Frozen(0xF8, 0xD0, 0x38);
    private static readonly Brush HpRed = Frozen(0xF0, 0x50, 0x38);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Brush HpBrush(double ratio) => ratio > 0.5 ? HpGreen : ratio >= 0.2 ? HpYellow : HpRed;

    /// <summary>Fills in your Pokémon (name, level, HP, EXP and frames).</summary>
    public void SetMe(PokemonInfo me, IReadOnlyList<ImageSource> frames)
    {
        MeName = me.Name.ToUpperInvariant();
        MeLevel = me.Level is { } lv ? $"{Loc.T("Taskbar.LevelShort")}{lv}" : "";
        MeShiny = me.Shiny;
        MeHp = me.HpRatio;
        MeHpText = me.Hp is { } hp && me.MaxHp is { } max ? $"{hp}/{max}" : "";
        var xp = me.XpRatio;
        HasExp = xp != null;
        MeExp = xp ?? 0;
        MeFrames = frames;
    }

    /// <summary>Fills in the enemy; null = out of combat (only your Pokémon shows).</summary>
    public void SetFoe(PokemonInfo? foe, IReadOnlyList<ImageSource> frames)
    {
        HasFoe = foe != null;
        FoeName = foe?.Name.ToUpperInvariant() ?? "";
        FoeLevel = foe?.Level is { } lv ? $"{Loc.T("Taskbar.LevelShort")}{lv}" : "";
        FoeShiny = foe?.Shiny ?? false;
        FoeHp = foe?.HpRatio ?? 1;
        FoeFrames = foe == null ? Array.Empty<ImageSource>() : frames;
    }

    // ----- info strip below the battle (Controls/BattleInfo) -----
    private bool _hasInfo;
    private string _time = "—";
    private string _xpPerHour = "—";
    private string _goldPerHour = "—";
    private string _profitPerHour = "—";
    private Brush _profitBrush = CssColor.White;
    private CaptureRow? _record;
    private IReadOnlyList<CaptureRow> _recent = Array.Empty<CaptureRow>();
    private CapturesInfo? _caps;

    private static readonly Brush ProfitUp = Frozen(0x5F, 0xE0, 0x8A);
    private static readonly Brush ProfitDown = Frozen(0xFF, 0x6B, 0x6B);

    /// <summary>The page sent the summary or the captures (the strip shows up).</summary>
    public bool HasInfo { get => _hasInfo; set => Set(ref _hasInfo, value); }
    public string Time { get => _time; set => Set(ref _time, value); }
    public string XpPerHour { get => _xpPerHour; set => Set(ref _xpPerHour, value); }
    public string GoldPerHour { get => _goldPerHour; set => Set(ref _goldPerHour, value); }
    public string ProfitPerHour { get => _profitPerHour; set => Set(ref _profitPerHour, value); }
    /// <summary>Green if positive, red if negative, white with no value.</summary>
    public Brush ProfitBrush { get => _profitBrush; set => Set(ref _profitBrush, value); }

    public CaptureRow? Record { get => _record; set { if (Set(ref _record, value)) Raise(nameof(HasRecord)); } }
    public bool HasRecord => Record != null;
    public IReadOnlyList<CaptureRow> Recent { get => _recent; set { if (Set(ref _recent, value)) Raise(nameof(HasRecent)); } }
    public bool HasRecent => Recent.Count > 0;

    /// <summary>
    /// Fills in the strip. The summary changes every second (only the texts that changed notify the UI); the captures are only
    /// replaced when the object changes (ProfileView only swaps it when the content changed).
    /// </summary>
    public void SetInfo(SessionSummary? summary, CapturesInfo? caps)
    {
        Time = Text(summary?.Time);
        XpPerHour = Text(summary?.Xp);
        GoldPerHour = Text(summary?.Gold);
        var profit = summary?.Profit;
        ProfitPerHour = Text(profit);
        ProfitBrush = ProfitColor(profit);

        if (!ReferenceEquals(caps, _caps))
        {
            _caps = caps;
            Record = caps?.Record;
            Recent = caps?.Recent ?? Array.Empty<CaptureRow>();
        }
        HasInfo = summary != null || HasRecord || HasRecent;
    }

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static Brush ProfitColor(string? profit)
    {
        if (string.IsNullOrWhiteSpace(profit) || !profit.Any(char.IsDigit)) return CssColor.White;
        var t = profit.TrimStart();
        if (t[0] is '−' or '-') return ProfitDown;
        return profit.Any(ch => ch is >= '1' and <= '9') ? ProfitUp : CssColor.White;
    }

    // Animation frames (the window's timer advances the index).
    public IReadOnlyList<ImageSource> MeFrames { get; set; } = Array.Empty<ImageSource>();
    public IReadOnlyList<ImageSource> FoeFrames { get; set; } = Array.Empty<ImageSource>();
    internal int FrameIndex { get; set; }

    // Move effect over the enemy (plays once per attack).
    private ImageSource? _effectFrame;
    public ImageSource? EffectFrame { get => _effectFrame; set => Set(ref _effectFrame, value); }
    private IReadOnlyList<ImageSource> _effectFrames = Array.Empty<ImageSource>();
    private int _effectMs = 60;
    private DateTime _effectStart;

    public void PlayEffect(IReadOnlyList<ImageSource> frames, int ms)
    {
        if (frames.Count == 0) return;
        _effectFrames = frames;
        _effectMs = Math.Max(20, ms);
        _effectStart = DateTime.UtcNow;
        EffectFrame = frames[0];
    }

    /// <summary>Advances the effect by the clock (not by tick count), so each frame's duration is respected.</summary>
    public void TickEffect(DateTime now)
    {
        if (_effectFrames.Count == 0) return;
        var i = (int)((now - _effectStart).TotalMilliseconds / _effectMs);
        if (i >= _effectFrames.Count || !HasFoe)
        {
            _effectFrames = Array.Empty<ImageSource>();
            EffectFrame = null;
            return;
        }
        var frame = _effectFrames[i];
        if (!ReferenceEquals(frame, EffectFrame)) EffectFrame = frame;
    }

    /// <summary>Shows the next frame. Out of combat your Pokémon stays still (first frame).</summary>
    public void Advance()
    {
        FrameIndex++;
        MeFrame = MeFrames.Count == 0 ? null : HasFoe ? MeFrames[FrameIndex % MeFrames.Count] : MeFrames[0];
        FoeFrame = !HasFoe || FoeFrames.Count == 0 ? null : FoeFrames[FrameIndex % FoeFrames.Count];
    }
}
