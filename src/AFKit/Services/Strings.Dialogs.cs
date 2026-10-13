namespace AFKit.Services;

internal static partial class Strings
{
    // Dialogs, battle window, extensions window, app-level messages.
    private static void Dialogs()
    {
        // ---- shared buttons
        Add("Dlg.Cancel", "Cancel", "Cancelar", "Cancelar");
        Add("Dlg.Save", "Save", "Salvar", "Guardar");
        Add("Dlg.SignIn", "Sign in", "Entrar", "Entrar");
        Add("Dlg.Close", "Close", "Fechar", "Cerrar");
        Add("Dlg.Color", "Color", "Cor", "Color");

        // ---- app level
        Add("App.NoWebView2", "The Microsoft Edge WebView2 Runtime is not installed.\n\nOpen the download page?",
            "O Microsoft Edge WebView2 Runtime não está instalado.\n\nDeseja abrir a página de download?",
            "Microsoft Edge WebView2 Runtime no está instalado.\n\n¿Abrir la página de descarga?");
        Add("App.UnexpectedError", "AFKit — unexpected error", "AFKit — erro inesperado", "AFKit — error inesperado");

        // ---- site credentials
        Add("Cred.Title", "Authentication required", "Autenticação necessária", "Se requiere autenticación");
        Add("Cred.User", "Username", "Usuário", "Usuario");
        Add("Cred.Password", "Password", "Senha", "Contraseña");
        Add("Cred.Request", "Login requested for:\n{0}", "Login solicitado para:\n{0}", "Inicio de sesión solicitado para:\n{0}");

        // ---- group editor
        Add("Group.Title", "Group", "Grupo", "Grupo");
        Add("Group.Name", "Group name", "Nome do grupo", "Nombre del grupo");
        Add("Group.New", "New group", "Novo grupo", "Nuevo grupo");
        Add("Group.Edit", "Edit group", "Editar grupo", "Editar grupo");
        Add("Group.NameRequired", "Give the group a name.", "Dê um nome ao grupo.", "Ponle un nombre al grupo.");

        // ---- profile editor
        Add("Prof.Title", "Profile", "Perfil", "Perfil");
        Add("Prof.Name", "Name", "Nome", "Nombre");
        Add("Prof.Icon", "Icon", "Ícone", "Icono");
        Add("Prof.IconTip", "An emoji or up to 2 letters. Empty = initials of the name.",
            "Um emoji ou até 2 letras. Vazio = iniciais do nome.",
            "Un emoji o hasta 2 letras. Vacío = iniciales del nombre.");
        Add("Prof.Group", "Group", "Grupo", "Grupo");
        Add("Prof.StartPage", "Start page", "Página inicial", "Página de inicio");
        Add("Prof.NoGroup", "(no group)", "(sem grupo)", "(sin grupo)");
        Add("Prof.NewTitle", "New profile", "Novo perfil", "Nuevo perfil");
        Add("Prof.EditTitle", "Edit — {0}", "Editar — {0}", "Editar — {0}");
        Add("Prof.NameRequired", "Give the profile a name.", "Dê um nome ao perfil.", "Ponle un nombre al perfil.");
        Add("Prof.BadColor", "Invalid color. Use the #RRGGBB format.", "Cor inválida. Use o formato #RRGGBB.", "Color no válido. Usa el formato #RRGGBB.");

        // ---- battle window
        Add("Battle.Title", "AFKit — battle", "AFKit — batalha", "AFKit — batalla");
        Add("Battle.TitlePlural", "AFKit — battles", "AFKit — batalhas", "AFKit — batallas");
        Add("Battle.Overlay", "Overlay (always on top)", "Overlay (sempre por cima)", "Overlay (siempre encima)");
        Add("Battle.Hide", "Hide the window", "Esconder a janelinha", "Ocultar la ventana");
        Add("Taskbar.HpLabel", "HP", "PS", "PS");
        Add("Taskbar.LblTime", "Time", "Tempo", "Tiempo");
        Add("Taskbar.LblXp", "XP/h", "XP/h", "XP/h");
        Add("Taskbar.LblGold", "Gold/h", "Gold/h", "Gold/h");
        Add("Taskbar.LblProfit", "Profit/h", "Lucro/h", "Ganancia/h");
        Add("Taskbar.LblRecord", "Session record", "Recorde da sessão", "Récord de la sesión");

        // ---- extensions window
        Add("Ext.Title", "Extensions", "Extensões", "Extensiones");
        Add("Ext.WindowTitle", "Extensions — {0}", "Extensões — {0}", "Extensiones — {0}");
        Add("Ext.HeaderOf", "Extensions of \"{0}\"", "Extensões de \"{0}\"", "Extensiones de \"{0}\"");
        Add("Ext.StoreLabel", "Chrome Web Store link or extension ID", "Link da Chrome Web Store ou ID da extensão", "Enlace de Chrome Web Store o ID de la extensión");
        Add("Ext.Install", "Install", "Instalar", "Instalar");
        Add("Ext.FromFile", "From .crx file…", "De arquivo .crx…", "Desde archivo .crx…");
        Add("Ext.FromFileTip", "A .crx (or .zip) file downloaded earlier", "Arquivo .crx (ou .zip) baixado antes", "Archivo .crx (o .zip) descargado antes");
        Add("Ext.FromFolder", "From folder…", "De pasta…", "Desde carpeta…");
        Add("Ext.FromFolderTip", "Unpacked extension (a folder with manifest.json). It is copied into AFKit's extensions folder.",
            "Extensão descompactada (pasta com manifest.json). Ela é copiada para a pasta de extensões do AFKit.",
            "Extensión descomprimida (carpeta con manifest.json). Se copia a la carpeta de extensiones de AFKit.");
        Add("Ext.ToggleTip", "Enable / disable", "Ativar / desativar", "Activar / desactivar");
        Add("Ext.Options", "Options", "Opções", "Opciones");
        Add("Ext.Remove", "Remove", "Remover", "Quitar");
        Add("Ext.Empty", "No extensions in this profile. Paste a Chrome Web Store extension link above and click Install.",
            "Nenhuma extensão neste perfil. Cole acima o link de uma extensão da Chrome Web Store e clique em Instalar.",
            "No hay extensiones en este perfil. Pega arriba el enlace de una extensión de Chrome Web Store y haz clic en Instalar.");
        Add("Ext.FooterNote", "No toolbar button or popup: extensions that only work through the toolbar icon cannot be opened.",
            "Sem botão na barra nem popup: extensões que só funcionam pelo ícone da barra não têm como abrir.",
            "Sin botón en la barra ni popup: las extensiones que solo funcionan desde el icono de la barra no se pueden abrir.");
        Add("Ext.ReloadTabs", "Reload tabs", "Recarregar abas", "Recargar pestañas");
        Add("Ext.ReloadTabsTip", "Reloads the profile's tabs to apply the changes to pages already open",
            "Recarrega as abas do perfil para aplicar as mudanças nas páginas já abertas",
            "Recarga las pestañas del perfil para aplicar los cambios en las páginas ya abiertas");
        Add("Ext.OptionsTip", "Opens the options page in a tab of this profile", "Abre a página de opções numa aba deste perfil", "Abre la página de opciones en una pestaña de este perfil");
        Add("Ext.NoOptionsTip", "This extension has no options page", "Esta extensão não tem página de opções", "Esta extensión no tiene página de opciones");
        Add("Ext.FolderLabel", "Folder", "Pasta", "Carpeta");
        Add("Ext.VersionLabel", "version {0}", "versão {0}", "versión {0}");
        Add("Ext.SessionOpen", "Session open: changes take effect right away.", "Sessão aberta: as mudanças valem na hora.", "Sesión abierta: los cambios se aplican al instante.");
        Add("Ext.SessionClosed", "Session closed: changes are applied when the profile is opened.",
            "Sessão fechada: as mudanças são aplicadas quando o perfil for aberto.",
            "Sesión cerrada: los cambios se aplican cuando se abra el perfil.");
        Add("Ext.ReadFailed", "Could not read the open session's extensions: {0}",
            "Não foi possível ler as extensões da sessão aberta: {0}",
            "No se pudieron leer las extensiones de la sesión abierta: {0}");
        Add("Ext.NotLoaded", "not loaded in the browser — install it again", "não carregada no navegador — instale de novo", "no cargada en el navegador — instálala de nuevo");
        Add("Ext.WillInstall", "will be installed when the session opens", "será instalada quando a sessão abrir", "se instalará cuando se abra la sesión");
        Add("Ext.StatusActive", "active", "ativa", "activa");
        Add("Ext.StatusDisabled", "disabled", "desativada", "desactivada");
        Add("Ext.PasteLink", "Paste a Chrome Web Store extension link (chromewebstore.google.com/detail/…) or the extension's 32-letter ID.",
            "Cole o link de uma extensão da Chrome Web Store (chromewebstore.google.com/detail/…) ou o ID de 32 letras da extensão.",
            "Pega el enlace de una extensión de Chrome Web Store (chromewebstore.google.com/detail/…) o el ID de 32 letras de la extensión.");
        Add("Ext.Downloading", "Downloading the extension from the Chrome Web Store…", "Baixando a extensão da Chrome Web Store…", "Descargando la extensión de Chrome Web Store…");
        Add("Ext.InstallFileTitle", "Install extension from file", "Instalar extensão de arquivo", "Instalar extensión desde archivo");
        Add("Ext.ChromeExtension", "Chrome extension", "Extensão do Chrome", "Extensión de Chrome");
        Add("Ext.AllFiles", "All files", "Todos os arquivos", "Todos los archivos");
        Add("Ext.Unpacking", "Unpacking the extension…", "Descompactando a extensão…", "Descomprimiendo la extensión…");
        Add("Ext.FolderPickTitle", "Extension folder (with manifest.json)", "Pasta da extensão (com manifest.json)", "Carpeta de la extensión (con manifest.json)");
        Add("Ext.Copying", "Copying the extension…", "Copiando a extensão…", "Copiando la extensión…");
        Add("Ext.Installing", "Installing \"{0}\" in the profile…", "Instalando \"{0}\" no perfil…", "Instalando \"{0}\" en el perfil…");
        Add("Ext.BrowserRefused", "The browser (WebView2) refused the extension: {0}", "O navegador (WebView2) recusou a extensão: {0}", "El navegador (WebView2) rechazó la extensión: {0}");
        Add("Ext.InstalledLive", "\"{0}\" installed and enabled. The profile's tabs were reloaded so it can start working.",
            "\"{0}\" instalada e ativada. As abas do perfil foram recarregadas para ela começar a funcionar.",
            "\"{0}\" instalada y activada. Se recargaron las pestañas del perfil para que empiece a funcionar.");
        Add("Ext.InstalledLater", "\"{0}\" will be installed when this profile's session opens.",
            "\"{0}\" será instalada quando a sessão deste perfil for aberta.",
            "\"{0}\" se instalará cuando se abra la sesión de este perfil.");
        Add("Ext.InstallFailed", "Could not install the extension: {0}", "Não foi possível instalar a extensão: {0}", "No se pudo instalar la extensión: {0}");
        Add("Ext.EnabledLive", "\"{0}\" enabled. Use \"Reload tabs\" to apply it to pages already open.",
            "\"{0}\" ativada. Use \"Recarregar abas\" para aplicar nas páginas já abertas.",
            "\"{0}\" activada. Usa \"Recargar pestañas\" para aplicarla en las páginas ya abiertas.");
        Add("Ext.DisabledLive", "\"{0}\" disabled. Use \"Reload tabs\" to apply it to pages already open.",
            "\"{0}\" desativada. Use \"Recarregar abas\" para aplicar nas páginas já abertas.",
            "\"{0}\" desactivada. Usa \"Recargar pestañas\" para aplicarlo en las páginas ya abiertas.");
        Add("Ext.EnabledLater", "\"{0}\" will be enabled when the session opens.", "\"{0}\" ficará ativada quando a sessão abrir.", "\"{0}\" quedará activada cuando se abra la sesión.");
        Add("Ext.DisabledLater", "\"{0}\" will be disabled when the session opens.", "\"{0}\" ficará desativada quando a sessão abrir.", "\"{0}\" quedará desactivada cuando se abra la sesión.");
        Add("Ext.EnableFailed", "Could not enable the extension: {0}", "Não foi possível ativar a extensão: {0}", "No se pudo activar la extensión: {0}");
        Add("Ext.DisableFailed", "Could not disable the extension: {0}", "Não foi possível desativar a extensão: {0}", "No se pudo desactivar la extensión: {0}");
        Add("Ext.RemoveTitle", "Remove extension", "Remover extensão", "Quitar extensión");
        Add("Ext.RemoveConfirm", "Remove the extension \"{0}\" from this profile?", "Remover a extensão \"{0}\" deste perfil?", "¿Quitar la extensión \"{0}\" de este perfil?");
        Add("Ext.RemovedLive", "\"{0}\" removed. Use \"Reload tabs\" to take it off pages already open.",
            "\"{0}\" removida. Use \"Recarregar abas\" para tirá-la das páginas já abertas.",
            "\"{0}\" quitada. Usa \"Recargar pestañas\" para quitarla de las páginas ya abiertas.");
        Add("Ext.Removed", "\"{0}\" removed.", "\"{0}\" removida.", "\"{0}\" quitada.");
        Add("Ext.RemoveFailed", "Could not remove the extension: {0}", "Não foi possível remover a extensão: {0}", "No se pudo quitar la extensión: {0}");
        Add("Ext.TabsReloaded", "Profile tabs reloaded.", "Abas do perfil recarregadas.", "Pestañas del perfil recargadas.");
    }
}
