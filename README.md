# RamCleaner

Limpador de memória estilo Firemin, só que para **vários apps ao mesmo tempo** (Discord, Spotify, Claude, navegador...).

## Como compilar

Precisa do **.NET 8 SDK** (ou mais novo).

```powershell
cd C:\Users\%USER%\Documents\Projetos\RamCleaner
dotnet run                                   # testar
dotnet publish -c Release -r win-x64         # gera um .exe único em bin\Release\net8.0-windows\win-x64\publish\
```

Se tiver o .NET 9/10 e não o 8, troque `net8.0-windows` por `net9.0-windows` (ou `net10.0-windows`) no `RamCleaner.csproj`.

## Como usar

1. **+ Adicionar**: marque os processos na lista (dá pra buscar). Se preencher **Nome** (ex.: "Edge"), todos os marcados viram um grupo só (ex.: `msedge` + `msedgewebview2`). Sem nome, cada processo vira uma linha.
2. **Editar** (ou duplo clique na linha): troca nome, processos e limite. O nome também pode ser renomeado direto na célula (F2).
3. **Limite MB**: só limpa quando a soma de todos os processos do grupo passar desse valor (0 = limpa sempre). Editável na tabela.
4. **Intervalo (ms)**: de quanto em quanto tempo limpa, em milissegundos (mínimo 100 ms; 500 = meio segundo).
5. Fechar no X esconde na bandeja; para sair de verdade use **botão direito no ícone > Sair**.

Config salva em (configs da versão anterior são convertidas automaticamente) `%AppData%\RamCleaner\config.json`.
"Iniciar com o Windows" grava em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (não precisa de admin).

## Visual

Baseado no design system do davimf.dev (fundo quase preto quente, painéis com borda fina, accent dourado `#E6B566`, botões em pílula, rótulos em caixa alta, badges de status). Tudo desenhado com GDI+ no próprio WinForms: nenhuma biblioteca extra, nenhuma animação ou timer de UI. O tema (claro/escuro) segue o Windows e é escolhido ao abrir o app.

## Arquivos

| Arquivo | O que faz |
|---|---|
| `Native.cs` | P/Invoke: `OpenProcess` + `EmptyWorkingSet` (a mesma técnica do Firemin) |
| `Trimmer.cs` | Soma a RAM de todos os processos do app e limpa se passar do limite |
| `MainForm.cs` | Janela principal, tabela, timers e ícone da bandeja |
| `AddProcessForm.cs` | Janela de adicionar/editar app |
| `Theme.cs` | Cores, fontes, barra de título, menu da bandeja |
| `UiControls.cs` | Botão pílula, interruptor, cards, campos, estilo das tabelas |
| `AppConfig.cs` | Carrega/salva o JSON |
| `Startup.cs` | Liga/desliga iniciar com o Windows |

## O que esperar (importante)

`EmptyWorkingSet` não "apaga" memória: ele tira as páginas do app da RAM física e manda para a lista de standby / arquivo de paginação. Por isso:

- O número no Gerenciador de Tarefas cai muito (é a coluna **RAM (working set)**).
- A coluna **Privada (commit)** quase não muda — é a memória que o app realmente reservou.
- Quando o app volta a usar aquelas páginas, elas voltam (page faults). Em apps parados em segundo plano isso é imperceptível; em jogos ou apps em uso ativo causa travadinhas, igual você viu com o Firemin.
- Intervalos muito curtos (100–1000 ms) em apps ativos costumam piorar: o app fica trazendo memória de volta o tempo todo. 30–60 s com limite por app é um bom começo; use intervalos curtos só em apps parados em segundo plano.

Apps rodando como administrador ou serviços aparecem como "Sem permissão"; para esses, troque `asInvoker` por `requireAdministrator` no `app.manifest`.
