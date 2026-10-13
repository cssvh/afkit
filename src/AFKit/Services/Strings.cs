namespace AFKit.Services;

/// <summary>
/// All UI texts, one <c>Add(key, English, Português, Español)</c> line each, grouped by area in <c>Strings.*.cs</c>.
/// English is the fallback when a translation is empty.
/// </summary>
internal static partial class Strings
{
    private static void Add(string key, string en, string pt, string es) => Loc.Add(key, en, pt, es);

    public static void RegisterAll()
    {
        Core();
        MainWindowTexts();
        ProfileView();
        Dialogs();
        PageScripts();
    }

    private static void Core()
    {
        Add("Profile.New", "New profile", "Novo perfil", "Nuevo perfil");
        Add("Group.Default", "Group", "Grupo", "Grupo");

        Add("Store.ReadError", "Could not read {0}. A copy was saved to {1}.",
            "Não foi possível ler {0}. Uma cópia foi salva em {1}.",
            "No se pudo leer {0}. Se guardó una copia en {1}.");

        Add("Archive.InvalidFile", "Invalid file: profile.json not found.",
            "Arquivo inválido: profile.json não encontrado.",
            "Archivo no válido: no se encontró profile.json.");
        Add("Archive.EmptyManifest", "profile.json is empty.", "profile.json vazio.", "profile.json está vacío.");
        Add("Archive.ImportedSuffix", " (imported)", " (importado)", " (importado)");

        Add("Tray.Open", "Open AFKit", "Abrir o AFKit", "Abrir AFKit");
        Add("Tray.MuteAll", "Mute everything", "Silenciar tudo", "Silenciar todo");
        Add("Tray.AutoRecover", "Auto recovery (reloads if it fails)", "Recuperação automática (recarrega se cair)", "Recuperación automática (recarga si falla)");
        Add("Tray.CloseToTray", "Close button sends to tray", "Fechar no X manda para a bandeja", "El botón X envía a la bandeja");
        Add("Tray.Taskbar", "Task bar (battle window)", "Task bar (janelinha de batalha)", "Task bar (ventana de batalla)");
        Add("Tray.TaskbarAll", "Task bar: show all profiles", "Task bar: mostrar todos os perfis", "Task bar: mostrar todos los perfiles");
        Add("Tray.TaskbarNone", "Task bar: remove all profiles", "Task bar: tirar todos os perfis", "Task bar: quitar todos los perfiles");
        Add("Tray.Language", "Language", "Idioma", "Idioma");
        Add("Tray.Exit", "Exit AFKit", "Sair do AFKit", "Salir de AFKit");

        Add("ExtSvc.StoreEmpty",
            "Google did not return the extension (empty response). It may have been removed from the store, may not be available for your country/browser, or the ID is wrong.",
            "O Google não devolveu a extensão (resposta vazia). Ela pode ter sido removida da loja, não estar disponível para o seu país/navegador ou o ID está errado.",
            "Google no devolvió la extensión (respuesta vacía). Puede haber sido retirada de la tienda, no estar disponible en tu país/navegador, o el ID es incorrecto.");
        Add("ExtSvc.StoreError", "The Chrome Web Store replied with error {0} ({1}).",
            "A Chrome Web Store respondeu com erro {0} ({1}).",
            "Chrome Web Store respondió con el error {0} ({1}).");
        Add("ExtSvc.Timeout", "Timed out downloading the extension. Check your internet connection and try again.",
            "Tempo esgotado ao baixar a extensão. Verifique a internet e tente de novo.",
            "Se agotó el tiempo al descargar la extensión. Revisa tu conexión a internet e inténtalo de nuevo.");
        Add("ExtSvc.NetworkError", "Network failure while downloading the extension: {0}",
            "Falha de rede ao baixar a extensão: {0}",
            "Fallo de red al descargar la extensión: {0}");
        Add("ExtSvc.StoreEmptyFile", "Google did not return the extension (empty file). It may not be available.",
            "O Google não devolveu a extensão (arquivo vazio). Ela pode não estar disponível.",
            "Google no devolvió la extensión (archivo vacío). Puede que no esté disponible.");
        Add("ExtSvc.NotAnExtensionFile",
            "The store did not return an extension file (probably a web page). Check the link/ID; paid, removed or restricted extensions cannot be downloaded.",
            "A loja não devolveu um arquivo de extensão (provavelmente uma página da web). Confira o link/ID; extensões pagas, removidas ou restritas não podem ser baixadas.",
            "La tienda no devolvió un archivo de extensión (probablemente una página web). Revisa el enlace/ID; las extensiones de pago, retiradas o restringidas no se pueden descargar.");
        Add("ExtSvc.ReadFileError", "Could not read the file: {0}", "Não foi possível ler o arquivo: {0}", "No se pudo leer el archivo: {0}");
        Add("ExtSvc.InvalidFile", "The file is not a valid extension (.crx or .zip).",
            "O arquivo não é uma extensão válida (.crx ou .zip).",
            "El archivo no es una extensión válida (.crx o .zip).");
        Add("ExtSvc.NoManifestInFolder", "The chosen folder has no manifest.json (pick the extension's root folder).",
            "A pasta escolhida não tem manifest.json (escolha a pasta raiz da extensão).",
            "La carpeta elegida no tiene manifest.json (elige la carpeta raíz de la extensión).");
        Add("ExtSvc.CrxCorruptHeader", "Corrupt .crx file (header larger than the file).",
            "Arquivo .crx corrompido (cabeçalho maior que o arquivo).",
            "Archivo .crx dañado (encabezado mayor que el archivo).");
        Add("ExtSvc.CrxVersionUnsupported", ".crx format version {0} is not supported.",
            "Formato .crx versão {0} não suportado.",
            "Formato .crx versión {0} no compatible.");
        Add("ExtSvc.CrxCorrupt", "Corrupt .crx file.", "Arquivo .crx corrompido.", "Archivo .crx dañado.");
        Add("ExtSvc.PackageCorrupt", "The extension package is corrupt: {0}",
            "O pacote da extensão está corrompido: {0}",
            "El paquete de la extensión está dañado: {0}");
        Add("ExtSvc.NoManifest", "The package has no manifest.json; it does not look like an extension.",
            "O pacote não tem manifest.json; não parece ser uma extensão.",
            "El paquete no tiene manifest.json; no parece una extensión.");
        Add("ExtSvc.ManifestInvalid", "Invalid manifest.json.", "manifest.json inválido.", "manifest.json no válido.");
        Add("ExtSvc.ManifestInvalidDetail", "Invalid manifest.json: {0}", "manifest.json inválido: {0}", "manifest.json no válido: {0}");
        Add("ExtSvc.ManifestMissingField", "Invalid manifest.json: the \"{0}\" field is missing.",
            "manifest.json inválido: falta o campo \"{0}\".",
            "manifest.json no válido: falta el campo \"{0}\".");
        Add("ExtSvc.BrowserLoadFailed", "The browser did not load the extension: {0}",
            "O navegador não carregou a extensão: {0}",
            "El navegador no cargó la extensión: {0}");
        Add("ExtSvc.FolderMissing", "Extension folder not found on this machine. Install it again.",
            "Pasta da extensão não encontrada nesta máquina. Instale de novo.",
            "Carpeta de la extensión no encontrada en este equipo. Instálala de nuevo.");
        Add("ExtSvc.EnableFailed", "Could not enable: {0}", "Não foi possível ativar: {0}", "No se pudo activar: {0}");
        Add("ExtSvc.DisableFailed", "Could not disable: {0}", "Não foi possível desativar: {0}", "No se pudo desactivar: {0}");
    }
}
