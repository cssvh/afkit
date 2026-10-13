namespace AFKit.Services;

internal static partial class Strings
{
    // ProfileView: tabs, navigation bar, grid header, automatic recovery.
    private static void ProfileView()
    {
        // ---- tab context menu and tab strip
        Add("Tab.New", "New tab", "Nova aba", "Nueva pestaña");
        Add("Tab.ReloadsEvery", "Reloads every {0} min", "Recarrega a cada {0} min", "Se recarga cada {0} min");
        Add("Tab.MenuMute", "Mute tab", "Silenciar aba", "Silenciar pestaña");
        Add("Tab.MenuPin", "Pin this tab to the grid", "Fixar esta aba no grid", "Fijar esta pestaña en la cuadrícula");
        Add("Tab.MenuUnpin", "Remove from grid", "Tirar do grid", "Quitar de la cuadrícula");
        Add("Tab.MenuBackground", "In the background", "Em segundo plano", "En segundo plano");
        Add("Tab.ModeLight", "Light — scripts keep running, no drawing (default)",
            "Leve — scripts continuam, sem desenhar (padrão)",
            "Ligero — los scripts siguen, sin dibujar (predeterminado)");
        Add("Tab.ModeLightTip", "Off screen the tab is not drawn (not even video), but the game keeps running. Saves a lot of CPU.",
            "Fora da tela a aba não é desenhada (nem vídeo), mas o jogo continua rodando. Economiza bastante CPU.",
            "Fuera de pantalla la pestaña no se dibuja (ni el vídeo), pero el juego sigue funcionando. Ahorra mucha CPU.");
        Add("Tab.ModeActive", "Active — keeps drawing (more CPU)", "Ativa — continua desenhando (mais CPU)", "Activa — sigue dibujando (más CPU)");
        Add("Tab.ModeActiveTip", "For pages that only progress while they are being drawn.",
            "Para páginas que só progridem enquanto estão sendo desenhadas.",
            "Para páginas que solo avanzan mientras se están dibujando.");
        Add("Tab.ModeSuspended", "Suspended — freezes until you open it", "Suspensa — congela até você abrir", "Suspendida — se congela hasta que la abras");
        Add("Tab.ModeSuspendedTip", "Almost zero CPU. Good for streams and reference pages.",
            "Quase zero CPU. Bom para streams e páginas de consulta.",
            "Casi cero CPU. Bueno para streams y páginas de consulta.");
        Add("Tab.MenuAutoReload", "Reload automatically", "Recarregar automaticamente", "Recargar automáticamente");
        Add("Tab.ReloadOff", "Off", "Desligado", "Desactivado");
        Add("Tab.Every1m", "Every 1 minute", "A cada 1 minuto", "Cada 1 minuto");
        Add("Tab.Every5m", "Every 5 minutes", "A cada 5 minutos", "Cada 5 minutos");
        Add("Tab.Every10m", "Every 10 minutes", "A cada 10 minutos", "Cada 10 minutos");
        Add("Tab.Every15m", "Every 15 minutes", "A cada 15 minutos", "Cada 15 minutos");
        Add("Tab.Every30m", "Every 30 minutes", "A cada 30 minutos", "Cada 30 minutos");
        Add("Tab.Every1h", "Every 1 hour", "A cada 1 hora", "Cada 1 hora");
        Add("Tab.MenuAutoRecover", "Automatic recovery (all tabs)", "Recuperação automática (todas as abas)", "Recuperación automática (todas las pestañas)");
        Add("Tab.MenuAutoRecoverTip", "Reloads by itself when the page hangs, the connection drops, the site goes down or the internet is out",
            "Recarrega sozinho quando a página trava, a conexão cai, o site sai do ar ou falta internet",
            "Se recarga solo cuando la página se bloquea, se cae la conexión, el sitio deja de funcionar o falta internet");
        Add("Tab.MenuMoveLeft", "Move left", "Mover para a esquerda", "Mover a la izquierda");
        Add("Tab.MenuMoveRight", "Move right", "Mover para a direita", "Mover a la derecha");
        Add("Tab.MenuClose", "Close tab", "Fechar aba", "Cerrar pestaña");
        Add("Tab.MuteTip", "Mute tab (Ctrl+M)", "Silenciar aba (Ctrl+M)", "Silenciar pestaña (Ctrl+M)");
        Add("Tab.InGridTip", "This tab shows up in the grid", "Esta aba aparece no grid", "Esta pestaña aparece en la cuadrícula");
        Add("Tab.CloseTip", "Close tab (Ctrl+W)", "Fechar aba (Ctrl+W)", "Cerrar pestaña (Ctrl+W)");
        Add("Tab.ActiveBadge", "Active in the background (keeps drawing)", "Ativa em segundo plano (continua desenhando)", "Activa en segundo plano (sigue dibujando)");
        Add("Tab.SuspendedBadge", "Suspended while off screen", "Suspensa fora da tela", "Suspendida fuera de pantalla");
        Add("Tab.NewTip", "New tab (Ctrl+T)", "Nova aba (Ctrl+T)", "Nueva pestaña (Ctrl+T)");

        // ---- navigation bar
        Add("Nav.Back", "Back (Alt+←)", "Voltar (Alt+←)", "Atrás (Alt+←)");
        Add("Nav.Forward", "Forward (Alt+→)", "Avançar (Alt+→)", "Adelante (Alt+→)");
        Add("Nav.Reload", "Reload (F5)", "Recarregar (F5)", "Recargar (F5)");
        Add("Nav.AddressHint", "Search or type an address", "Pesquisar ou digitar endereço", "Buscar o escribir una dirección");
        Add("Nav.ZoomTip", "Zoom — click to go back to 100% (Ctrl 0)", "Zoom — clique para voltar a 100% (Ctrl 0)", "Zoom — haz clic para volver al 100% (Ctrl 0)");

        // ---- grid cell header
        Add("PV.ZoomOut", "Zoom out (Ctrl −)", "Diminuir zoom (Ctrl −)", "Reducir zoom (Ctrl −)");
        Add("PV.ZoomReset", "Page zoom — click to go back to 100% (Ctrl 0)", "Zoom da página — clique para voltar a 100% (Ctrl 0)", "Zoom de la página — haz clic para volver al 100% (Ctrl 0)");
        Add("PV.ZoomIn", "Zoom in (Ctrl +)", "Aumentar zoom (Ctrl +)", "Aumentar zoom (Ctrl +)");
        Add("PV.Reload", "Reload", "Recarregar", "Recargar");
        Add("PV.EconomyOn", "Show the game's Economy Mode", "Mostrar o Modo Economia do jogo", "Mostrar el Modo Economía del juego");
        Add("PV.EconomyOff", "Back to the battle view", "Voltar para a visão da batalha", "Volver a la vista de batalla");
        Add("PV.OpenFull", "Open the full profile (leave the grid)", "Abrir o perfil completo (sair do grid)", "Abrir el perfil completo (salir de la cuadrícula)");
        Add("PV.MuteTip", "Mute (Ctrl+M)", "Silenciar (Ctrl+M)", "Silenciar (Ctrl+M)");
        Add("PV.UnmuteTip", "Unmute (Ctrl+M)", "Ativar som (Ctrl+M)", "Activar sonido (Ctrl+M)");
        Add("Grid.DragTooltip", "Drag to swap this window with another one in the grid",
            "Arraste para trocar esta janela de lugar com outra do grid",
            "Arrastra para intercambiar esta ventana con otra de la cuadrícula");
        Add("PV.LoadingDots", "loading…", "carregando…", "cargando…");
        Add("PV.StatusRecovering", "Recovering", "Recuperando", "Recuperando");
        Add("PV.StatusLoading", "Loading", "Carregando", "Cargando");
        Add("PV.StatusOk", "All good", "Tudo certo", "Todo bien");
        Add("PV.OpenedSeconds", "open for {0} s", "aberta há {0} s", "abierta hace {0} s");
        Add("PV.OpenedMinutes", "open for {0} min", "aberta há {0} min", "abierta hace {0} min");
        Add("PV.OpenedHours", "open for {0} h", "aberta há {0} h", "abierta hace {0} h");

        // ---- session / errors / recovery
        Add("PV.OpeningProfile", "Opening profile…", "Abrindo perfil…", "Abriendo perfil…");
        Add("PV.StartFailed", "Could not start this profile's browser.\n\n{0}",
            "Não foi possível iniciar o navegador deste perfil.\n\n{0}",
            "No se pudo iniciar el navegador de este perfil.\n\n{0}");
        Add("PV.CreateTabError", "Error creating tab", "Erro ao criar aba", "Error al crear la pestaña");
        Add("PV.PageHung", "The page hung", "A página travou", "La página se bloqueó");
        Add("PV.PageCrashed", "The page crashed", "A página caiu", "La página falló");
        Add("PV.SiteDownCode", "Site is down (error {0})", "Site fora do ar (erro {0})", "Sitio caído (error {0})");
        Add("PV.ConnectionDropped", "The connection dropped", "A conexão caiu", "Se cayó la conexión");
        Add("PV.SiteNoResponse", "Site is down or not responding", "Site fora do ar ou sem resposta", "Sitio caído o sin respuesta");
        Add("PV.LoadFailed", "Failed to load the page", "Falha ao carregar a página", "Error al cargar la página");
        Add("PV.NoInternet", "No internet — reloads by itself when the connection returns",
            "Sem internet — recarrega sozinho quando a conexão voltar",
            "Sin internet — se recarga solo cuando vuelva la conexión");
        Add("PV.RetryIn", "{0} — retrying in {1} (attempt {2})", "{0} — nova tentativa em {1} (tentativa {2})", "{0} — nuevo intento en {1} (intento {2})");
        Add("PV.ConnectionBack", "Connection is back — reloading…", "Conexão de volta — recarregando…", "Conexión recuperada — recargando…");
        Add("PV.SecureConnection", "Secure connection (https)", "Conexão segura (https)", "Conexión segura (https)");

        // ---- extensions (session) and task bar
        Add("Ext.SessionNotOpen", "The profile's session is not open.", "A sessão do perfil não está aberta.", "La sesión del perfil no está abierta.");
        Add("Taskbar.NoneOpen", "No profile with PokeIdle open in the task bar. Right-click a profile → Show in task bar.",
            "Nenhum perfil com o PokeIdle aberto na task bar. Botão direito num perfil → Mostrar na task bar.",
            "Ningún perfil con PokeIdle abierto en la task bar. Clic derecho en un perfil → Mostrar en la task bar.");
        Add("Taskbar.LevelShort", "Lv", "Nv", "Nv");
    }
}
