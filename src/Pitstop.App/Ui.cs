using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Pitstop.App;

/// <summary>Peças da tela montadas em código (equivalentes às classes da web/ui.html: bloco, campo, grade, cartão...).</summary>
static class Ui
{
    /// <summary>Liga a propriedade a um recurso do tema (troca ao vivo com claro/escuro).</summary>
    public static T Res<T>(this T o, AvaloniaProperty p, string chave) where T : AvaloniaObject
    {
        o[!p] = new DynamicResourceExtension(chave);
        return o;
    }

    public static T Also<T>(this T o, Action<T> a) { a(o); return o; }

    public static T Dica<T>(this T c, string texto) where T : Control { ToolTip.SetTip(c, texto); return c; }

    public static TextBlock Txt(string texto, double tam = 14, string cor = "Fg", FontWeight? peso = null, bool mono = false)
    {
        var t = new TextBlock { Text = texto, FontSize = tam, FontWeight = peso ?? FontWeight.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
        t.Res(TextBlock.ForegroundProperty, cor);
        if (mono) t.Res(TextBlock.FontFamilyProperty, "FonteMono");
        return t;
    }

    public static TextBlock Paragrafo(string texto, double tam = 13, string cor = "Mut") =>
        Txt(texto, tam, cor).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.LineHeight = tam * 1.5; });

    /// <summary>Rótulo de seção (PERFIS, NOVO PERFIL): 11 / 600, caixa alta, espaçado.</summary>
    public static TextBlock Rotulo(string texto) =>
        new TextBlock { Text = texto.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.9 }
            .Res(TextBlock.ForegroundProperty, "Mut");

    public static TextBox Inp(string exemplo = "", double altura = 42)
    {
        var t = new TextBox { Watermark = exemplo, MinHeight = altura };
        t.Classes.Add("inp");
        return t;
    }

    public static CheckBox Chk(string texto, bool marcado = false) => new() { Content = texto, IsChecked = marcado };

    /// <summary>Botão com texto e ícone opcional. <paramref name="classe"/>: b (padrão), pri, perigo, fantasma, link.</summary>
    public static Button Btn(string texto, string classe = "", string? icone = null, string corIcone = "Fg")
    {
        var b = new Button();
        b.Classes.Add("b");
        foreach (var c in classe.Split(' ', StringSplitOptions.RemoveEmptyEntries)) b.Classes.Add(c);
        if (icone == null) b.Content = texto;
        else
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            sp.Children.Add(Icones.Criar(icone, 16, corIcone));
            if (texto != "") sp.Children.Add(new TextBlock { Text = texto, VerticalAlignment = VerticalAlignment.Center });
            b.Content = sp;
        }
        return b;
    }

    public static Button BtnIco(string icone, string dica, string classe = "", double px = 16, string cor = "Fg")
    {
        var b = new Button { Content = Icones.Criar(icone, px, cor) };
        b.Classes.Add("b");
        b.Classes.Add("ico");
        foreach (var c in classe.Split(' ', StringSplitOptions.RemoveEmptyEntries)) b.Classes.Add(c);
        ToolTip.SetTip(b, dica);
        Avalonia.Automation.AutomationProperties.SetName(b, dica);
        return b;
    }

    /// <summary>Rótulo 12 --mut em cima do controle (a .campo da ui.html), com dica opcional embaixo.</summary>
    public static Control Campo(string rotulo, Control controle, TextBlock? info = null, string? ajuda = null)
    {
        var sp = new StackPanel { Spacing = 6 };
        var cab = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        cab.Children.Add(Txt(rotulo, 12, "Mut", FontWeight.Medium));
        if (info != null) cab.Children.Add(info);
        sp.Children.Add(cab);
        sp.Children.Add(controle);
        if (ajuda != null) sp.Children.Add(Paragrafo(ajuda, 12, "Off"));
        return sp;
    }

    /// <summary>Grade de N colunas iguais, 16 px de espaço (a .grade da ui.html).</summary>
    public static Grid Grade(int colunas, params Control[] itens)
    {
        var g = new Grid();
        for (var c = 0; c < colunas; c++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            if (c < colunas - 1) g.ColumnDefinitions.Add(new ColumnDefinition(16, GridUnitType.Pixel));
        }
        var linhas = (itens.Length + colunas - 1) / colunas;
        for (var r = 0; r < linhas; r++)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (r < linhas - 1) g.RowDefinitions.Add(new RowDefinition(16, GridUnitType.Pixel));
        }
        for (var i = 0; i < itens.Length; i++)
        {
            Grid.SetColumn(itens[i], i % colunas * 2);
            Grid.SetRow(itens[i], i / colunas * 2);
            g.Children.Add(itens[i]);
        }
        return g;
    }

    /// <summary>Seção com título 15/600 (+ complemento --mut) e itens com 14 px entre eles (o .bloco).</summary>
    public static StackPanel Bloco(string titulo, string? sub, Control? direita, params Control[] itens)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 30), MaxWidth = 1100, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Stretch };
        var cab = new DockPanel { LastChildFill = true };
        if (direita != null) { DockPanel.SetDock(direita, Dock.Right); cab.Children.Add(direita); }
        var tit = new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        tit.Res(TextBlock.ForegroundProperty, "Fg");
        tit.Inlines!.Add(new Avalonia.Controls.Documents.Run(titulo));
        if (sub != null)
            tit.Inlines.Add(new Avalonia.Controls.Documents.Run("  ·  " + sub) { FontWeight = FontWeight.Normal, FontSize = 13 }
                .Res(Avalonia.Controls.Documents.TextElement.ForegroundProperty, "Mut"));
        cab.Children.Add(tit);
        sp.Children.Add(cab);
        foreach (var i in itens) sp.Children.Add(i);
        return sp;
    }

    /// <summary>Cartão (--surf, borda --line, raio 14).</summary>
    public static Border Cartao(Control filho, double raio = 14, Thickness? padding = null) =>
        new Border { CornerRadius = new CornerRadius(raio), BorderThickness = new Thickness(1), Padding = padding ?? new Thickness(18, 16), Child = filho }
            .Res(Border.BackgroundProperty, "Surf").Res(Border.BorderBrushProperty, "Line");

    public static Ellipse Ponto(double d = 8) =>
        new Ellipse { Width = d, Height = d, VerticalAlignment = VerticalAlignment.Center }.Res(Shape.FillProperty, "Off");

    /// <summary>Recurso de cor do estado: parado · noar · debug · ocupado · erro (tokens do design visual do Pitstop).</summary>
    public static string CorEstado(string k) => k switch { "noar" => "Ok", "debug" => "Dbg", "ocupado" => "Warn", "erro" => "Err", _ => "Off" };

    public static void PintarPonto(Ellipse e, string k) => e.Res(Shape.FillProperty, CorEstado(k));

    /// <summary>Ícone do tipo em quadrado --soft 36 px com traço --acc (menu Novo perfil, diálogo).</summary>
    public static Border IconeTipo(string icone, bool perigo = false, double lado = 36) =>
        new Border { Width = lado, Height = lado, CornerRadius = new CornerRadius(lado * 0.28), Child = Icones.Criar(icone, lado / 2, perigo ? "Err" : "AccTxt") }
            .Res(Border.BackgroundProperty, "Soft");

    /// <summary>Campo com botões à direita (dentro da borda): ▾ opções, 📁 procurar.</summary>
    public static Control ComBotoes(TextBox campo, params Button[] botoes)
    {
        var g = new Grid();
        g.Children.Add(campo);
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0), Spacing = 2 };
        foreach (var b in botoes)
        {
            b.Width = b.Height = 34;
            b.MinHeight = 34;
            b.Focusable = false;
            sp.Children.Add(b);
        }
        campo.Padding = new Thickness(12, 0, 8 + botoes.Length * 36, 0);
        g.Children.Add(sp);
        return g;
    }

    /// <summary>
    /// Botão ▾ com lista de sugestões (classes main, scripts, versões, Tomcats): o texto segue editável; as opções são
    /// carregadas na hora (podem ler disco). Escolher preenche o campo.
    /// </summary>
    public static Button Opcoes(TextBox campo, Func<Task<List<(string Valor, string Rotulo)>>> opcoes)
    {
        var b = BtnIco("seta", "Opções", "fantasma", 16, "Mut");
        b.Click += async (_, _) =>
        {
            var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
            List<(string Valor, string Rotulo)> l;
            try { l = await opcoes(); }
            catch (Exception ex) { l = []; menu.Items.Add(new MenuItem { Header = ex.Message, IsEnabled = false }); }
            if (l.Count == 0 && menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "nenhuma opção", IsEnabled = false });
            foreach (var (valor, rotulo) in l.Take(60))
            {
                var sp = new StackPanel { Spacing = 2, MaxWidth = 640 };
                sp.Children.Add(Txt(valor, 13, "Fg", mono: true));
                if (rotulo != "") sp.Children.Add(Txt(rotulo, 12, "Mut"));
                var mi = new MenuItem { Header = sp };
                mi.Click += (_, _) => { campo.Text = valor; campo.Focus(); campo.CaretIndex = valor.Length; };
                menu.Items.Add(mi);
            }
            menu.ShowAt(campo);
        };
        return b;
    }

    /// <summary>Botão 📁 que abre o seletor do sistema (pasta, ou arquivo com as extensões dadas) e preenche o campo.</summary>
    public static Button Procurar(TextBox campo, string titulo, bool pasta = true, params string[] extensoes)
    {
        var b = BtnIco(pasta ? "pasta" : "arquivo", pasta ? "Escolher pasta…" : "Escolher arquivo…", "fantasma", 16, "Mut");
        b.Click += async (_, _) =>
        {
            var sp = TopLevel.GetTopLevel(campo)?.StorageProvider;
            if (sp == null) return;
            IStorageFolder? inicio = null;
            var atual = campo.Text?.Trim() ?? "";
            var dirAtual = Directory.Exists(atual) ? atual : File.Exists(atual) ? System.IO.Path.GetDirectoryName(atual) : null;
            if (dirAtual != null) inicio = await sp.TryGetFolderFromPathAsync(dirAtual);
            string? escolhido;
            if (pasta)
            {
                var r = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = titulo, SuggestedStartLocation = inicio });
                escolhido = r.FirstOrDefault()?.TryGetLocalPath();
            }
            else
            {
                var r = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = titulo, SuggestedStartLocation = inicio,
                    FileTypeFilter = extensoes.Length == 0 ? null : [new FilePickerFileType(string.Join(", ", extensoes)) { Patterns = extensoes.Select(e => "*" + e).ToArray() }],
                });
                escolhido = r.FirstOrDefault()?.TryGetLocalPath();
            }
            if (escolhido != null) { campo.Text = escolhido; campo.Focus(); }
        };
        return b;
    }

    /// <summary>Texto que vira link (abre no navegador).</summary>
    public static Button Link(string texto, string url)
    {
        var b = Btn(texto, "link");
        b.Padding = new Thickness(0);
        b.MinHeight = 0;
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.Click += (_, _) => Proc.AbrirUrl(url);
        ToolTip.SetTip(b, url);
        return b;
    }

    public static void Mostrar(Control c, bool sim) => c.IsVisible = sim;

    public static IBinding Recurso(string chave) => new DynamicResourceExtension(chave);
}
