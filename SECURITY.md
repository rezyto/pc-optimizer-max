# Segurança

O PC Max corre como administrador (precisa disso para mudar o plano de energia, prioridades e o ecrã). Por isso, tudo o que ele executa ou lê tem de estar fora do alcance de um utilizador normal ou de um programa sem privilégios.

## Princípios

1. **Nada irreversível.** Cada ajuste guarda o estado anterior em disco *antes* de reportar sucesso. Se não conseguir guardar, desfaz logo a alteração.
2. **Menor risco por defeito.** Só ajustes de nível "Seguro" correm sem confirmação. Os níveis "Avançado" e "Extremo" têm de ser escolhidos explicitamente.
3. **Lista de processos protegidos.** O sistema, o Windows Defender, os anti-cheats e os launchers de jogos nunca são suspensos, fechados ou mudados de prioridade, mesmo que o utilizador os ponha na lista.
4. **Pausa de apps em segundo plano** só em processos do próprio utilizador, na mesma sessão, sem privilégios de administrador, que não sejam críticos para o sistema, nem o jogo nem nenhum processo da cadeia que o lançou (launcher, etc.).
5. **Recuperação de falhas.** Se a app ou o PC falharem a meio de uma sessão de jogo, no arranque seguinte a app repõe os ajustes temporários e retoma os processos que tinha pausado. Antes de retomar um processo confirma a hora de início (para não mexer num processo diferente que reutilize o mesmo PID), que pertence ao utilizador atual e que o nome do executável é o registado.
6. **Limpeza segura.** Nunca segue atalhos, symlinks ou junctions, nunca sai da pasta da categoria e só apaga ficheiros temporários com mais de 24 horas.
7. **Drivers:** a app não instala drivers. Para ler a temperatura do CPU usa o driver PawnIO (assinado e com código aberto) se já estiver instalado, e não o antigo WinRing0, que é vulnerável. O pipeline de release falha se a pasta publicada tiver algum `*.sys` ou uma DLL que mencione WinRing0 (a única exceção é a `RAMSPDToolkit-NDD.dll` da LibreHardwareMonitor, que só nomeia essa interface e não traz driver; é aceite apenas com o hash revisto).

## Instalação e arranque

- **Instalação por máquina** em `%ProgramFiles%\PC Max` (Inno Setup, só x64, exige administrador). Só administradores podem alterar esses ficheiros.
- **Arranque com o Windows:** tarefa "PC Max" do Agendador de Tarefas (gatilho ao iniciar sessão do utilizador atual, "privilégios mais elevados"), registada pela API COM do Agendador com o XML em memória (sem ficheiro temporário nem `schtasks.exe`). A app recusa registar a tarefa se o executável não estiver dentro de `%ProgramFiles%`, por isso uma cópia numa pasta que o utilizador pode alterar (build de desenvolvimento, download) nunca é posta a arrancar como administrador.
- **Desinstalação:** antes de apagar os ficheiros, o desinstalador fecha a app e corre `PcMax.exe --uninstall-cleanup`, que retoma apps pausadas, reverte todos os ajustes registados e remove a tarefa de arranque. Os ajustes no registo do utilizador (HKCU) são revertidos na conta de quem corre o desinstalador.
- **Uma instância por utilizador:** o mutex e o evento usam o espaço de nomes da sessão (`Local\PcMax-<SID>`).

## Dados

- O estado (definições, ajustes por reverter, processos pausados, registos, atualizações transferidas) fica em `%ProgramData%\PcMax`. Em cada arranque, antes de ler qualquer estado, a app confirma que essa pasta e as subpastas `logs` e `updates` não são links/junctions, têm como dono Administradores ou SYSTEM, e só dão controlo total a SYSTEM e Administradores e leitura a Utilizadores (permissões protegidas, sem herança de ProgramData). Se alguma verificação falhar, a pasta é posta de parte (renomeada, nunca usada) e recriada com essas permissões. Se não for possível, a app não arranca.
- Os ficheiros de estado são limitados a 1 MB, não são lidos através de links e são validados: nulos e valores fora de limites passam aos valores por defeito, nomes de processos têm de ser nomes simples.
- Cada ajuste só aceita snapshots com as suas próprias chaves e valores no formato que ele próprio produz (GUID para o plano de energia, nomes de valores de registo conhecidos com DWORD ou "ausente", entradas de ecrã no formato `\\.\DISPLAYn` → `Hz|largura|altura`). Snapshots rejeitados são descartados e nunca revertidos, por isso um ficheiro adulterado não consegue levar a app a escrever valores arbitrários.
- Migração única da pasta antiga por utilizador (`%LOCALAPPDATA%\PcMax`, versões 0.1.x): os ficheiros passam pelos mesmos validadores, nada é escrito nessa pasta, e cada ficheiro antigo só é apagado depois de confirmar, pelo handle aberto, que o caminho final é mesmo o esperado (sem links pelo meio).

## Atualizações

- Vêm apenas das Releases deste repositório: `https://api.github.com/repos/<dono>/<repo>/releases/latest`, por HTTPS com a validação de certificados do sistema (redirecionamentos só em HTTPS). Versões de teste e rascunhos são ignorados.
- O URL de transferência é construído pela app a partir da tag validada (`vX.Y.Z`), nunca copiado da resposta. O instalador é guardado em `%ProgramData%\PcMax\updates` e só é usado se o SHA-256 bater certo com o ficheiro `.sha256` da mesma release; se não bater, é apagado.
- Se o `PcMax.exe` em execução tiver assinatura Authenticode, o instalador tem de ter uma assinatura válida (WinVerifyTrust, com verificação de revogação) feita pelo mesmo certificado. Enquanto as versões não forem assinadas, só o SHA-256 é verificado. Nesse caso o hash protege contra ficheiros corrompidos ou trocados na CDN, mas não contra alguém que controle as Releases do repositório.
- O instalador é verificado de novo imediatamente antes de correr, e corre com argumentos separados (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS`), nunca através de uma linha de shell.
- Nada é verificado, transferido ou instalado durante um jogo. A instalação só acontece quando carregas em "Instalar agora" ou no arranque seguinte (uma tentativa por versão).

## Outras proteções

- **Sem execução de shell.** O plano de energia é copiado com `PowerDuplicateScheme` (powrprof.dll), sem `powercfg.exe`. Os únicos programas externos lançados são o PresentMon e o instalador de atualizações, com argumentos separados.
- **Links abertos sem privilégios:** os links e pastas são abertos pelo Explorador do ambiente de trabalho (que corre como utilizador normal), por isso o navegador nunca corre como administrador. Se não for possível, o endereço é copiado para a área de transferência.
- **DLLs:** no início do processo, `SetDefaultDllDirectories` limita a pesquisa de DLLs à pasta da app e a System32, e as chamadas nativas da app só procuram em System32.
- **Variável `PCMAX_FAKE`** (dados simulados) só funciona em builds Debug.
- **Pipeline de release:** a tag é validada por regex e nunca interpolada em scripts; permissões mínimas por job (escrita só no job que publica a release); PresentMon numa versão fixa, aceite só com assinatura Authenticode válida da Intel; segredos de assinatura expostos só aos passos de assinatura.

## Limitações conhecidas

- As releases ainda não são assinadas por defeito, por isso o SmartScreen avisa e as atualizações dependem só do SHA-256 publicado na mesma release.
- `%ProgramData%\PcMax` pode ser lido por outros utilizadores do PC (definições e registos não têm dados pessoais, mas os registos têm nomes de jogos e apps).

## Reportar uma vulnerabilidade

Abre um *security advisory* privado no separador **Security** deste repositório. Não abras um issue público.
