using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pitstop;

namespace Pitstop.Cli;

/// <summary>
/// Servidor MCP stdio: descobre capacidades do Pitstop e usa somente sua API HTTP em loopback.
/// Não inicia outro Runner, não expõe portas e não recebe shell arbitrário pelo protocolo.
/// </summary>
public static class Mcp
{
    const string Vazio = """{"type":"object","properties":{}}""";
    const string Perfil = """{"type":"object","properties":{"perfil":{"type":"string"}},"required":["perfil"]}""";
    const string Dir = """{"type":"object","properties":{"dir":{"type":"string"}}}""";
    static object T(string n, string d, string schema)
    {
        var mutavel = n is "pitstop_save_profile" or "pitstop_delete_profile" or
            "pitstop_rename_profile" or "pitstop_action" or "pitstop_update_settings" or
            "pitstop_show_window" or "pitstop_shutdown";
        var destrutiva = n is "pitstop_delete_profile" or "pitstop_action" or "pitstop_shutdown";
        return new
        {
            name = n, description = d, inputSchema = JsonNode.Parse(schema)!,
            annotations = new
            {
                readOnlyHint = !mutavel, destructiveHint = destrutiva,
                idempotentHint = !mutavel, openWorldHint = false
            }
        };
    }

    public static readonly object[] Ferramentas =
    [
        T("pitstop_status", "Consulta perfis, execução, URLs e estado dos serviços; não altera nada.", Vazio),
        T("pitstop_profiles", "Lista os nomes e tipos de perfis configurados sem mostrar todos os detalhes.", Vazio),
        T("pitstop_profile", "Lê os detalhes do perfil indicado (pode conter caminhos e variáveis privadas).", Perfil),
        T("pitstop_save_profile", "Cria ou altera campos de um perfil, preservando demais campos. Pode configurar comandos executáveis; obtenha aprovação do usuário.", """{"type":"object","properties":{"perfil":{"type":"string"},"dados":{"type":"object"}},"required":["perfil","dados"]}"""),
        T("pitstop_delete_profile", "Remove o perfil da configuração. Ação destrutiva; confirme antes.", Perfil),
        T("pitstop_rename_profile", "Renomeia um perfil parado, preservando dados quando possível.", """{"type":"object","properties":{"perfil":{"type":"string"},"novo":{"type":"string"}},"required":["perfil","novo"]}"""),
        T("pitstop_action", "Ações de perfil pela API local: start, debug, stop, reiniciar, build, sync, preparar, abrir, terminal ou limpar. Confirme ações destrutivas.", """{"type":"object","properties":{"perfil":{"type":"string"},"acao":{"type":"string","enum":["start","debug","stop","reiniciar","build","sync","preparar","abrir","terminal","limpar"]}},"required":["perfil","acao"]}"""),
        T("pitstop_logs", "Lê as últimas linhas do log da instância Pitstop em execução, sem acompanhar indefinidamente.", """{"type":"object","properties":{"perfil":{"type":"string"},"linhas":{"type":"integer","minimum":1,"maximum":200}},"required":["perfil"]}"""),
        T("pitstop_projects", "Descobre repositórios Maven e módulos WAR (dir opcional).", Dir),
        T("pitstop_inspect", "Inspeciona uma pasta para sugerir novos perfis Maven ou npm; não executa nada.", """{"type":"object","properties":{"dir":{"type":"string"}},"required":["dir"]}"""),
        T("pitstop_folders", "Navega por pastas locais sem listar conteúdo de arquivos.", Dir),
        T("pitstop_tomcats", "Lista instalações Tomcat identificadas na máquina.", Vazio),
        T("pitstop_java_mains", "Descobre classes Java com main no módulo indicado.", """{"type":"object","properties":{"modulo":{"type":"string"}},"required":["modulo"]}"""),
        T("pitstop_npm_scripts", "Lista scripts npm de um perfil salvo.", Perfil),
        T("pitstop_package_versions", "Descobre versões disponíveis de pacote WAR/JAR/ZIP.", """{"type":"object","properties":{"arq":{"type":"string"},"ext":{"type":"string"}},"required":["arq"]}"""),
        T("pitstop_settings", "Lê ajustes globais locais (podem conter caminhos privados).", Vazio),
        T("pitstop_update_settings", "Altera somente os ajustes globais fornecidos, validando pelo Pitstop; requer aprovação.", """{"type":"object","properties":{"valores":{"type":"object"}},"required":["valores"]}"""),
        T("pitstop_show_window", "Pede à instância desktop para exibir sua janela.", Vazio),
        T("pitstop_shutdown", "Encerra a instância Pitstop e seus perfis. Ação destrutiva; confirmar.", """{"type":"object","properties":{"rapido":{"type":"boolean"}}}""")
    ];

    public static string CatalogoJson() => JsonSerializer.Serialize(new
    {
        nome = "Pitstop",
        protocolo = "MCP stdio",
        requisito = "Pitstop em execução (janela desktop ou pit ui) na mesma máquina",
        inicializacao = "pit mcp",
        ferramentas = Ferramentas
    }, new JsonSerializerOptions { WriteIndented = true });

    public static async Task<int> Rodar()
    {
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);
        using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + Env.Num("RUNNER_PORTA", 9999)), Timeout = TimeSpan.FromSeconds(20) };
        string? linha;
        while ((linha = await Console.In.ReadLineAsync()) != null)
        {
            JsonElement? id = null;
            try
            {
                using var msg = JsonDocument.Parse(linha);
                var raiz = msg.RootElement;
                if (raiz.TryGetProperty("id", out var xid)) id = xid.Clone();
                if (id == null) continue; // notificações MCP não recebem resposta
                var metodo = Campo(raiz, "method");
                object resultado = metodo switch
                {
                    "initialize" => new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new { tools = new { listChanged = false } },
                        serverInfo = new { name = "pitstop", version = typeof(Mcp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0" },
                        instructions = "Use tools/list. Requer Pitstop desktop ou pit ui no mesmo computador. Solicite aprovação antes de alterações, build, start, stop ou exclusões."
                    },
                    "ping" => new { },
                    "tools/list" => new { tools = Ferramentas },
                    "tools/call" => await Chamar(http, raiz.GetProperty("params")),
                    _ => throw new InvalidOperationException("método MCP desconhecido: " + metodo),
                };
                await Responder(new { jsonrpc = "2.0", id, result = resultado });
            }
            catch (Exception ex)
            {
                if (id != null)
                    await Responder(new { jsonrpc = "2.0", id, error = new { code = -32603, message = ex.Message } });
            }
        }
        return 0;
    }

    static async Task Responder(object obj)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(obj));
        await Console.Out.FlushAsync();
    }

    static string Campo(JsonElement obj, string nome, string padrao = "")
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(nome, out var x) || x.ValueKind == JsonValueKind.Null)
            return padrao;
        return x.ValueKind == JsonValueKind.String ? x.GetString() ?? padrao : x.ToString();
    }

    static string Nome(JsonElement obj, string key = "perfil")
    {
        var nome = Campo(obj, key);
        if (!Config.NomeValido(nome)) throw new ArgumentException("nome de perfil inválido");
        return Uri.EscapeDataString(nome);
    }

    static string Url(string endpoint, string chave, string valor) =>
        endpoint + "?" + chave + "=" + Uri.EscapeDataString(valor);

    static JsonElement Argumentos(JsonElement parameters) =>
        parameters.TryGetProperty("arguments", out var args) ? args : default;

    static async Task<object> Chamar(HttpClient http, JsonElement parameters)
    {
        var nome = Campo(parameters, "name");
        var args = Argumentos(parameters);
        try
        {
            string resultado = nome switch
            {
                "pitstop_status" => await Pedir(http, HttpMethod.Get, "/api/status"),
                "pitstop_profiles" => await ListarPerfis(http),
                "pitstop_profile" => await PerfilDetalhes(http, Nome(args)),
                "pitstop_save_profile" => await SalvarPerfil(http, Nome(args), args.GetProperty("dados")),
                "pitstop_delete_profile" => await ExcluirPerfil(http, Nome(args)),
                "pitstop_rename_profile" => await Pedir(http, HttpMethod.Post, "/api/p/" + Nome(args) + "/renomear",
                    JsonSerializer.Serialize(new { para = Campo(args, "novo") })),
                "pitstop_action" => await Acao(http, args),
                "pitstop_logs" => await Logs(http, Nome(args), args),
                "pitstop_projects" => await Pedir(http, HttpMethod.Get, Url("/api/projetos", "dir", Campo(args, "dir"))),
                "pitstop_inspect" => await Pedir(http, HttpMethod.Get, Url("/api/inspecionar", "dir", Campo(args, "dir"))),
                "pitstop_folders" => await Pedir(http, HttpMethod.Get, Url("/api/pastas", "dir", Campo(args, "dir"))),
                "pitstop_tomcats" => await Pedir(http, HttpMethod.Get, "/api/tomcats"),
                "pitstop_java_mains" => await Pedir(http, HttpMethod.Get, Url("/api/mains", "modulo", Campo(args, "modulo"))),
                "pitstop_npm_scripts" => await Pedir(http, HttpMethod.Get, Url("/api/scripts", "perfil", Campo(args, "perfil"))),
                "pitstop_package_versions" => await Pedir(http, HttpMethod.Get,
                    "/api/versoes?arq=" + Uri.EscapeDataString(Campo(args, "arq")) + "&ext=" + Uri.EscapeDataString(Campo(args, "ext"))),
                "pitstop_settings" => await Pedir(http, HttpMethod.Get, "/api/ajustes"),
                "pitstop_update_settings" => await Pedir(http, HttpMethod.Put, "/api/ajustes", args.GetProperty("valores").GetRawText()),
                "pitstop_show_window" => await Pedir(http, HttpMethod.Post, "/api/tela", "{}"),
                "pitstop_shutdown" => await Pedir(http, HttpMethod.Post, "/api/sair" +
                    (Campo(args, "rapido") == "True" || Campo(args, "rapido") == "true" ? "?rapido=1" : ""), "{}"),
                _ => throw new ArgumentException("ferramenta não encontrada: " + nome)
            };
            return new { content = new[] { new { type = "text", text = resultado } }, isError = false };
        }
        catch (Exception ex)
        {
            return new { content = new[] { new { type = "text", text = "Pitstop: " + ex.Message +
                ". Verifique se o aplicativo ou 'pit ui' está aberto no computador local." } }, isError = true };
        }
    }

    static async Task<string> Acao(HttpClient http, JsonElement args)
    {
        var acao = Campo(args, "acao");
        if (!new[] { "start", "debug", "stop", "reiniciar", "build", "sync", "preparar", "abrir", "terminal", "limpar" }.Contains(acao))
            throw new ArgumentException("ação não permitida: " + acao);
        return await Pedir(http, HttpMethod.Post, "/api/p/" + Nome(args) + "/" + acao, "{}");
    }

    static async Task<JsonObject> LerConfig(HttpClient http) =>
        JsonNode.Parse(await Pedir(http, HttpMethod.Get, "/api/cfg"))!.AsObject();

    static async Task<string> ListarPerfis(HttpClient http)
    {
        var cfg = await LerConfig(http);
        var perfis = cfg["perfis"]?.AsObject() ?? new JsonObject();
        return JsonSerializer.Serialize(perfis.Select(p => new
        {
            nome = p.Key,
            tipo = p.Value?["tipo"]?.ToString() ?? "tomcat"
        }));
    }

    static async Task<string> PerfilDetalhes(HttpClient http, string nome)
    {
        var cfg = await LerConfig(http);
        var perfil = cfg["perfis"]?[Uri.UnescapeDataString(nome)];
        if (perfil == null) throw new ArgumentException("perfil não encontrado");
        return perfil.ToJsonString();
    }

    static async Task<string> SalvarPerfil(HttpClient http, string nome, JsonElement dados)
    {
        if (dados.ValueKind != JsonValueKind.Object) throw new ArgumentException("dados precisam ser um objeto JSON");
        var cfg = await LerConfig(http);
        var perfis = cfg["perfis"]!.AsObject();
        var chave = Uri.UnescapeDataString(nome);
        var novos = JsonNode.Parse(dados.GetRawText())!.AsObject();
        if (perfis[chave] is JsonObject existente)
        {
            foreach (var (campo, valor) in novos)
                existente[campo] = valor?.DeepClone();
        }
        else perfis[chave] = novos;
        return await Pedir(http, HttpMethod.Put, "/api/cfg", cfg.ToJsonString());
    }

    static async Task<string> ExcluirPerfil(HttpClient http, string nome)
    {
        var chave = Uri.UnescapeDataString(nome);
        var status = JsonNode.Parse(await Pedir(http, HttpMethod.Get, "/api/status"))?.AsArray();
        if (status?.Any(s => s?["nome"]?.ToString() == chave && s?["emUso"]?.GetValue<bool>() == true) == true)
            throw new InvalidOperationException("pare o perfil antes de excluí-lo");
        var cfg = await LerConfig(http);
        if (!cfg["perfis"]!.AsObject().Remove(chave))
            throw new ArgumentException("perfil não encontrado");
        return await Pedir(http, HttpMethod.Put, "/api/cfg", cfg.ToJsonString());
    }

    static async Task<string> Pedir(HttpClient http, HttpMethod metodo, string rota, string? json = null)
    {
        using var req = new HttpRequestMessage(metodo, rota);
        req.Headers.Add("X-PIT", "1");
        if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req);
        var txt = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException("HTTP " + (int)resp.StatusCode + ": " + txt);
        return txt;
    }

    static async Task<string> Logs(HttpClient http, string perfil, JsonElement args)
    {
        var max = 80;
        if (int.TryParse(Campo(args, "linhas"), out var n)) max = Math.Clamp(n, 1, 200);
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/p/" + perfil + "/log");
        req.Headers.Add("X-PIT", "1");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? linha;
        while ((linha = await reader.ReadLineAsync(cts.Token)) != null)
            if (linha.StartsWith("data: ", StringComparison.Ordinal))
            {
                var linhas = JsonNode.Parse(linha[6..])?.AsArray() ?? new JsonArray();
                return JsonSerializer.Serialize(linhas.TakeLast(max).ToArray());
            }
        return "[]";
    }
}
