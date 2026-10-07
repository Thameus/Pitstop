using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Pitstop.App;

/// <summary>Linha do log com a classe de cor: t = [pit], e = erro, w = aviso, o = ok, "" = texto comum.</summary>
sealed class LinhaLog(string texto)
{
    static readonly Regex Erro = new(@"(SEVERE|GRAVE|ERROR|Exception|\bat [\w.$]+\(|BUILD FAILURE)");
    static readonly Regex Aviso = new(@"(WARNING|WARN|ADVERTÊNCIA|AVISO)");
    static readonly Regex Ok = new(@"(Server startup in|BUILD SUCCESS|[Cc]ompiled successfully)");

    public string Texto { get; } = texto;
    public string Classe { get; } =
        texto.StartsWith("[pit]") ? (texto.StartsWith("[pit] ERRO") ? "e" : "t")
        : Erro.IsMatch(texto) ? "e" : Aviso.IsMatch(texto) ? "w" : Ok.IsMatch(texto) ? "o" : "";
}

sealed partial class Janela
{
    /// <summary>Linhas guardadas para o filtro (o LogHub guarda LOG_MAX_LINHAS; a lista é virtualizada).</summary>
    static int LogMax => Config.LogMax;

    readonly List<string> linhasLog = [];
    ObservableCollection<LinhaLog> visiveis = [];
    ListBox logLista = null!;
    TextBox filtroLog = null!;
    ToggleSwitch seguirLog = null!;
    StackPanel logVazio = null!;
    TextBlock logVazioTitulo = null!, logVazioTexto = null!;
    IDisposable? assinaturaLog;
    int geracaoLog;
    readonly DispatcherTimer filtroRelogio = new() { Interval = TimeSpan.FromMilliseconds(200) };

    Control MontarLog()
    {
        filtroLog = new TextBox { Watermark = "Filtrar log (texto, [pit], ERROR…)", VerticalAlignment = VerticalAlignment.Center };
        filtroLog.Classes.Add("inp");
        filtroLog.Classes.Add("busca");
        filtroLog.TextChanged += (_, _) => { filtroRelogio.Stop(); filtroRelogio.Start(); };
        filtroRelogio.Tick += (_, _) => { filtroRelogio.Stop(); Repintar(); };
        seguirLog = new ToggleSwitch { OnContent = "Seguir", OffContent = "Seguir", IsChecked = true, Margin = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center };
        seguirLog.IsCheckedChanged += (_, _) => RolarLogSeSeguindo();
        var limpar = Ui.Btn("Limpar", "fantasma");
        limpar.Click += async (_, _) =>
        {
            var nome = atual;
            try { await Task.Run(() => runner.Acao(nome, "limpar")); }
            catch (Exception e) { Aviso(e.Message, "err"); return; }
            linhasLog.Clear();
            Repintar();
        };

        var barra = new DockPanel { Height = 56 };
        var busca = Icones.Criar("busca", 16, "Mut");
        busca.Margin = new Thickness(0, 0, 4, 0);
        DockPanel.SetDock(busca, Dock.Left);
        barra.Children.Add(busca);
        DockPanel.SetDock(limpar, Dock.Right);
        barra.Children.Add(limpar);
        DockPanel.SetDock(seguirLog, Dock.Right);
        barra.Children.Add(seguirLog);
        barra.Children.Add(filtroLog);
        var barraBorda = new Border { Child = barra, Padding = new Thickness(16, 0, 8, 0), BorderThickness = new Thickness(0, 0, 0, 1) }
            .Res(Border.BackgroundProperty, "Surf").Res(Border.BorderBrushProperty, "Line");

        logLista = new ListBox
        {
            ItemsSource = visiveis,
            ItemTemplate = new FuncDataTemplate<LinhaLog>((l, _) =>
            {
                var t = new TextBlock { Text = l?.Texto };
                t.Classes.Add("linha");
                if (l is { Classe.Length: > 0 }) t.Classes.Add(l.Classe);
                return t;
            }),
        };
        logLista.Classes.Add("log");
        logLista.KeyDown += (_, e) =>
        {
            if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; CopiarSelecao(); }
            else if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; logLista.SelectAll(); }
        };
        var copiar = new MenuItem { Header = "Copiar seleção (Ctrl+C)" };
        copiar.Click += (_, _) => CopiarSelecao();
        var tudo = new MenuItem { Header = "Copiar tudo o que está visível" };
        tudo.Click += (_, _) => Copiar(visiveis.Select(l => l.Texto));
        logLista.ContextMenu = new ContextMenu { Items = { copiar, tudo } };

        logVazioTitulo = Ui.Txt("Nenhum log ainda", 15, "Fg", FontWeight.SemiBold);
        logVazioTexto = Ui.Paragrafo("Os logs deste perfil aparecerão aqui depois que ele for iniciado.", 13);
        logVazio = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Children = { logVazioTitulo, logVazioTexto }
        };
        var areaLog = new Grid { Children = { logLista, logVazio } };

        var caixa = new DockPanel();
        DockPanel.SetDock(barraBorda, Dock.Top);
        caixa.Children.Add(barraBorda);
        caixa.Children.Add(areaLog);
        var borda = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Child = caixa, ClipToBounds = true }
            .Res(Border.BorderBrushProperty, "Line").Res(Border.BackgroundProperty, "LogBg");
        return new Grid { Margin = new Thickness(40, 24, 40, 32), Children = { borda } };
    }

    void AtualizarLogVazio()
    {
        if (logVazio == null) return;
        var vazio = visiveis.Count == 0;
        logVazio.IsVisible = vazio;
        if (!vazio) return;
        var filtrando = linhasLog.Count > 0 && !string.IsNullOrWhiteSpace(filtroLog.Text);
        logVazioTitulo.Text = filtrando ? "Nenhuma linha corresponde ao filtro" : "Nenhum log ainda";
        logVazioTexto.Text = filtrando
            ? "Ajuste ou limpe o filtro para voltar a ver as linhas do log."
            : "Os logs deste perfil aparecerão aqui depois que ele for iniciado.";
    }
    /// <summary>Troca o ouvinte para o perfil atual: fim do buffer na hora, depois os lotes de 150 ms.</summary>
    void ConectarLog()
    {
        assinaturaLog?.Dispose();
        linhasLog.Clear();
        visiveis.Clear();
        AtualizarLogVazio();
        var g = ++geracaoLog;
        assinaturaLog = runner.Logs.Assinar(atual, lote => Dispatcher.UIThread.Post(() => { if (g == geracaoLog) Receber(lote); }));
    }

    void Receber(List<string> lote)
    {
        if (lote.Count == 0) return;
        linhasLog.AddRange(lote);
        var sobra = linhasLog.Count - LogMax;
        if (sobra > 0) linhasLog.RemoveRange(0, sobra);
        // lote grande (abertura, troca de perfil): refaz a lista de uma vez em vez de milhares de eventos
        if (lote.Count > 300) { Repintar(); return; }
        var f = filtroLog.Text ?? "";
        foreach (var l in lote)
            if (f == "" || l.Contains(f, StringComparison.OrdinalIgnoreCase)) visiveis.Add(new LinhaLog(l));
        while (visiveis.Count > LogMax) visiveis.RemoveAt(0);
        AtualizarLogVazio();
        RolarLogSeSeguindo();
    }

    void Repintar()
    {
        var f = filtroLog.Text ?? "";
        visiveis = new ObservableCollection<LinhaLog>(
            linhasLog.Where(l => f == "" || l.Contains(f, StringComparison.OrdinalIgnoreCase)).Select(l => new LinhaLog(l)));
        logLista.ItemsSource = visiveis;
        AtualizarLogVazio();
        RolarLogSeSeguindo();
    }

    void RolarLogSeSeguindo()
    {
        if (seguirLog?.IsChecked != true || logLista == null) return;
        // linhas quebradas têm alturas diferentes: a lista virtualizada só sabe o fim depois de medir a última
        Dispatcher.UIThread.Post(() =>
        {
            if (visiveis.Count > 0) logLista.ScrollIntoView(visiveis.Count - 1);
            logLista.FindDescendantOfType<ScrollViewer>()?.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    void CopiarSelecao()
    {
        var sel = logLista.SelectedItems?.OfType<LinhaLog>().OrderBy(l => visiveis.IndexOf(l)).Select(l => l.Texto).ToList() ?? [];
        if (sel.Count > 0) Copiar(sel);
    }

    async void Copiar(IEnumerable<string> l)
    {
        try
        {
            var cb = GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("área de transferência indisponível");
            await cb.SetTextAsync(string.Join(Environment.NewLine, l));
            Aviso("copiado", "on");
        }
        catch (Exception e) { Aviso("não copiei: " + e.Message, "err"); }
    }
}
