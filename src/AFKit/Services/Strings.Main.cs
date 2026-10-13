namespace AFKit.Services;

internal static partial class Strings
{
    // Main window: sidebar, grid, profiles, groups, backup.
    private static void MainWindowTexts()
    {
        Add("Main.NoGroup", "No group", "Sem grupo", "Sin grupo");
        Add("Main.SavingSessions", "Saving sessions…", "Salvando sessões…", "Guardando sesiones…");
        Add("Main.Footer", "{0} profiles · {1} open", "{0} perfis · {1} abertos", "{0} perfiles · {1} abiertos");

        Add("Main.GridExit", "Leave grid mode (Ctrl+G)", "Sair do modo grid (Ctrl+G)", "Salir del modo cuadrícula (Ctrl+G)");
        Add("Main.GridEnter", "Grid mode (Ctrl+G)", "Modo grid (Ctrl+G)", "Modo cuadrícula (Ctrl+G)");
        Add("Main.GridTitle", "Grid mode", "Modo grid", "Modo cuadrícula");
        Add("Main.GridFull", "The grid holds up to {0} profiles (one tab each). Remove one from the grid first.",
            "O grid comporta até {0} perfis (uma aba de cada). Tire um do grid antes.",
            "La cuadrícula admite hasta {0} perfiles (una pestaña de cada uno). Quita uno antes.");
        Add("Main.GridHowTo",
            "Pin tabs to build the grid (up to 4, one per profile): right-click a tab → \"Pin this tab to the grid\", or use the pin next to the address bar.",
            "Fixe abas para montar o grid (até 4, uma por perfil): clique com o botão direito numa aba → \"Fixar esta aba no grid\", ou use o alfinete ao lado da barra de endereço.",
            "Fija pestañas para montar la cuadrícula (hasta 4, una por perfil): clic derecho en una pestaña → \"Fijar esta pestaña en la cuadrícula\", o usa el alfiler junto a la barra de direcciones.");

        Add("Main.EmptyNone", "No profile open", "Nenhum perfil aberto", "Ningún perfil abierto");
        Add("Main.EmptyWelcome", "Welcome to AFKit", "Bem-vindo ao AFKit", "Bienvenido a AFKit");
        Add("Main.EmptyPickHint", "Pick a profile in the sidebar.", "Escolha um perfil na barra lateral.", "Elige un perfil en la barra lateral.");
        Add("Main.EmptyIsolatedHint", "Each profile has its own cookies and logins — completely isolated.",
            "Cada perfil tem cookies e logins próprios — totalmente isolados.",
            "Cada perfil tiene sus propias cookies e inicios de sesión — totalmente aislados.");

        Add("Main.SaveError", "Could not save: {0}", "Não foi possível salvar: {0}", "No se pudo guardar: {0}");
        Add("Main.DefaultProfileName", "Profile {0}", "Perfil {0}", "Perfil {0}");

        Add("Main.DeleteProfileTitle", "Delete profile", "Excluir perfil", "Eliminar perfil");
        Add("Main.DeleteProfileText",
            "Delete \"{0}\"?\n\nCookies, logins, history and all data of this profile will be erased. This cannot be undone.",
            "Excluir \"{0}\"?\n\nCookies, logins, histórico e todos os dados deste perfil serão apagados. Isso não pode ser desfeito.",
            "¿Eliminar \"{0}\"?\n\nSe borrarán las cookies, los inicios de sesión, el historial y todos los datos de este perfil. No se puede deshacer.");

        Add("Main.ProfileFileType", "AFKit profile", "Perfil do AFKit", "Perfil de AFKit");
        Add("Main.ExportTitle", "Export profile", "Exportar perfil", "Exportar perfil");
        Add("Main.ExportNeedsClose", "To export, this profile's session has to be closed (the tabs stay saved). Continue?",
            "Para exportar, a sessão deste perfil precisa ser fechada (as abas ficam salvas). Continuar?",
            "Para exportar, hay que cerrar la sesión de este perfil (las pestañas quedan guardadas). ¿Continuar?");
        Add("Main.Exporting", "Exporting \"{0}\"…", "Exportando \"{0}\"…", "Exportando \"{0}\"…");
        Add("Main.ExportSkipped", "{0} file(s) in use were skipped.", "{0} arquivo(s) em uso foram ignorados.", "Se omitieron {0} archivo(s) en uso.");
        Add("Main.ExportDone", "Backup saved ({0} MB).", "Backup salvo ({0} MB).", "Copia guardada ({0} MB).");
        Add("Main.ExportFailed", "Export failed: {0}", "Falha ao exportar: {0}", "Error al exportar: {0}");
        Add("Main.ImportTitle", "Import profile", "Importar perfil", "Importar perfil");
        Add("Main.Importing", "Importing profile…", "Importando perfil…", "Importando perfil…");
        Add("Main.ImportFailed", "Import failed: {0}", "Falha ao importar: {0}", "Error al importar: {0}");

        Add("Main.DeleteGroupTitle", "Delete group", "Excluir grupo", "Eliminar grupo");
        Add("Main.DeleteGroupText",
            "Delete the group \"{0}\"? Its profiles will move to \"No group\" (nothing is erased).",
            "Excluir o grupo \"{0}\"? Os perfis dele vão para \"Sem grupo\" (nada é apagado).",
            "¿Eliminar el grupo \"{0}\"? Sus perfiles pasarán a \"Sin grupo\" (no se borra nada).");

        // Main window XAML
        Add("Main.HideMenu", "Hide the menu (Ctrl+B)", "Esconder o menu (Ctrl+B)", "Ocultar el menú (Ctrl+B)");
        Add("Main.MuteAll", "Mute everything (Ctrl+Alt+M)", "Silenciar tudo (Ctrl+Alt+M)", "Silenciar todo (Ctrl+Alt+M)");
        Add("Main.UnmuteAll", "Unmute everything (Ctrl+Alt+M)", "Ativar o som de tudo (Ctrl+Alt+M)", "Activar el sonido de todo (Ctrl+Alt+M)");
        Add("Main.Language", "Language", "Idioma", "Idioma");
        Add("Main.LanguageRestart", "AFKit needs to restart to change the language. Restart now? (open sessions are saved and restored)",
            "O AFKit precisa reiniciar para trocar o idioma. Reiniciar agora? (as sessões abertas são salvas e restauradas)",
            "AFKit debe reiniciarse para cambiar el idioma. ¿Reiniciar ahora? (las sesiones abiertas se guardan y se restauran)");
        Add("Main.ImportTooltip", "Import profile (.afkitprofile)", "Importar perfil (.afkitprofile)", "Importar perfil (.afkitprofile)");
        Add("Main.NewGroup", "New group", "Novo grupo", "Nuevo grupo");
        Add("Main.NewProfile", "New profile (Ctrl+Shift+N)", "Novo perfil (Ctrl+Shift+N)", "Nuevo perfil (Ctrl+Shift+N)");
        Add("Main.SearchProfiles", "Search profiles…", "Buscar perfis…", "Buscar perfiles…");
        Add("Main.NewProfileInGroup", "New profile in this group", "Novo perfil neste grupo", "Nuevo perfil en este grupo");
        Add("Main.EditGroup", "Edit group…", "Editar grupo…", "Editar grupo…");
        Add("Main.DeleteGroup", "Delete group", "Excluir grupo", "Eliminar grupo");
        Add("Main.MenuOpen", "Open", "Abrir", "Abrir");
        Add("Main.MenuEdit", "Edit…", "Editar…", "Editar…");
        Add("Main.MenuExtensions", "Extensions…", "Extensões…", "Extensiones…");
        Add("Main.MenuPinnedToGrid", "Pinned to grid", "Fixado no grid", "Fijado en la cuadrícula");
        Add("Main.MenuShowInTaskbar", "Show in task bar", "Mostrar na task bar", "Mostrar en la task bar");
        Add("Main.MenuMuteProfile", "Mute profile", "Silenciar perfil", "Silenciar perfil");
        Add("Main.MenuCloseSession", "Close session", "Fechar sessão", "Cerrar sesión");
        Add("Main.MenuMoveUp", "Move up", "Mover para cima", "Mover arriba");
        Add("Main.MenuMoveDown", "Move down", "Mover para baixo", "Mover abajo");
        Add("Main.MenuExportBackup", "Export backup…", "Exportar backup…", "Exportar copia…");
        Add("Main.MenuOpenDataFolder", "Open data folder", "Abrir pasta de dados", "Abrir carpeta de datos");
        Add("Main.MenuDeleteProfile", "Delete profile…", "Excluir perfil…", "Eliminar perfil…");
        Add("Main.UsageTooltip", "CPU and memory use of this profile's browser processes",
            "Uso de CPU e memória dos processos do navegador deste perfil",
            "Uso de CPU y memoria de los procesos del navegador de este perfil");
        Add("Main.ProfileMutedTooltip", "Profile muted", "Perfil silenciado", "Perfil silenciado");
        Add("Main.SessionOpenTooltip", "Session open", "Sessão aberta", "Sesión abierta");
        Add("Main.CreateProfile", "Create profile", "Criar perfil", "Crear perfil");

        // Tray balloon and tooltips
        Add("Tray.Tooltip", "AFKit — {0} open · CPU {1}% · {2}", "AFKit — {0} abertos · CPU {1}% · {2}", "AFKit — {0} abiertos · CPU {1}% · {2}");
        Add("Tray.NetworkBack", "AFKit — connection is back, reloading pages", "AFKit — conexão de volta, recarregando páginas", "AFKit — conexión recuperada, recargando páginas");
        Add("Tray.HintTitle", "AFKit keeps running", "O AFKit continua rodando", "AFKit sigue funcionando");
        Add("Tray.HintText",
            "It is here in the tray, with every profile active. Click the icon to open it; to quit, use \"Exit AFKit\" in the menu.",
            "Ele está aqui na bandeja, com todos os perfis ativos. Clique no ícone para abrir; para sair, use \"Sair do AFKit\" no menu.",
            "Está aquí en la bandeja, con todos los perfiles activos. Haz clic en el icono para abrirlo; para salir, usa \"Salir de AFKit\" en el menú.");
        Add("Main.HideMenuLong", "Hide the menu (Ctrl+B) — only the avatar strip stays", "Esconder o menu (Ctrl+B) — fica só a faixa com os avatares", "Ocultar el menú (Ctrl+B): solo queda la franja de avatares");
        Add("Main.ShowMenu", "Show the menu (Ctrl+B)", "Mostrar o menu (Ctrl+B)", "Mostrar el menú (Ctrl+B)");
    }
}
