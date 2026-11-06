using System;
using System.IO;
using System.Text.Json;

namespace HandheldLauncher.Core.Services;

public class SessionTrackerService
{
    private readonly string _logPath;
    private DateTime _gameStartTime;
    private string _activeGameTitle = string.Empty;

    public SessionTrackerService()
    {
        // Salva no AppData do usuário. O arquivo é JSONL (uma sessão por linha),
        // não um array JSON — por isso a extensão .jsonl. Ler = File.ReadLines()
        // + JsonSerializer.Deserialize por linha.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logPath = Path.Combine(appData, "HandheldLauncher", "sessions.jsonl");
    }

    public void StartSession(string gameTitle, string tdpProfile)
    {
        _gameStartTime = DateTime.Now;
        _activeGameTitle = gameTitle ?? string.Empty;
        System.Diagnostics.Debug.WriteLine($"[SESSÃO] Iniciada: {gameTitle} com TDP {tdpProfile}");
    }

    public void EndSession(string gameTitle)
    {
        if (_gameStartTime == default) return;

        // Usa o título guardado no StartSession, não o parâmetro — se o título
        // passado divergir do ativo, prefere o ativo (fonte da verdade).
        var title = string.IsNullOrEmpty(_activeGameTitle) ? (gameTitle ?? "?") : _activeGameTitle;
        var duration = DateTime.Now - _gameStartTime;
        _gameStartTime = default;
        _activeGameTitle = string.Empty;

        var entry = new { Game = title, DurationSeconds = (int)duration.TotalSeconds, Date = DateTime.Now };
        var json = JsonSerializer.Serialize(entry);

        try
        {
            var dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(_logPath, json + Environment.NewLine);
            System.Diagnostics.Debug.WriteLine($"[SESSÃO] Finalizada: {title} ({duration.TotalMinutes:F1} min)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SESSÃO] Erro ao salvar: {ex.Message}");
        }
    }
}