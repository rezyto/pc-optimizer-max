# Segurança

## Princípios

1. **Nada irreversível.** Cada ajuste guarda o estado anterior em disco *antes* de reportar sucesso. Se não conseguir guardar, desfaz logo a alteração.
2. **Menor risco por defeito.** Só ajustes de nível "Seguro" correm sem confirmação. Os níveis "Avançado" e "Extremo" têm de ser escolhidos explicitamente.
3. **Lista de processos protegidos.** O sistema, o Windows Defender e os anti-cheats nunca são suspensos, fechados ou mudados de prioridade, mesmo que o utilizador os ponha na lista.
4. **Recuperação de falhas.** Se a app ou o PC falharem a meio de uma sessão de jogo, no arranque seguinte a app repõe os ajustes temporários e retoma os processos que tinha pausado. Antes de retomar, confirma a hora de início de cada processo, para não mexer num processo diferente que reutilize o mesmo PID.
5. **Sem execução de shell.** Os comandos externos (`powercfg`, `schtasks`, PresentMon) são lançados com argumentos separados, nunca concatenados numa linha de shell.
6. **Limpeza segura.** Nunca segue atalhos, symlinks ou junctions, nunca sai da pasta da categoria e só apaga ficheiros temporários com mais de 24 horas.
7. **Atualizações** vêm apenas das Releases deste repositório no GitHub, por HTTPS, através do Velopack, que verifica o hash SHA de cada pacote.
8. **Drivers:** a app não instala drivers. Para ler a temperatura do CPU usa o driver PawnIO (assinado e com código aberto) se já estiver instalado, e não o antigo WinRing0, que é vulnerável.

## Reportar uma vulnerabilidade

Abre um *security advisory* privado no separador **Security** deste repositório. Não abras um issue público.
