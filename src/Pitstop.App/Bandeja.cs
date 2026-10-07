using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Pitstop.App;

/// <summary>
/// Ícone na bandeja + servidor da tela web + janela, no mesmo processo: a bandeja chama o Runner direto. Sair = stop
/// gracioso de cada perfil; no Windows, se o processo morrer de outro jeito, o Job Object derruba os filhos.
/// Sem bandeja no sistema (Linux sem StatusNotifier), a janela continua sendo a porta de entrada: abrir o Pitstop de
/// novo traz a janela, e o menu ⋯ da janela tem Parar e Sair.
/// </summary>
sealed class Bandeja
{
    const string Titulo = "Pitstop";

    readonly App app;
    readonly IClassicDesktopStyleApplicationLifetime vida;
    readonly Runner runner = Instancia.Runner;
    readonly Servidor servidor;
    readonly TrayIcon icone;
    readonly DispatcherTimer tique = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly Dictionary<string, (long? Pronto, long? Fim)> ant = new();
    readonly List<IDisposable> sinais = [];
    Janela? janela;

    List<StatusPerfil> lista = [];
    bool online, saindo, consultando;
    string? foraDoAr;
    string estadoGeral = "ocupado";
    string assinaturaMenu = "";

    static readonly string[] Prioridade = ["parado", "externo", "noar", "debug", "portaocupada", "ocupado"];

    public Bandeja(App app, IClassicDesktopStyleApplicationLifetime vida)
    {
        this.app = app;
        this.vida = vida;
        servidor = new Servidor(runner, Instancia.Porta)
        {
            // /api/sair vindo de fora (tela web, script): perfis já parados pelo servidor; aqui só some
            Encerrar = () => Dispatcher.UIThread.Post(Finalizar),
            MostrarTela = () => Dispatcher.UIThread.Post(AbrirJanela),   // segunda instância do Pitstop
        };
        icone = new TrayIcon { Icon = Desenho.Icone("ocupado"), ToolTipText = Titulo + " — iniciando…", Menu = new NativeMenu(), IsVisible = true };
        icone.Clicked += (_, _) => AbrirJanela();
        TrayIcon.SetIcons(app, [icone]);
        Notificacao.AoClicar = AbrirJanela;
    }

    public void Iniciar()
    {
        Autostart.Corrigir();
        MontarMenu();
        tique.Tick += (_, _) => Poll();
        tique.Start();
        // SIGTERM (logout, systemctl, kill) e SIGHUP: para os perfis antes de sair
        foreach (var s in new[] { PosixSignal.SIGTERM, PosixSignal.SIGHUP })
            sinais.Add(PosixSignalRegistration.Create(s, c => { c.Cancel = true; Dispatcher.UIThread.Post(() => _ = SairSemPerguntar()); }));
        _ = Subir();
        if (Ajustes.PrimeiraVez) Assistente.Abrir(this, primeiraVez: true);
        else if (!Instancia.Auto) AbrirJanela();
    }

    // ---------------------------------------------------------------- servidor

    async Task Subir()
    {
        try
        {
            if (await servidor.Iniciar())
            {
                online = true;
                await SubirAutomaticos();
                Poll();
                return;
            }
            foraDoAr = "porta " + Instancia.Porta + " já em uso por outro programa";
        }
        catch (Exception ex)
        {
            Registro.Erro(ex);
            foraDoAr = "servidor não subiu: " + ex.Message;
        }
        // sem servidor a janela funciona (chama o Runner direto); só a tela web e a 2ª instância ficam sem
        online = true;
        Notificacao.Mostrar("Tela web fora do ar", foraDoAr + ". A janela funciona; mude a porta em Ajustes.", "erro");
        await SubirAutomaticos();
        Poll();
    }

    async Task SubirAutomaticos()
    {
        try { await runner.IniciarAutomaticos(); }
        catch (Exception ex)
        {
            Registro.Erro(ex);
            Notificacao.Mostrar("Início automático", "Não consegui iniciar os perfis automáticos: " + ex.Message, "erro");
        }
    }

    public string? ForaDoAr => foraDoAr;

    public void AbrirJanela()
    {
        if (saindo) return;
        janela ??= new Janela(runner, this);
        janela.Trazer();
    }

    public Janela? JanelaAtual => janela;

    // ---------------------------------------------------------------- estado

    public static string EstadoPerfil(StatusPerfil p)
    {
        if (p.Execucao is { } e)
        {
            if (e.Tipo == "build" || e.Pronto == null) return "ocupado";
            return e.Tipo == "debug" ? "debug" : "noar";
        }
        return p.EmUso ? (p.Tipo is "java" or "comando" ? "externo" : "portaocupada") : "parado";
    }

    static string TextoEstado(StatusPerfil p)
    {
        if (p.Execucao is { } e)
        {
            if (e.Tipo == "build") return "build " + Agora.Duracao(Agora.Ms - e.Desde);
            if (e.Tipo == "reinicio") return "reiniciando";
            if (e.Pronto == null) return e.Tipo == "debug" ? "subindo (debug)" : "subindo";
            return e.Tipo == "debug" ? "debug :" + p.PortaDebug : "no ar";
        }
        if (p.EmUso) return p.Tipo is "java" or "comando" ? "executando externamente" : "porta " + p.Porta + " ocupada";
        return "parado";
    }

    async void Poll()
    {
        if (consultando || saindo || !online) return;
        consultando = true;
        try { Atualizar(await Task.Run(() => runner.Status())); }
        catch (Exception ex)
        {
            Registro.Erro(ex);
            Mostrar("erro", Titulo + " — erro lendo perfis (" + ex.Message + ")");
        }
        finally { consultando = false; }
    }

    void Mostrar(string estado, string dica)
    {
        if (estado != estadoGeral) icone.Icon = Desenho.Icone(estado);
        estadoGeral = estado;
        icone.ToolTipText = dica.Length > 120 ? dica[..117] + "…" : dica;
    }

    void DetectarTransicao(StatusPerfil p)
    {
        var pronto = p.Execucao?.Pronto;
        var fim = p.Ultimo?.Fim;
        if (ant.TryGetValue(p.Nome, out var a))
        {
            if (pronto != null && pronto != a.Pronto)
                Notificacao.Mostrar(p.Nome + " no ar", "subiu em " + Agora.Duracao(pronto.Value - p.Execucao!.Desde) + (p.Url != null ? " · " + p.Url : ""),
                    p.Execucao.Tipo == "debug" ? "debug" : "noar");
            if (fim != null && fim != a.Fim && p.Ultimo is { } u)
            {
                if (u.Tipo == "build")
                {
                    if (u.Ok) Notificacao.Mostrar(p.Nome + " · build ok", "em " + Agora.Duracao(u.Duracao));
                    else if (u.Cancelado == true) Notificacao.Mostrar(p.Nome + " · build cancelado", u.Erro, "ocupado");
                    else Notificacao.Mostrar(p.Nome + " · build FALHOU", u.Erro, "erro");
                }
                else if (u.Parado != true)
                    Notificacao.Mostrar(p.Nome + " caiu", u.Tipo + " terminou com código " + u.Codigo + " após " + Agora.Duracao(u.Duracao), "erro");
            }
        }
        ant[p.Nome] = (pronto, fim);
    }

    void Atualizar(List<StatusPerfil> l)
    {
        if (saindo) return;
        lista = l;
        var geral = "parado";
        var linhas = new List<string> { Titulo };
        foreach (var p in l)
        {
            DetectarTransicao(p);
            var est = EstadoPerfil(p);
            if (Array.IndexOf(Prioridade, est) > Array.IndexOf(Prioridade, geral)) geral = est;
            linhas.Add(p.Nome + ": " + TextoEstado(p));
        }
        if (l.Count == 0) linhas.Add("nenhum perfil ainda");
        Mostrar(geral, string.Join("\n", linhas));
        MontarMenu();
    }

    // ---------------------------------------------------------------- ações

    /// <summary>Ação de perfil em segundo plano (stop espera até STOP_TIMEOUT_SEG): erro vira aviso.</summary>
    async void Acao(string nome, string acao)
    {
        try
        {
            var r = await Task.Run(() => runner.Acao(nome, acao));
            if (acao == "sync") Notificacao.Mostrar(nome + " · sync", r.Copiados + " arquivo(s) copiado(s)");
        }
        catch (Exception ex) { Notificacao.Mostrar(nome + " · " + acao, ex.Message, "erro"); }
        Poll();
    }

    // ---------------------------------------------------------------- menu (refeito quando o estado muda)

    static readonly Dictionary<string, Bitmap> Pontos = new();

    static Bitmap Ponto(string estado)
    {
        if (Pontos.TryGetValue(estado, out var b)) return b;
        var bmp = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));
        using (var g = bmp.CreateDrawingContext())
        {
            var cor = Desenho.Selos.TryGetValue(estado, out var c) ? c : Color.Parse("#8E879B");
            g.DrawEllipse(new SolidColorBrush(cor), null, new Point(16, 16), 8, 8);
        }
        Pontos[estado] = bmp;
        return bmp;
    }

    static NativeMenuItem Item(string texto, bool ativo, Action? clique)
    {
        var mi = new NativeMenuItem(texto) { IsEnabled = ativo };
        if (clique != null) mi.Click += (_, _) => clique();
        return mi;
    }

    void MontarMenu()
    {
        var assinatura = online + "|" + foraDoAr + "|" + string.Join(";", lista.Select(p => p.Nome + ":" + TextoEstado(p) + ":" + p.EmUso))
                         + "|" + Autostart.Ligado;
        if (assinatura == assinaturaMenu) return;
        assinaturaMenu = assinatura;

        var m = new NativeMenu();
        m.Items.Add(Item("Abrir Pitstop", true, AbrirJanela));
        m.Items.Add(new NativeMenuItemSeparator());
        if (!online) m.Items.Add(Item("Iniciando…", false, null));
        else if (lista.Count == 0) m.Items.Add(Item("Nenhum perfil: crie na janela", false, null));
        else foreach (var p in lista) m.Items.Add(ItemPerfil(p));
        m.Items.Add(new NativeMenuItemSeparator());
        m.Items.Add(Item("Ajustes…", true, () => Assistente.Abrir(this, primeiraVez: false)));
        var auto = new NativeMenuItem(So.Windows ? "Iniciar com o Windows" : "Iniciar com a sessão")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = Autostart.Ligado,
        };
        auto.Click += (_, _) =>
        {
            try { Autostart.Definir(!Autostart.Ligado); }
            catch (Exception ex) { Registro.Erro(ex); Notificacao.Mostrar(Titulo, ex.Message, "erro"); }
            MontarMenu();
        };
        m.Items.Add(auto);
        m.Items.Add(Item("Parar e Sair", true, () => _ = Sair()));
        icone.Menu = m;
    }

    /// <summary>Perfil com o estado no texto; as ações ficam no submenu, habilitadas conforme o estado.</summary>
    NativeMenuItem ItemPerfil(StatusPerfil p)
    {
        var e = p.Execucao;
        var livre = e == null && !p.EmUso;
        var build = e is { Tipo: "build" };
        var nome = p.Nome;
        var java = p.Tipo == "java";
        var npm = p.Tipo == "npm";
        var comando = p.Tipo == "comando";
        var item = new NativeMenuItem(nome + "  —  " + TextoEstado(p)) { Icon = Ponto(EstadoPerfil(p)) };
        var sub = new NativeMenu();
        sub.Items.Add(Item("Iniciar", livre, () => Acao(nome, "start")));
        if (!npm && !comando) sub.Items.Add(Item("Depurar", livre, () => Acao(nome, "debug")));
        sub.Items.Add(Item(build ? "Cancelar build" : "Parar", e != null || p.EmUso, () => Acao(nome, "stop")));
        var reiniciavel = (e != null && e.Tipo is not ("build" or "reinicio")) || (e == null && p.EmUso);
        sub.Items.Add(Item("Reiniciar", reiniciavel, () => Acao(nome, "reiniciar")));
        sub.Items.Add(new NativeMenuItemSeparator());
        if (!comando)
            sub.Items.Add(Item(build ? "Build (em andamento)" : livre ? "Build" : "Build (pare antes)", livre, () => Acao(nome, "build")));
        if (!java)
        {
            if (!npm && !comando && p.Pacote == null) sub.Items.Add(Item("Sync (estáticos + classes)", !build, () => Acao(nome, "sync")));
            var url = p.Url;
            if (!comando || url != null)
                sub.Items.Add(Item("Abrir no navegador", p.EmUso && url != null, () => Proc.AbrirUrl(url!)));
            if (comando)
                sub.Items.Add(Item("Abrir terminal", true, () => Acao(nome, "terminal")));
        }
        item.Menu = sub;
        return item;
    }

    // ---------------------------------------------------------------- saída

    /// <summary>Pergunta (se houver perfil no ar), para cada perfil com stop gracioso e só então sai.</summary>
    public async Task Sair()
    {
        if (saindo) return;
        var rodando = runner.EmExecucao();
        if (rodando.Length > 0)
        {
            AbrirJanela();
            var ok = await Dialogo.Confirmar(janela!, "Parar e sair?", "Vai parar " + string.Join(", ", rodando) + " e fechar o Pitstop.",
                "Parar e sair", () => { }, "sair");
            if (!ok) return;
        }
        await SairSemPerguntar();
    }

    /// <summary>
    /// Atualização: confirma a parada dos perfis, inicia o processo externo (que espera este PID terminar)
    /// e encerra o Pitstop pelo mesmo caminho gracioso do menu Parar e Sair.
    /// </summary>
    public async Task<bool> SairParaAtualizar(Action iniciarAtualizador)
    {
        if (saindo) return false;
        var rodando = runner.EmExecucao();
        if (rodando.Length > 0)
        {
            AbrirJanela();
            var ok = await Dialogo.Confirmar(janela!, "Parar e atualizar?",
                "Vai parar " + string.Join(", ", rodando) + ", atualizar o Pitstop e abrir novamente.",
                "Atualizar", () => { }, "sync");
            if (!ok) return false;
        }

        iniciarAtualizador();
        await SairSemPerguntar();
        return true;
    }

    async Task SairSemPerguntar()
    {
        if (saindo) return;
        saindo = true;
        Mostrar("ocupado", Titulo + " — encerrando…");
        runner.IniciarSaida();
        try { await Task.Run(() => runner.PararTodos()); }
        catch (Exception ex) { Registro.Erro(ex); }
        Finalizar();
    }

    void Finalizar()
    {
        saindo = true;
        tique.Stop();
        foreach (var s in sinais) s.Dispose();
        icone.IsVisible = false;
        icone.Dispose();
        runner.MatarTodos();   // o que não parou no prazo (normalmente nada)
        janela?.FecharDeVez();
        vida.Shutdown();
    }
}
