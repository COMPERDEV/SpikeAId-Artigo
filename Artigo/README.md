# Experimento do artigo — engenharia de prompt × alucinação no laudo

Artefato do artigo *Engenharia de prompt para redução de alucinações ao integrar LLM à
Unity Engine via API REST* (Anderson Comper de Araujo, UNIVALI, 2026).

## As duas branches

| Branch | `Artigo/condicao.txt` | Prompt de sistema |
|---|---|---|
| `artigo-prompt-amador` | `amador` | `prompts/prompt_amador.txt` — prompt original do projeto (commit `9eb320c`, 08/05/2026), só com o bloco de formato trocado pelo contrato atual |
| `artigo-prompt-profissional` | `profissional` | `prompts/prompt_profissional.txt` — prompt em camadas (papel, inventário dos dados, 10 regras, autoconferência, formato) |

**A única diferença entre as branches é a linha de `condicao.txt`.** Código, casos,
esquema de resposta, modelo e parâmetros são idênticos. Confira:

```
git diff artigo-prompt-amador artigo-prompt-profissional -- . ":(exclude)Artigo/respostas" ":(exclude)Artigo/resultados"
```

Nas duas branches, o laudo feito pelo próprio plugin (aba Snapshots → Analisar) também
usa o prompt da condição, sem a instrução de idioma, e no Gemini com o mesmo esquema.

## Constantes do experimento

| Item | Valor | Onde |
|---|---|---|
| Modelo | `gemini-3.8-flash` (fixado; escolhido depois de um teste com 3 casos × 2 prompts contra o 3.6, em `teste-modelo/`) | `config.json` |
| Repetições por caso | 5 | `config.json` |
| Temperatura | 0 | `response_schema.json` |
| Esquema de resposta nativo | `responseMimeType` + `responseSchema` | `response_schema.json` |
| Idioma dos prompts e do payload | português | captura fixa o idioma do plugin em PT |
| Teste estatístico | Wilcoxon de postos sinalizados, pareado por caso, bilateral, p exato, α = 0,05 | `ArtigoAnalysis.cs` |
| Tamanho de efeito | correlação bisserial de postos pareada | `ArtigoAnalysis.cs` |

Cada resposta grava o SHA-256 do prompt, do texto enviado e do `generationConfig`, a
versão do modelo que a API declara ter usado (`modelVersion`) e o corpo HTTP inteiro.

## Casos de teste (30)

Capturados pelo próprio plugin, medindo no Editor contra um alvo de PC declarado
(o da aba Build no momento da captura, 60 FPS). Cada caso é a **seleção** dos snapshots
do tipo do gargalo plantado — como o desenvolvedor faz na aba Snapshots — e o payload é
montado pelo mesmo formatador do plugin (`GeminiRestBridge.FormatUserText`).

| Grupo | Casos | Como é gerado | Resposta correta |
|---|---|---|---|
| G1_CPU | 5 | cena de CPU: varreduras `FindObjectsOfType` por quadro (16 a 64) sobre 10.000 objetos | achado CPU com evidência |
| G2_GC | 5 | 01–03: cena de GC, blocos de ~80 KB por quadro (32 a 64) — coleta grande; 04–05: amostras de GC de sessões da cena de CPU (32 e 64 varreduras) — alocação alta, coleta pequena perto do pico | achado GC julgado por `gc_collect_ms`; nos casos 04–05, dizer que a coleta NÃO explica o pico |
| G3_DRAWCALLS | 5 | 3.000 a 8.000 cubos com material único | achado DrawCalls; dicas de level design preenchidas |
| G4_GPU | 5 | 16 quads transparentes em tela cheia com laço por pixel (32 a 128 iterações) + 400 cubos com material único, abaixo do teto de draw calls — muitas chamadas de desenho ao lado de um gargalo que é só de GPU | achado GPU sem prescrever batching |
| G5_FALTANTE | 5 | derivado de G1_CPU_01, G2_GC_01, G2_GC_02, G2_GC_03 e G2_GC_04: orçamento elevado até caber o pior tempo com 25% de folga, resumo de violações zerado, **pilha removida** | indeterminação (`Geral`, código vazio) |
| G6_ENGANOSA | 5 | o mesmo caso-base do G5 correspondente, com a **pilha mantida** | nenhum estouro afirmado |

Se uma sessão não produz amostra do tipo planejado, a intensidade é multiplicada (até 3
vezes) e o parâmetro que de fato gerou o caso fica gravado nele. Log da captura em
`casos/_captura_log.txt`, com o diagnóstico de cada sessão (CPU e GPU brutas, quadros com
coleta).

Cada sessão dura 10 s depois de 1 s de acomodação (os 2 s de aquecimento do plugin estão
dentro dos 10). Durante a captura o `runInBackground` fica ligado: sem foco, o Play Mode do
Editor roda estrangulado (cerca de 200 quadros em 10 s contra 700 a 6.000 com ele ligado) e
o contador de GPU passa a ler zero.

Quando o alvo é diferente da máquina medida, `cpu_ms`, `gc_collect_ms` e `gpu_ms` vão
projetados para o alvo, e o payload declara os fatores (linha `projecao`).

**Código anexado (script_sources).** Desde 15/10/2026 o laudo do plugin anexa o código-fonte
dos scripts do projeto que aparecem na pilha (opção na aba Configuração, ligada por padrão).
No experimento, os 15 casos com pilha (G1, G2, G6) levam o `StressTestScene.cs`; os outros 15
não têm pilha e vão sem código, como no plugin. **Só no experimento**, o código vai sem
comentários e sem `[Tooltip]`/`[Header]`, porque o script de estresse descreve neles os
defeitos plantados e entregaria a resposta. A regra R4 do prompt profissional passou a
exigir que o código parta do script anexado; o prompt amador não mudou.

A primeira coleta com o 3.8 (sem código anexado) está em
`arquivo/gemini-3.8-flash-sem-codigo/`: nela o prompt profissional deixou o código vazio em
100% dos achados, porque sem o corpo do método não havia como corrigir sem inventar.

**Limitação conhecida do dado:** quando o contador "GPU Frame Time" não entrega leitura
naquele quadro, o plugin envia `gpu_ms:0.00` (e não `null`) medindo no Editor. Alguns casos
trazem esse zero; ele é parte do dado como o plugin o produz.

## Métricas (Quadro 6 do artigo — definição operacional)

Calculadas por `ArtigoValidator.cs` para cada resposta. Taxa por caso = fração das
repetições com formato válido em que a falha ocorreu.

| Métrica | Regra |
|---|---|
| Violação de formato | a resposta não é exatamente um objeto JSON com `findings` (lista de objetos com `tipo` da lista fechada e os três campos de texto) e `dicas_level_design` string ou null |
| Quantidade de achados errada | nº de achados ≠ nº de amostras enviadas |
| Atribuição de tipo errada | o multiconjunto de tipos dos achados ≠ o multiconjunto de arrays de origem das amostras (em G5/G6, `Geral` pode ocupar o lugar de qualquer amostra) |
| Fabricação de símbolo | o código cita classe ou método que não está na pilha das amostras daquele tipo, não é declarado no próprio trecho e não existe em nenhum assembly da Unity, do .NET ou do projeto |
| Símbolo fora da pilha (R4 estrita) | o código cita classe ou método fora da pilha, existindo ou não |
| Fabricação de valor ou quadro | `causa_raiz` ou `explicacao_didatica` cita um quadro que não está nas amostras, ou um valor em ms, bytes/KB/MB/GB ou draw calls que não aparece no payload (tolerância de arredondamento; percentuais e fatores não são conferidos) |
| Teto ausente tratado como estourado | uma frase afirma estouro de draw calls ou memória, sem negação, quando esse teto foi enviado como "sem teto (nao julgar)" |
| Vazamento de escopo | `dicas_level_design` preenchido sem achado DrawCalls, ou achado GPU que prescreve batching/instancing/redução de draw calls (frase sem negação) |
| Dicas ausentes | há achado DrawCalls e `dicas_level_design` está vazio (reportada à parte) |
| Indeterminação correta (G5) | todos os achados são indeterminação: código vazio e tipo `Geral` ou frase de indeterminação na causa |
| Indeterminação indevida (G1–G4) | algum achado é indeterminação quando os dados eram suficientes |
| Estouro afirmado sem estouro (G5–G6) | alguma frase afirma estouro, sem negação, quando nenhuma métrica passa de um teto existente |
| Válida porém errada | formato válido e qualquer uma das falhas acima |

As regras de frase (estouro, batching, indeterminação) são lexicais, sentença a
sentença, com detecção de negação; os padrões estão em `ArtigoValidator.cs`. É uma
aproximação, declarada como limitação, e a amostra da rubrica permite conferi-la à mão.
`Autoteste do validador` planta cada classe numa resposta sintética e exige que a regra
certa dispare (saída em `resultados/_autoteste.txt`).

## Como rodar

Tudo pelo menu **Tools → Performance Advisor → Artigo**, ou pela linha de comando com o
Unity fechado (`<Unity>` = `C:\Program Files\Unity\Hub\Editor\6000.4.6f1\Editor\Unity.exe`):

1. **Capturar os casos** (uma vez; precisa de placa de vídeo, então sem `-batchmode`):
   `<Unity> -projectPath D:\FlightLog -executeMethod PerformanceAdvisor.Artigo.ArtigoCapture.CaptureCli -logFile Artigo\captura.log`
2. **Coletar as respostas** da condição da branch (sem janela, sem Play; retoma de onde parou):
   `<Unity> -batchmode -projectPath D:\FlightLog -executeMethod PerformanceAdvisor.Artigo.ArtigoRunner.RunBatch -logFile Artigo\coleta.log`
3. Trocar de branch e repetir o passo 2 com a outra condição (os casos são os mesmos — ver abaixo).
4. **Calcular as métricas** (com as respostas das duas condições em `respostas/`):
   `<Unity> -batchmode -projectPath D:\FlightLog -executeMethod PerformanceAdvisor.Artigo.ArtigoRunner.AnalyzeBatch -logFile Artigo\analise.log`

Os casos são capturados uma vez, na branch `artigo-prompt-amador`, e levados para a outra
com `git checkout artigo-prompt-amador -- Artigo/casos`. Para a análise final, as
respostas da outra condição vêm do mesmo jeito (`-- Artigo/respostas/<condição>`).

## Arquivos

```
Artigo/
  condicao.txt, config.json, response_schema.json, prompts/
  casos/<id>.json                     valores medidos, payload enviado (userText) e gabarito
  respostas/<condição>/<id>_r<k>.json resposta bruta da API + metadados
  resultados/<condição>/metricas_por_resposta.csv
  resultados/comparacao_metricas.csv  taxas por condição, Wilcoxon, p, r
  resultados/taxas_por_grupo.csv      descritivo por grupo
  resultados/estabilidade.csv         concordância entre repetições
  resultados/rubrica_amostra.csv      amostra para os dois avaliadores (semente fixa)
  resultados/resumo.md
```
