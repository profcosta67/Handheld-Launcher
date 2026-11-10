using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HandheldLauncher.App.Input;
using HandheldLauncher.Core.Services;

namespace HandheldLauncher.App.Views.Qs;

/// <summary>
/// QS 2.0 — painel do Quick Settings. Só UI: presets de TDP e resoluções vêm
/// do Core (testados). O <c>OverlayWindow</c> liga os serviços de hardware pelos
/// eventos abaixo — mantém este painel sem chamadas Windows e reutilizável.
/// </summary>
public partial class QsPanel : UserControl
{
    private readonly List<Button> _tdpButtons = new();
    private readonly List<Button> _resolutionButtons = new();
    private readonly List<Button> _fanButtons = new();
    private readonly List<Button> _colorButtons = new();
    private readonly List<Button> _losslessButtons = new();
    private readonly List<Button> _captureButtons = new();
    private readonly List<Button> _hudButtons = new();

    // Navegação por D-pad (mesma semântica do painel antigo).
    private readonly List<Border> _cards = new();
    private int _focusGroup = 0;   // 0.._cards.Count (o último = Fechar Menu)
    private bool _subOpen = false;
    private int _subIndex = 0;

    private int _minWatts = 5;
    private int _maxWatts = 35;
    private int _customWatts = QsPowerPresets.DesempenhoWatts;
    private bool _customPresetActive;
    private bool _suppressEvents = false;
    private bool _losslessOn = false;

    // O TDP demora ~500 ms a aplicar no EC: só mandamos depois de o dedo parar.
    private readonly DispatcherTimer _tdpDebounce = new() { Interval = TimeSpan.FromMilliseconds(450) };

    public QsPanel()
    {
        InitializeComponent();

        _tdpDebounce.Tick += (_, _) =>
        {
            _tdpDebounce.Stop();
            TdpWattsRequested?.Invoke(this, _customWatts);
        };

        TdpSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            _customWatts = (int)Math.Round(TdpSlider.Value);
            TxtTdp.Text = $"{_customWatts} W";
            if (_suppressEvents) return;
            SelectPowerPreset(PowerPreset.Custom);
            ScheduleTdpApply();
        };

        BrightnessSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            var brightness = (int)Math.Round(BrightnessSlider.Value);
            TxtBrightness.Text = $"{brightness}%";
            if (_suppressEvents) return;
            BrightnessChanged?.Invoke(this, brightness);
        };

        BuildPowerPresets();
        BuildHudChips();

        // Ordem vertical do D-pad = ordem visual (Brilho, Resolução, Cor,
        // Refrigeração, TDP, Lossless, Captura) + Fechar no fim.
        _cards.Add(BrightnessCard);
        _cards.Add(ResolutionCard);
        _cards.Add(ColorCard);
        _cards.Add(FanCard);
        _cards.Add(TdpCard);
        _cards.Add(HudCard);
        _cards.Add(LosslessCard);
        _cards.Add(CaptureCard);
        _losslessButtons.AddRange(new[] { BtnLosslessToggle, BtnLossless2x, BtnLossless3x, BtnLossless4x });
        _captureButtons.AddRange(new[] { BtnPrint, BtnRecord });

        RefreshFocusVisual();
    }

    // ----------------------------------------------------------------- eventos

    public event EventHandler? CloseRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler<int>? BrightnessChanged;
    public event EventHandler<bool>? LosslessToggled;

    // Ponte com o painel antigo: cada evento leva o **Button de origem**, com o
    // Tag no formato que os handlers existentes esperam ("Eco", "1600x900",
    // "Automático", "1"). Assim nada do OverlayWindow é reescrito.
    public event EventHandler<int>? TdpWattsRequested;
    public event EventHandler<Button>? ResolutionRequested;
    public event EventHandler<Button>? FanRequested;
    public event EventHandler<Button>? ColorRequested;
    public event EventHandler<Button>? MultiplierRequested;
    public event EventHandler? PrintRequested;
    public event EventHandler? RecordRequested;
    public event EventHandler<int>? HudPresetRequested;

    // ----------------------------------------------------------------- estado

    public void Initialize(
        string? deviceId,
        int minWatts,
        int maxWatts,
        int currentWatts,
        IReadOnlyList<string> fanModes,
        IReadOnlyList<string> colorModes,
        int nativeWidth = 1920,
        int nativeHeight = 1080,
        string currentFan = "",
        int currentColor = 0,
        int currentMultiplier = 0,
        PowerPreset currentPreset = PowerPreset.Normal)
    {
        _suppressEvents = true;
        try
        {
            _minWatts = Math.Min(minWatts, maxWatts);
            _maxWatts = Math.Max(minWatts, maxWatts);
            _customWatts = Math.Clamp(currentWatts, _minWatts, _maxWatts);

            TdpSlider.Minimum = _minWatts;
            TdpSlider.Maximum = _maxWatts;
            TdpSlider.Value = _customWatts;
            TxtTdp.Text = $"{_customWatts} W";

            BuildResolutions(ScreenPresets.ForDevice(deviceId, nativeWidth, nativeHeight));
            BuildFanChips(fanModes);
            BuildColorChips(colorModes);

            SelectPowerPreset(currentPreset);
            SelectFan(currentFan);
            SelectColor(currentColor);
            SelectMultiplier(currentMultiplier);
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    public void HideElevationWarning() => ElevationWarning.IsVisible = false;

    public void ShowElevationWarning(string message)
    {
        TxtElevationWarning.Text = message;
        ElevationWarning.IsVisible = true;
    }

    public void SetScreenInfo(string text) => TxtScreenInfo.Text = text;

    /// <summary>Bateria mostrada no rodapé (o painel não tem DataContext).</summary>
    public void SetBattery(string text) => TxtBattery.Text = text;

    public void SetBrightness(int percent)
    {
        _suppressEvents = true;
        BrightnessSlider.Value = Math.Clamp(percent, 0, 100);
        TxtBrightness.Text = $"{BrightnessSlider.Value:0}%";
        _suppressEvents = false;
    }

    public void SetFanStatus(string text) => TxtFanStatus.Text = text;

    private void ScheduleTdpApply()
    {
        _tdpDebounce.Stop();
        _tdpDebounce.Start();
    }

    // ----------------------------------------------------------------- seleção

    public void SelectPowerPreset(PowerPreset preset)
    {
        for (var i = 0; i < _tdpButtons.Count; i++)
            _tdpButtons[i].Classes.Set("active", (PowerPreset)_tdpButtons[i].Tag! == preset);

        // v0.25.3: no "Person." o D-pad ajusta o slider diretamente.
        _customPresetActive = preset == PowerPreset.Custom;
        if (TxtTdpHint is not null)
            TxtTdpHint.Text = _customPresetActive ? "Personalizado: ←/→ ajusta 1 W" : "";
    }

    // v0.25.3: HUD de desempenho de volta ao QS 2.0 (Off/Resumido/Médio/Completo).
    private void BuildHudChips()
    {
        HudHost.Children.Clear();
        _hudButtons.Clear();

        var labels = new[] { "Desligado", "Mín", "Méd", "Completo" };
        var emojis = new[] { "🚫", "📉", "📊", "📈" };
        for (var i = 0; i < labels.Length; i++)
        {
            var index = i;
            var button = MakeChip(emojis[i], labels[i]);
            button.Tag = index;
            button.Click += (_, _) => { SelectHud(index); HudPresetRequested?.Invoke(this, index); };
            _hudButtons.Add(button);
            HudHost.Children.Add(button);
        }

        SelectHud(0);
    }

    public void SelectHud(int preset)
    {
        for (var i = 0; i < _hudButtons.Count; i++)
            _hudButtons[i].Classes.Set("active", i == preset);
    }

    /// <summary>Preset vindo das settings (0..3).</summary>
    public void SetHud(int preset) => SelectHud(Math.Clamp(preset, 0, Math.Max(0, _hudButtons.Count - 1)));

    public void SelectFan(string label)
    {
        for (var i = 0; i < _fanButtons.Count; i++)
            _fanButtons[i].Classes.Set("active", Equals(_fanButtons[i].Tag, label));
    }

    public void SelectColor(int index)
    {
        for (var i = 0; i < _colorButtons.Count; i++)
            _colorButtons[i].Classes.Set("active", i == index);
    }

    public void SelectMultiplier(int index)
    {
        var buttons = new[] { BtnLossless2x, BtnLossless3x, BtnLossless4x };
        for (var i = 0; i < buttons.Length; i++)
            buttons[i].Classes.Set("active", i == index);
    }

    public void SelectResolution(string wxh)
    {
        foreach (var b in _resolutionButtons)
            b.Classes.Set("active", Equals(b.Tag, wxh));
    }

    public void SetLossless(bool on)
    {
        _losslessOn = on;
        BtnLosslessToggle.Content = on ? "🦆  Desligar" : "🦆  Ligar";
        BtnLosslessToggle.Classes.Set("active", on);
    }

    // ----------------------------------------------------------------- grelha

    private void BuildPowerPresets()
    {
        TdpPresetsHost.Children.Clear();
        _tdpButtons.Clear();

        foreach (var preset in QsPowerPresets.All)
        {
            var button = MakeChip(EmojiFor(preset), LabelFor(preset));
            button.Tag = preset;
            button.Click += (_, _) => OnPowerPresetClick(preset);
            _tdpButtons.Add(button);
            TdpPresetsHost.Children.Add(button);
        }

        SelectPowerPreset(PowerPreset.Normal);
    }

    private static string EmojiFor(PowerPreset preset) => preset switch
    {
        PowerPreset.Eco => "🍃",
        PowerPreset.Normal => "🎯",
        PowerPreset.Desempenho => "🚀",
        _ => "🎚",
    };

    private static string LabelFor(PowerPreset preset) => preset switch
    {
        PowerPreset.Eco => "Eco",
        PowerPreset.Normal => "Normal",
        PowerPreset.Desempenho => "Desemp.",
        _ => "Person.",
    };

    private Button MakeChip(string? emoji, string label)
    {
        var stack = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 2,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        if (!string.IsNullOrEmpty(emoji))
            stack.Children.Add(new TextBlock { Text = emoji, FontSize = 16, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });

        return new Button
        {
            Content = stack,
            Classes = { "pill" },
            Focusable = false,
            Height = 46,
            Padding = new Avalonia.Thickness(10, 4),
        };
    }

    private void OnPowerPresetClick(PowerPreset preset)
    {
        var watts = QsPowerPresets.Resolve(preset, _minWatts, _maxWatts, _customWatts);

        _suppressEvents = true;
        TdpSlider.Value = watts;
        _suppressEvents = false;

        _customWatts = watts;
        TxtTdp.Text = $"{watts} W";
        SelectPowerPreset(preset);

        _tdpDebounce.Stop();
        TdpWattsRequested?.Invoke(this, watts);
    }

    private void BuildResolutions(IReadOnlyList<ScreenPreset> presets)
    {
        ResolutionHost.Children.Clear();
        _resolutionButtons.Clear();

        foreach (var preset in presets)
        {
            var button = MakeChip("🖥️", preset.Label);
            button.Tag = $"{preset.Width}x{preset.Height}";
            button.Click += (_, _) => ActivateResolution(button);
            _resolutionButtons.Add(button);
            ResolutionHost.Children.Add(button);
        }
    }

    private void ActivateResolution(Button button)
    {
        foreach (var other in _resolutionButtons) other.Classes.Remove("active");
        button.Classes.Add("active");
        ResolutionRequested?.Invoke(this, button);
    }

    private void BuildFanChips(IReadOnlyList<string> labels)
    {
        FanHost.Children.Clear();
        _fanButtons.Clear();
        if (labels is null) return;

        foreach (var label in labels)
        {
            var button = MakeChip(FanEmoji(label), FanLabel(label));
            button.Tag = label; // handler antigo lê o nome por extenso
            button.Click += (_, _) => ActivateFan(button);
            _fanButtons.Add(button);
            FanHost.Children.Add(button);
        }
    }

    private static string FanLabel(string label) => label switch
    {
        "Automático" => "Auto",
        "Silencioso" => "Silenc",
        "Desempenho" => "Desemp",
        _ => label,
    };

    private static string FanEmoji(string label) => label switch
    {
        "Automático" => "🤖",
        "Silencioso" => "🤫",
        "Normal" => "⚖️",
        "Desempenho" => "🚀",
        "Boost" => "🔥",
        _ => "✳️",
    };

    private static string ColorEmoji(int index) => index switch
    {
        0 => "⚪",
        1 => "💡",
        2 => "⚫",
        _ => "🌙",
    };

    private void ActivateFan(Button button)
    {
        foreach (var other in _fanButtons) other.Classes.Remove("active");
        button.Classes.Add("active");
        FanRequested?.Invoke(this, button);
    }

    private void BuildColorChips(IReadOnlyList<string> labels)
    {
        ColorHost.Children.Clear();
        _colorButtons.Clear();
        if (labels is null) return;

        for (var i = 0; i < labels.Count; i++)
        {
            var button = MakeChip(ColorEmoji(i), labels[i]);
            button.Tag = i.ToString(); // handler antigo lê o índice 0..3
            button.Click += (_, _) => ActivateColor(button);
            _colorButtons.Add(button);
            ColorHost.Children.Add(button);
        }
    }

    private void ActivateColor(Button button)
    {
        foreach (var other in _colorButtons) other.Classes.Remove("active");
        button.Classes.Add("active");
        ColorRequested?.Invoke(this, button);
    }

    // ----------------------------------------------------------------- cliques

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    // Engrenagem do rodapé: o OverlayWindow fecha o painel e pede as
    // Configurações ao MainViewModel (mesma rota do menu global).
    private void OnSettingsClick(object? sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnLosslessToggleClick(object? sender, RoutedEventArgs e)
    {
        var turnOn = !_losslessOn;
        SetLossless(turnOn);
        LosslessToggled?.Invoke(this, turnOn);
    }

    private void OnLosslessMultiplierClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button clicked || clicked.Tag is not string tag || !int.TryParse(tag, out var multiplier))
            return;

        SelectMultiplier(multiplier - 2); // 2x→0, 3x→1, 4x→2
        MultiplierRequested?.Invoke(this, clicked);
    }

    private void OnPrintClick(object? sender, RoutedEventArgs e) => PrintRequested?.Invoke(this, EventArgs.Empty);

    private void OnRecordClick(object? sender, RoutedEventArgs e) => RecordRequested?.Invoke(this, EventArgs.Empty);

    // ----------------------------------------------------------------- D-pad

    public void HandleGamepadAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.MoveUp: Move(-1); break;
            case GamepadAction.MoveDown: Move(1); break;
            case GamepadAction.MoveLeft: SubMove(-1); break;
            case GamepadAction.MoveRight: SubMove(1); break;
            case GamepadAction.Confirm: Confirm(); break;
            case GamepadAction.Back:
                if (_subOpen) { _subOpen = false; _subIndex = 0; }
                else CloseRequested?.Invoke(this, EventArgs.Empty);
                RefreshFocusVisual();
                break;
            case GamepadAction.GlobalMenu:
            case GamepadAction.ToggleQuickSettings:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void Move(int dir)
    {
        if (_subOpen) { SubMove(dir); return; }
        _focusGroup = Math.Clamp(_focusGroup + dir, 0, _cards.Count);
        RefreshFocusVisual();
    }

    private void SubMove(int dir)
    {
        // v0.25.3: TDP personalizado — com o preset "Person." ativo, ←/→ altera
        // o slider de watts sem precisar de tocar no ecrã.
        if (_focusGroup == 4 && !_subOpen && _customPresetActive && TdpSlider is not null)
        {
            var current = (int)Math.Round(TdpSlider.Value);
            var next = Math.Clamp(current + dir, _minWatts, _maxWatts);
            if (next != current) TdpSlider.Value = next;
            return;
        }

        var items = ItemsFor(_focusGroup);
        if (items is null)
        {
            // sliders (Brilho): esquerda/direita ajusta de 5 em 5
            if (_focusGroup == 0 && BrightnessSlider is not null)
                BrightnessSlider.Value = Math.Clamp(BrightnessSlider.Value + (dir * 5), 0, 100);
            return;
        }

        _subIndex = Math.Clamp(_subIndex + dir, 0, items.Count - 1);
        RefreshFocusVisual();
    }

    private void Confirm()
    {
        if (!_subOpen)
        {
            if (_focusGroup == _cards.Count) { CloseRequested?.Invoke(this, EventArgs.Empty); return; }
            _subOpen = true;
            _subIndex = 0;
            RefreshFocusVisual();
            return;
        }

        Activate(_focusGroup, _subIndex);
        _subOpen = false;
        RefreshFocusVisual();
    }

    private List<Button>? ItemsFor(int group) => group switch
    {
        1 => _resolutionButtons,
        2 => _colorButtons,
        3 => _fanButtons,
        4 => _tdpButtons,
        5 => _hudButtons,
        6 => _losslessButtons,
        7 => _captureButtons,
        _ => null, // 0 = Brilho (slider), 8 = Fechar
    };

    private void Activate(int group, int index)
    {
        switch (group)
        {
            case 1:
                if (index >= 0 && index < _resolutionButtons.Count)
                    ActivateResolution(_resolutionButtons[index]);
                break;
            case 2:
                if (index >= 0 && index < _colorButtons.Count)
                    ActivateColor(_colorButtons[index]);
                break;
            case 3:
                if (index >= 0 && index < _fanButtons.Count)
                    ActivateFan(_fanButtons[index]);
                break;
            case 4:
                if (index >= 0 && index < _tdpButtons.Count && _tdpButtons[index].Tag is PowerPreset p)
                    OnPowerPresetClick(p);
                break;
            case 5:
                if (index >= 0 && index < _hudButtons.Count)
                    HudPresetRequested?.Invoke(this, index);
                break;
            case 6:
                if (index == 0) OnLosslessToggleClick(BtnLosslessToggle, new RoutedEventArgs());
                else if (index >= 1 && index < _losslessButtons.Count)
                    OnLosslessMultiplierClick(_losslessButtons[index], new RoutedEventArgs());
                break;
            case 7:
                if (index == 0) PrintRequested?.Invoke(this, EventArgs.Empty);
                else RecordRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void RefreshFocusVisual()
    {
        for (var i = 0; i < _cards.Count; i++)
            _cards[i].Classes.Set("focused", i == _focusGroup);

        BtnCloseMenu.Classes.Set("active", !_subOpen && _focusGroup == _cards.Count);

        // cursor (foco de navegação) dentro de um grupo
        for (var group = 0; group <= 7; group++)
        {
            var items = ItemsFor(group);
            if (items is null) continue;
            for (var i = 0; i < items.Count; i++)
                items[i].Classes.Set("cursor", _subOpen && group == _focusGroup && i == _subIndex);
        }

        // v0.25.3: manter o cartão focado visível (o QS tem mais cartões do que
        // a altura do painel — sem isto o D-pad "desaparecia" no fim da lista).
        if (_focusGroup >= 0 && _focusGroup < _cards.Count)
            _cards[_focusGroup].BringIntoView();

        if (_subOpen)
        {
            var focused = ItemsFor(_focusGroup);
            if (focused is not null && _subIndex >= 0 && _subIndex < focused.Count)
                focused[_subIndex].BringIntoView();
        }
    }
}


