using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Pitstop;

/// <summary>
/// Tela (web\ui.html) + API HTTP. Escuta só em 127.0.0.1.
/// Proteções: Host precisa ser localhost/127.0.0.1:&lt;porta&gt; (DNS rebinding); método que não é GET exige
/// o cabeçalho X-PIT: 1 (POST vindo de outra página); corpo limitado a 1 MB.
/// </summary>
public sealed class Servidor(Runner runner, int porta)
{
    static readonly Regex RotaPerfil = new(@"^/api/p/([\w.-]+)/(log|start|debug|stop|reiniciar|build|sync|preparar|abrir|limpar)$");
    static readonly Regex RotaRenomear = new(@"^/api/p/([\w.-]+)/renomear$");
    static readonly Dictionary<string, (string Arquivo, string Tipo)> Icones = new()
    {
        ["/favicon.ico"] = ("icone.ico", "image/x-icon"),
        ["/icone.svg"] = ("icone.svg", "image/svg+xml"),
    };

    WebApplication? app;

    public int Porta => porta;
    public string Endereco => "http://localhost:" + porta + "/";

    /// <summary>Chamado depois do /api/sair (perfis já parados). Padrão: derruba o que sobrou e sai do processo.</summary>
    public Action Encerrar { get; set; } = () => { runner.MatarTodos(); Environment.Exit(0); };

    /// <summary>POST /api/tela: segunda instância do Pitstop.exe pedindo para mostrar a janela nativa. Nulo = 404.</summary>
    public Action? MostrarTela { get; set; }

    /// <summary>Sobe o servidor. false = porta já em uso (outro runner no ar).</summary>
    public async Task<bool> Iniciar()
    {
        var b = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = Raiz.Dir });
        b.Logging.ClearProviders();
        b.WebHost.ConfigureKestrel(o =>
        {
            o.Listen(IPAddress.Loopback, porta);
            o.Limits.MaxRequestBodySize = 1_000_000;
        });
        app = b.Build();
        app.Run(ctx => Tratar(ctx));
        try
        {
            await app.StartAsync().ConfigureAwait(false);
            return true;
        }
        catch
        {
            // Kestrel embrulha o "address already in use" em IOException; confere pela porta em vez do tipo
            await app.DisposeAsync().ConfigureAwait(false);
            app = null;
            if (await Proc.PortaOcupada(porta).ConfigureAwait(false)) return false;
            throw;
        }
    }

    async Task Tratar(HttpContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        res.Headers["X-Content-Type-Options"] = "nosniff";
        res.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
        res.Headers["Referrer-Policy"] = "no-referrer";
        var host = req.Headers.Host.ToString();
        if (host != "localhost:" + porta && host != "127.0.0.1:" + porta) { res.StatusCode = 403; return; }
        var url = req.Path.Value ?? "/";
        var get = HttpMethods.IsGet(req.Method);
        try
        {
            if (get && (url == "/" || url == "/index.html"))
            {
                res.ContentType = "text/html; charset=utf-8";
                await res.SendFileAsync(Path.Combine(Raiz.Web, "ui.html"));
                return;
            }
            // ícone da aba / janela --app (web\icone.svg é a fonte; web\icone.ico é o mesmo desenho em 16-256 px)
            if (get && Icones.TryGetValue(url, out var icone))
            {
                var arq = Path.Combine(Raiz.Web, icone.Arquivo);
                if (!File.Exists(arq)) { res.StatusCode = 404; return; }
                res.ContentType = icone.Tipo;
                res.Headers.CacheControl = "max-age=86400";
                await res.SendFileAsync(arq);
                return;
            }
            // fontes da tela (web\fonts, OFL): o runner roda sem internet, então não dependem do Google Fonts
            if (get && url is "/fonts/SpaceGrotesk.woff2" or "/fonts/JetBrainsMono.woff2")
            {
                var arq = Path.Combine(Raiz.Web, "fonts", url["/fonts/".Length..]);
                if (!File.Exists(arq)) { res.StatusCode = 404; return; }
                res.ContentType = "font/woff2";
                res.Headers.CacheControl = "max-age=31536000, immutable";
                await res.SendFileAsync(arq);
                return;
            }
            if (get && url == "/api/cfg") { await Responder(res, 200, Config.Ler()); return; }
            if (get && url == "/api/status") { await Responder(res, 200, await runner.Status()); return; }
            if (get && url == "/api/tomcats") { await Responder(res, 200, Tomcat.Instalados()); return; }
            // ?dir= pasta de projetos do perfil (vazio = PROJETOS_DIR do .env)
            if (get && url == "/api/projetos") { await Responder(res, 200, Projetos.Listar(req.Query["dir"].ToString())); return; }
            // seletor de classe da tela: classes com main nos fontes do módulo (?modulo=<pasta com pom.xml>)
            if (get && url == "/api/mains")
            {
                var mod = req.Query["modulo"].ToString();
                if (mod == "" || !File.Exists(Path.Combine(mod, "pom.xml"))) { await Responder(res, 400, new { erro = "módulo sem pom.xml: " + mod }); return; }
                await Responder(res, 200, JavaApp.ClassesMain(mod));
                return;
            }
            // seletor de script npm: recebe só o nome de um perfil já salvo; o caminho vem do perfis.json local.
            // X-PIT impede que uma página de outra origem use o navegador para sondar arquivos locais.
            if (get && url == "/api/scripts")
            {
                if (req.Headers["X-PIT"].ToString() != "1") { res.StatusCode = 403; return; }
                var nome = req.Query["perfil"].ToString();
                if (!Config.NomeValido(nome)) throw new ErroRunner("perfil inválido");
                var perfil = Config.LerPerfil(Config.Ler(), nome);
                if (!perfil.EhNpm) throw new ErroRunner("perfil não é npm: " + nome);
                await Responder(res, 200, new { pasta = perfil.Pasta, scripts = NodeApp.Scripts(perfil.Pasta) });
                return;
            }
            // seletor de versão dos perfis war/zip: o mesmo arquivo nas pastas irmãs (?arq=<caminho>&ext=<.war|.zip,.jar se vazio>)
            if (get && url == "/api/versoes")
            {
                await Responder(res, 200, Pacote.Versoes(req.Query["arq"].ToString(), req.Query["ext"].ToString()));
                return;
            }
            // ajustes globais (.env): campos, valores, sugestões do ambiente e se é a primeira execução
            if (get && url == "/api/ajustes")
            {
                await Responder(res, 200, new
                {
                    campos = Ajustes.Campos, valores = Ajustes.Ler(), sugestoes = Ajustes.Sugestoes(),
                    primeiraVez = Ajustes.PrimeiraVez, so = So.Windows ? "windows" : "linux",
                });
                return;
            }

            var m = RotaPerfil.Match(url);
            if (m.Success && get && m.Groups[2].Value == "log") { await Log(ctx, m.Groups[1].Value); return; }

            if (!get && req.Headers["X-PIT"].ToString() != "1") { res.StatusCode = 403; return; }
            if (HttpMethods.IsPut(req.Method) && url == "/api/cfg")
            {
                using var sr = new StreamReader(req.Body, Encoding.UTF8);
                var txt = await sr.ReadToEndAsync();
                var versao = Config.Gravar(txt.Trim() == "" ? new JsonObject() : JsonNode.Parse(txt));
                await Responder(res, 200, new { ok = true, versao });
                return;
            }
            if (HttpMethods.IsPut(req.Method) && url == "/api/ajustes")
            {
                using var sr = new StreamReader(req.Body, Encoding.UTF8);
                var corpo = JsonNode.Parse(await sr.ReadToEndAsync()) as JsonObject ?? new JsonObject();
                Ajustes.Gravar(corpo.ToDictionary(kv => kv.Key, kv => JsonAux.Txt(corpo, kv.Key)));
                await Responder(res, 200, new { ok = true });
                return;
            }
            if (HttpMethods.IsPost(req.Method) && url == "/api/sair") { await Sair(ctx); return; }
            if (HttpMethods.IsPost(req.Method) && url == "/api/tela" && MostrarTela is { } mostrar)
            {
                mostrar();
                await Responder(res, 200, new { ok = true });
                return;
            }
            // renomear: corpo { "para": "<nome novo>" }
            if (HttpMethods.IsPost(req.Method) && RotaRenomear.Match(url) is { Success: true } mr)
            {
                using var sr = new StreamReader(req.Body, Encoding.UTF8);
                var corpo = JsonNode.Parse(await sr.ReadToEndAsync());
                var para = JsonAux.Txt(corpo, "para") ?? throw new ErroRunner("informe o nome novo");
                await Responder(res, 200, runner.Renomear(mr.Groups[1].Value, para));
                return;
            }
            if (m.Success && HttpMethods.IsPost(req.Method))
            {
                await Responder(res, 200, await runner.Acao(m.Groups[1].Value, m.Groups[2].Value));
                return;
            }
            res.StatusCode = 404;
        }
        catch (Exception e)
        {
            if (!res.HasStarted) await Responder(res, 400, new { erro = e.Message });
        }
    }

    /// <summary>SSE do log do perfil: o fim do buffer na hora, depois lotes de 150 ms.</summary>
    async Task Log(HttpContext ctx, string nome)
    {
        var res = ctx.Response;
        res.ContentType = "text/event-stream";
        res.Headers.CacheControl = "no-cache";
        var canal = runner.Logs.Ouvir(nome);
        try
        {
            await res.StartAsync(ctx.RequestAborted);
            await foreach (var ev in canal.Reader.ReadAllAsync(ctx.RequestAborted))
            {
                await res.WriteAsync(ev, ctx.RequestAborted);
                await res.Body.FlushAsync(ctx.RequestAborted);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally { runner.Logs.Largar(nome, canal); }
    }

    /// <summary>
    /// Processo sem console: não há janela para fechar nem sinal, a saída vem por aqui.
    /// Normal: stop gracioso de cada perfil. ?rapido=1: derruba direto.
    /// </summary>
    async Task Sair(HttpContext ctx)
    {
        var nomes = runner.EmExecucao();
        await Responder(ctx.Response, 200, new { ok = true, parando = nomes });
        await ctx.Response.CompleteAsync();
        if (!runner.IniciarSaida()) return;   // segundo clique em Sair: o primeiro já está parando
        var rapido = ctx.Request.Query["rapido"].ToString() == "1";
        _ = Task.Run(async () =>
        {
            if (!rapido) await runner.PararTodos().ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
            Encerrar();
        });
    }

    static async Task Responder(HttpResponse res, int codigo, object? corpo)
    {
        res.StatusCode = codigo;
        res.ContentType = "application/json; charset=utf-8";
        await res.WriteAsync(JsonAux.Serializar(corpo), Encoding.UTF8);
    }

    public async Task Parar()
    {
        if (app != null) await app.StopAsync().ConfigureAwait(false);
    }
}
