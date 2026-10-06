using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Pitstop.App;

public partial class App : Application
{
    Bandeja? bandeja;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) => { Registro.Erro(e.Exception); e.Handled = true; };
        Prefs.Carregar();
        AplicarTema();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime vida)
        {
            bandeja = new Bandeja(this, vida);
            bandeja.Iniciar();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Tema fixado pelo botão da lateral (cache/tela.json) ou o do sistema.</summary>
    public void AplicarTema() => RequestedThemeVariant = Prefs.Atual.Tema switch
    {
        "claro" => ThemeVariant.Light,
        "escuro" => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    public bool Escuro => ActualThemeVariant == ThemeVariant.Dark;

    public static App Atual => (App)Current!;
}
