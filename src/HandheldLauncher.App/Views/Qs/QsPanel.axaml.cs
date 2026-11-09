using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Views.Qs;

/// <summary>
/// QS 2.0 — painel do Quick Settings.
///
/// <para>Só UI: os presets de TDP (`QsPowerPresets`) e as resoluções por
/// aparelho (`ScreenPresets`) vêm do Core e já têm testes. Os serviços de
/// hardware (TDP real, ventoinha, cor, Lossless) são ligados pelo
/// <c>OverlayWindow</c> através dos eventos abaixo — é o que mantém este painel
/// sem nenhuma chamada Windows e reutilizável no Linux.</para>
/// </summary>
public partial class QsPanel : UserControl
{
    private readonly List<Button> _tdpButtons = new();
    private readonly List<Button> _resolutionButtons = new();
    private readonly List<Button> _fanButtons = new();
    private readonly List<Button> _colorButtons = new();

    private int _minWatts = 5;
    private int _maxWatts = 35;
    private int _customWatts = QsPowerPresets.NormalWatts;

    public QsPanel()
    {
        InitializeComponent();

        TdpSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            _customWatts = (int)Math.Round(TdpSlider.Value);
            TxtTdp.Text = $"{_customWatts} W";
            SelectPowerPreset(PowerPreset.Custom);
            if (_tdpButtons.Count > 3) TdpRequested?.Invoke(this, _tdpButtons[3]);
        };

        VolumeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            var volume = (int)Math.Round(VolumeSlider.Value);
            TxtVolume.Text = $"{volume}%";
            VolumeChanged?.Invoke(this, new VolumeEventArgs(volume));
        };

        BuildPowerPresets();
    }

    // ----------------------------------------------------------------- eventos

    /// <summary>Pedido de fecho (✕ ou B).</summary>
    public event EventHandler? CloseRequested;

    public event EventHandler<VolumeEventArgs>? VolumeChanged;
    public event EventHandler<bool>? LosslessToggled;

    // Ponte com o painel antigo: cada evento leva o **Button de origem**, com o
    // Tag no formato que os handlers existentes esperam ("Eco", "1600x900",
    // "Automático", "1"). Assim nada do OverlayWindow é reescrito.
    public event EventHandler<Button>? TdpRequested;
    public event EventHandler<Button>? ResolutionRequested;
    public event EventHandler<Button>? FanRequested;
    public event EventHandler<Button>? ColorRequested;
    public event EventHandler<Button>? MultiplierRequested;
    public event EventHandler? PrintRequested;
    public event EventHandler? RecordRequested;

    // ----------------------------------------------------------------- estado

    /// <summary>
    /// Preenche o painel: limites de TDP do aparelho, resoluções suportadas
    /// (as três do modelo) e os nomes de ventoinha/cor que o host conhece.
    /// </summary>
    public void Initialize(
        string? deviceId,
        int minWatts,
        int maxWatts,
        int currentWatts,
        IReadOnlyList<string> fanModes,
        IReadOnlyList<string> colorModes,
        int nativeWidth = 1920,
        int nativeHeight = 1080)
    {
        _minWatts = Math.Min(minWatts, maxWatts);
        _maxWatts = Math.Max(minWatts, maxWatts);
        _customWatts = Math.Clamp(currentWatts, _minWatts, _maxWatts);

        TdpSlider.Minimum = _minWatts;
        TdpSlider.Maximum = _maxWatts;
        TdpSlider.Value = _customWatts;
        TxtTdp.Text = $"{_customWatts} W";

        BuildResolutions(ScreenPresets.ForDevice(deviceId, nativeWidth, nativeHeight));
        BuildChips(FanHost, fanModes, _fanButtons, button => FanRequested?.Invoke(this, button), numericTags: false);
        BuildChips(ColorHost, colorModes, _colorButtons, button => ColorRequested?.Invoke(this, button), numericTags: true);
    }

    /// <summary>Aviso de que o TDP não vai aplicar (ex.: launcher sem administrador).</summary>
    public void ShowElevationWarning(string message)
    {
        TxtElevationWarning.Text = message;
        ElevationWarning.IsVisible = true;
    }

    public void SetScreenInfo(string text) => TxtScreenInfo.Text = text;

    public void SetVolume(int percent)
    {
        VolumeSlider.Value = Math.Clamp(percent, 0, 100);
        TxtVolume.Text = $"{VolumeSlider.Value:0}%";
    }

    /// <summary>Marca o preset ativo (sem disparar evento).</summary>
    public void SelectPowerPreset(PowerPreset preset)
    {
        for (var i = 0; i < _tdpButtons.Count; i++)
        {
            if ((PowerPreset)i == preset) _tdpButtons[i].Classes.Add("active");
            else _tdpButtons[i].Classes.Remove("active");
        }
    }

    // ----------------------------------------------------------------- grelha

    private void BuildPowerPresets()
    {
        TdpPresetsHost.Children.Clear();
        _tdpButtons.Clear();

        foreach (var preset in QsPowerPresets.All)
        {
            var button = new Button
            {
                Content = preset.ToString(),
                Classes = { "pill" },
                Tag = preset,
                Focusable = false,
            };
            button.Click += (_, _) => OnPowerPresetClick(preset);
            _tdpButtons.Add(button);
            TdpPresetsHost.Children.Add(button);
        }

        SelectPowerPreset(PowerPreset.Normal);
    }

    private void OnPowerPresetClick(PowerPreset preset)
    {
        var watts = QsPowerPresets.Resolve(preset, _minWatts, _maxWatts, _customWatts);

        // Só o Custom usa o slider como está; os outros movem o slider.
        if (preset != PowerPreset.Custom) TdpSlider.Value = watts;
        TxtTdp.Text = $"{watts} W";
        SelectPowerPreset(preset);

        var index = (int)preset;
        if (index >= 0 && index < _tdpButtons.Count) TdpRequested?.Invoke(this, _tdpButtons[index]);
    }

    private void BuildResolutions(IReadOnlyList<ScreenPreset> presets)
    {
        ResolutionHost.Children.Clear();
        _resolutionButtons.Clear();

        foreach (var preset in presets)
        {
            var button = new Button
            {
                Content = preset.Label,
                Classes = { "pill" },
                Tag = $"{preset.Width}x{preset.Height}",
                Focusable = false,
            };
            button.Click += (_, _) =>
            {
                foreach (var other in _resolutionButtons) other.Classes.Remove("active");
                button.Classes.Add("active");
                ResolutionRequested?.Invoke(this, button);
            };
            _resolutionButtons.Add(button);
            ResolutionHost.Children.Add(button);
        }
    }

    private void BuildChips(StackPanel host, IReadOnlyList<string> labels, List<Button> bucket,
        Action<Button> onPick, bool numericTags)
    {
        host.Children.Clear();
        bucket.Clear();
        if (labels is null) return;

        foreach (var label in labels)
        {
            var index = bucket.Count;
            var button = new Button
            {
                Content = label,
                Classes = { "pill" },
                // Os handlers antigos de cor leem o Tag como índice (0..3); os de
                // ventoinha leem o nome por extenso.
                Tag = numericTags ? index.ToString() : label,
                Focusable = false,
            };
            button.Click += (_, _) =>
            {
                foreach (var other in bucket) other.Classes.Remove("active");
                button.Classes.Add("active");
                onPick(button);
            };
            bucket.Add(button);
            host.Children.Add(button);
        }
    }

    // ----------------------------------------------------------------- cliques

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnLosslessToggleClick(object? sender, RoutedEventArgs e)
    {
        var turnOn = BtnLosslessToggle.Content as string != "Desligar";
        BtnLosslessToggle.Content = turnOn ? "Desligar" : "Ligar";
        LosslessToggled?.Invoke(this, turnOn);
    }

    private void OnLosslessMultiplierClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button clicked || clicked.Tag is not string tag || !int.TryParse(tag, out var multiplier))
            return;

        foreach (var button in new[] { BtnLossless2x, BtnLossless3x, BtnLossless4x })
            button.Classes.Remove("active");
        clicked.Classes.Add("active");

        MultiplierRequested?.Invoke(this, clicked);
    }

    private void OnPrintClick(object? sender, RoutedEventArgs e) => PrintRequested?.Invoke(this, EventArgs.Empty);

    private void OnRecordClick(object? sender, RoutedEventArgs e) => RecordRequested?.Invoke(this, EventArgs.Empty);

}

/// <summary>Preset escolhido e watts resolvidos (já com clamp do firmware).</summary>
public sealed class PowerPresetEventArgs(PowerPreset preset, int watts) : EventArgs
{
    public PowerPreset Preset { get; } = preset;
    public int Watts { get; } = watts;
}

public sealed class VolumeEventArgs(int percent) : EventArgs
{
    public int Percent { get; } = percent;
}

