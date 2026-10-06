# Avisos de terceiros

O Pitstop é licenciado sob Apache License 2.0. Os componentes abaixo são de terceiros e mantêm as suas próprias
licenças. Os arquivos originais de licença/NOTICE que acompanham a distribuição ficam em `third-party/`.

| Componente redistribuído | Versão atual | Licença / aviso incluído |
|---|---:|---|
| .NET Runtime | 10.0.12 | `third-party/dotnet/LICENSE.TXT` e `THIRD-PARTY-NOTICES.TXT` |
| ASP.NET Core Runtime | 10.0.12 | `third-party/aspnetcore/LICENSE.txt` e `THIRD-PARTY-NOTICES.txt` |
| .NET Windows Desktop Runtime | 10.0.12 | WinForms/WPF upstream — `third-party/windowsdesktop/winforms/` e `third-party/windowsdesktop/wpf/` |
| Avalonia | 11.3.22 | MIT — `third-party/avalonia/LICENSE.md` e `NOTICE.md` |
| Avalonia ANGLE natives (Windows) | 2.1.25547.20250602 | BSD-3-Clause — `third-party/angle/LICENSE` |
| SkiaSharp | 2.88.9 | MIT — `third-party/skiasharp/LICENSE.md`; dependências nativas em `EXTERNAL-DEPENDENCY-INFO.txt` |
| Skia | transitiva via SkiaSharp | BSD-3-Clause — `third-party/skia/LICENSE` |
| HarfBuzzSharp / HarfBuzz | 8.3.1.1 / 8.3.1 | MIT / Old MIT — `third-party/harfbuzz/COPYING` |
| MicroCom.Runtime | 0.11.0 | MIT — `third-party/microcom/LICENSE` |
| Tmds.DBus.Protocol | 0.21.3 | MIT — `third-party/tmds-dbus/LICENSE` |
| Space Grotesk | versão distribuída no projeto | SIL Open Font License 1.1 — `web/fonts/OFL-SpaceGrotesk.txt` |
| JetBrains Mono | versão distribuída no projeto | SIL Open Font License 1.1 — `web/fonts/OFL-JetBrainsMono.txt` |

`third-party/VERSIONS.json` registra as versões esperadas. O smoke test do pacote confere as dependências presentes
no `Pitstop.deps.json` para impedir que uma atualização de runtime/biblioteca seja publicada com notices antigos.

JDK, Tomcat, Maven e Node.js **não são embutidos** nos binários publicados do Pitstop. Quando o usuário escolhe
a opção de download no instalador, eles são obtidos diretamente dos distribuidores oficiais e extraídos localmente,
mantendo os arquivos de licença/NOTICE presentes nas distribuições upstream.
