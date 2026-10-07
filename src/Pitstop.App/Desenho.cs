using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Pitstop.App;

/// <summary>
/// O ícone do Pitstop (web/icone.svg: quadrado roxo, "play" e as três linhas de velocidade) desenhado na hora, com o
/// selo de estado no canto. Serve à janela e à bandeja nos dois sistemas, sem depender de leitor de .ico/.svg.
/// </summary>
static class Desenho
{
    static readonly Color Roxo = Color.Parse("#7338B3");
    static readonly Dictionary<string, WindowIcon> Cache = new();

    public static readonly Dictionary<string, Color> Selos = new()
    {
        ["noar"] = Color.Parse("#43C07F"), ["debug"] = Color.Parse("#6EA8FE"), ["externo"] = Color.Parse("#6EA8FE"),
        ["ocupado"] = Color.Parse("#E3A63F"), ["portaocupada"] = Color.Parse("#E3A63F"), ["erro"] = Color.Parse("#F0625E"),
    };

    /// <summary>Ícone com selo (parado = sem selo). 64 px: o sistema reduz para o tamanho da bandeja.</summary>
    public static WindowIcon Icone(string estado = "parado")
    {
        if (Cache.TryGetValue(estado, out var ic)) return ic;
        ic = new WindowIcon(Bitmap(64, Selos.TryGetValue(estado, out var c) ? c : null));
        Cache[estado] = ic;
        return ic;
    }

    public static RenderTargetBitmap Bitmap(int t, Color? selo)
    {
        var bmp = new RenderTargetBitmap(new PixelSize(t, t), new Vector(96, 96));
        using (var g = bmp.CreateDrawingContext())
        {
            var k = t / 32.0;
            g.DrawRectangle(new SolidColorBrush(Roxo), null, new Rect(0, 0, t, t), 9 * k, 9 * k);
            using (g.PushTransform(Matrix.CreateScale(k, k)))
            {
                g.DrawGeometry(Brushes.White, null, Geometry.Parse("M13.2,10.4 L22.4,15.6 A0.5,0.5 0 0 1 22.4,16.4 L13.2,21.6 A0.5,0.5 0 0 1 12.5,21.2 V10.8 A0.5,0.5 0 0 1 13.2,10.4 Z"));
                var pena = new Pen(Brushes.White, 1.8, lineCap: PenLineCap.Round);
                g.DrawLine(pena, new Point(7.5, 13), new Point(10.3, 13));
                g.DrawLine(pena, new Point(6.5, 16), new Point(10.3, 16));
                g.DrawLine(pena, new Point(7.5, 19), new Point(10.3, 19));
            }
            if (selo is Color c)
            {
                var d = t * 0.5;
                var anel = t / 14.0;
                var centro = new Point(t - d / 2, t - d / 2);
                g.DrawEllipse(new SolidColorBrush(Color.Parse("#FFFFFF")), null, centro, d / 2, d / 2);
                g.DrawEllipse(new SolidColorBrush(c), null, centro, d / 2 - anel, d / 2 - anel);
            }
        }
        return bmp;
    }

    /// <summary>Grava o ícone em PNG (o instalador do Linux usa no .desktop).</summary>
    public static void SalvarPng(string arquivo, int t = 256)
    {
        using var bmp = Bitmap(t, null);
        bmp.Save(arquivo);
    }
}
