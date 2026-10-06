# Avisos de terceiros

O Pitstop e licenciado sob Apache License 2.0. Os componentes abaixo sao de terceiros e mantem suas proprias licencas. Os arquivos originais de licenca/NOTICE que acompanham a distribuicao ficam em `third-party/`.

| Componente redistribuido | Versao atual | Licenca / aviso incluido |
|---|---:|---|
| .NET Runtime | 10.0.12 | MIT (`third-party/dotnet/LICENSE.TXT`) + mapeamento Windows (`LICENSE-INFORMATION-WINDOWS.md`) + termos Microsoft .NET Library (`DOTNET-LIBRARY-LICENSE.html`) + `THIRD-PARTY-NOTICES.TXT` |
| ASP.NET Core Runtime | 10.0.12 | `third-party/aspnetcore/LICENSE.txt` e `THIRD-PARTY-NOTICES.txt` |
| .NET Windows Desktop Runtime | 10.0.12 | WinForms/WPF upstream em `third-party/windowsdesktop/`; binarios da distribuicao Windows seguem tambem o mapeamento em `third-party/dotnet/LICENSE-INFORMATION-WINDOWS.md` |
| Avalonia | 11.3.22 | MIT — `third-party/avalonia/LICENSE.md` e `NOTICE.md` |
| Avalonia ANGLE natives (Windows) | 2.1.25547.20250602 | BSD-3-Clause — `third-party/angle/LICENSE` |
| SkiaSharp | 2.88.9 | MIT — `third-party/skiasharp/LICENSE.md`; dependencias nativas em `EXTERNAL-DEPENDENCY-INFO.txt` |
| Skia | transitiva via SkiaSharp | BSD-3-Clause — `third-party/skia/LICENSE` |
| HarfBuzzSharp / HarfBuzz | 8.3.1.1 / 8.3.1 | MIT / Old MIT — `third-party/harfbuzz/COPYING` |
| MicroCom.Runtime | 0.11.0 | MIT — `third-party/microcom/LICENSE` |
| Tmds.DBus.Protocol | 0.21.3 | MIT — `third-party/tmds-dbus/LICENSE` |
| Space Grotesk | versao distribuida no projeto | SIL Open Font License 1.1 — `web/fonts/OFL-SpaceGrotesk.txt` |
| JetBrains Mono | versao distribuida no projeto | SIL Open Font License 1.1 — `web/fonts/OFL-JetBrainsMono.txt` |

### Regra especifica do .NET no Windows

O pacote `win-x64` atual contem `coreclr.dll` e `Microsoft.DiaSymReader.Native.amd64.dll`, que a Microsoft inclui entre os binarios cobertos pela Microsoft .NET Library License. O arquivo `LICENSE-INFORMATION-WINDOWS.md` e uma copia do mapeamento oficial mantido em `dotnet/core`; `DOTNET-LIBRARY-LICENSE.html` preserva os termos oficiais publicados pela Microsoft.

`third-party/VERSIONS.json` registra as versoes esperadas. O smoke test do pacote confere as dependencias presentes no `Pitstop.deps.json`, os arquivos de licenca obrigatorios e o mapeamento dos binarios Windows. Um binario com licenca nova/desconhecida deve interromper a release ate revisao.

### Ferramentas baixadas sob demanda

JDK, Apache Tomcat, Apache Maven e Node.js nao sao embutidos nos binarios publicados do Pitstop. Quando o usuario escolhe o download no instalador, eles sao obtidos diretamente dos distribuidores oficiais e extraidos localmente, mantendo os arquivos de licenca/NOTICE presentes nas distribuicoes upstream.

- Eclipse Temurin/OpenJDK: GPLv2 + Classpath Exception e notices aplicaveis da distribuicao.
- Apache Maven e Apache Tomcat: Apache License 2.0 e respectivos NOTICE upstream.
- Node.js: MIT para o projeto Node.js, alem das licencas dos componentes de terceiros e do npm incluidos na distribuicao oficial.