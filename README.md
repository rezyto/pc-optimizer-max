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
- **Arranca com o Windows** (na bandeja do sistema) e **atualiza-se sozinho**.

## Instalar

Transfere `PcMax-win-Setup.exe` da página [Releases](../../releases/latest) e corre-o. A app pede permissão de administrador, que é necessária para mudar o plano de energia, as prioridades e o ecrã.

> O instalador ainda não tem assinatura digital, por isso o Windows SmartScreen pode mostrar "O Windows protegeu o computador". Clica em **Mais informações → Executar mesmo assim**.

Para ler a temperatura do CPU, instala o driver gratuito e assinado [PawnIO](https://pawnio.eu). A temperatura da gráfica funciona sem ele.

## Segurança

- Nunca muda o BIOS, voltagens ou frequências nesta versão. O overclock fica para uma versão futura, com testes de estabilidade.
- Nunca toca em processos do sistema, do antivírus ou de anti-cheats (Vanguard, EAC, BattlEye, FACEIT).
- Nunca usa prioridade "Realtime".
- Não envia dados para lado nenhum. As únicas ligações à internet são os pings que tu pedes, o teste de bufferbloat e a verificação de atualizações no GitHub.

Vê [SECURITY.md](SECURITY.md) e [PRIVACY.md](PRIVACY.md).

## Desenvolvimento

Requisitos: .NET 8 SDK.

```bash
dotnet build PcMax.sln
dotnet test tests/PcMax.Core.Tests
dotnet run --project src/PcMax.App          # no Linux/macOS corre com dados simulados
```

Para publicar uma versão nova, cria uma tag `vX.Y.Z` e faz push. O GitHub Actions compila, cria o instalador com Velopack e publica-o em Releases. As apps instaladas atualizam-se sozinhas a partir daí.

Estrutura: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
