using System.Drawing;

namespace Pitstop.Setup;

sealed class SetupForm : Form
{
    readonly Panel host = new() { Dock = DockStyle.Fill, Padding = new Padding(36, 20, 36, 12) };
    readonly Label stepLabel = new() { AutoSize = true, ForeColor = Color.DimGray, Font = new Font("Segoe UI", 9F) };
    readonly Button back = new() { Text = "Voltar", Width = 100, Height = 34 };
    readonly Button next = new() { Text = "Continuar", Width = 120, Height = 34 };
    readonly Button cancel = new() { Text = "Cancelar", Width = 100, Height = 34 };
    readonly List<Control> pages = new();
    int page;
    bool installing;
    bool finished;

    readonly TextBox destination = new() { Width = 520 };
    readonly CheckBox desktop = new() { Text = "Criar atalho na Área de Trabalho", AutoSize = true };
    readonly CheckBox addPath = new() { Text = "Adicionar o comando pit ao PATH do usuário", AutoSize = true, Checked = true };
    readonly CheckBox launch = new() { Text = "Abrir o Pitstop ao concluir", AutoSize = true, Checked = true };

    readonly TextBox jdk = new() { Width = 410 };
    readonly TextBox maven = new() { Width = 410 };
    readonly TextBox tomcat = new() { Width = 410 };
    readonly TextBox node = new() { Width = 410 };
    readonly TextBox projects = new() { Width = 410 };
    readonly CheckBox downloadJdk = new() { Text = "Baixar JDK 21 (Temurin)", AutoSize = true };
    readonly CheckBox downloadMaven = new() { Text = "Baixar Maven 3.10.0", AutoSize = true };
    readonly CheckBox downloadNode = new() { Text = "Baixar Node.js LTS", AutoSize = true };
    readonly ComboBox downloadTomcat = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };

    readonly Label summary = new() { Dock = DockStyle.Fill, AutoSize = false, Font = new Font("Segoe UI", 10F), Padding = new Padding(0, 10, 0, 0) };
    readonly TextBox progress = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, Font = new Font("Consolas", 9F) };
    readonly Label progressTitle = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 15F), Text = "Instalando..." };

    public SetupForm()
    {
        Text = "Pitstop - Instalação";
        Width = 820;
        Height = 650;
        MinimumSize = new Size(760, 590);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5F);
        BackColor = Color.White;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        destination.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Pitstop");
        jdk.Text = Environment.GetEnvironmentVariable("JAVA_HOME") ?? "";
        maven.Text = Environment.GetEnvironmentVariable("MAVEN_HOME") ?? Environment.GetEnvironmentVariable("M2_HOME") ?? "";
        tomcat.Text = Environment.GetEnvironmentVariable("CATALINA_HOME") ?? "";
        node.Text = Environment.GetEnvironmentVariable("NODE_HOME") ?? "";
        projects.Text = Environment.GetEnvironmentVariable("PROJETOS_DIR") ?? "";

        downloadTomcat.Items.AddRange(new object[]
        {
            "Não baixar Tomcat",
            "Tomcat 9.0.122 (Java EE / javax)",
            "Tomcat 10.1.60 (Jakarta EE 10)",
            "Tomcat 11.0.26 (Jakarta EE 11)"
        });
        downloadTomcat.SelectedIndex = 0;

        pages.Add(WelcomePage());
        pages.Add(LocationPage());
        pages.Add(ToolsPage());
        pages.Add(SummaryPage());
        pages.Add(ProgressPage());

        var header = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(36, 18, 36, 0), BackColor = Color.FromArgb(247, 247, 249) };
        var brand = new Label { Text = "PITSTOP", AutoSize = true, Font = new Font("Segoe UI Semibold", 16F), ForeColor = Color.FromArgb(86, 45, 146), Location = new Point(36, 18) };
        stepLabel.Location = new Point(38, 49);
        header.Controls.Add(brand);
        header.Controls.Add(stepLabel);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(36, 12, 36, 12), BackColor = Color.FromArgb(247, 247, 249) };
        cancel.Location = new Point(36, 15);
        back.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        next.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        back.Location = new Point(Width - 36 - 100 - 130, 15);
        next.Location = new Point(Width - 36 - 120, 15);
        footer.Resize += (_, _) =>
        {
            next.Left = footer.ClientSize.Width - 36 - next.Width;
            back.Left = next.Left - 12 - back.Width;
        };
        footer.Controls.Add(cancel);
        footer.Controls.Add(back);
        footer.Controls.Add(next);

        Controls.Add(host);
        Controls.Add(header);
        Controls.Add(footer);

        back.Click += (_, _) => ShowPage(page - 1);
        next.Click += async (_, _) => await NextAsync();
        cancel.Click += (_, _) => Close();
        FormClosing += (_, e) => { if (installing) e.Cancel = true; };

        downloadJdk.CheckedChanged += (_, _) => ToggleTool(jdk, downloadJdk.Checked);
        downloadMaven.CheckedChanged += (_, _) => ToggleTool(maven, downloadMaven.Checked);
        downloadNode.CheckedChanged += (_, _) => ToggleTool(node, downloadNode.Checked);
        downloadTomcat.SelectedIndexChanged += (_, _) => ToggleTool(tomcat, downloadTomcat.SelectedIndex > 0);

        ShowPage(0);
    }
    Control WelcomePage()
    {
        var p = NewPage();
        var title = Title("Instale o Pitstop em poucos passos", 24F);
        title.Top = 30;
        p.Controls.Add(title);
        var body = TextBlock(
            "O instalador copia o Pitstop, cria atalhos e pode preparar as ferramentas que seus perfis usam.\n\n" +
            "O .NET já vem embutido no Pitstop: você não precisa instalar runtime separado. JDK, Tomcat, Maven e Node.js são opcionais e só são necessários para os tipos de perfil que usam essas tecnologias.",
            520);
        body.Top = 90;
        p.Controls.Add(body);

        var note = TextBlock(
            "Tudo é instalado no seu usuário por padrão, sem precisar de administrador. Atualizar por cima preserva seus perfis, ajustes, cache, logs e bases.",
            520);
        note.Top = 230;
        note.ForeColor = Color.FromArgb(70, 70, 70);
        p.Controls.Add(note);
        return p;
    }

    Control LocationPage()
    {
        var p = NewPage();
        var title = Title("Onde instalar", 20F);
        p.Controls.Add(title);

        var label = LabelAt("Pasta de instalação", 0, 56);
        p.Controls.Add(label);
        destination.Location = new Point(0, 82);
        p.Controls.Add(destination);
        var browse = BrowseButton(destination);
        browse.Location = new Point(530, 80);
        p.Controls.Add(browse);

        desktop.Location = new Point(0, 132);
        addPath.Location = new Point(0, 166);
        launch.Location = new Point(0, 200);
        p.Controls.Add(desktop);
        p.Controls.Add(addPath);
        p.Controls.Add(launch);

        var note = TextBlock("Recomendado: deixe a pasta padrão em %LOCALAPPDATA%\\Programs\\Pitstop. Se escolher a raiz de uma unidade (ex.: D:\\), o Pitstop usará automaticamente D:\\Pitstop.", 650);
        note.Top = 250;
        note.ForeColor = Color.DimGray;
        p.Controls.Add(note);
        return p;
    }

    Control ToolsPage()
    {
        var p = NewPage();
        p.AutoScroll = true;
        p.Controls.Add(Title("Ferramentas opcionais", 20F));

        var info = TextBlock("Aponte uma instalação que você já possui ou marque para o Pitstop baixar uma cópia portátil. Você pode deixar tudo vazio e configurar depois no próprio aplicativo.", 650);
        info.Top = 42;
        p.Controls.Add(info);

        AddToolRow(p, "JDK / Java", jdk, downloadJdk, 105);
        AddToolRow(p, "Apache Maven", maven, downloadMaven, 180);

        var tomLabel = LabelAt("Apache Tomcat", 0, 255);
        p.Controls.Add(tomLabel);
        tomcat.Location = new Point(0, 280);
        p.Controls.Add(tomcat);
        var tomBrowse = BrowseButton(tomcat);
        tomBrowse.Location = new Point(420, 278);
        p.Controls.Add(tomBrowse);
        downloadTomcat.Location = new Point(500, 278);
        p.Controls.Add(downloadTomcat);

        AddToolRow(p, "Node.js", node, downloadNode, 330);

        var projLabel = LabelAt("Pasta dos projetos (opcional)", 0, 405);
        p.Controls.Add(projLabel);
        projects.Location = new Point(0, 430);
        p.Controls.Add(projects);
        var projBrowse = BrowseButton(projects);
        projBrowse.Location = new Point(420, 428);
        p.Controls.Add(projBrowse);

        var note = TextBlock("Tomcat 9 mantém compatibilidade com aplicações javax.*; Tomcat 10/11 usam Jakarta. Escolha conforme o projeto que você executa.", 650);
        note.Top = 475;
        note.ForeColor = Color.DimGray;
        p.Controls.Add(note);
        return p;
    }

    void AddToolRow(Control p, string name, TextBox box, CheckBox download, int top)
    {
        p.Controls.Add(LabelAt(name, 0, top));
        box.Location = new Point(0, top + 25);
        p.Controls.Add(box);
        var browse = BrowseButton(box);
        browse.Location = new Point(420, top + 23);
        p.Controls.Add(browse);
        download.Location = new Point(500, top + 26);
        p.Controls.Add(download);
        download.Tag = browse;
    }
    Control SummaryPage()
    {
        var p = NewPage();
        p.Controls.Add(Title("Pronto para instalar", 20F));
        summary.Location = new Point(0, 46);
        summary.Size = new Size(680, 360);
        p.Controls.Add(summary);
        return p;
    }

    Control ProgressPage()
    {
        var p = NewPage();
        progressTitle.Location = new Point(0, 0);
        p.Controls.Add(progressTitle);
        progress.Location = new Point(0, 44);
        progress.Size = new Size(690, 420);
        progress.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        p.Controls.Add(progress);
        return p;
    }

    Panel NewPage() => new() { Dock = DockStyle.Fill, BackColor = Color.White };

    static Label Title(string text, float size) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", size),
        ForeColor = Color.FromArgb(35, 35, 40),
        Location = new Point(0, 0)
    };

    static Label LabelAt(string text, int left, int top) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(left, top),
        Font = new Font("Segoe UI Semibold", 9.5F)
    };

    static Label TextBlock(string text, int width) => new()
    {
        Text = text,
        AutoSize = false,
        Width = width,
        Height = 140,
        Font = new Font("Segoe UI", 10.5F),
        ForeColor = Color.FromArgb(45, 45, 50)
    };

    Button BrowseButton(TextBox target)
    {
        var b = new Button { Text = "Procurar", Width = 72, Height = 29 };
        b.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog
            {
                ShowNewFolderButton = true,
                SelectedPath = Directory.Exists(target.Text) ? target.Text : ""
            };
            if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.SelectedPath;
        };
        target.Tag = b;
        return b;
    }

    void ToggleTool(TextBox box, bool download)
    {
        box.Enabled = !download;
        if (box.Tag is Button browse) browse.Enabled = !download;
    }

    void ShowPage(int index)
    {
        if (index < 0 || index >= pages.Count) return;
        page = index;
        host.Controls.Clear();
        host.Controls.Add(pages[index]);
        back.Visible = index > 0 && index < 4;
        cancel.Visible = index < 4;
        next.Visible = index < 4;
        next.Enabled = true;
        next.Text = index == 3 ? "Instalar" : "Continuar";
        stepLabel.Text = index switch
        {
            0 => "1 de 4 · Boas-vindas",
            1 => "2 de 4 · Instalação",
            2 => "3 de 4 · Ferramentas",
            3 => "4 de 4 · Revisão",
            _ => "Instalando"
        };
        if (index == 3) summary.Text = BuildSummary();
    }

    string BuildSummary()
    {
        var lines = new List<string>
        {
            $"Destino: {destination.Text}",
            $"Atalho na Área de Trabalho: {(desktop.Checked ? "sim" : "não")}",
            $"Comando pit no PATH: {(addPath.Checked ? "sim" : "não")}",
            "",
            "Ferramentas:"
        };
        lines.Add("• JDK: " + ToolSummary(jdk, downloadJdk.Checked, "baixar Temurin 21"));
        lines.Add("• Maven: " + ToolSummary(maven, downloadMaven.Checked, "baixar 3.10.0"));
        lines.Add("• Tomcat: " + (downloadTomcat.SelectedIndex > 0 ? downloadTomcat.SelectedItem!.ToString() : EmptyOr(tomcat.Text, "configurar depois")));
        lines.Add("• Node.js: " + ToolSummary(node, downloadNode.Checked, "baixar LTS"));
        lines.Add("• Projetos: " + EmptyOr(projects.Text, "configurar depois"));
        lines.Add("");
        lines.Add("Se uma ferramenta ficar vazia, nada impede a instalação: o caminho poderá ser definido depois em Ajustes ou no próprio perfil.");
        return string.Join(Environment.NewLine, lines);
    }

    static string ToolSummary(TextBox box, bool download, string whenDownload) =>
        download ? whenDownload : EmptyOr(box.Text, "configurar depois");

    static string EmptyOr(string? value, string empty) =>
        string.IsNullOrWhiteSpace(value) ? empty : value.Trim();
    async Task NextAsync()
    {
        if (finished) { Close(); return; }
        if (page < 3)
        {
            if (page == 1 && string.IsNullOrWhiteSpace(destination.Text))
            {
                MessageBox.Show(this, "Informe a pasta de instalação.", "Pitstop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (page == 1)
            {
                try
                {
                    var normalizado = InstallerEngine.NormalizeDestination(destination.Text);
                    if (!string.Equals(normalizado, destination.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                        destination.Text = normalizado;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Pasta de instalação inválida: " + ex.Message, "Pitstop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            ShowPage(page + 1);
            return;
        }

        var opts = BuildOptions();
        ShowPage(4);
        installing = true;
        cancel.Visible = false;
        back.Visible = false;
        next.Visible = false;
        progress.Clear();
        AppendLog("Pitstop Setup");
        AppendLog("Destino: " + opts.Destination);
        AppendLog("");

        try
        {
            await InstallerEngine.InstallAsync(opts, AppendLog);
            installing = false;
            finished = true;
            progressTitle.Text = "Pitstop instalado";
            AppendLog("");
            AppendLog("Instalação concluída com sucesso.");
            next.Visible = true;
            next.Enabled = true;
            next.Text = "Concluir";
            stepLabel.Text = "Concluído";
        }
        catch (Exception ex)
        {
            installing = false;
            progressTitle.Text = "A instalação não foi concluída";
            AppendLog("");
            AppendLog("ERRO: " + ex.Message);
            cancel.Visible = true;
            cancel.Text = "Fechar";
            back.Visible = true;
            back.Enabled = true;
            back.Click -= (_, _) => ShowPage(page - 1);
            MessageBox.Show(this, ex.Message, "Falha na instalação", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    InstallOptions BuildOptions()
    {
        var tc = downloadTomcat.SelectedIndex switch
        {
            1 => "9",
            2 => "10",
            3 => "11",
            _ => ""
        };
        return new InstallOptions
        {
            Destination = InstallerEngine.NormalizeDestination(destination.Text),
            DesktopShortcut = desktop.Checked,
            AddToPath = addPath.Checked,
            LaunchAfterInstall = launch.Checked,
            DownloadJdk = downloadJdk.Checked,
            DownloadMaven = downloadMaven.Checked,
            DownloadNode = downloadNode.Checked,
            DownloadTomcat = tc,
            JdkHome = jdk.Text.Trim(),
            MavenHome = maven.Text.Trim(),
            TomcatHome = tomcat.Text.Trim(),
            NodeHome = node.Text.Trim(),
            ProjectsDir = projects.Text.Trim()
        };
    }

    void AppendLog(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), line);
            return;
        }
        progress.AppendText(line + Environment.NewLine);
        progress.SelectionStart = progress.TextLength;
        progress.ScrollToCaret();
    }
}
