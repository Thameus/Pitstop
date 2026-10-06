# Licencas

## Pitstop

Copyright 2026 Matheus Silva.

O codigo do Pitstop usa a Apache License 2.0. O texto integral esta em `LICENSE` e o aviso do projeto em `NOTICE`.
Os componentes de terceiros mantem suas proprias licencas e avisos; a licenca do Pitstop nao substitui nem relicencia essas dependencias.

### O que a Apache 2.0 permite

A Apache 2.0 permite uso, modificacao, redistribuicao e uso comercial do codigo do Pitstop, desde que as condicoes da licenca sejam respeitadas, incluindo preservacao dos avisos aplicaveis. Ela nao transforma dependencias de terceiros em Apache 2.0 e nao concede direitos sobre marcas de terceiros.

## Componentes redistribuidos

O release e self-contained e inclui runtime .NET e bibliotecas de terceiros. Os textos upstream usados para a distribuicao acompanham o pacote em `third-party/`, com as versoes registradas em `third-party/VERSIONS.json`.

| Componente | Versao | Uso | Licenca / avisos |
|---|---:|---|---|
| .NET Runtime | 10.0.12 | runtime self-contained | MIT para os arquivos cobertos por MIT; no Windows, binarios especificos usam Microsoft .NET Library License; `third-party/dotnet/` |
| ASP.NET Core Runtime | 10.0.12 | Kestrel/API local | MIT + notices upstream; `third-party/aspnetcore/` |
| .NET Windows Desktop Runtime | 10.0.12 | instalador grafico Windows (WinForms) | componentes upstream + regras da distribuicao .NET para Windows; `third-party/windowsdesktop/` |
| Avalonia | 11.3.22 | janela e bandeja | MIT; `third-party/avalonia/` |
| Avalonia ANGLE Windows natives | 2.1.25547.20250602 | aceleracao grafica | BSD-3-Clause; `third-party/angle/LICENSE` |
| SkiaSharp | 2.88.9 | renderizacao | MIT; `third-party/skiasharp/` |
| Skia | transitiva | motor grafico | BSD-3-Clause; `third-party/skia/LICENSE` |
| HarfBuzzSharp / HarfBuzz | 8.3.1.1 / 8.3.1 | shaping de texto | MIT / Old MIT; `third-party/harfbuzz/COPYING` |
| MicroCom.Runtime | 0.11.0 | interop do Avalonia | MIT; `third-party/microcom/LICENSE` |
| Tmds.DBus.Protocol | 0.21.3 | D-Bus no Linux | MIT; `third-party/tmds-dbus/LICENSE` |
| Space Grotesk | incluida no projeto | fonte da interface | SIL OFL 1.1; `web/fonts/OFL-SpaceGrotesk.txt` |
| JetBrains Mono | incluida no projeto | fonte de logs/caminhos | SIL OFL 1.1; `web/fonts/OFL-JetBrainsMono.txt` |

## .NET no Windows

A distribuicao Windows do .NET usa mais de uma licenca. O arquivo oficial `third-party/dotnet/LICENSE-INFORMATION-WINDOWS.md` registra o mapeamento publicado pela Microsoft. Os termos oficiais da Microsoft .NET Library License sao preservados no snapshot `third-party/dotnet/DOTNET-LIBRARY-LICENSE.html`, obtido de `https://dotnet.microsoft.com/en-us/dotnet_library_license.htm`.

No pacote Windows atual do Pitstop, `coreclr.dll` e `Microsoft.DiaSymReader.Native.amd64.dll` estao entre os binarios cobertos pela Microsoft .NET Library License. Os demais arquivos seguem a licenca indicada pelo mapeamento oficial da Microsoft e pelos notices upstream.

O smoke de release verifica a presenca desses arquivos e dos respectivos avisos. Se uma futura atualizacao passar a incluir um binario com outra licenca, como `D3DCompiler_47_cor3.dll` sob Windows SDK License, a release falha ate que a nova obrigacao seja revisada e documentada.

Veja tambem `THIRD-PARTY-NOTICES.md`. O smoke do pacote confere as dependencias presentes no `Pitstop.deps.json` para evitar publicar binarios com notices de outra versao.

## Ferramentas externas

JDK, Apache Maven, Apache Tomcat e Node.js nao sao embutidos nos binarios publicados do Pitstop. O instalador pode, opcionalmente, baixa-los diretamente dos distribuidores oficiais para uma pasta local do usuario; os arquivos baixados mantem suas licencas e notices upstream. Tambem e possivel apontar instalacoes ja existentes.

| Ferramenta opcional | Origem atual | Licenciamento principal | Como o Pitstop trata |
|---|---|---|---|
| Eclipse Temurin / OpenJDK 21 | Eclipse Adoptium | OpenJDK GPLv2 + Classpath Exception, alem de notices dos componentes incluidos | download oficial, sem reempacotar no release do Pitstop |
| Apache Maven 3.10.0 | Apache Software Foundation | Apache License 2.0 | download oficial com LICENSE/NOTICE upstream |
| Apache Tomcat 9/10/11 | Apache Software Foundation | Apache License 2.0 | download oficial com LICENSE/NOTICE upstream |
| Node.js LTS | OpenJS / nodejs.org | Node.js sob MIT, com componentes de terceiros e npm sob termos proprios | download oficial preservando os arquivos de licenca da distribuicao |

`Apache Tomcat` e `Apache Maven` sao nomes/marcas da Apache Software Foundation. O Pitstop usa esses nomes apenas de forma descritiva e nao se apresenta como produto oficial ou endossado pela Apache.