using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Pitstop.App;

/// <summary>Estado do perfil para a tela (mesma regra do estadoDe da web/ui.html).</summary>
sealed record Estado(string K, string Txt, string Pill, bool Rodando = false, bool Build = false, bool Reinicio = false);

/// <summary>
/// Janela do Pitstop: lateral de perfis, cabeçalho com estado e ações, números, abas (Log + as do tipo) e painéis.
/// Mesma função e visual da web/ui.html (design visual do Pitstop), chamando o Runner direto. Fechar no X só esconde: quem
/// encerra é o Parar e Sair (bandeja ou menu ⋯).
/// </summary>
sealed partial class Janela : Window
{
    readonly Runner runner;
    readonly Bandeja bandeja;
    Prefs prefs => Prefs.Atual;
    bool deVez;

    JsonObject cfg = new();
    string atual = "";
    bool sujo, preenchendo, ocupado;
    List<StatusPerfil> status = [];
    readonly DispatcherTimer relogio = new() { Interval = TimeSpan.FromSeconds(3) };
    bool consultando;

    // lateral
    readonly StackPanel listaPerfis = new() { Spacing = 2 };
    readonly Dictionary<string, (Button B, Ellipse Ponto, TextBlock Meta, TextBlock Est)> itensPerfil = new();
    readonly Button novoBtn;
    readonly Button temaBtn;
    readonly Button ajustesBtn;
    readonly TextBlock rodapeTxt = Ui.Txt("", 12, "Mut");
    readonly Ellipse rodapePonto = Ui.Ponto(6);

    // cabeçalho e corpo
    readonly TextBlock nomeTxt = new() { FontSize = 30, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, LetterSpacing = -0.6 };
    readonly Ellipse pillPonto = Ui.Ponto(7);
    readonly TextBlock pillTxt = Ui.Txt("parado", 13);
    readonly TextBlock tipoTxt = Ui.Txt("", 13, "Mut");
    readonly TextBlock alvoTxt = Ui.Txt("", 12, "Mut", mono: true);
    readonly Button salvarBtn, iniciarBtn, depurarBtn, pararBtn, reiniciarBtn, buildBtn, syncBtn, abrirBtn, terminalBtn, maisBtn;
    readonly TextBlock pararTxt = new() { Text = "Parar", VerticalAlignment = VerticalAlignment.Center };
    readonly UniformGrid stats = new() { Columns = 4, Rows = 1 };
    readonly StackPanel abas = new() { Orientation = Orientation.Horizontal };
    readonly Grid paineis = new();
    readonly Dictionary<string, (Button Aba, Control Painel)> abasPorId = new();
    string abaAtual = "log";
    Control corpo = null!;
    Control vazio = null!;

    // avisos e véu do diálogo
    readonly Border toast = new()
    {
        CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 24, 24),
        IsVisible = false, MaxWidth = 560,
    };
    readonly Ellipse toastPonto = Ui.Ponto(8);
    readonly TextBlock toastTxt = Ui.Txt("", 13);
    readonly DispatcherTimer toastRelogio = new() { Interval = TimeSpan.FromSeconds(4) };
    readonly Border veu = new() { IsVisible = false };

    public Janela(Runner runner, Bandeja bandeja)
    {
        this.runner = runner;
        this.bandeja = bandeja;
        Title = "Pitstop";
        MinWidth = 980;
        MinHeight = 620;
        Icon = Desenho.Icone();
        LerPosicao();

        // ---- lateral
        novoBtn = Ui.BtnIco("novo", "Novo perfil", "fantasma", 18);
        novoBtn.Width = novoBtn.Height = 36;
        novoBtn.MinHeight = 36;
        novoBtn.Click += (_, _) => MenuNovo(novoBtn);
        temaBtn = Ui.BtnIco("lua", "Mudar tema (claro/escuro)", "fantasma", 18);
        temaBtn.Click += (_, _) => AlternarTema();
        ajustesBtn = Ui.BtnIco("ajustes", "Ajustes", "fantasma", 18);
        ajustesBtn.Click += (_, _) => Assistente.Abrir(bandeja, primeiraVez: false);

        // ---- cabeçalho
        salvarBtn = Ui.Btn("Salvar").Dica("Salvar alterações (Ctrl+S)");
        salvarBtn.IsVisible = false;
        salvarBtn.Res(Button.BorderBrushProperty, "Acc").Res(Button.ForegroundProperty, "AccTxt");
        salvarBtn.Click += (_, _) => SalvarComAviso();
        iniciarBtn = Ui.Btn("Iniciar", "pri", "play", "AccFg");
        depurarBtn = Ui.Btn("Depurar", "", "debug");
        pararBtn = Ui.Btn("", "pri");
        pararBtn.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Icones.Criar("stop", 16, "AccFg"), pararTxt } };
        reiniciarBtn = Ui.Btn("Reiniciar", "", "reiniciar").Dica("Parar e iniciar novamente no mesmo modo");
        buildBtn = Ui.Btn("Build", "", "build").Dica("Compilar e atualizar os artefatos");
        syncBtn = Ui.Btn("Sync", "", "sync").Dica("Sincronizar arquivos alterados com o Tomcat");
        abrirBtn = Ui.BtnIco("abrir", "Abrir no navegador");
        terminalBtn = Ui.BtnIco("terminal", "Abrir terminal na pasta deste perfil");
        maisBtn = Ui.BtnIco("mais", "Mais ações", "fantasma", 18);
        maisBtn.Click += (_, _) => MenuMais();
        foreach (var (b, a) in new[] { (iniciarBtn, "start"), (depurarBtn, "debug"), (pararBtn, "stop"), (reiniciarBtn, "reiniciar"),
                                       (buildBtn, "build"), (syncBtn, "sync"), (abrirBtn, "abrir"), (terminalBtn, "terminal") })
            b.Click += (_, _) => Executar(a);

        Content = Montar();
        MontarPaineis();
        PintarTemaBtn();
        App.Atual.ActualThemeVariantChanged += (_, _) => PintarTemaBtn();

        toastRelogio.Tick += (_, _) => { toastRelogio.Stop(); toast.IsVisible = false; };
        relogio.Tick += (_, _) => AtualizarStatus();
        relogio.Start();
        KeyDown += Teclas;
        Closing += Fechando;

        Carregar(prefs.Perfil);
    }

    // ---------------------------------------------------------------- montagem

    Control Montar()
    {
        var raiz = new Grid();
        var principal = new Grid { ColumnDefinitions = new ColumnDefinitions("280,*") };
        principal.Children.Add(MontarLado());
        corpo = MontarCorpo();
        vazio = MontarVazio();
        var direita = new Grid { Children = { corpo, vazio } };
        Grid.SetColumn(direita, 1);
        principal.Children.Add(direita);
        raiz.Children.Add(principal);

        toastPonto.Margin = new Thickness(0, 0, 10, 0);
        toastTxt.TextWrapping = TextWrapping.Wrap;
        toastTxt.TextTrimming = TextTrimming.None;
        toast.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { toastPonto, toastTxt } };
        toast.Res(Border.BackgroundProperty, "Surf").Res(Border.BorderBrushProperty, "Line").Res(Border.BoxShadowProperty, "Sombra");
        raiz.Children.Add(toast);
        veu.Res(Border.BackgroundProperty, "Veu");
        raiz.Children.Add(veu);
        return raiz;
    }

    Control MontarLado()
    {
        var lado = new DockPanel { LastChildFill = true };
        var borda = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Child = lado }
            .Res(Border.BackgroundProperty, "Lado").Res(Border.BorderBrushProperty, "Line");

        // marca
        var marca = new Border { Height = 76, Padding = new Thickness(24, 0), BorderThickness = new Thickness(0, 0, 0, 1) }.Res(Border.BorderBrushProperty, "Line");
        var msp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 12 };
        msp.Children.Add(new Image { Source = Desenho.Bitmap(64, null), Width = 32, Height = 32 });
        var mt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mt.Children.Add(Ui.Txt("Pitstop", 16, "Fg", FontWeight.SemiBold));
        mt.Children.Add(Ui.Txt("v" + Versao + " · " + (So.Windows ? "Windows" : "Linux"), 12, "Mut"));
        msp.Children.Add(mt);
        marca.Child = msp;
        DockPanel.SetDock(marca, Dock.Top);
        lado.Children.Add(marca);

        // PERFIS  [+]
        var rot = new DockPanel { Margin = new Thickness(24, 14, 12, 8) };
        DockPanel.SetDock(novoBtn, Dock.Right);
        rot.Children.Add(novoBtn);
        var r = Ui.Rotulo("Perfis");
        r.VerticalAlignment = VerticalAlignment.Center;
        rot.Children.Add(r);
        DockPanel.SetDock(rot, Dock.Top);
        lado.Children.Add(rot);

        // rodapé: estado da tela web (clique abre no navegador), ajustes, tema
        var rodape = new Border { Height = 64, Padding = new Thickness(16, 0, 12, 0), BorderThickness = new Thickness(0, 1, 0, 0) }.Res(Border.BorderBrushProperty, "Line");
        var rd = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var botoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { ajustesBtn, temaBtn } };
        DockPanel.SetDock(botoes, Dock.Right);
        rd.Children.Add(botoes);
        var web = Ui.Btn("", "fantasma").Dica("Abrir a tela web no navegador");
        web.HorizontalAlignment = HorizontalAlignment.Left;
        web.Padding = new Thickness(8, 0);
        rodapePonto.Margin = new Thickness(0, 0, 8, 0);
        web.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { rodapePonto, rodapeTxt } };
        web.Click += (_, _) => Proc.AbrirUrl("http://localhost:" + Instancia.Porta + "/");
        rd.Children.Add(web);
        rodape.Child = rd;
        DockPanel.SetDock(rodape, Dock.Bottom);
        lado.Children.Add(rodape);

        listaPerfis.Margin = new Thickness(12, 0, 12, 12);
        lado.Children.Add(new ScrollViewer { Content = listaPerfis, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return borda;
    }

    static string Versao => typeof(Janela).Assembly.GetName().Version is { } v ? v.Major + "." + v.Minor + "." + v.Build : "";

    Control MontarCorpo()
    {
        var dock = new DockPanel();
        var topo = new StackPanel { Margin = new Thickness(40, 28, 40, 0) };

        var linha1 = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var titulo = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 24, 0) };
        var nomeLinha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        nomeTxt.Res(TextBlock.ForegroundProperty, "Fg");
        ToolTip.SetTip(nomeTxt, "Duplo clique para renomear");
        nomeTxt.DoubleTapped += (_, _) => _ = Renomear();
        nomeLinha.Children.Add(nomeTxt);
        var pill = new Border { Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(12, 0),
            Margin = new Thickness(0, 4, 0, 0), VerticalAlignment = VerticalAlignment.Center }.Res(Border.BorderBrushProperty, "Line");
        pillPonto.Margin = new Thickness(0, 0, 8, 0);
        pill.Child = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { pillPonto, pillTxt } };
        nomeLinha.Children.Add(pill);
        titulo.Children.Add(nomeLinha);
        var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Spacing = 10 };
        sub.Children.Add(tipoTxt);
        sub.Children.Add(Ui.Txt("·", 13, "Mut"));
        alvoTxt.VerticalAlignment = VerticalAlignment.Center;
        sub.Children.Add(alvoTxt);
        titulo.Children.Add(sub);
        linha1.Children.Add(titulo);

        var acoes = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        var sep = new Border { Width = 1, Height = 24, Margin = new Thickness(4, 8, 12, 8), VerticalAlignment = VerticalAlignment.Top }.Res(Border.BackgroundProperty, "Line");
        foreach (var b in new Control[] { salvarBtn, iniciarBtn, depurarBtn, pararBtn, reiniciarBtn, buildBtn, syncBtn, sep, abrirBtn, terminalBtn, maisBtn })
        {
            if (b != sep) b.Margin = new Thickness(0, 0, 8, 8);
            acoes.Children.Add(b);
        }
        Grid.SetColumn(acoes, 1);
        linha1.Children.Add(acoes);
        topo.Children.Add(linha1);

        stats.Margin = new Thickness(-6, 16, -6, 0);
        topo.Children.Add(stats);

        var linhaAbas = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 24, 0, 0), Child = abas }.Res(Border.BorderBrushProperty, "Line");
        topo.Children.Add(linhaAbas);

        DockPanel.SetDock(topo, Dock.Top);
        dock.Children.Add(topo);
        dock.Children.Add(paineis);
        return dock;
    }

    /// <summary>Sem perfis (primeira vez): cartões grandes com os tipos, no lugar do corpo.</summary>
    Control MontarVazio()
    {
        var sp = new StackPanel { MaxWidth = 720, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 10, Margin = new Thickness(40) };
        sp.Children.Add(new TextBlock { Text = "Crie seu primeiro perfil", FontSize = 28, FontWeight = FontWeight.SemiBold, LetterSpacing = -0.5 }.Res(TextBlock.ForegroundProperty, "Fg"));
        sp.Children.Add(Ui.Paragrafo("Um perfil é uma coisa que o Pitstop sobe e para: um Tomcat com os wars dos seus projetos, uma aplicação Java " +
                                     "com main, um pacote pronto, um script npm ou um comando personalizado. Cada tipo pede só os caminhos de que precisa.", 14));
        var grade = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        foreach (var (tipo, icone, nome, desc, _) in Tipos)
        {
            var linha = new StackPanel { Spacing = 12 };
            linha.Children.Add(Ui.IconeTipo(icone, false, 40).Also(b => b.HorizontalAlignment = HorizontalAlignment.Left));
            linha.Children.Add(Ui.Txt(nome, 15, "Fg", FontWeight.SemiBold));
            linha.Children.Add(Ui.Paragrafo(desc, 12));
            var b = new Button { Content = linha, Width = 220, MinHeight = 156, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(18),
                HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top };
            b.Classes.Add("b");
            b.CornerRadius = new CornerRadius(14);
            var t = tipo;
            b.Click += (_, _) => _ = Criar(NovoPerfil(t));
            grade.Children.Add(b);
        }
        sp.Children.Add(grade);
        var dica = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
        dica.Children.Add(Ui.Txt("Pastas padrão (JDK, Tomcat, Maven, projetos) ficam em", 13, "Mut"));
        var aj = Ui.Btn("Ajustes", "link");
        aj.Padding = new Thickness(0);
        aj.MinHeight = 0;
        aj.Click += (_, _) => Assistente.Abrir(bandeja, primeiraVez: false);
        dica.Children.Add(aj);
        sp.Children.Add(dica);
        return new ScrollViewer { Content = sp, IsVisible = false };
    }

    /// <summary>Painel rolável com a margem dos painéis da ui.html (24 40 32).</summary>
    static ScrollViewer Rolavel(params Control[] blocos)
    {
        var sp = new StackPanel { Margin = new Thickness(40, 24, 40, 32) };
        foreach (var b in blocos) sp.Children.Add(b);
        return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsVisible = false };
    }

    void RegistrarAba(string id, string titulo, Control painel)
    {
        var b = new Button { Content = titulo };
        b.Classes.Add("aba");
        b.Click += (_, _) => EscolherAba(id);
        abasPorId[id] = (b, painel);
        painel.IsVisible = false;
        paineis.Children.Add(painel);
    }

    static readonly Dictionary<string, string[]> AbasDoTipo = new()
    {
        ["tomcat"] = ["log", "artefatos", "servidor"],
        ["war"] = ["log", "wars", "servidor"],
        ["java"] = ["log", "app"],
        ["zip"] = ["log", "zip"],
        ["npm"] = ["log", "npm"],
        ["comando"] = ["log", "comando"],
    };

    /// <summary>Abas do tipo do perfil atual, na ordem; a escolhida fica lembrada.</summary>
    void PintarAbas()
    {
        abas.Children.Clear();
        foreach (var id in AbasDoTipo[TipoAtual]) abas.Children.Add(abasPorId[id].Aba);
    }

    void EscolherAba(string id)
    {
        if (!AbasDoTipo[TipoAtual].Contains(id)) id = "log";
        abaAtual = id;
        foreach (var (k, (aba, painel)) in abasPorId)
        {
            aba.Classes.Set("sel", k == id);
            painel.IsVisible = k == id;
        }
        if (id == "log") RolarLogSeSeguindo();
        prefs.Aba = id;
        Prefs.Gravar();
    }

    // ---------------------------------------------------------------- mostrar, esconder, fechar

    public void Trazer()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = prefs.Max ? WindowState.Maximized : WindowState.Normal;
        Activate();
        Topmost = true;   // trazer para a frente a partir da bandeja nem sempre ganha o foco
        Topmost = false;
        AtualizarStatus();
    }

    void Fechando(object? s, WindowClosingEventArgs e)
    {
        GuardarPosicao();
        if (deVez) return;
        e.Cancel = true;   // X só esconde: a bandeja continua e a janela volta pelo ícone
        if (sujo)
        {
            try { Salvar(); }
            catch (Exception ex) { Aviso("não salvei " + atual + ": " + ex.Message, "err"); return; }
        }
        Hide();
    }

    public void FecharDeVez()
    {
        deVez = true;
        relogio.Stop();
        assinaturaLog?.Dispose();
        Close();
    }

    void Teclas(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; SalvarComAviso(); }
        else if (e.Key == Key.F5) { e.Handled = true; Recarregar(); }
    }

    // ---------------------------------------------------------------- posição e tema

    void LerPosicao()
    {
        if (prefs.W is double w && prefs.H is double h && w >= MinWidth && h >= MinHeight)
        {
            Width = w;
            Height = h;
            if (prefs.X is double x && prefs.Y is double y)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint((int)x, (int)y);
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        else
        {
            Width = 1320;
            Height = 860;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        if (prefs.Max) WindowState = WindowState.Maximized;
        // posição salva fora da área visível (monitor desconectado): centraliza
        Opened += (_, _) =>
        {
            if (Screens.ScreenFromWindow(this) == null) Position = new PixelPoint(80, 80);
        };
    }

    void GuardarPosicao()
    {
        prefs.Max = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            prefs.X = Position.X;
            prefs.Y = Position.Y;
            prefs.W = Width;
            prefs.H = Height;
        }
        Prefs.Gravar();
    }

    void AlternarTema()
    {
        prefs.Tema = App.Atual.Escuro ? "claro" : "escuro";
        Prefs.Gravar();
        App.Atual.AplicarTema();
    }

    void PintarTemaBtn() => temaBtn.Content = Icones.Criar(App.Atual.Escuro ? "sol" : "lua", 18);

    // ---------------------------------------------------------------- estado

    static string Dur(long ms) => Agora.Duracao(ms);

    static Estado EstadoDe(StatusPerfil? st)
    {
        if (st == null) return new("parado", "parado", "Parado");
        if (st.Execucao is { } e)
        {
            if (e.Tipo == "reinicio") return new("ocupado", "reiniciando", "Reiniciando…", Rodando: true, Reinicio: true);
            if (e.Tipo == "build") return new("ocupado", "build", "Build em andamento · " + Dur(Agora.Ms - e.Desde), Build: true);
            if (e.Pronto == null) return new("ocupado", "subindo", e.Tipo == "debug" ? "Subindo em debug…" : "Subindo…", Rodando: true);
            return e.Tipo == "debug"
                ? new("debug", "debug", "Debug · porta " + st.PortaDebug, Rodando: true)
                : new("noar", "no ar", "No ar · subiu em " + Dur(e.Pronto.Value - e.Desde), Rodando: true);
        }
        if (st.EmUso)
        {
            if (st.Tipo is "java" or "comando")
                return new("externo", "externo", "Executando externamente", Rodando: true);
            return new("portaocupada", "porta ocupada", "Porta " + st.Porta + " ocupada por outro processo", Rodando: true);
        }
        var u = st.Ultimo;
        if (u != null && u.Tipo != "build" && u.Parado == false) return new("erro", "caiu", "Caiu · código " + u.Codigo);
        return new("parado", "parado", u is { Tipo: "build", Ok: false } && u.Cancelado != true ? "Parado · último build falhou" : "Parado");
    }

    async void AtualizarStatus()
    {
        PintarRodape();
        if (consultando || !IsVisible) return;
        consultando = true;
        try
        {
            status = await Task.Run(() => runner.Status());
            PintarPerfis();
            PintarCabecalho();
            PintarStatusPaineis();
        }
        catch (Exception) { /* perfis.json inválido: o erro aparece ao salvar/recarregar */ }
        finally { consultando = false; }
    }

    void PintarRodape()
    {
        var fora = bandeja.ForaDoAr;
        Ui.PintarPonto(rodapePonto, fora == null ? "noar" : "erro");
        rodapeTxt.Text = fora == null ? "Tela web · :" + Instancia.Porta : "Tela web fora do ar";
        ToolTip.SetTip(rodapeTxt, fora ?? "http://localhost:" + Instancia.Porta + "/");
    }

    StatusPerfil? StatusAtual => status.FirstOrDefault(s => s.Nome == atual);
}
