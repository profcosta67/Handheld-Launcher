using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using HandheldLauncher.App.Diagnostics;

namespace HandheldLauncher.App.Views;

// v0.27 — Configurações no modo handheld, como na proposta aprovada: cada opção é uma linha
// (nome à esquerda, valor à direita), a linha em foco fica destacada com ‹ valor › e o valor
// troca com esquerda/direita. As páginas continuam escritas como antes no .axaml; as linhas
// são montadas aqui ao abrir, e só no layout de console. Em "Uso em PC" nada muda.
// Qualquer opção que não se encaixe fica como estava (nunca some).
public partial class SettingsWindow
{
    private readonly Dictionary<Control, Border> _rows = new();
    private readonly Dictionary<Control, TextBlock> _rowLabels = new();
    private readonly Dictionary<ComboBox, List<(RadioButton Radio, string Title, string Text)>> _radioProxies = new();
    private Border? _focusedRow;
    private bool _syncingRadios;

    /// <summary>Janela compacta e centralizada em telas grandes; tela cheia só quando a tela é pequena.</summary>
    private void ApplyConsoleWindowSize()
    {
        const double wantedWidth = 1280, wantedHeight = 800;
        var screen = (Owner is Window owner ? Screens.ScreenFromWindow(owner) : null) ?? Screens.Primary;
        if (screen is null || screen.Scaling <= 0) { WindowState = WindowState.FullScreen; return; }

        var area = screen.Bounds;
        var logicalWidth = area.Width / screen.Scaling;
        var logicalHeight = area.Height / screen.Scaling;

        // Sem folga para uma janela de 1280×800 com margem: ocupa a tela toda (ex.: escala de 150%).
        if (logicalWidth < wantedWidth + 160 || logicalHeight < wantedHeight + 100)
        {
            WindowState = WindowState.FullScreen;
            return;
        }

        WindowState = WindowState.Normal;
        Width = wantedWidth;
        Height = wantedHeight;
        Position = new PixelPoint(
            area.X + (int)((area.Width - wantedWidth * screen.Scaling) / 2),
            area.Y + (int)((area.Height - wantedHeight * screen.Scaling) / 2));
        ConsoleFrame.IsVisible = true;
    }

    private void BuildConsoleRows()
    {
        try { ReplaceRadioGroups(); }
        catch (Exception ex) { DebugLog.Write($"[CONFIG] Grupos de opção não convertidos em linha: {ex.GetType().Name}: {ex.Message}"); }

        foreach (var panel in ContentHost.GetLogicalDescendants().OfType<StackPanel>().ToList())
        {
            if (panel.Orientation != Orientation.Vertical) continue;
            foreach (var control in panel.Children.OfType<Control>().Where(c => c is ComboBox or ToggleSwitch or Slider).ToList())
            {
                try { WrapInRow(panel, control); }
                catch (Exception ex) { DebugLog.Write($"[CONFIG] Opção mantida no formato antigo: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// Um grupo de botões de opção vira uma linha só: o valor é a opção marcada e troca com
    /// esquerda/direita. Os botões originais continuam existindo (escondidos) e ligados às
    /// configurações — a linha só marca o botão correspondente.
    /// </summary>
    private void ReplaceRadioGroups()
    {
        foreach (var panel in ContentHost.GetLogicalDescendants().OfType<StackPanel>().ToList())
        {
            if (panel.Orientation != Orientation.Vertical) continue;
            var groups = panel.Children.OfType<RadioButton>()
                .GroupBy(r => r.GroupName ?? string.Empty)
                .Where(g => g.Count() >= 2)
                .ToList();

            foreach (var group in groups)
            {
                var radios = group.ToList();
                var firstIndex = panel.Children.IndexOf(radios[0]);
                if (firstIndex < 0) continue;

                TextBlock? label = null;
                string? sectionText = null;
                if (firstIndex > 0 && panel.Children[firstIndex - 1] is TextBlock before && !before.Classes.Contains("subtle"))
                {
                    if (before.Classes.Contains("sectionTitle")) sectionText = before.Text;
                    else label = before;
                }

                var entries = new List<(RadioButton Radio, string Title, string Text)>();
                foreach (var radio in radios)
                {
                    string title;
                    var text = new List<string>();
                    if (radio.Content is string plain)
                    {
                        title = plain;
                        var at = panel.Children.IndexOf(radio);
                        for (var j = at + 1; j < panel.Children.Count && panel.Children[j] is TextBlock note && note.Classes.Contains("subtle"); j++)
                        {
                            if (note.IsSet(IsVisibleProperty)) break; // tem condição própria: fica onde está
                            if (!string.IsNullOrWhiteSpace(note.Text)) text.Add(note.Text!);
                            note.IsVisible = false;
                        }
                    }
                    else
                    {
                        var lines = radio.GetLogicalDescendants().OfType<TextBlock>()
                            .Select(t => t.Text ?? string.Empty).Where(t => t.Length > 0).ToList();
                        title = lines.Count > 0 ? lines[0] : "Opção";
                        text.AddRange(lines.Skip(1));
                    }
                    entries.Add((radio, title, string.Join("\n\n", text)));
                }

                var combo = new ComboBox
                {
                    ItemsSource = entries.Select(e => e.Title).ToList(),
                    SelectedIndex = Math.Max(0, entries.FindIndex(e => e.Radio.IsChecked == true)),
                };

                combo.SelectionChanged += (_, _) =>
                {
                    if (_syncingRadios) return;
                    var i = combo.SelectedIndex;
                    if (i < 0 || i >= entries.Count) return;
                    if (entries[i].Radio.IsChecked != true) entries[i].Radio.IsChecked = true;
                    if (combo.IsFocused) UpdateHelp(combo);
                };

                foreach (var entry in entries)
                {
                    entry.Radio.IsCheckedChanged += (_, _) =>
                    {
                        // A configuração pode recusar a troca (ex.: congelar pede confirmação):
                        // a linha acompanha o que ficou marcado de verdade.
                        var marked = entries.FindIndex(e => e.Radio.IsChecked == true);
                        if (marked < 0 || marked == combo.SelectedIndex) return;
                        _syncingRadios = true;
                        try { combo.SelectedIndex = marked; }
                        finally { _syncingRadios = false; }
                        if (combo.IsFocused) UpdateHelp(combo);
                    };
                    entry.Radio.IsVisible = false;
                }

                if (label is null)
                {
                    label = new TextBlock
                    {
                        Text = group.Key == "Modo" ? "Como o launcher abre" : sectionText ?? "Opção",
                    };
                    label.Classes.Add("label");
                    panel.Children.Insert(firstIndex, label);
                    firstIndex++;
                }

                panel.Children.Insert(firstIndex, combo);
                _radioProxies[combo] = entries;
                _rowLabels[combo] = label;
            }
        }
    }

    private void WrapInRow(StackPanel panel, Control control)
    {
        if (_rows.ContainsKey(control)) return;

        TextBlock? label = null;
        if (_rowLabels.TryGetValue(control, out var known)) label = known;
        else if (_help.TryGetValue(control, out var help)) label = help.Title;

        if (control is ToggleSwitch toggle)
        {
            if (label is null && toggle.Content is Panel inner)
            {
                // Interruptor com o título e a explicação dentro dele.
                var lines = inner.Children.OfType<TextBlock>().ToList();
                if (lines.Count == 0) return;
                label = new TextBlock { Text = lines[0].Text };
                label.Classes.Add("label");
                var extra = _help.TryGetValue(control, out var prev) ? prev.Text : new List<TextBlock>();
                _help[control] = (label, lines.Skip(1).Concat(extra).ToList());
                toggle.Content = null;
            }
            else if (label is null && toggle.Content is string caption && caption.Length > 0)
            {
                label = new TextBlock { Text = caption };
                label.Classes.Add("label");
                _help[control] = (label, _help.TryGetValue(control, out var old) ? old.Text : new List<TextBlock>());
                toggle.Content = null;
            }
            toggle.OnContent = "Ligado";
            toggle.OffContent = "Desligado";
        }

        if (label is null) return;
        if (label.Parent is not null && !ReferenceEquals(label.Parent, panel)) return; // rótulo em outro lugar da página

        var controlIndex = panel.Children.IndexOf(control);
        if (controlIndex < 0) return;
        var labelIndex = panel.Children.IndexOf(label);
        var insertAt = labelIndex >= 0 ? Math.Min(labelIndex, controlIndex) : controlIndex;

        panel.Children.Remove(control);
        if (labelIndex >= 0) panel.Children.Remove(label);

        label.Margin = default;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        label.ClearValue(TextBlock.ForegroundProperty); // a cor vem da linha (muda quando em foco)
        label.ClearValue(TextBlock.FontWeightProperty);
        label.Classes.Add("rowLabel");

        control.Margin = default;
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.FocusAdorner = null; // o destaque é a própria linha
        if (control is ComboBox combo)
        {
            combo.MinWidth = 0;
            combo.Margin = new Thickness(0, 0, -28, 0); // a coluna da setinha (escondida) continua no modelo
            combo.Classes.Add("rowValue");
        }
        else if (control is Slider slider)
        {
            slider.Width = 240;
        }

        var left = Chevron("‹");
        var right = Chevron("›");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(left, 1);
        Grid.SetColumn(control, 2);
        Grid.SetColumn(right, 3);
        grid.Children.Add(label);
        grid.Children.Add(left);
        grid.Children.Add(control);
        grid.Children.Add(right);

        var row = new Border { Child = grid, IsVisible = control.IsVisible };
        row.Classes.Add("optRow");
        control.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty) row.IsVisible = control.IsVisible;
        };

        panel.Children.Insert(Math.Clamp(insertAt, 0, panel.Children.Count), row);
        _rows[control] = row;
    }

    private static TextBlock Chevron(string glyph)
    {
        var chevron = new TextBlock
        {
            Text = glyph,
            FontSize = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, -3, 10, 0),
        };
        chevron.Classes.Add("rowChevron");
        return chevron;
    }

    /// <summary>Destaca a linha que contém o controle em foco (e tira o destaque da anterior).</summary>
    private void UpdateRowFocus(Visual focused)
    {
        Border? row = null;
        for (Visual? v = focused; v is not null && !ReferenceEquals(v, this); v = v.GetVisualParent())
        {
            if (v is Border border && border.Classes.Contains("optRow")) { row = border; break; }
        }

        if (ReferenceEquals(row, _focusedRow)) return;
        _focusedRow?.Classes.Remove("focused");
        row?.Classes.Add("focused");
        _focusedRow = row;
    }

    /// <summary>Texto do painel da direita para uma linha de grupo de opções.</summary>
    private bool TryRadioHelp(Control control, out string title, out string text)
    {
        title = text = string.Empty;
        if (control is not ComboBox combo || !_radioProxies.TryGetValue(combo, out var options)) return false;
        var i = Math.Clamp(combo.SelectedIndex, 0, options.Count - 1);
        title = options[i].Title;
        var others = options.Where((_, n) => n != i).Select(o => o.Title).ToList();
        text = options[i].Text;
        if (others.Count > 0)
            text = (text.Length > 0 ? text + "\n\n" : string.Empty) + "Outras opções: " + string.Join("; ", others) + ".";
        return true;
    }
}
