using System;
using System.IO;
using System.Text.Json;
using HandheldLauncher.Core.Models;

namespace HandheldLauncher.Core.Services;

public class SessionTrackerService
{
    private readonly string _logPath;
    private DateTime _gameStartTime;

    public SessionTrackerService()
    {
        // Salva no AppData do usuário
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logPath = Path.Combine(appData, "HandheldLauncher", "sessions.json");
    }

    public void StartSession(string gameTitle, string tdpProfile)
    {
        _gameStartTime = DateTime.Now;
        // Apenas loga o início para debug por enquanto
        System.Diagnostics.Debug.WriteLine($"[SESSÃO] Iniciada: {gameTitle} com TDP {tdpProfile}");
    }

    public void EndSession(string gameTitle)
    {
        if (_gameStartTime == default) return;
        
        var duration = DateTime.Now - _gameStartTime;
        _gameStartTime = default;
        
        // Salva um JSON simples no arquivo (append)
        var entry = new { Game = gameTitle, DurationSeconds = (int)duration.TotalSeconds, Date = DateTime.Now };
        var json = JsonSerializer.Serialize(entry);
        
        try
        {
            var dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(_logPath, json + Environment.NewLine);
            System.Diagnostics.Debug.WriteLine($"[SESSÃO] Finalizada: {gameTitle} ({duration.TotalMinutes:F1} min)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SESSÃO] Erro ao salvar: {ex.Message}");
        }
    }
}