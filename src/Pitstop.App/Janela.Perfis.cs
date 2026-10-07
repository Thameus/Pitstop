using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Pitstop.App;

sealed partial class Janela
{
    static readonly (string Tipo, string Icone, string Nome, string Desc, string TituloNovo)[] Tipos =
    [
        ("tomcat", "tomcat", "Tomcat", "WARs explodidos dos projetos Maven, com Build e Sync", "Novo perfil Tomcat"),
        ("java", "java", "Aplicação Java", "classe main de um módulo Maven (desktop, serviço)", "Nova aplicação Java"),
        ("war", "war", "Tomcat (war)", "WAR pronto de uma release, sem compilar", "Novo Tomcat com war pronto"),
        ("zip", "zip", "Pacote Java", ".zip ou .jar pronto, java -jar", "Novo pacote Java"),
        ("npm", "npm", "npm", "script do package.json (dev server)", "Novo perfil npm"),
        ("comando", "terminal", "Comando", "comando ou processo local genérico", "Novo perfil Comando"),
    ];

    static readonly Dictionary<string, string> NomeTipo = new()
    {
        ["tomcat"] = "Tomcat", ["java"] = "app Java", ["npm"] = "npm", ["war"] = "Tomcat · WAR pronto", ["zip"] = "pacote Java",
        ["comando"] = "Comando",
    };

    JsonObject Perfis => cfg["perfis"] as JsonObject ?? throw new ErroRunner("config inválida: falta \"perfis\"");
    JsonObject? PerfilCfg(string n) => (cfg["perfis"] as JsonObject)?[n] as JsonObject;
    static string TipoDe(JsonObject? p) => JsonAux.Txt(p, "tipo") is { } t && NomeTipo.ContainsKey(t) ? t : "tomcat";
    string TipoAtual => TipoDe(PerfilCfg(atual));

    static string Base(string? p) => (p ?? "").Split('\\', '/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
    /// <summary>.../v1.2.0/app.war -> v1.2.0</summary>
    static string VersaoDe(string? f)
    {
        var partes = (f ?? "").Split('\\', '/', StringSplitOptions.RemoveEmptyEntries);
        return partes.Length >= 2 ? partes[^2] : "";
    }

    // ---------------------------------------------------------------- carga e troca de perfil

    void Carregar(string? sel)
    {
        try { cfg = Config.Ler(); }
        catch (Exception e) { Aviso("não li o perfis.json: " + e.Message, "err"); return; }
        var nomes = Config.NomesPerfis(cfg).ToList();
        sujo = false;
        PintarSujo();
        itensPerfil.Clear();   // lista pode ter mudado (novo, excluído, renomeado)
        if (nomes.Count == 0)
        {
            atual = "";
            corpo.IsVisible = false;
            vazio.IsVisible = true;
            PintarPerfis();
            return;
        }
        corpo.IsVisible = true;
        vazio.IsVisible = false;
        atual = sel != null && nomes.Contains(sel) ? sel : nomes[0];
        prefs.Perfil = atual;
        Prefs.Gravar();
        PintarPerfis();
        Preencher();
        ConectarLog();
        AtualizarStatus();
    }

    void Recarregar()
    {
        Carregar(atual);
        Aviso("recarregado do disco", "on");
    }

    void Selecionar(string n)
    {
        if (n == atual) return;
        if (sujo)
        {
            try { Salvar(); }
            catch (Exception e) { Aviso("não salvei " + atual + ": " + e.Message, "err"); return; }
        }
        atual = n;
        prefs.Perfil = n;
        Prefs.Gravar();
        sujo = false;
        PintarSujo();
        PintarPerfis();
        Preencher();
        ConectarLog();
        AtualizarStatus();
    }

    // ---------------------------------------------------------------- lateral

    void PintarPerfis()
    {
        var nomes = Config.NomesPerfis(cfg).ToList();
        if (!nomes.SequenceEqual(itensPerfil.Keys))
        {
            listaPerfis.Children.Clear();
            itensPerfil.Clear();
            if (nomes.Count == 0)
                listaPerfis.Children.Add(Ui.Paragrafo("Nenhum perfil ainda. Use o + acima.", 12).Also(t => t.Margin = new Thickness(12, 8)));
            foreach (var n in nomes)
            {
                var ponto = Ui.Ponto(8);
                var meta = Ui.Txt("", 12, "Mut");
                var est = Ui.Txt("", 12, "Mut");
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
                ponto.Margin = new Thickness(0, 0, 12, 0);
                g.Children.Add(ponto);
                var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
                txt.Children.Add(Ui.Txt(n, 14, "Fg", FontWeight.SemiBold));
                txt.Children.Add(meta);
                Grid.SetColumn(txt, 1);
                g.Children.Add(txt);
                est.VerticalAlignment = VerticalAlignment.Center;
                est.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(est, 2);
                g.Children.Add(est);
                var b = new Button { Content = g };
                b.Classes.Add("perfil");
                var nome = n;
                b.Click += (_, _) => Selecionar(nome);
                listaPerfis.Children.Add(b);
                itensPerfil[n] = (b, ponto, meta, est);
            }
        }
        foreach (var (n, (b, ponto, meta, estTxt)) in itensPerfil)
        {
            var p = PerfilCfg(n);
            var t = TipoDe(p);
            var est = EstadoDe(status.FirstOrDefault(s => s.Nome == n));
            Ui.PintarPonto(ponto, est.K);
            meta.Text = t switch
            {
                "java" => "app Java",
                "npm" => "npm · :" + (JsonAux.Num(p, "porta") ?? 4200),
                "comando" => "comando" + (JsonAux.Num(p, "porta") is int cp ? " · :" + cp : ""),
                "zip" => "pacote · " + (VersaoDe(JsonAux.Txt(p, "pacote")) is { Length: > 0 } v ? v : "Java"),
                _ => (t == "war" ? "war" : "Tomcat") + " · :" + (JsonAux.Num(p, "porta") ?? 8080),
            };
            estTxt.Text = est.Txt;
            b.Classes.Set("sel", n == atual);
        }
    }

    // ---------------------------------------------------------------- cabeçalho

    void PintarCabecalho()
    {
        var p = PerfilCfg(atual);
        if (p == null) return;
        var st = StatusAtual;
        var est = EstadoDe(st);
        var tipo = TipoAtual;
        nomeTxt.Text = atual;
        tipoTxt.Text = NomeTipo[tipo];
        Ui.PintarPonto(pillPonto, est.K);
        pillTxt.Text = est.Pill;
        var pacote = JsonAux.Txt(p, "pacote");
        alvoTxt.Text = tipo switch
        {
            "java" => JsonAux.Txt(p, "mainClass") ?? "sem classe main",
            "zip" => pacote != null ? Base(pacote) + (VersaoDe(pacote) is { Length: > 0 } v ? " · " + v : "") : "sem pacote",
            "npm" => "npm run " + (JsonAux.Txt(p, "script") ?? "start") + (JsonAux.Txt(p, "pasta") is { } pa ? " · " + Base(pa) : ""),
            "comando" => (JsonAux.Txt(p, "comando") ?? "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "sem comando",
            _ => JsonAux.Txt(p, "url") ?? st?.Url ?? "",
        };

        // uma primária por estado (design visual do Pitstop § Componentes)
        var livre = !est.Rodando && !est.Build;
        Ui.Mostrar(iniciarBtn, livre);
        Ui.Mostrar(depurarBtn, livre && tipo is not ("npm" or "comando"));
        Ui.Mostrar(pararBtn, !livre);
        Ui.Mostrar(reiniciarBtn, est.Rodando && !est.Reinicio);
        Ui.Mostrar(buildBtn, tipo != "comando");
        Ui.Mostrar(syncBtn, tipo == "tomcat");
        Ui.Mostrar(abrirBtn, (tipo is "tomcat" or "npm" or "war") || (tipo == "comando" && JsonAux.Txt(p, "url") != null));
        Ui.Mostrar(terminalBtn, tipo == "comando");
        pararTxt.Text = est.Build ? "Cancelar build" : "Parar";
        var buildNpm = JsonAux.Txt(p, "build");
        if (!ocupado)
        {
            buildBtn.IsEnabled = livre && !(tipo == "npm" && buildNpm == null);
            syncBtn.IsEnabled = !est.Build;
            abrirBtn.IsEnabled = est.Rodando;
        }
        ToolTip.SetTip(buildBtn, est.Build ? "Build em andamento"
            : !livre ? "Pare o perfil antes do build (os arquivos estão em uso)"
            : tipo == "java" ? "Regera o classpath e compila a cadeia inteira"
            : tipo is "war" or "zip" ? "Extrai o pacote de novo (descarta o que o app gravou na extração)"
            : tipo == "npm" ? (buildNpm != null ? buildNpm + " (em " + Base(JsonAux.Txt(p, "pasta")) + ")" : "Sem comando de build (aba Projeto)")
            : "Build dos artefatos ativos");

        var u = st?.Ultimo is { Tipo: "build" } ub ? ub : null;
        var ultimo = u == null ? "—" : u.Ok ? "ok · " + Dur(u.Duracao) : u.Cancelado == true ? "cancelado" : "falhou";
        string Num(string k, int padrao) => (JsonAux.Num(p, k) ?? padrao).ToString();
        var porta = JsonAux.Num(p, "porta") ?? 8080;
        var java = JsonAux.Txt(p, "javaHome") is { } jh ? Base(jh) : JsonAux.Txt(cfg, "javaHome") is { } jg ? Base(jg) : "JAVA_HOME";
        (string, string)[] linhas = tipo switch
        {
            "java" => [("Debug", Num("portaDebug", 5006)), ("Java", java), ("Módulo", Base(JsonAux.Txt(p, "modulo")) is { Length: > 0 } m ? m : "—"), ("Última compilação", ultimo)],
            "zip" => [("Debug", Num("portaDebug", 5006)), ("Java", java), ("Versão", VersaoDe(pacote) is { Length: > 0 } vz ? vz : "—"), ("Última extração", ultimo)],
            "war" => [("HTTP", Num("porta", 8080)), ("Debug", Num("portaDebug", 5005)), ("Versão", VersoesWar(p)), ("Última extração", ultimo)],
            "npm" => [("HTTP", Num("porta", 4200)), ("Script", JsonAux.Txt(p, "script") ?? "start"), ("Node", JsonAux.Txt(p, "nodeHome") is { } nh ? Base(nh) : "PATH"), ("Último build", ultimo)],
            "comando" => [("Pasta", Base(st?.Pasta) is { Length: > 0 } pcwd ? pcwd : "HOME"), ("Shell", JsonAux.Txt(p, "shell") ?? "auto"), ("Porta", JsonAux.Num(p, "porta")?.ToString() ?? "—"), ("Última execução", UltimoComando(st?.Ultimo))],
            _ => [("HTTP", porta.ToString()), ("Shutdown", Num("portaShutdown", porta - 75)), ("Debug", Num("portaDebug", 5005)), ("Último build", ultimo)],
        };
        PintarStats(linhas);
    }

    static string UltimoComando(Ultimo? u) => u == null ? "—"
        : u.Motivo == "timeout" ? "timeout"
        : u.Motivo == "usuario" ? "interrompida"
        : u.Ok ? "ok · " + Dur(u.Duracao)
        : "falhou" + (u.Codigo is int c ? " · " + c : "");

    static string VersoesWar(JsonObject p)
    {
        var l = (p["artefatos"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Where(a => JsonAux.NaoFalso(a, "ativo")).Select(a => VersaoDe(JsonAux.Txt(a, "war"))).Where(v => v != "").Distinct().ToList();
        return l.Count > 0 ? string.Join(", ", l) : "—";
    }

    void PintarStats((string Rotulo, string Valor)[] linhas)
    {
        if (stats.Children.Count != linhas.Length)
        {
            stats.Children.Clear();
            foreach (var _ in linhas)
            {
                var sp = new StackPanel { Spacing = 6 };
                sp.Children.Add(Ui.Txt("", 12, "Mut"));
                sp.Children.Add(Ui.Txt("", 18, "Fg", FontWeight.Medium, mono: true));
                var c = Ui.Cartao(sp, 12, new Thickness(16, 14));
                c.Margin = new Thickness(6, 0);
                stats.Children.Add(c);
            }
        }
        for (var i = 0; i < linhas.Length; i++)
        {
            var sp = (StackPanel)((Border)stats.Children[i]).Child!;
            ((TextBlock)sp.Children[0]).Text = linhas[i].Rotulo;
            var v = (TextBlock)sp.Children[1];
            v.Text = linhas[i].Valor;
            ToolTip.SetTip(v, linhas[i].Valor);
        }
    }

    // ---------------------------------------------------------------- ações

    static readonly Dictionary<string, string> NomeAcao = new()
    {
        ["start"] = "iniciar", ["debug"] = "depurar", ["stop"] = "parar", ["reiniciar"] = "reiniciar",
        ["build"] = "build", ["sync"] = "sync", ["abrir"] = "abrir", ["terminal"] = "abrir terminal",
    };

    async void Executar(string a)
    {
        if (ocupado || atual == "") return;
        ocupado = true;
        Aviso(NomeAcao[a] + "…", "busy");
        var nome = atual;
        var tipo = TipoAtual;
        try
        {
            if (a is not ("abrir" or "stop")) Salvar();
            var r = await Task.Run(() => runner.Acao(nome, a));
            var pacote = tipo is "war" or "zip";
            Aviso(a switch
            {
                "sync" => r.Copiados + " arquivo(s) copiado(s)",
                "build" => pacote ? "extraindo o pacote de novo — acompanhe no log" : "build iniciado — acompanhe no log",
                "reiniciar" => "reiniciando — para e sobe no mesmo modo (acompanhe no log)",
                "start" or "debug" when pacote => "subindo — extrai o pacote se preciso (acompanhe no log)",
                "start" or "debug" when tipo == "java" => "pedido: compila o que mudou e sobe — acompanhe no log",
                "start" when tipo == "npm" => "npm subindo — fica no ar quando compilar (acompanhe no log)",
                _ => NomeAcao[a] + ": ok",
            }, "on");
            if (a is "build" or "start" or "debug" or "reiniciar") EscolherAba("log");
        }
        catch (Exception e) { Aviso(e.Message, "err"); }
        ocupado = false;
        AtualizarStatus();
    }

    // ---------------------------------------------------------------- salvar

    void MarcarSujo()
    {
        if (preenchendo || atual == "") return;
        var alterado = true;
        try { alterado = !JsonNode.DeepEquals(Coletar(), PerfilCfg(atual)); }
        catch { /* enquanto o usuário digita um valor incompleto, continua pendente */ }
        if (sujo == alterado) return;
        sujo = alterado;
        PintarSujo();
    }

    void PintarSujo() => Ui.Mostrar(salvarBtn, sujo);

    /// <summary>Grava o perfil atual (validação + conferência de versão do perfis.json). Erro = exceção.</summary>
    void Salvar()
    {
        if (atual == "") return;
        var p = Coletar();
        Validar(p);
        Perfis[atual] = p;
        cfg["_versao"] = Config.Gravar(cfg);   // a próxima gravação desta janela passa na conferência
        DepoisDeSalvar(p);
        sujo = false;
        PintarSujo();
        PintarPerfis();
        PintarCabecalho();
    }

    void SalvarComAviso()
    {
        try { Salvar(); Aviso("salvo", "on"); }
        catch (Exception e) { Aviso(e.Message, "err"); }
    }

    // ---------------------------------------------------------------- criar, renomear, duplicar, excluir

    string ValidarNome(string n) =>
        !Config.NomeValido(n) ? "Use até 64 letras/números e - . _; não comece/termine com ponto"
        : PerfilCfg(n) != null ? "Já existe um perfil " + n : "";

    static string IconeDe(string tipo) => Tipos.First(t => t.Tipo == tipo).Icone;

    async Task<T> ComVeu<T>(Func<Task<T>> f)
    {
        veu.IsVisible = true;
        try { return await f(); }
        finally { veu.IsVisible = false; }
    }

    /// <summary>Primeira porta livre a partir de <paramref name="inicio"/>, de 10 em 10, entre as que os perfis já usam.</summary>
    int PortaLivre(string chave, int inicio)
    {
        var usadas = (cfg["perfis"] as JsonObject ?? new JsonObject()).Select(kv => kv.Value as JsonObject)
            .SelectMany(p => new[] { "porta", "portaDebug", "portaShutdown", "portaJmx", "portaAjp" }.Select(k => JsonAux.Num(p, k)))
            .OfType<int>().ToHashSet();
        var p = inicio;
        while (usadas.Contains(p) || usadas.Contains(p - 75)) p += 10;
        return p;
    }

    JsonObject NovoPerfil(string tipo) => tipo switch
    {
        "java" => new() { ["tipo"] = "java", ["portaDebug"] = PortaLivre("portaDebug", 5006), ["compilarAntes"] = true },
        "npm" => new() { ["tipo"] = "npm", ["script"] = "start", ["porta"] = PortaLivre("porta", 4200), ["build"] = "npm run build" },
        "comando" => new() { ["tipo"] = "comando", ["shell"] = "auto" },
        "war" => new() { ["tipo"] = "war", ["porta"] = PortaLivre("porta", 8080), ["portaDebug"] = PortaLivre("portaDebug", 5005), ["artefatos"] = new JsonArray() },
        "zip" => new() { ["tipo"] = "zip", ["portaDebug"] = PortaLivre("portaDebug", 5006) },
        _ => new() { ["porta"] = PortaLivre("porta", 8080), ["portaDebug"] = PortaLivre("portaDebug", 5005), ["projetos"] = new JsonArray(), ["artefatos"] = new JsonArray() },
    };

    // ---- caminhos pedidos ao criar (cada tipo pede só o que precisa; vazio = o padrão de Ajustes)

    string? Global(string k) => JsonAux.Txt(cfg, k);
    bool TemJavaPadrao => Global("javaHome") != null || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JAVA_HOME"));

    CampoDialogo CampoJava(string rotulo = "JDK (JAVA_HOME)") => new("javaHome", rotulo, "",
        Global("javaHome") is { } j ? "vazio = " + j : TemJavaPadrao ? "vazio = JAVA_HOME do sistema" : "pasta do JDK (com bin/java)",
        "Sem JDK? Eclipse Temurin é gratuito: adoptium.net", Pasta: true, Obrigatorio: !TemJavaPadrao);

    List<CampoDialogo> CamposNovo(string tipo) => tipo switch
    {
        "tomcat" =>
        [
            new("tomcatHome", "Tomcat (CATALINA_HOME)", "", Global("tomcatHome") is { } t ? "vazio = " + t : "pasta do Tomcat (com bin/" + So.Catalina + ")",
                "Sem Tomcat? Baixe o zip/tar.gz em tomcat.apache.org e descompacte", Pasta: true, Obrigatorio: Global("tomcatHome") == null),
            CampoJava(),
            new("projetosDir", "Pasta dos projetos", "", Global("projetosDir") is { } d ? "vazio = " + d : "pasta com os repositórios Maven",
                null, Pasta: true, Obrigatorio: Global("projetosDir") == null),
        ],
        "war" =>
        [
            new("tomcatHome", "Tomcat (CATALINA_HOME)", "", Global("tomcatHome") is { } t2 ? "vazio = " + t2 : "pasta do Tomcat",
                null, Pasta: true, Obrigatorio: Global("tomcatHome") == null),
            CampoJava(),
        ],
        "java" =>
        [
            new("modulo", "Módulo Maven", "", "pasta com o pom.xml da aplicação", null, Pasta: true, Obrigatorio: true),
            CampoJava("Java (JAVA_HOME)"),
        ],
        "zip" =>
        [
            new("pacote", "Pacote (.zip ou .jar)", "", "arquivo do release", null, Arquivo: true, Extensoes: [".zip", ".jar"], Obrigatorio: true),
            CampoJava("Java (JAVA_HOME)"),
        ],
        "npm" =>
        [
            new("pasta", "Pasta do projeto", "", "pasta com o package.json", null, Pasta: true, Obrigatorio: true),
            new("nodeHome", "Node.js", "", "vazio = node do PATH", "Sem Node? nodejs.org (versão LTS)", Pasta: true),
        ],
        "comando" =>
        [
            new("comando", "Comando inicial", "", "ex.: dotnet run", "Depois você pode usar várias linhas na aba Comando.", Obrigatorio: true),
            new("pasta", "Pasta de execução", "", Global("projetosDir") is { } d ? "vazio = " + d : "vazio = HOME do usuário", null, Pasta: true),
        ],
        _ => [],
    };

    /// <summary>Confere os caminhos informados; vazio passa (usa o padrão).</summary>
    static void ValidarCaminhos(string tipo, IReadOnlyDictionary<string, string> v)
    {
        string? V(string k) => v.TryGetValue(k, out var s) && s != "" ? s : null;
        if (V("tomcatHome") is { } t && !File.Exists(Path.Combine(t, "bin", So.Catalina)))
            throw new ErroRunner("não achei bin/" + So.Catalina + " em " + t);
        if (V("javaHome") is { } j && !File.Exists(Path.Combine(j, "bin", So.Exe("java"))))
            throw new ErroRunner("não achei bin/" + So.Exe("java") + " em " + j);
        if (V("projetosDir") is { } d && !Directory.Exists(d)) throw new ErroRunner("pasta não existe: " + d);
        if (V("modulo") is { } m && !File.Exists(Path.Combine(m, "pom.xml"))) throw new ErroRunner("sem pom.xml em " + m);
        if (V("pacote") is { } pc && !File.Exists(pc)) throw new ErroRunner("arquivo não existe: " + pc);
        if (V("pasta") is { } pa)
        {
            if (tipo == "npm" && !File.Exists(Path.Combine(pa, "package.json"))) throw new ErroRunner("sem package.json em " + pa);
            if (tipo == "comando" && !Directory.Exists(pa)) throw new ErroRunner("pasta não existe: " + pa);
        }
        if (V("nodeHome") is { } n && !File.Exists(Path.Combine(n, So.Exe("node"))) && !File.Exists(Path.Combine(n, "bin", So.Exe("node"))))
            throw new ErroRunner("não achei " + So.Exe("node") + " em " + n);
    }

    async Task Criar(JsonObject p, string? titulo = null, string sugestao = "", bool pedirCaminhos = true)
    {
        var tipo = TipoDe(p);
        var extras = pedirCaminhos ? CamposNovo(tipo) : [];
        var (n, _) = await ComVeu(() => Dialogo.Nome(this, titulo ?? Tipos.First(t => t.Tipo == tipo).TituloNovo,
            "O nome aparece na lateral, na bandeja e no pit up <nome>." + (extras.Count > 0 ? " Os caminhos dá para mudar depois nas abas do perfil." : ""),
            IconeDe(tipo), "Nome do perfil", sugestao, "Criar perfil",
            ValidarNome, (nome, valores) =>
            {
                ValidarCaminhos(tipo, valores);
                foreach (var (k, v) in valores) if (v != "") p[k] = v;
                // projeto npm: a pasta sugere o nome do script e do perfil não muda; Tomcat: a pasta de projetos fica no perfil
                Perfis[nome] = p;
                try { cfg["_versao"] = Config.Gravar(cfg); }
                catch { Perfis.Remove(nome); throw; }
                return Task.CompletedTask;
            }, extras));
        if (n == null) return;
        sujo = false;
        Carregar(n);
        EscolherAba(tipo switch { "tomcat" => "artefatos", "war" => "wars", "java" => "app", "zip" => "zip", "npm" => "npm", "comando" => "comando", _ => "log" });
    }

    async Task Renomear()
    {
        if (atual == "") return;
        if (!(!EstadoDe(StatusAtual).Rodando && !EstadoDe(StatusAtual).Build)) { Aviso("pare o " + atual + " antes de renomear", "err"); return; }
        if (sujo)
        {
            try { Salvar(); }
            catch (Exception e) { Aviso("não salvei " + atual + ": " + e.Message, "err"); return; }
        }
        var velho = atual;
        var (n, _) = await ComVeu(() => Dialogo.Nome(this, "Renomear perfil", "A pasta do perfil (base do Tomcat ou cache) e o log vão junto.",
            IconeDe(TipoAtual), "Nome do perfil", velho, "Renomear", ValidarNome, (nome, _) => { runner.Renomear(velho, nome); return Task.CompletedTask; }));
        if (n == null) return;
        Aviso(velho + " agora é " + n, "on");
        Carregar(n);
    }

    async Task Duplicar()
    {
        JsonObject c;
        try { c = Coletar(); }
        catch (Exception e) { Aviso(e.Message, "err"); return; }
        foreach (var k in new[] { "porta", "portaDebug", "portaJmx", "portaAjp" })
            if (JsonAux.Num(c, k) is int v) c[k] = PortaLivre(k, v + 1);
        c.Remove("portaShutdown");
        c.Remove("url");
        await Criar(c, "Duplicar " + atual, atual + "-copia", pedirCaminhos: false);
    }

    async Task Excluir()
    {
        var alvo = atual;
        if (alvo == "") return;
        var ok = await ComVeu(() => Dialogo.Confirmar(this, "Excluir " + alvo + "?",
            "Sai do perfis.json (a versão anterior fica em perfis.json.bak). A pasta da base não é apagada.", "Excluir perfil", () =>
            {
                var salvo = Perfis[alvo]!;
                Perfis.Remove(alvo);
                try { cfg["_versao"] = Config.Gravar(cfg); }
                catch { Perfis[alvo] = salvo; throw; }
            }));
        if (!ok) return;
        sujo = false;
        Carregar(null);
    }

    // ---------------------------------------------------------------- menus

    void MenuMais()
    {
        var est = EstadoDe(StatusAtual);
        var livre = !est.Rodando && !est.Build;
        var m = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        void Item(string texto, Action a, bool ativo = true, bool perigo = false)
        {
            var mi = new MenuItem { Header = texto, IsEnabled = ativo };
            if (perigo) mi.Classes.Add("perigo");
            mi.Click += (_, _) => a();
            m.Items.Add(mi);
        }
        Item("Salvar agora", SalvarComAviso, atual != "" && sujo);
        Item(livre ? "Renomear perfil" : "Renomear perfil (pare antes)", () => _ = Renomear(), livre && atual != "");
        Item("Duplicar perfil", () => _ = Duplicar(), atual != "");
        Item("Recarregar do disco (F5)", Recarregar);
        m.Items.Add(new Separator());
        Item("Abrir a tela web", () => Proc.AbrirUrl("http://localhost:" + Instancia.Porta + "/"));
        Item("Ajustes…", () => Assistente.Abrir(bandeja, primeiraVez: false));
        m.Items.Add(new Separator());
        Item("Excluir perfil", () => _ = Excluir(), atual != "", true);
        Item("Parar e sair do Pitstop", () => _ = bandeja.Sair());
        m.ShowAt(maisBtn);
    }

    void MenuNovo(Control alvo)
    {
        var m = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        m.Items.Add(new MenuItem { Header = Ui.Rotulo("Novo perfil"), IsEnabled = false });
        foreach (var (tipo, icone, nome, desc, _) in Tipos)
        {
            var g = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            g.Children.Add(Ui.IconeTipo(icone));
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            tx.Children.Add(Ui.Txt(nome, 14, "Fg", FontWeight.SemiBold));
            tx.Children.Add(Ui.Txt(desc, 12, "Mut"));
            g.Children.Add(tx);
            var mi = new MenuItem { Header = g, MinWidth = 320 };
            var t = tipo;
            mi.Click += (_, _) => _ = Criar(NovoPerfil(t));
            m.Items.Add(mi);
        }
        m.ShowAt(alvo);
    }

    // ---------------------------------------------------------------- aviso (toast)

    /// <summary>Canto inferior direito; some em 4 s (erro fica até o próximo aviso). busy · on · err.</summary>
    void Aviso(string texto, string cls)
    {
        toastTxt.Text = texto;
        Ui.PintarPonto(toastPonto, cls switch { "on" => "noar", "err" => "erro", _ => "ocupado" });
        toast.IsVisible = true;
        toastRelogio.Stop();
        if (cls != "err") toastRelogio.Start();
    }

    /// <summary>Ajustes gravados (assistente): relê a config para os padrões aparecerem nos campos.</summary>
    public void AjustesMudaram() => Carregar(atual == "" ? null : atual);
}
