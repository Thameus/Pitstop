using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Pitstop.App;

/// <summary>
/// Assistente da primeira execução (boas-vindas → ferramentas → preferências → pronto) e, depois, a tela de Ajustes
/// (mesmos campos numa página só). Grava o .env pela <see cref="Ajustes"/>. Nada é obrigatório: JDK, Tomcat, Maven e
/// Node também podem ser informados em cada perfil, na hora de criar.
/// </summary>
sealed class Assistente : Window
{
    static Assistente? aberto;

    readonly Bandeja bandeja;
    readonly bool primeiraVez;
    readonly Dictionary<string, TextBox> campos = new();
    readonly Dictionary<string, TextBlock> checagens = new();
    readonly CheckBox autostart = Ui.Chk("Iniciar o Pitstop junto com o sistema (só o ícone na bandeja)");
    readonly CheckBox offline = Ui.Chk("Maven offline (-o): só com as dependências já baixadas no .m2");
    readonly TextBlock erro = Ui.Paragrafo("", 12, "Err").Also(t => t.IsVisible = false);
    readonly ContentControl pagina = new();
    readonly StackPanel passos = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    readonly Button voltar, avancar;
    int passo;

    static readonly string[] NomesPassos = ["Boas-vindas", "Ferramentas", "Preferências", "Pronto"];

    public static void Abrir(Bandeja bandeja, bool primeiraVez)
    {
        if (aberto != null) { aberto.Activate(); return; }
        aberto = new Assistente(bandeja, primeiraVez);
        aberto.Closed += (_, _) => aberto = null;
        var dono = bandeja.JanelaAtual;
        if (dono is { IsVisible: true }) aberto.Show(dono);
        else aberto.Show();
    }

    Assistente(Bandeja bandeja, bool primeiraVez)
    {
        this.bandeja = bandeja;
        this.primeiraVez = primeiraVez;
        Title = primeiraVez ? "Bem-vindo ao Pitstop" : "Ajustes do Pitstop";
        Icon = Desenho.Icone();
        Width = 760;
        Height = primeiraVez ? 640 : 720;
        MinWidth = 640;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var valores = Ajustes.Ler();
        var sugestoes = primeiraVez ? Ajustes.Sugestoes() : new Dictionary<string, string>();
        foreach (var c in Ajustes.Campos)
        {
            var t = Ui.Inp(c.Padrao != "" ? "vazio = " + c.Padrao : c.Tipo == "pasta" ? "vazio = não definido" : "");
            t.Text = valores.GetValueOrDefault(c.Chave) is { Length: > 0 } v ? v : sugestoes.GetValueOrDefault(c.Chave, "");
            campos[c.Chave] = t;
            var chk = Ui.Txt("", 12, "Mut");
            checagens[c.Chave] = chk;
            t.TextChanged += (_, _) => Checar(c.Chave);
            Checar(c.Chave);
        }
        autostart.IsChecked = primeiraVez || Autostart.Ligado;
        offline.IsChecked = Env.Sim(valores.GetValueOrDefault("MAVEN_OFFLINE"));

        voltar = Ui.Btn("Voltar", "fantasma");
        voltar.Click += (_, _) => Ir(passo - 1);
        avancar = Ui.Btn("Continuar", "pri");
        avancar.IsDefault = true;
        avancar.Click += (_, _) => Avancar();
        var cancelar = Ui.Btn(primeiraVez ? "Pular" : "Cancelar", "fantasma").Dica(primeiraVez ? "Grava tudo vazio; dá para ajustar depois" : "");
        cancelar.Click += (_, _) => { if (primeiraVez) Concluir(abrirNovo: false); else Close(); };

        var rodape = new DockPanel { Margin = new Thickness(32, 0, 32, 24) };
        var dir = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { voltar, avancar } };
        DockPanel.SetDock(dir, Dock.Right);
        rodape.Children.Add(dir);
        rodape.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar } });

        var topo = new DockPanel { Margin = new Thickness(32, 26, 32, 0) };
        var marca = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        marca.Children.Add(new Image { Source = Desenho.Bitmap(64, null), Width = 28, Height = 28 });
        marca.Children.Add(Ui.Txt(primeiraVez ? "Pitstop · primeira configuração" : "Ajustes", 15, "Fg", FontWeight.SemiBold).Also(t => t.VerticalAlignment = VerticalAlignment.Center));
        DockPanel.SetDock(marca, Dock.Left);
        topo.Children.Add(marca);
        if (primeiraVez) { passos.HorizontalAlignment = HorizontalAlignment.Right; topo.Children.Add(passos); }

        var corpo = new DockPanel();
        DockPanel.SetDock(topo, Dock.Top);
        corpo.Children.Add(topo);
        DockPanel.SetDock(rodape, Dock.Bottom);
        corpo.Children.Add(rodape);
        var meio = new StackPanel { Margin = new Thickness(32, 24, 32, 16), Children = { pagina, erro } };
        corpo.Children.Add(new ScrollViewer { Content = meio, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = corpo;
        KeyDown += (_, e) => { if (e.Key == Key.Escape && !primeiraVez) Close(); };

        if (primeiraVez) Ir(0);
        else
        {
            voltar.IsVisible = false;
            avancar.Content = "Salvar";
            pagina.Content = PaginaAjustes();
        }
    }

    // ---------------------------------------------------------------- páginas

    void Ir(int p)
    {
        passo = Math.Clamp(p, 0, NomesPassos.Length - 1);
        erro.IsVisible = false;
        pagina.Content = passo switch { 0 => PaginaBoasVindas(), 1 => PaginaFerramentas(), 2 => PaginaPreferencias(), _ => PaginaPronto() };
        voltar.IsVisible = passo > 0 && passo < 3;
        avancar.Content = passo switch { 2 => "Salvar", 3 => "Criar meu primeiro perfil", _ => "Continuar" };
        passos.Children.Clear();
        for (var i = 0; i < NomesPassos.Length; i++)
        {
            var ativo = i == passo;
            var b = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4), Child = Ui.Txt((i + 1) + "  " + NomesPassos[i], 12, ativo ? "Fg" : "Mut", ativo ? FontWeight.SemiBold : FontWeight.Normal) };
            if (ativo) b.Res(Border.BackgroundProperty, "Soft");
            passos.Children.Add(b);
        }
    }

    void Avancar()
    {
        if (!primeiraVez) { if (Gravar()) { bandeja.JanelaAtual?.AjustesMudaram(); Close(); } return; }
        if (passo == 2) { if (Gravar()) Ir(3); return; }
        if (passo == 3) { Concluir(abrirNovo: true); return; }
        Ir(passo + 1);
    }

    static Control Titulo(string titulo, string texto)
    {
        var sp = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 22) };
        sp.Children.Add(new TextBlock { Text = titulo, FontSize = 26, FontWeight = FontWeight.SemiBold, LetterSpacing = -0.4, TextWrapping = TextWrapping.Wrap }.Res(TextBlock.ForegroundProperty, "Fg"));
        sp.Children.Add(Ui.Paragrafo(texto, 14));
        return sp;
    }

    Control PaginaBoasVindas()
    {
        var sp = new StackPanel();
        sp.Children.Add(Titulo("Bem-vindo ao Pitstop",
            "Sobe e para, com um clique, o que você usaria numa Run Configuration da IDE: Tomcat com os wars dos seus projetos, " +
            "aplicações Java com main, pacotes prontos (.war, .zip, .jar), scripts npm e comandos personalizados. Tudo roda na sua máquina, com log, " +
            "debug e build, pela janela, pela bandeja ou pelo terminal (pit)."));
        sp.Children.Add(Ui.Rotulo("O que você vai precisar (só o que for usar)").Also(t => t.Margin = new Thickness(0, 0, 0, 12)));
        var itens = new (string Icone, string Nome, string Para, string Link, string Url)[]
        {
            ("java", "JDK", "Tomcat, aplicações Java e Maven", "Eclipse Temurin (gratuito)", "https://adoptium.net/temurin/releases/"),
            ("tomcat", "Apache Tomcat", "perfis Tomcat e war", "tomcat.apache.org", "https://tomcat.apache.org/"),
            ("build", "Apache Maven", "build e classpath dos projetos", "maven.apache.org", "https://maven.apache.org/download.cgi"),
            ("npm", "Node.js", "perfis npm", "nodejs.org (LTS)", "https://nodejs.org/"),
        };
        var grade = new StackPanel { Spacing = 8 };
        foreach (var (ic, nome, para, link, url) in itens)
        {
            var linha = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            linha.Children.Add(Ui.IconeTipo(ic).Also(b => b.Margin = new Thickness(0, 0, 14, 0)));
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            tx.Children.Add(Ui.Txt(nome, 14, "Fg", FontWeight.SemiBold));
            tx.Children.Add(Ui.Txt(para, 12, "Mut"));
            Grid.SetColumn(tx, 1);
            linha.Children.Add(tx);
            var l = Ui.Link(link, url);
            l.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(l, 2);
            linha.Children.Add(l);
            grade.Children.Add(Ui.Cartao(linha, 12, new Thickness(14, 10)));
        }
        sp.Children.Add(grade);
        sp.Children.Add(Ui.Paragrafo("Nada disso vem embutido nem é obrigatório agora: cada perfil pede o caminho de que precisa quando você o cria. " +
                                     "No próximo passo dá para deixar caminhos padrão, para não repetir em todo perfil.", 13)
            .Also(t => t.Margin = new Thickness(0, 18, 0, 0)));
        return sp;
    }

    Control CampoAjuste(string chave)
    {
        var c = Ajustes.Campos.First(x => x.Chave == chave);
        var t = campos[chave];
        Control ctl = c.Tipo == "pasta" ? Ui.ComBotoes(t, Ui.Procurar(t, c.Rotulo)) : t;
        var sp = new StackPanel { Spacing = 6 };
        var cab = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        cab.Children.Add(Ui.Txt(c.Rotulo, 12, "Mut", FontWeight.Medium));
        cab.Children.Add(checagens[chave]);
        sp.Children.Add(cab);
        sp.Children.Add(ctl);
        sp.Children.Add(Ui.Paragrafo(c.Dica.Replace("<Pitstop>", Raiz.Dir), 12, "Off"));
        return sp;
    }

    Control PaginaFerramentas()
    {
        var sp = new StackPanel { Spacing = 18 };
        sp.Children.Add(Titulo("Caminhos padrão (opcional)",
            "Valem para todo perfil que não informar outro. Deixe vazio o que não usa." +
            (Ajustes.Sugestoes().Count > 0 ? " Os que já vieram preenchidos foram achados no seu sistema (JAVA_HOME, MAVEN_HOME, CATALINA_HOME)." : "")));
        sp.Children.Add(CampoAjuste("JDK_HOME"));
        sp.Children.Add(CampoAjuste("TOMCAT_HOME"));
        sp.Children.Add(CampoAjuste("MAVEN_HOME"));
        sp.Children.Add(CampoAjuste("NODE_HOME"));
        sp.Children.Add(CampoAjuste("PROJETOS_DIR"));
        return sp;
    }

    Control PaginaPreferencias()
    {
        var sp = new StackPanel { Spacing = 18 };
        sp.Children.Add(Titulo("Preferências", "Dá para mudar tudo depois em Ajustes (engrenagem na lateral ou menu da bandeja)."));
        sp.Children.Add(autostart);
        sp.Children.Add(Ui.Grade(2, CampoAjuste("RUNNER_PORTA"), CampoAjuste("STOP_TIMEOUT_SEG")));
        sp.Children.Add(CampoAjuste("BASES_DIR"));
        sp.Children.Add(CampoAjuste("PACOTES_DIR"));
        sp.Children.Add(CampoAjuste("MAVEN_ARGS"));
        sp.Children.Add(offline);
        return sp;
    }

    Control PaginaPronto()
    {
        var sp = new StackPanel { Spacing = 14 };
        sp.Children.Add(Titulo("Tudo pronto",
            "O Pitstop fica no ícone da bandeja: clique para abrir a janela; o botão direito tem os perfis e as ações. " +
            "Fechar a janela não para nada — use Parar e Sair."));
        var dicas = new[]
        {
            ("tomcat", "Perfil Tomcat", "marque os projetos da pasta de projetos; cada módulo war vira um contexto"),
            ("debug", "Depurar", "sobe com a porta de debug aberta: conecte a IDE (Remote JVM Debug)"),
            ("sync", "Sync", "copia JSP/JS/CSS e classes para o Tomcat no ar, sem rebuild"),
            ("tela", "Terminal e navegador", "pit up <perfil> no terminal; a mesma tela em http://localhost:" + (campos["RUNNER_PORTA"].Text is { Length: > 0 } p ? p : "9999") + "/"),
        };
        foreach (var (ic, t, d) in dicas)
        {
            var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            linha.Children.Add(Ui.IconeTipo(ic));
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            tx.Children.Add(Ui.Txt(t, 14, "Fg", FontWeight.SemiBold));
            tx.Children.Add(Ui.Txt(d, 12, "Mut"));
            linha.Children.Add(tx);
            sp.Children.Add(linha);
        }
        return sp;
    }

    Control PaginaAjustes()
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Bloco("Caminhos padrão", "valem para os perfis que não informarem outro", null,
            CampoAjuste("JDK_HOME"), CampoAjuste("TOMCAT_HOME"), CampoAjuste("MAVEN_HOME"), CampoAjuste("NODE_HOME"),
            CampoAjuste("PROJETOS_DIR"), CampoAjuste("BASES_DIR"), CampoAjuste("PACOTES_DIR")));
        sp.Children.Add(Ui.Bloco("Maven", null, null, CampoAjuste("MAVEN_ARGS"), offline));
        sp.Children.Add(Ui.Bloco("Pitstop", null, null,
            autostart,
            Ui.Grade(2, CampoAjuste("RUNNER_PORTA"), CampoAjuste("STOP_TIMEOUT_SEG")),
            Ui.Paragrafo("Porta e tempo de stop valem a partir da próxima abertura do Pitstop. Arquivo: " + Env.Arquivo, 12, "Off")));
        sp.Children.Add(BlocoAtualizacao());
        return sp;
    }

    Control BlocoAtualizacao()
    {
        ReleasePitstop? release = null;
        var status = Ui.Paragrafo("A verificação só acontece quando você pedir.", 12, "Mut");
        var buscar = Ui.Btn("Buscar atualização", "fantasma");
        var atualizar = Ui.Btn("Atualizar e reiniciar", "pri");
        atualizar.IsVisible = false;

        buscar.Click += async (_, _) =>
        {
            buscar.IsEnabled = false;
            atualizar.IsVisible = false;
            status.Text = "Consultando a versão mais recente...";
            try
            {
                release = await Atualizador.ConsultarAsync();
                if (!release.Nova)
                {
                    status.Text = "Você já está na versão mais recente.";
                    return;
                }

                var tamanho = Atualizador.Tamanho(release.Tamanho);
                status.Text = "Pitstop " + release.VersaoTexto + " disponível" +
                              (tamanho == "" ? "." : " · " + tamanho + ".");
                atualizar.IsVisible = true;
                atualizar.IsEnabled = true;
            }
            catch (Exception ex)
            {
                status.Text = "Não foi possível verificar atualizações: " + ex.Message;
            }
            finally
            {
                buscar.IsEnabled = true;
                buscar.Content = "Buscar novamente";
            }
        };

        atualizar.Click += async (_, _) =>
        {
            if (release == null) return;
            buscar.IsEnabled = false;
            atualizar.IsEnabled = false;
            status.Text = "Baixando Pitstop " + release.VersaoTexto + "...";
            try
            {
                var progresso = new Progress<(long Recebidos, long Total)>(p =>
                {
                    if (p.Total <= 0) return;
                    var pct = Math.Clamp((int)Math.Round(p.Recebidos * 100d / p.Total), 0, 100);
                    status.Text = "Baixando Pitstop " + release.VersaoTexto + "... " + pct + "%";
                });
                var pacote = await Atualizador.BaixarAsync(release, progresso);
                status.Text = "Download validado. Preparando atualização...";
                var iniciou = await bandeja.SairParaAtualizar(() =>
                {
                    Atualizador.CriarBackupMinimo();
                    Atualizador.IniciarInstalador(release, pacote);
                });
                if (!iniciou)
                {
                    status.Text = "Atualização cancelada.";
                    buscar.IsEnabled = true;
                    atualizar.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                status.Text = "A atualização não foi iniciada: " + ex.Message;
                buscar.IsEnabled = true;
                atualizar.IsEnabled = true;
            }
        };

        var botoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { buscar, atualizar } };
        return Ui.Bloco("Atualizações", "consulta manual ao GitHub Releases; nada roda em segundo plano", null,
            Ui.Txt("Versão instalada: " + Atualizador.VersaoAtual, 13, "Fg", FontWeight.SemiBold),
            status,
            botoes);
    }

    // ---------------------------------------------------------------- validação e gravação

    void Checar(string chave)
    {
        var c = Ajustes.Campos.First(x => x.Chave == chave);
        var v = (campos[chave].Text ?? "").Trim();
        var t = checagens[chave];
        string msg = "", cor = "Mut";
        if (v != "" && c.Tipo == "pasta")
        {
            (bool ok, string oque) = chave switch
            {
                "JDK_HOME" => (File.Exists(Path.Combine(v, "bin", So.Exe("java"))), "bin/" + So.Exe("java")),
                "TOMCAT_HOME" => (File.Exists(Path.Combine(v, "bin", So.Catalina)), "bin/" + So.Catalina),
                "MAVEN_HOME" => (File.Exists(Path.Combine(v, "bin", So.Lancador("mvn"))), "bin/" + So.Lancador("mvn")),
                "NODE_HOME" => (File.Exists(Path.Combine(v, So.Exe("node"))) || File.Exists(Path.Combine(v, "bin", So.Exe("node"))), So.Exe("node")),
                _ => (Directory.Exists(v), "pasta"),
            };
            msg = ok ? "✓ " + oque + " encontrado" : Directory.Exists(v) ? "✗ sem " + oque : "✗ pasta não existe";
            cor = ok ? "Ok" : "Err";
        }
        t.Text = msg;
        t.Res(TextBlock.ForegroundProperty, cor);
        campos[chave].Classes.Set("invalido", cor == "Err");
    }

    bool Gravar()
    {
        try
        {
            var v = campos.ToDictionary(kv => kv.Key, kv => (string?)(kv.Value.Text ?? "").Trim());
            v["MAVEN_OFFLINE"] = offline.IsChecked == true ? "true" : "";
            Ajustes.Gravar(v);
            if (autostart.IsChecked != Autostart.Ligado) Autostart.Definir(autostart.IsChecked == true);
            return true;
        }
        catch (Exception e)
        {
            erro.Text = e.Message;
            erro.IsVisible = true;
            return false;
        }
    }

    void Concluir(bool abrirNovo)
    {
        // Pular: cria o .env sem nenhum caminho (o assistente não volta; Ajustes continua à mão)
        if (passo < 3)
        {
            try { Ajustes.Gravar(new Dictionary<string, string?>()); }
            catch (Exception e) { erro.Text = e.Message; erro.IsVisible = true; return; }
        }
        Close();
        bandeja.AbrirJanela();
        if (abrirNovo) bandeja.JanelaAtual?.AjustesMudaram();
    }
}
