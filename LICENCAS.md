# Licencas

## Pitstop

Copyright 2026 Matheus Silva.

O codigo do Pitstop usa a Apache License 2.0. O texto integral esta em `LICENSE` e o aviso do projeto em `NOTICE`.
Os componentes de terceiros mantem suas proprias licencas e avisos.

## Componentes redistribuidos

O release e self-contained e inclui runtime .NET e bibliotecas de terceiros. Os textos upstream usados para a
distribuicao acompanham o pacote em `third-party/`, com as versoes registradas em `third-party/VERSIONS.json`.

| Componente | Versao | Uso | Avisos |
|---|---:|---|---|
| .NET Runtime | 10.0.12 | runtime self-contained | `third-party/dotnet/` |
| ASP.NET Core Runtime | 10.0.12 | Kestrel/API local | `third-party/aspnetcore/` |
| .NET Windows Desktop Runtime | 10.0.12 | instalador gráfico Windows (WinForms) | upstream .NET; `third-party/windowsdesktop/` |
| Avalonia | 11.3.22 | janela e bandeja | MIT; `third-party/avalonia/` |
| Avalonia ANGLE Windows natives | 2.1.25547.20250602 | aceleracao grafica | BSD-3-Clause; `third-party/angle/LICENSE` |
| SkiaSharp | 2.88.9 | renderizacao | MIT; `third-party/skiasharp/` |
| Skia | transitiva | motor grafico | BSD-3-Clause; `third-party/skia/LICENSE` |
| HarfBuzzSharp / HarfBuzz | 8.3.1.1 / 8.3.1 | shaping de texto | MIT / Old MIT; `third-party/harfbuzz/COPYING` |
| MicroCom.Runtime | 0.11.0 | interop do Avalonia | MIT; `third-party/microcom/LICENSE` |
| Tmds.DBus.Protocol | 0.21.3 | D-Bus no Linux | MIT; `third-party/tmds-dbus/LICENSE` |
| Space Grotesk | incluida no projeto | fonte da interface | SIL OFL 1.1; `web/fonts/OFL-SpaceGrotesk.txt` |
| JetBrains Mono | incluida no projeto | fonte de logs/caminhos | SIL OFL 1.1; `web/fonts/OFL-JetBrainsMono.txt` |

Veja tambem `THIRD-PARTY-NOTICES.md`. O smoke do pacote confere as dependencias presentes no
`Pitstop.deps.json` para evitar publicar binarios com notices de outra versao.

## Ferramentas externas

O Pitstop nao inclui JDK, Tomcat, Maven ou Node.js dentro dos binarios publicados. O instalador pode,
opcionalmente, baixa-los diretamente dos distribuidores oficiais para uma pasta local do usuario; os arquivos
baixados mantem suas licencas e avisos upstream. Tambem e possivel apontar instalacoes ja existentes.
