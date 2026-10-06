using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Pitstop;

/// <summary>
/// Serialização no formato que a tela espera (camelCase, null omitido = "undefined" do JS) e leitura
/// tolerante do perfis.json com a mesma semântica de "falsy" do JS antigo (0 e "" contam como ausente).
/// </summary>
public static class JsonAux
{
    public static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // acentos legíveis no perfis.json e no log
    };

    public static readonly JsonSerializerOptions Indentado = new(Opcoes) { WriteIndented = true, NewLine = "\n" };

    public static string Serializar(object? o) => JsonSerializer.Serialize(o, Opcoes);

    /// <summary>Texto da chave; número vira texto; ausente, null ou "" = null.</summary>
    public static string? Txt(JsonNode? o, string chave)
    {
        if (o is not JsonObject obj || !obj.TryGetPropertyValue(chave, out var v) || v is not JsonValue jv) return null;
        if (jv.TryGetValue<string>(out var s)) return s == "" ? null : s;
        if (jv.TryGetValue<double>(out var d)) return d.ToString(CultureInfo.InvariantCulture);
        // valor criado em memória (p["porta"] = 8080, janela nativa): só responde ao tipo de origem
        if (jv.TryGetValue<int>(out var i)) return i.ToString(CultureInfo.InvariantCulture);
        if (jv.TryGetValue<long>(out var l)) return l.ToString(CultureInfo.InvariantCulture);
        return null;
    }

    /// <summary>Número da chave (aceita "8080"); 0, ausente ou inválido = null.</summary>
    public static int? Num(JsonNode? o, string chave)
    {
        var t = Txt(o, chave);
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d != 0 ? (int)d : null;
    }

    /// <summary>JS "x !== false": só o booleano false desliga.</summary>
    public static bool NaoFalso(JsonNode? o, string chave) =>
        !(o is JsonObject obj && obj.TryGetPropertyValue(chave, out var v) && v is JsonValue jv
          && jv.TryGetValue<bool>(out var b) && !b);
}
