using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Pitstop.App;

/// <summary>
/// Aviso da bandeja (subiu, caiu, build ok/falhou) no canto inferior direito da tela: janela pequena, sem borda do
/// sistema, some em 6 s. Igual no Windows e no Linux (o Avalonia não tem balão de bandeja). Um de cada vez: os
/// próximos esperam numa fila curta. Clicar abre a janela do Pitstop.
/// </summary>
static class Notificacao
{
    static readonly Queue<(string Titulo, string Texto, string Estado)> Fila = new();
    static Window? aberta;

    public static Action? AoClicar { get; set; }

    public static void Mostrar(string titulo, string? texto, string estado = "noar")
    {
        Dispatcher.UIThread.Post(() =>
        {
            while (Fila.Count >= 5) Fila.Dequeue();
            Fila.Enqueue((titulo, texto ?? "", estado));
            if (aberta == null) Proxima();
        });
    }

    static void Proxima()
    {
        if (Fila.Count == 0) return;
        var (titulo, texto, estado) = Fila.Dequeue();
        var ponto = Ui.Ponto(10);
        Ui.PintarPonto(ponto, estado);
        ponto.Margin = new Thickness(0, 6, 12, 0);
        ponto.VerticalAlignment = VerticalAlignment.Top;
        var txt = new StackPanel { Spacing = 3 };
        txt.Children.Add(Ui.Txt(titulo, 14, "Fg", FontWeight.SemiBold));
        if (texto != "") txt.Children.Add(Ui.Paragrafo(texto, 12));
        var linha = new DockPanel();
        DockPanel.SetDock(ponto, Dock.Left);
        linha.Children.Add(ponto);
        linha.Children.Add(txt);
        var caixa = Ui.Cartao(linha, 14, new Thickness(16, 14));
        caixa.Margin = new Thickness(16);
        caixa.Res(Border.BoxShadowProperty, "Sombra");
        var w = new Window
        {
            SystemDecorations = SystemDecorations.None,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            Content = caixa,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        w.PointerPressed += (_, _) => { AoClicar?.Invoke(); w.Close(); };
        w.Opened += (_, _) =>
        {
            var tela = w.Screens.Primary;
            if (tela == null) return;
            var area = tela.WorkingArea;
            var esc = tela.Scaling;
            var largura = (int)(w.Bounds.Width * esc);
            var altura = (int)(w.Bounds.Height * esc);
            w.Position = new PixelPoint(area.Right - largura - 8, area.Bottom - altura - 8);
        };
        w.Closed += (_, _) =>
        {
            aberta = null;
            DispatcherTimer.RunOnce(Proxima, TimeSpan.FromMilliseconds(400));
        };
        aberta = w;
        w.Show();
        DispatcherTimer.RunOnce(() => { if (aberta == w) w.Close(); }, TimeSpan.FromSeconds(6));
    }
}
