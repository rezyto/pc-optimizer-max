# PC Max

**O otimizador de PC para jogos que prova que funciona.**

O PC Max encontra o que está realmente a travar o teu PC, aplica só ajustes seguros e reversíveis, e mede o ganho no teu PC em vez de prometer milagres.

## O que faz

- **Médico do PC:** deteta os problemas que mais custam desempenho: monitor a 60 Hz num ecrã de 144 Hz, RAM sem XMP/EXPO, RAM num só canal, portátil com bateria, calor e throttling, disco cheio, Wi-Fi em vez de cabo.
- **Modo jogo automático:** quando abres um jogo, ativa o plano de alto desempenho, dá prioridade ao jogo, baixa a latência do temporizador e pausa apps em segundo plano. Quando fechas, repõe tudo.
- **Monitor em tempo real:** temperaturas, carga e memória do CPU e da gráfica, com avisos em pop-up quando algo aquece demais. Numa temperatura crítica, desliga os ajustes sozinho.
- **Teste de ganho:** mede FPS e 1% lows antes e depois de um ajuste (com PresentMon). Se não melhorar, o ajuste é revertido.
- **Rede:** ping, jitter, perda de pacotes e teste de bufferbloat, com conselhos em português simples.
- **Limpeza e arranque:** ficheiros temporários, caches de shaders e programas que abrem com o Windows.
- **Tudo reversível:** cada alteração guarda o valor anterior, mesmo que o PC desligue a meio. Há um botão "Reverter tudo" e um ponto de restauro do Windows antes das alterações.
- **Arranca com o Windows** (na bandeja do sistema) e **atualiza-se sozinho**, sem nunca interromper um jogo.

## Instalar

Transfere `PcMax-Setup-<versão>.exe` da página [Releases](../../releases/latest) e corre-o. O instalador pede permissão de administrador e instala o PC Max para todos os utilizadores em `C:\Program Files\PC Max`, uma pasta que só os administradores podem alterar. A app também corre como administrador, porque precisa disso para mudar o plano de energia, as prioridades e o ecrã.

Se quiseres confirmar o ficheiro, cada versão traz um `PcMax-Setup-<versão>.exe.sha256`. No PowerShell: `Get-FileHash .\PcMax-Setup-<versão>.exe` e compara com o valor do ficheiro.

> Enquanto o instalador não tiver assinatura digital, o Windows SmartScreen pode mostrar "O Windows protegeu o computador". Clica em **Mais informações → Executar mesmo assim**. Quando as versões passarem a ser assinadas, a app só aceita atualizações assinadas pelo mesmo certificado.

Se tinhas a versão 0.1.x (instalada só para o teu utilizador, em `%LOCALAPPDATA%\PcMax`), instala esta versão por cima e depois desinstala a antiga em **Definições do Windows → Aplicações**. Ao abrir pela primeira vez, a versão nova copia as tuas definições e substitui a tarefa de arranque automático pela da cópia em Program Files.

Para desinstalar, usa **Definições do Windows → Aplicações**. O desinstalador fecha a app, retoma as apps que estivessem em pausa, reverte todos os ajustes ativos e remove o arranque automático. As definições e os registos ficam em `%ProgramData%\PcMax`, que podes apagar à mão.

### Atualizações

Com "Atualizações automáticas" ligado, a app procura uma versão nova 30 segundos depois de abrir e depois de 6 em 6 horas, nas Releases deste repositório no GitHub. Também podes carregar em **Definições → Procurar atualizações agora**. A versão nova é transferida e verificada (SHA-256 e, nas versões assinadas, a assinatura digital). Depois aparece um aviso com **Instalar agora**; se não carregares, instala-se no próximo arranque da app. Nada é verificado, transferido ou instalado enquanto estás a jogar. Só a cópia instalada em Program Files se atualiza.

Para ler a temperatura do CPU, instala o driver gratuito e assinado [PawnIO](https://pawnio.eu). A temperatura da gráfica funciona sem ele.

## Segurança

- Nunca muda o BIOS, voltagens ou frequências nesta versão. O overclock fica para uma versão futura, com testes de estabilidade.
- Nunca toca em processos do sistema, do antivírus ou de anti-cheats (Vanguard, EAC, BattlEye, FACEIT).
- Nunca usa prioridade "Realtime".
- Não envia dados para lado nenhum. As únicas ligações à internet são os pings que tu pedes, o teste de bufferbloat e a verificação de atualizações no GitHub.
- Os links (GitHub, ajuda) abrem no navegador como utilizador normal, nunca como administrador.

Vê [SECURITY.md](SECURITY.md) e [PRIVACY.md](PRIVACY.md).

## Desenvolvimento

Requisitos: .NET 8 SDK.

```bash
dotnet build PcMax.sln
dotnet test tests/PcMax.Core.Tests
dotnet run --project src/PcMax.App          # no Linux/macOS corre com dados simulados
```

Uma cópia de desenvolvimento (fora de Program Files) funciona, mas não se atualiza nem regista o arranque com o Windows. Nas builds Debug, `PCMAX_FAKE=1` faz a app correr com dados simulados também no Windows; nas builds Release essa variável é ignorada.

Para publicar uma versão nova, cria uma tag `vX.Y.Z` e faz push. O GitHub Actions corre os testes, publica a app (win-x64), junta o PresentMon (versão fixa, com a assinatura da Intel verificada), cria o instalador com o [Inno Setup](https://jrsoftware.org/isinfo.php) (`installer/pcmax.iss`) e publica-o em Releases com o ficheiro `.sha256`. Se existirem os segredos `SIGN_PFX_BASE64` e `SIGN_PFX_PASSWORD`, a app e o instalador são assinados.

Para criar o instalador à mão no Windows:

```powershell
dotnet publish src/PcMax.App -c Release -r win-x64 --self-contained true -p:Version=1.2.3 -o publish
iscc /DAppVersion=1.2.3 /DPublishDir=$PWD\publish installer\pcmax.iss
```

Estrutura: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
