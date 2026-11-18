using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HandheldLauncher.Core.Services;
using HandheldLauncher.Core.Models;
using HandheldLauncher.App.Diagnostics;
using HandheldLauncher.App.Platform;
using HandheldLauncher.App.Input;
using HandheldLauncher.App.ViewModels;
using HandheldLauncher.App.Platform.Hardware; 
using HandheldLauncher.Services.HidHide;

namespace HandheldLauncher.App.Views;

// Parte do OverlayWindow (0.28): volume, brilho, captura, gravação, Lossless e utilitários.
// Movido sem alterar nenhuma linha de código; ver OverlayWindow.axaml.cs.
public partial class OverlayWindow
{
    private void OnVolumeChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        _hardwareService?.SetVolume(Math.Round(e.NewValue)); SaveSetting("Volume", e.NewValue.ToString("F0"));
    }

    private void OnBrightnessChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        _hardwareService?.SetBrightness(Math.Round(e.NewValue)); SaveSetting("Brightness", e.NewValue.ToString("F0"));
    }

    private void OnScreenshotClick(object? sender, RoutedEventArgs e) =>
        HandheldLauncher.App.Diagnostics.UiSafe.Run(() => OnScreenshotClickCore(sender, e), "OnScreenshotClick");

    private async Task OnScreenshotClickCore(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;

        var wasVisible = IsVisible;
        try
        {
            if (wasVisible) HideWithUnblock();
            await Task.Delay(250);

            SendKey(VK_LWIN, false);
            await Task.Delay(80);
            SendKey(VK_SNAPSHOT, false);
            await Task.Delay(80);
            SendKey(VK_SNAPSHOT, true);
            await Task.Delay(80);
            SendKey(VK_LWIN, true);

            DebugLog.Write("[CAPTURA] Atalho Win+PrintScreen enviado após ocultar o overlay.");
            if (BtnScreenshot != null) BtnScreenshot.Content = "Captura enviada";
            _printFeedback = "✅ Capturado!";
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[CAPTURA] Erro: {ex.GetType().Name}: {ex.Message}");
            if (BtnScreenshot != null) BtnScreenshot.Content = "Falha";
            _printFeedback = "❌ Falhou";
        }
        finally
        {
            if (wasVisible && !IsAppClosing)
            {
                Show();
                ForceTopmost();
                // 18/11: a resposta ("Capturado!") só é mostrada com o painel de volta na tela —
                // antes era escrita e apagada enquanto ele estava escondido, e ninguém via.
                if (_printFeedback is not null)
                {
                    NewQsPanel?.SetPrintFeedback(_printFeedback);
                    await Task.Delay(1500);
                }
            }
            _printFeedback = null;
            if (BtnScreenshot != null) BtnScreenshot.Content = "📷 Print";
            NewQsPanel?.SetPrintFeedback(null);
        }
    }

    private string? _printFeedback;
    private bool _recordBusy;

    private void OnRecordClick(object? sender, RoutedEventArgs e) =>
        HandheldLauncher.App.Diagnostics.UiSafe.Run(() => OnRecordClickCore(), "OnRecordClick");

    /// <summary>
    /// 18/11 (pedido do usuário): ao começar a gravar, o painel sai da frente (senão entra no
    /// vídeo), aparece a contagem "3, 2, 1" no meio da tela e só então a gravação começa — e um
    /// selo "GRAVANDO" fica na tela enquanto durar (ver <see cref="RecordingUx"/>). Para parar não
    /// há contagem: para na hora.
    /// </summary>
    private async Task OnRecordClickCore()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_hardwareService is not WindowsHardwareService winService) return;

        if (winService.IsRecording)
        {
            winService.ToggleScreenRecording();
            _viewModel.UpdateHudRecordingState(winService.IsRecording);
            UpdateRecordButtonState();
            return;
        }

        // Durante os 3 s da contagem a gravação ainda não começou, então um segundo aperto
        // entraria aqui de novo e a segunda vez PARARIA a gravação da primeira.
        if (_recordBusy) return;
        _recordBusy = true;

        var wasVisible = IsVisible;
        var gameBefore = _viewModel.RunningGame?.Id;
        try
        {
            if (wasVisible) HideWithUnblock();
            await Task.Delay(150);          // o painel precisa sair da tela antes da contagem
            await RecordingUx.CountdownAsync();

            // O launcher pode ter sido fechado, ou um jogo ter aberto/fechado, nesses 3 s: começar
            // a gravar agora deixaria o FFmpeg rodando sozinho (o fechamento já passou).
            if (IsAppClosing)
            {
                DebugLog.Write("[GRAVADOR] Launcher fechando durante a contagem — gravação não começou.");
                return;
            }
            if (_viewModel.RunningGame?.Id != gameBefore)
            {
                DebugLog.Write("[GRAVADOR] O jogo mudou durante a contagem — gravação não começou.");
                return;
            }

            winService.ToggleScreenRecording();
            _viewModel.UpdateHudRecordingState(winService.IsRecording);
        }
        finally
        {
            _recordBusy = false;
            if (!winService.IsRecording) RecordingUx.HideRecording();   // fecha a janela do aviso
            UpdateRecordButtonState();
        }
    }

    private void UpdateRecordButtonState()
    {
        if (_hardwareService is WindowsHardwareService winService)
        {
            bool isRecording = winService.IsRecording;

            if (BtnRecord != null)
            {
                if (isRecording)
                {
                    BtnRecord.Content = "⏹ Parar";
                    BtnRecord.Background = new SolidColorBrush(Color.FromRgb(255, 59, 48));
                    BtnRecord.Foreground = Brushes.White;
                }
                else
                {
                    BtnRecord.Content = "🎥 Gravar";
                    BtnRecord.Background = _inactiveColor;
                    BtnRecord.Foreground = Brushes.White;
                }
            }

            if (RecordingBadge != null)
            {
                RecordingBadge.IsVisible = isRecording;
            }

            NewQsPanel?.SetRecordingVisual(isRecording);
        }
    }

    private void OnLsfgToggleClick(object? sender, RoutedEventArgs e) =>
        HandheldLauncher.App.Diagnostics.UiSafe.Run(() => OnLsfgToggleClickCore(sender, e), "OnLsfgToggleClick");

    private async Task OnLsfgToggleClickCore(object? sender, RoutedEventArgs e)
    {
        if (OperatingSystem.IsLinux())
        {
            // Linux: liga/desliga o LSFG-VK (camada Vulkan) via env + conf.toml.
            var turnOn = !_lsfgIsActive;
            if (LinuxLosslessService.SetEnabled(turnOn, _currentMultiplierIndex + 2))
            {
                _lsfgIsActive = turnOn;
                UpdateLsfgToggleVisual();
            }
            if (LsfgStatusText != null) LsfgStatusText.Text = LinuxLosslessService.GetStatus();
            return;
        }

        if (!OperatingSystem.IsWindows()) return;

        if (BtnLsfgToggle != null) BtnLsfgToggle.IsEnabled = false;

        try
        {
            if (!LosslessScalingService.IsLosslessRunning())
            {
                DebugLog.Write("[PATINHO] Lossless não está rodando — iniciando em segundo plano.");

                var wasVisible = IsVisible;
                if (wasVisible) HideWithUnblock();

                if (LsfgStatusText != null) LsfgStatusText.Text = "Iniciando Lossless...";

                if (!LosslessScalingService.TryLaunchLossless())
                {
                    if (LsfgStatusText != null) LsfgStatusText.Text = "❌ Falha ao iniciar Lossless";
                    if (wasVisible) { Show(); ForceTopmost(); }
                    return;
                }

                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(500);
                    if (LosslessScalingService.IsLosslessRunning()) break;
                }
                await Task.Delay(1500);

                if (wasVisible && !IsAppClosing)
                {
                    Show();
                    ForceTopmost();
                    await Task.Delay(300);
                }
            }

            if (LsfgStatusText != null) LsfgStatusText.Text = "Enviando atalho...";

            bool success = LosslessScalingService.ToggleFrameGen();

            if (success)
            {
                _lsfgIsActive = !_lsfgIsActive;
                UpdateLsfgToggleVisual();
                DebugLog.Write($"[PATINHO] Toggle OK — estado: {(_lsfgIsActive ? "ativado" : "desativado")}");
            }
            else
            {
                if (LsfgStatusText != null) LsfgStatusText.Text = "❌ Falha ao enviar atalho";
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[PATINHO] Toggle EXCEPTION: {ex.Message}");
            if (LsfgStatusText != null) LsfgStatusText.Text = $"❌ Erro: {ex.Message}";
        }
        finally
        {
            if (BtnLsfgToggle != null) BtnLsfgToggle.IsEnabled = true;
            if (!IsVisible && !IsAppClosing)
            {
                Show();
                ForceTopmost();
            }
        }
    }

    private void UpdateLsfgToggleVisual()
    {
        if (BtnLsfgToggle == null) return;

        if (_lsfgIsActive)
        {
            BtnLsfgToggle.Content = "🟢 Ativado";
            BtnLsfgToggle.Background = new SolidColorBrush(Color.FromRgb(46, 151, 98));
        }
        else
        {
            BtnLsfgToggle.Content = "⚪ Desativado";
            BtnLsfgToggle.Background = new SolidColorBrush(Color.FromRgb(42, 42, 58));
        }

        if (LsfgStatusText != null)
        {
            LsfgStatusText.Text = _lsfgIsActive ? "Frame Generation ativo" : "Frame Generation inativo";
        }
    }

    private void UpdateLsfgButtonState()
    {
        if (BtnLsfgToggle == null) return;

        if (OperatingSystem.IsLinux())
        {
            var ready = LinuxLosslessService.IsInstalled;
            BtnLsfgToggle.Content = _lsfgIsActive
                ? "🟢 Ativado"
                : (ready ? "Aplicar (LSFG-VK)" : "LSFG-VK ausente");
            BtnLsfgToggle.Background = _lsfgIsActive
                ? new SolidColorBrush(Color.FromRgb(46, 151, 98))
                : _inactiveColor;
            BtnLsfgToggle.IsEnabled = ready;
            return;
        }

        bool running = LosslessScalingService.IsLosslessRunning();
        bool activeContext = running && _gameHwnd != IntPtr.Zero && _lsfgIsActive;

        if (!running)
        {
            BtnLsfgToggle.Content = "Abrir Lossless";
            BtnLsfgToggle.Background = _inactiveColor;
            BtnLsfgToggle.IsEnabled = true;
        }
        else if (activeContext)
        {
            BtnLsfgToggle.Content = "🟢 Desativar";
            BtnLsfgToggle.Background = new SolidColorBrush(Color.FromRgb(46, 151, 98));
            BtnLsfgToggle.IsEnabled = true;
        }
        else
        {
            BtnLsfgToggle.Content = "Aplicar";
            BtnLsfgToggle.Background = _inactiveColor;
            BtnLsfgToggle.IsEnabled = true;
        }
    }

    private void OnLsfgOpenClick(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var path = LosslessScalingService.GetExecutablePath();
            if (!string.IsNullOrEmpty(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            else
            {
                DebugLog.Write("[PATINHO] Caminho do Lossless Scaling não encontrado.");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[PATINHO] Erro ao abrir Lossless Scaling: {ex.Message}");
        }
    }

    private void SendKey(ushort key, bool keyUp)
    {
        var input = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = key, dwFlags = keyUp ? KEYEVENTF_KEYUP : 0 } } };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private void SaveSetting(string key, string value)
    {
        try
        {
            var settings = AppSettingsStore.Load();
            switch (key) {
                case "TdpMode": settings.TdpMode = value; break;
                case "TdpCustomWatts": settings.TdpCustomWatts = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "FanMode": settings.FanMode = value; break;
                case "ColorMode": settings.ColorMode = int.Parse(value); break;
                case "OverlayPreset": settings.OverlayPreset = int.Parse(value); break;
                case "Volume": settings.Volume = (int)double.Parse(value); break;
                case "Brightness": settings.Brightness = (int)double.Parse(value); break;
            }
            AppSettingsStore.Save(settings);
        }
        catch (Exception ex) { DebugLog.Write($"[OVERLAY] Erro ao salvar setting {key}: {ex.Message}"); }
    }
}
