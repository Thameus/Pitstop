using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Pitstop.App;

/// <summary>Campo extra de um diálogo (ex.: caminho do Tomcat ao criar perfil).</summary>
sealed record CampoDialogo(string Chave, string Rotulo, string Valor = "", string Exemplo = "", string? Ajuda = null,
    bool Pasta = false, bool Arquivo = false, string[]? Extensoes = null, bool Obrigatorio = false);

/// <summary>
/// Diálogo do design visual do Pitstop: 460 px, --surf, borda --line, raio 16, ícone em quadrado --soft + título 18/600 + texto.
/// Com campo de nome: vazio ou igual ao inicial desabilita; inválido = borda --err + motivo. <c>acao</c> roda antes de
/// fechar: erro dela aparece no próprio diálogo, que continua aberto. Enter confirma, Esc cancela.
/// </summary>
sealed class Dialogo : Window
{
    readonly TextBox? campo;
    readonly Dictionary<string, TextBox> extras = new();
    readonly IReadOnlyList<CampoDialogo> camposExtras;
    readonly TextBlock erro;
    readonly Button sim;
    readonly string valorInicial;
    readonly Func<string, string>? validar;
    readonly Func<string, IReadOnlyDictionary<string, string>, Task> acao;

    public string? Resultado { get; private set; }
    public Dictionary<string, string> Valores { get; } = new();

    Dialogo(string titulo, string texto, string icone, bool perigo, string? rotulo, string valor, string botao,
        Func<string, string>? validar, IReadOnlyList<CampoDialogo>? camposExtras, Func<string, IReadOnlyDictionary<string, string>, Task> acao)
    {
        this.validar = validar;
        this.acao = acao;
        this.camposExtras = camposExtras ?? [];
        valorInicial = valor;
        Title = titulo;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        CanResize = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.Height;
        Width = 460 + 48;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var raiz = new StackPanel();
        var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
        var ic = Ui.IconeTipo(icone, perigo, 40);
        ic.VerticalAlignment = VerticalAlignment.Top;
        ic.Margin = new Thickness(0, 0, 14, 0);
        cab.Children.Add(ic);
        var tt = new StackPanel { Spacing = 4 };
        tt.Children.Add(new TextBlock { Text = titulo, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }.Res(TextBlock.ForegroundProperty, "Fg"));
        if (texto != "") tt.Children.Add(Ui.Paragrafo(texto));
        cab.Children.Add(tt);
        raiz.Children.Add(cab);

        var campos = new StackPanel { Spacing = 16 };
        erro = Ui.Paragrafo("", 12, "Err").Also(t => { t.Margin = new Thickness(0, 10, 0, 0); t.IsVisible = false; });
        if (rotulo != null)
        {
            campo = Ui.Inp();
            campo.Text = valor;
            campo.TextChanged += (_, _) => Checar();
            campos.Children.Add(Ui.Campo(rotulo, campo));
        }
        foreach (var c in this.camposExtras)
        {
            var t = Ui.Inp(c.Exemplo);
            t.Text = c.Valor;
            t.TextChanged += (_, _) => Checar();
            extras[c.Chave] = t;
            Control ctl = c.Pasta || c.Arquivo
                ? Ui.ComBotoes(t, Ui.Procurar(t, c.Rotulo, c.Pasta, c.Extensoes ?? []))
                : t;
            campos.Children.Add(Ui.Campo(c.Rotulo + (c.Obrigatorio ? "" : " (opcional)"), ctl, ajuda: c.Ajuda));
        }
        if (campos.Children.Count > 0) raiz.Children.Add(campos);
        raiz.Children.Add(erro);

        var acoes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0), Spacing = 8 };
        var nao = Ui.Btn("Cancelar", "fantasma");
        nao.Click += (_, _) => Close();
        sim = Ui.Btn(botao, perigo ? "perigo" : "pri");
        sim.IsDefault = true;
        sim.Click += async (_, _) => await Confirmar();
        acoes.Children.Add(nao);
        acoes.Children.Add(sim);
        raiz.Children.Add(acoes);

        var caixa = Ui.Cartao(raiz, 16, new Thickness(26));
        caixa.Margin = new Thickness(24);
        caixa.Res(Border.BoxShadowProperty, "Sombra");
        Content = caixa;

        Opened += (_, _) =>
        {
            if (campo != null) { campo.Focus(); campo.SelectAll(); }
            else if (extras.Count > 0) extras.Values.First().Focus();
            else nao.Focus();   // excluir: foco no botão seguro
            Checar();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        // sem barra de título: arrastar pelo cartão
        caixa.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source == caixa) BeginMoveDrag(e); };
    }

    bool Checar()
    {
        var e = "";
        var v = campo?.Text?.Trim() ?? "";
        if (campo != null && v != "" && v != valorInicial && validar != null) e = validar(v);
        erro.Text = e;
        erro.IsVisible = e != "";
        if (campo != null) campo.Classes.Set("invalido", e != "");
        var obrigatorios = camposExtras.Where(c => c.Obrigatorio).All(c => (extras[c.Chave].Text ?? "").Trim() != "");
        sim.IsEnabled = (campo == null || (v != "" && v != valorInicial)) && e == "" && obrigatorios;
        return sim.IsEnabled;
    }

    async Task Confirmar()
    {
        if (!Checar()) return;
        var v = campo?.Text?.Trim() ?? "";
        foreach (var (k, t) in extras) Valores[k] = (t.Text ?? "").Trim();
        try { await acao(v, Valores); }
        catch (Exception ex)
        {
            erro.Text = ex.Message;
            erro.IsVisible = true;
            return;
        }
        Resultado = v;
        Close();
    }

    /// <summary>Pede um nome (e campos extras). null = cancelou.</summary>
    public static async Task<(string? Nome, Dictionary<string, string> Valores)> Nome(Window dono, string titulo, string texto, string icone,
        string rotulo, string valor, string botao, Func<string, string> validar, Func<string, IReadOnlyDictionary<string, string>, Task> acao,
        IReadOnlyList<CampoDialogo>? extras = null)
    {
        var d = new Dialogo(titulo, texto, icone, false, rotulo, valor, botao, validar, extras, acao);
        await d.ShowDialog(dono);
        return (d.Resultado, d.Valores);
    }

    /// <summary>Confirmação (excluir, sair): ícone e primário em --err, foco inicial em Cancelar.</summary>
    public static async Task<bool> Confirmar(Window dono, string titulo, string texto, string botao, Action acao, string icone = "lixo")
    {
        var d = new Dialogo(titulo, texto, icone, true, null, "", botao, null, null, (_, _) => { acao(); return Task.CompletedTask; });
        await d.ShowDialog(dono);
        return d.Resultado != null;
    }
}
