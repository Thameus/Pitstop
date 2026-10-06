using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace Pitstop.App;

/// <summary>Ícones da web/ui.html (grade de 24 px, traço 1,8) como geometria; a cor segue o tema (recurso).</summary>
static class Icones
{
    static readonly Dictionary<string, (string Dados, bool Cheio)> Todos = new()
    {
        ["play"] = ("M8,5.5 V18.5 L19,12 Z", true),
        ["stop"] = ("M8,6 H16 A2,2 0 0 1 18,8 V16 A2,2 0 0 1 16,18 H8 A2,2 0 0 1 6,16 V8 A2,2 0 0 1 8,6 Z", true),
        ["debug"] = ("M8,11 A4,4 0 0 1 16,11 V15 A4,4 0 0 1 8,15 Z M12,7 V4 M8,12 H4 M20,12 H16 M8.5,16.5 L5,18.5 M15.5,16.5 L19,18.5", false),
        ["reiniciar"] = ("M20,12 A8,8 0 1 1 17.66,6.34 M20,4 V8 H16", false),
        ["build"] = ("M12,3 L20,7.5 V16.5 L12,21 L4,16.5 V7.5 Z M4,7.5 L12,12 L20,7.5 M12,12 V21", false),
        ["sync"] = ("M20,12 A8,8 0 0 1 5.7,16.9 M4,12 A8,8 0 0 1 18.3,7.1 M18.5,3 V7.5 H14 M5.5,21 V16.5 H10", false),
        ["abrir"] = ("M14,4 H20 V10 M20,4 L11,13 M18,14 V19 A1,1 0 0 1 17,20 H5 A1,1 0 0 1 4,19 V7 A1,1 0 0 1 5,6 H10", false),
        ["mais"] = ("M3.4,12 A1.6,1.6 0 1 0 6.6,12 A1.6,1.6 0 1 0 3.4,12 Z M10.4,12 A1.6,1.6 0 1 0 13.6,12 A1.6,1.6 0 1 0 10.4,12 Z M17.4,12 A1.6,1.6 0 1 0 20.6,12 A1.6,1.6 0 1 0 17.4,12 Z", true),
        ["novo"] = ("M12,5 V19 M5,12 H19", false),
        ["lixo"] = ("M4,7 H20 M10,11 V17 M14,11 V17 M6,7 L7,19 A1,1 0 0 0 8,20 H16 A1,1 0 0 0 17,19 L18,7 M9,7 V4 H15 V7", false),
        ["sol"] = ("M8,12 A4,4 0 1 0 16,12 A4,4 0 1 0 8,12 Z M12,2 V4 M12,20 V22 M4.9,4.9 L6.3,6.3 M17.7,17.7 L19.1,19.1 M2,12 H4 M20,12 H22 M4.9,19.1 L6.3,17.7 M17.7,6.3 L19.1,4.9", false),
        ["lua"] = ("M20,14.5 A8,8 0 0 1 9.5,4 A8,8 0 1 0 20,14.5 Z", false),
        ["busca"] = ("M4,11 A7,7 0 1 0 18,11 A7,7 0 1 0 4,11 Z M20,20 L16.5,16.5", false),
        ["tomcat"] = ("M6,4 H18 A2,2 0 0 1 20,6 V9 A2,2 0 0 1 18,11 H6 A2,2 0 0 1 4,9 V6 A2,2 0 0 1 6,4 Z M6,13 H18 A2,2 0 0 1 20,15 V18 A2,2 0 0 1 18,20 H6 A2,2 0 0 1 4,18 V15 A2,2 0 0 1 6,13 Z M8,7.5 H8.01 M8,16.5 H8.01", false),
        ["java"] = ("M5,10 H16 V14 A5,5 0 0 1 11,19 H10 A5,5 0 0 1 5,14 Z M16,11 H17.5 A2.5,2.5 0 0 1 17.5,16 H16 M9,3.5 C9,5 10.5,5 10.5,6.5 M12.5,3.5 C12.5,5 14,5 14,6.5", false),
        ["npm"] = ("M6,4 H18 A3,3 0 0 1 21,7 V17 A3,3 0 0 1 18,20 H6 A3,3 0 0 1 3,17 V7 A3,3 0 0 1 6,4 Z M7.5,9.5 L10.5,12 L7.5,14.5 M13,15 H16.5", false),
        ["terminal"] = ("M4,5 H20 A2,2 0 0 1 22,7 V17 A2,2 0 0 1 20,19 H4 A2,2 0 0 1 2,17 V7 A2,2 0 0 1 4,5 Z M6.5,9 L10,12 L6.5,15 M12.5,15 H17.5", false),
        ["war"] = ("M4,8 L12,4 L20,8 V16 L12,20 L4,16 Z M4,8 L12,12 L20,8 M12,12 V20 M8,6 L16,10", false),
        ["zip"] = ("M6,3 H15 L19,7 V21 H6 Z M11,3 V5 H13 V7 H11 V9 H13 V11 H11 M11,13 H12 A1,1 0 0 1 13,14 V16 A1,1 0 0 1 12,17 H11 A1,1 0 0 1 10,16 V14 A1,1 0 0 1 11,13 Z", false),
        ["seta"] = ("M7,10 L12,15 L17,10", false),
        ["pasta"] = ("M3,7 A2,2 0 0 1 5,5 H9.5 L11.5,7 H19 A2,2 0 0 1 21,9 V17 A2,2 0 0 1 19,19 H5 A2,2 0 0 1 3,17 Z", false),
        ["arquivo"] = ("M7,3 H14 L19,8 V19 A2,2 0 0 1 17,21 H7 A2,2 0 0 1 5,19 V5 A2,2 0 0 1 7,3 Z M14,3 V8 H19", false),
        ["ajustes"] = ("M4,6 H13 M17,6 H20 M15,4 V8 M4,12 H7 M11,12 H20 M9,10 V14 M4,18 H15 M19,18 H20 M17,16 V20", false),
        ["ok"] = ("M5,12.5 L10,17.5 L19,7.5", false),
        ["x"] = ("M6,6 L18,18 M18,6 L6,18", false),
        ["sair"] = ("M10,4 H6 A2,2 0 0 0 4,6 V18 A2,2 0 0 0 6,20 H10 M15,8 L19,12 L15,16 M19,12 H9", false),
        ["tela"] = ("M4,5 H20 A1,1 0 0 1 21,6 V16 A1,1 0 0 1 20,17 H4 A1,1 0 0 1 3,16 V6 A1,1 0 0 1 4,5 Z M8,21 H16 M12,17 V21", false),
    };

    /// <summary>Ícone de <paramref name="px"/> px na cor do recurso <paramref name="cor"/> (Fg, Acc, AccFg...).</summary>
    public static Control Criar(string nome, double px = 16, string cor = "Fg")
    {
        var (dados, cheio) = Todos[nome];
        var p = new Path
        {
            Data = Geometry.Parse(dados),
            StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };
        p.Res(cheio ? Shape.FillProperty : Shape.StrokeProperty, cor);
        var tela = new Canvas { Width = 24, Height = 24, Children = { p } };
        return new Viewbox { Width = px, Height = px, Child = tela, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    }

    public static Geometry Geometria(string nome) => Geometry.Parse(Todos[nome].Dados);
}
