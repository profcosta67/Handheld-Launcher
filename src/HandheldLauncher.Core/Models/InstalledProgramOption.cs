namespace HandheldLauncher.Core.Models;

/// <summary>
/// v0.25.3 — entrada da lista "Programas instalados" do diálogo Adicionar.
/// O <see cref="Source"/> só serve para diagnóstico/ordenação: "Registo",
/// "App Paths" ou "Menu Iniciar".
/// </summary>
public sealed record InstalledProgramOption(string DisplayName, string ExecutablePath, string Source = "Registo");
