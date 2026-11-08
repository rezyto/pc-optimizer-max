# Privacidade

O PC Max **não recolhe nem envia dados pessoais**.

- As definições, o histórico e os registos ficam só no teu PC, em `%ProgramData%\PcMax` (normalmente `C:\ProgramData\PcMax`). Só administradores podem alterar essa pasta; os outros utilizadores do PC podem lê-la.
- As versões 0.1.x guardavam os dados em `%LOCALAPPDATA%\PcMax`. Na primeira vez que abres a versão nova, as definições são copiadas e os ficheiros antigos apagados.
- Ligações à internet:
  - **Atualizações** (podem ser desligadas nas Definições; o botão "Procurar atualizações agora" funciona sempre): `api.github.com` para saber qual é a versão mais recente, e `github.com` mais a CDN de ficheiros do GitHub (`objects.githubusercontent.com` / `release-assets.githubusercontent.com`) para transferir o instalador e o `.sha256`. O pedido só leva o identificador "PcMax-Updater"; não envia dados do PC.
  - **Teste de rede:** pings para o servidor que escolheres (por defeito 1.1.1.1) e, no teste de bufferbloat, uma transferência temporária de `speed.cloudflare.com`. Só correm quando carregas no botão.
  - **Links** (GitHub, ajuda) só quando carregas neles, no teu navegador.
- A opção "Partilhar estatísticas anónimas" está desligada por defeito e, nesta versão, não envia nada.
- Sem publicidade, sem rastreadores, sem contas.
