# Análise agregada dos 40 laudos

Foram executadas avaliações separadas com gpt-6-astra (medium), gpt-6.1-sol (medium) e gpt-6-luna (high). O resultado central é consistente: a clareza das explicações supera a qualidade das correções e a preservação do script. Na agregação, 22 dos 40 laudos têm H4 até 2; nenhum alcança mediana H4 de 4 ou 5. Isso descreve estes artefatos, não uma comparação geral de capacidade dos modelos.

## Método e rastreabilidade

Cada avaliador recebeu o mesmo pedido de ler os 40 laudos, fixar notas próprias antes de abrir o CSV Claude e depois comparar. A rubrica foi adotada como critério de avaliação; sua instrução de responder apenas CSV não substituiu o pedido externo de análise e agregação. Os agentes não receberam as respostas uns dos outros. Na revisão de consistência, o coordenador pediu ao Luna normalizar os IDs e reexaminar a perspectiva das abstenções e os casos 8/30. Luna explicitou a perspectiva e revisou 8/30; portanto a versão final Luna inclui essa revisão dirigida. Não se deve chamar o conjunto final de três medições inteiramente sem intervenção.

O CSV agregado usa mediana por laudo/critério entre os três novos avaliadores. A amplitude é máximo menos mínimo. Claude fica fora da mediana. H1/H2 ausentes são não aplicáveis, nunca zero. H1/H2 têm 30 observações por avaliador, H3/H4 têm 40. As médias abaixo são descritivas de uma escala ordinal e não uma nota global. A análise foi estática, sem compilar no Unity ou medir ganhos reais.

## Notas médias por avaliador

| Avaliador | H1 Pertinência | H2 Preservação | H3 Clareza | H4 Utilidade |
|---|---:|---:|---:|---:|
| astra_medium | 1,93 | 1,80 | 3,10 | 1,82 |
| sol_medium | 2,40 | 2,03 | 3,45 | 2,10 |
| luna_high | 2,13 | 1,87 | 3,67 | 2,25 |
| claude | 2,77 | 2,43 | 3,38 | 2,58 |
| Média das medianas | 2,07 | 1,90 | 3,33 | 2,05 |

Os três novos avaliadores são mais rigorosos que Claude em pertinência, preservação e utilidade. Clareza varia: Astra é mais rigoroso; Sol e Luna dão notas médias superiores às de Claude. Isso sugere diferenças na interpretação de clareza versus precisão técnica, além de diferenças de severidade. Não há gabarito humano que permita declarar qual avaliador acertou.

## Conclusões compartilhadas

1. **Explicar uma técnica não equivale a refatorar o script.** Classes com senos, buffers e posições inventadas podem ilustrar boas práticas, mas não corrigem o comportamento recebido. Exemplos: 5, 13, 16, 28, 36 e 39.
2. **Baixar custo apagando trabalho cria um conflito de preservação.** Esvaziar stressGc ou stressCpu elimina a carga, mas os campos deixam de controlar o que controlavam. O script é um teste de estresse; a rubrica manda julgá-lo como jogo e preservar comportamento. É preciso definir essa fronteira para que H1/H2 sejam reproduzíveis. Exemplos: 2, 9, 12, 19, 20, 21, 31 e 32.
3. **Os laudos 8 e 30 corrigem o ramo inadequado à cena.** Removem trabalho de stressGc e mantêm FindObjectsOfType no ramo CPU ativo. Conhecer o fonte não informa os valores do Inspector; uma resposta responsável deveria formular hipóteses condicionais, sem assumir flags.
4. **O artefato entregue importa.** Uma linha única pode compilar; caracteres literais de barra e n, comentários que engolem o restante ou JSON dentro do código são defeitos distintos. Os laudos 7/29 concentram desacordo sobre quanto crédito dar à ideia quando a entrega não compila como recebida.
5. **A telemetria sustenta localização, mas não toda a causalidade afirmada.** GC.Alloc não prova sozinho que uma expressão específica causou toda a coleta do mesmo frame. Valores de gc_collect_ms superiores a cpu_ms exigem esclarecer contadores e bases temporais. Razões de 18% ou 31% são contas entre métricas; não provam decomposição exclusiva do tempo.
6. **Abstenções são geralmente mais prudentes que código inventado.** Não devem ser punidas por deixar de adivinhar o fonte que o produtor não recebeu. Continuam limitadas quando não explicam um próximo passo concreto de investigação.

## Onde há divergência relevante

Há amplitude de pelo menos dois pontos em algum critério nos laudos **7, 9, 15, 24, 26, 28, 29, 40**. Esses oito laudos merecem adjudicação humana antes de usar uma nota final como gabarito. A mediana resume o desacordo; não o resolve.

| Critério | Concordância exata dos três | Casos com amplitude de 2 ou mais |
|---|---:|---:|
| H1 | 12/30 | 6 |
| H2 | 22/30 | 0 |
| H3 | 12/40 | 2 |
| H4 | 22/40 | 1 |

A discussão principal é quanto H1 deve premiar a redução do custo mesmo quando H2 detecta perda funcional. Há também divergência em H3: a prosa pode ser fácil de ler e, ainda assim, ensinar uma afirmação falsa. Astra costuma descontar mais a falta de rigor e a perda funcional; Sol e Luna distinguem mais o mérito da exposição do defeito da implementação.

## Melhorias recomendadas no protocolo

- Definir preservação funcional para trabalho sem efeito externo, campos do Inspector e mudanças de defaults.
- Separar marcadores objetivos de código compilável, símbolos inventados, ramo ativo, sustentação causal e preservação. Manter H1–H4 para continuidade.
- Especificar o teto de H1/H4 para código inválido e a regra de combinação de múltiplos achados, especialmente duplicados ou contraditórios.
- Separar a verdade de referência disponível ao avaliador da informação disponível ao produtor.
- Documentar precisamente as métricas de telemetria e exigir validação antes/depois para afirmar ganho real.
- Calibrar os níveis 2 e 4 com exemplos e adjudicar os oito desacordos maiores.

Os 40 laudos repetem 10 casos de um único script. Sem rótulos das condições de geração e sem avaliação humana ou execução, não é possível atribuir diferenças a acesso ao fonte, provar melhoria de desempenho ou generalizar para projetos Unity em geral.

## Arquivos

- prompt_analise.md: texto comum e método.
- notas_agregadas.csv: medianas e amplitudes de 40 laudos.
- notas_astra_medium.csv, notas_sol_medium.csv, notas_luna_high.csv: avaliações individuais.
- analise_astra_medium.md, analise_sol_medium.md, analise_luna_high.md: justificativas individuais.
