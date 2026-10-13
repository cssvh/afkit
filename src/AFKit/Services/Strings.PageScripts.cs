namespace AFKit.Services;

internal static partial class Strings
{
    // Texts used by the scripts injected into pages (read there through window.__afkitText, without the "Js." prefix).
    private static void PageScripts()
    {
        Add("Js.Time", "Time", "Tempo", "Tiempo");
        Add("Js.XpPerHour", "XP/h", "XP/h", "XP/h");
        Add("Js.GoldPerHour", "Gold/h", "Gold/h", "Gold/h");
        Add("Js.ProfitPerHour", "Profit/h", "Lucro/h", "Ganancia/h");
        Add("Js.SessionRecord", "Session record", "Recorde da sessão", "Récord de la sesión");
        Add("Js.Level", "Lv", "Nv", "Nv");
        Add("Js.Kills", "Defeated", "Abatidos", "Derrotados");
        Add("Js.KillsPerHour", "Defeated/h", "Abatidos/h", "Derrotados/h");
        Add("Js.Captures", "Captures", "Capturas", "Capturas");
        Add("Js.CapturesPerHour", "Captures/h", "Capturas/h", "Capturas/h");
        Add("Js.Shiny", "Shiny seen/caught", "Shiny vistos/capt.", "Shiny vistos/capt.");
        Add("Js.ShowCaptures", "Show captures", "Mostrar capturas", "Mostrar capturas");
        Add("Js.HideCaptures", "Hide captures", "Ocultar capturas", "Ocultar capturas");
        Add("Js.ClearAll", "Clear all", "Limpar tudo", "Borrar todo");
    }
}
