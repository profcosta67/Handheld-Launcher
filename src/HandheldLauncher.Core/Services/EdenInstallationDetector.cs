namespace HandheldLauncher.Core.Services;

/// <summary>
/// Acha o `eden.exe` instalado neste PC sem perguntar nada pro usuário —
/// v0.9.16, resposta à pergunta dele "além de instalar o eden junto com
/// instalador do launcher?".
///
/// <para>
/// <b>Por que DETECTAR em vez de EMPACOTAR o Eden no instalador</b> (a
/// ideia original do usuário, avaliada e descartada de propósito):
/// </para>
/// <list type="bullet">
/// <item>O Eden é fork do Yuzu, que foi <b>encerrado por ação judicial da
/// Nintendo</b>. Este repositório é público no GitHub — redistribuir um
/// binário de emulador de Switch dentro do nosso instalador muda a
/// natureza do pacote e coloca o próprio repo em risco de takedown.</item>
/// <item>O Eden sozinho não roda nada: precisa de firmware + `prod.keys`,
/// que vêm do console do usuário. Isso nunca poderia ser empacotado de
/// qualquer forma, então o "instala junto e já funciona" não existiria
/// na prática.</item>
/// <item>A distribuição do Eden muda de lugar com frequência — nosso
/// release ficaria amarrado a uma URL que pode sair do ar.</item>
/// </list>
/// <para>
/// Detectar dá quase a mesma comodidade com zero redistribuição: no
/// primeiro boot o launcher preenche o caminho sozinho se achar (ver
/// App.axaml.cs, mesmo padrão do SteamAccountDetector), e quando não
/// achar, o usuário aponta na mão pelo botão "Procurar eden.exe…" da aba
/// Emuladores.
/// </para>
/// <para>
/// A busca por pasta-com-"eden"-no-nome na raiz dos discos existe porque
/// o caminho real do usuário é <c>C:\Eden Emu\eden.exe</c> — pasta
/// criada à mão, fora de qualquer local padrão de instalador. Só olha o
/// PRIMEIRO nível de cada disco (uma chamada de GetDirectories por
/// disco), nunca varre o disco inteiro.
/// </para>
/// </summary>
public static class EdenInstallationDetector
{
    private const string ExecutableName = "eden.exe";

    /// <summary>
    /// Devolve o caminho completo do primeiro `eden.exe` encontrado, ou
    /// null. Nunca lança: disco sem permissão, disco removível que saiu
    /// no meio, caminho inválido — tudo vira "não achei".
    /// </summary>
    public static string? FindExecutable()
    {
        foreach (var candidate in CandidatePaths())
        {
            try
            {
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // Caminho inválido/sem permissão — tenta o próximo.
            }
        }

        return FindInDriveRoots();
    }

    /// <summary>
    /// Locais prováveis, na ordem: pastas que instaladores usam, depois
    /// os lugares "à mão" mais comuns (inclui o caminho real do usuário,
    /// "C:\Eden Emu").
    /// </summary>
    private static IEnumerable<string> CandidatePaths()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var folderName in new[] { "Eden", "eden", "Eden Emu" })
        {
            if (!string.IsNullOrEmpty(localAppData))
            {
                yield return Path.Combine(localAppData, "Programs", folderName, ExecutableName);
                yield return Path.Combine(localAppData, folderName, ExecutableName);
            }
            if (!string.IsNullOrEmpty(programFiles)) yield return Path.Combine(programFiles, folderName, ExecutableName);
            if (!string.IsNullOrEmpty(programFilesX86)) yield return Path.Combine(programFilesX86, folderName, ExecutableName);
            if (!string.IsNullOrEmpty(userProfile)) yield return Path.Combine(userProfile, folderName, ExecutableName);
            yield return Path.Combine(@"C:\", folderName, ExecutableName);
        }
    }

    /// <summary>
    /// Último recurso: procura no primeiro nível de cada disco fixo uma
    /// pasta com "eden" no nome contendo o eden.exe (pega
    /// "C:\Eden Emu\", "D:\Eden Emulator\" e afins). Só um
    /// GetDirectories por disco — não varre nada recursivamente.
    /// </summary>
    private static string? FindInDriveRoots()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            return null;
        }

        foreach (var drive in drives)
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;

                foreach (var dir in Directory.GetDirectories(drive.RootDirectory.FullName))
                {
                    var name = Path.GetFileName(dir);
                    if (name.Contains("eden", StringComparison.OrdinalIgnoreCase))
                    {
                        var exe = Path.Combine(dir, ExecutableName);
                        if (File.Exists(exe)) return exe;
                    }
                }
            }
            catch
            {
                // Disco sem permissão/indisponível — tenta o próximo.
            }
        }

        return null;
    }
}
