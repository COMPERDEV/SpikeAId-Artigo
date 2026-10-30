# Avaliação independente — Astra medium

Avaliador solicitado: gpt-6-astra, esforço medium. A rubrica e os 40 laudos foram lidos integralmente em trechos consecutivos. As notas foram gravadas antes da leitura de notas_claude-opus-5.5.csv. Não consultei notas de outros agentes. Esta avaliação é uma revisão estática dos artefatos, sem compilação Unity ou medição do desempenho das propostas. Não infiro como os laudos foram produzidos.

O resultado principal é que nenhum laudo entrega uma correção simultaneamente aplicável, demonstradamente pertinente e preservadora do contrato descrito. Há abstenções prudentes, exemplos inventados, entregas sintaticamente inutilizáveis e supressões de trabalho que reduzem custo mas deixam controles públicos sem efeito. Não atribuí 5 a nenhum critério: mesmo textos legíveis apresentam generalizações ou orientação insuficiente.

## Interpretação adotada

H1 considera a proposta como entregue. Código contendo os caracteres literais barra invertida e n não equivale a código apenas compactado em uma linha. Os primeiros receberam H1=1 quando a implementação principal é inutilizável; os segundos foram julgados pelo conteúdo. H2 avalia a preservação estrutural e funcional visível, independentemente da penalidade sintática já feita em H1. Trechos de Update são substituições daquele método, não classes completas que automaticamente apagam todos os campos.

Adotei uma interpretação estrita da orientação de preservar o que o script faz e expõe. Remover todas as operações de stressGc ou stressCpu torna seus campos e parâmetros inoperantes. Isso pode diminuir custo, justificando H1=3 quando mira a fonte real, mas não é uma refatoração preservadora. A remoção seletiva de GC nos laudos 9, 19 e 31 recebeu H2=3; a remoção simultânea de CPU e GC em casos CPU recebeu H2=2. Essa decisão é central para a divergência com Claude e deve ser calibrada entre avaliadores.

Há uma tensão real: o texto pede que um gerador artificial de carga seja tratado como código de jogo, mas seus resultados locais só são mantidos vivos por GC.KeepAlive, sem consumo pela lógica de jogo. Numa avaliação limitada a efeitos visuais, eliminar esse trabalho poderia ser uma otimização válida. Numa avaliação que inclui o significado dos campos públicos e as operações executadas, deixa de preservar o comportamento. Usei a segunda interpretação, indicada pela formulação da rubrica. Não apresentei a primeira como erro objetivo do outro avaliador.

## Achados técnicos

- Laudos 8 e 30 são os erros mais claros de pertinência: a cena informada usa stressCpu. O código remove stressGc e mantém FindObjectsOfType dentro do laço, preservando a fonte de arrays temporários e custo de busca. Receberam H1=1 e H4=1. As configurações da cena são informação de avaliação, não algo que se possa presumir conhecido pelo autor do laudo. O problema é a prescrição categórica sem validar qual ramo está ativo.
- Laudos 5, 6, 13, 16, 17, 28, 33, 35, 36 e 39 substituem o script por tarefas inventadas com buffers, posições ou cálculos trigonométricos. Um exemplo de reutilização de memória não implementa a correção do script. Todos receberam H1=1 e H2=1. O 28 ainda oferece duas versões completas incompatíveis do mesmo componente; o 39 oferece classes novas não integradas.
- Laudos 6, 7, 17, 25, 29, 33, 34 e 35 contêm escapes literais na implementação principal. Os 25 e 34 também contêm material JSON e explicações dentro do campo de código. Isso é falha de entrega e não mera preferência estética. Os 16, 19, 20, 30, 36 e 39, embora compactados, não foram penalizados como se tivessem esse mesmo defeito sintático.
- Laudo 15 perde BuildOverdrawLayers e a chamada correspondente, embora retenha campos relacionados. Isso é regressão concreta. Vários laudos alteram os valores iniciais de stressGc e stressCpu. Mudanças em inicializadores não garantem alterar valores já serializados em cenas existentes; a queda de custo deve ser atribuída ao corpo realmente modificado, não presumida a partir dos defaults.
- Registros dos laudos 24, 25, 27 e 34 não são equivalentes a uma consulta global. Não abrangem necessariamente objetos criados por outros componentes, descendentes dos prefabs e todo o ciclo de ativação/destruição. O 24 sequer usa a lista no Update. O 34 registra apenas clones, deixa os 10000 objetos vazios de fora e acrescenta escrita redundante em transform.position. Não há demonstração de equivalência funcional.
- Trocar renderer.material por MaterialPropertyBlock é uma alteração extra nas cenas avaliadas. Não se pode garantir equivalência visual escolhendo _Color ou _BaseColor sem saber o shader; tampouco afirmar batching preservado em qualquer pipeline. Os 29 e 40 mudam também a distribuição de cores aleatórias para ColorHSV, alterando comportamento e consumo da sequência aleatória.
- StringBuilder reduz a concatenação acumulativa, mas a promessa irrestrita de zero alocação não foi demonstrada. Capacidade pode crescer e os detalhes de conversão numérica dependem da implementação. No 15 e no 34, a capacidade depende diretamente de gcConcatIterations sem validação, apesar de o campo aceitar valores negativos no código original. Não tratei riscos de versão não informada como defeitos comprovados.
- Laudo 24 diz que arrays não podem ser modificados no mesmo lugar da memória. Isso confunde tamanho fixo com imutabilidade dos elementos. O laudo 37 sugere corrotinas ou threads sem explicar que uma corrotina não torna processamento pesado automaticamente paralelo e que APIs Unity exigem cautela quanto à thread de execução. Para um iniciante, esse conselho precisa de delimitação.

## Limites da telemetria e da causalidade

A pilha localiza Update, mas não discrimina o tempo de cada ramo. Encontrar uma instrução potencialmente cara no código não prova que ela rodou na cena. Uma amostra e um resumo de máximos não autorizam atribuir todo o máximo de CPU a uma operação específica, nem presumir que os máximos de campos diferentes pertencem ao mesmo quadro.

cpu_ms, gc_collect_ms e gpu_ms são valores projetados. A escala dos tempos internos da pilha não está explicitamente definida. A soma de tempos aninhados pode contar a mesma execução mais de uma vez. Portanto, descrições como 55,60 ms de Update somados a 16,05 ms de GC explicando 240,29 ms são excessivas sem a definição dos contadores.

Nos casos GC, gc_collect_ms pode exceder cpu_ms: 24,57 contra 22,67; 23,18 contra 21,61; 25,84 contra 23,60. A desigualdade não prova que a coleta explica integralmente a CPU. Ela pede investigação da janela, escala, agregação e significado das métricas. Os laudos 3, 9, 11, 19, 31 e 38 normalizam essa inconsistência em vez de explicitá-la. A divisão 22,75/73,36 ou 21,92/122,68 é aritmeticamente correta; sua interpretação como fração exclusiva de CPU exige hipóteses adicionais. O 18 é prudente ao não atribuir todo o quadro ao GC, mas também trata a subtração como tempo restante de cálculos, sem essas ressalvas.

Também não se pode concluir que as alocações do mesmo quadro causaram sozinhas a coleta observada: existe histórico de alocações. As explicações de GC como parada integral obrigatória e de toda ocorrência de new como lixo imediato são simplificações excessivas. Não inferi teto de memória, falha de GPU, versão Unity, pipeline ou configuração de GC a partir de informação ausente.

## Comparação com Claude

H1 e H2 têm 30 observações, pois 10 laudos não trazem código. H3 e H4 têm 40. Médias são resumos descritivos de escala ordinal, não estimativas intervalares de qualidade.

| Critério | N | Média Astra | Média Claude | Notas iguais | Erro absoluto médio |
|---|---:|---:|---:|---:|---:|
| H1 | 30 | 1,93 | 2,77 | 10/30 | 0,83 |
| H2 | 30 | 1,80 | 2,43 | 11/30 | 0,63 |
| H3 | 40 | 3,10 | 3,38 | 23/40 | 0,43 |
| H4 | 40 | 1,83 | 2,58 | 19/40 | 0,75 |

A concordância exata global é 63/140 notas comparáveis, ou 45%. Os valores vazios não foram convertidos em zero. Não usei os números para inferir significância, superioridade de modelo ou condições ocultas de produção.

As maiores divergências substantivas:

- 9, 19 e 31: Claude dá H1=5, H2=4 e H4=4; atribuí 3, 3 e 2. A fonte de custo é removida, mas o comportamento controlado por stressGc desaparece e a narrativa temporal é excessiva.
- 2, 12, 20, 21 e 32: Claude dá H4=4; atribuí 2. O iniciante pode obter redução de custo apagando subsistemas, mas isso não demonstra uma solução preservadora.
- 7 e 29: Claude dá H1=3 apesar de reconhecer que não compilam colados; atribuí 1 pela aplicabilidade do material entregue. A rubrica pede explicitamente considerar esse defeito em H1/H4, porém não fixa um teto, deixando espaço de discordância.
- 8 e 30: Claude dá H1=2 e H4=2/3; atribuí H1=1 e H4=1, pois o código conserva exatamente o gargalo ativo e orienta uma intervenção sem efeito no caso.
- 3, 10, 11, 18 e 38: reduzi H3 pela confiança causal excessiva nos tempos. A abstenção em si não sofreu penalidade.
- 25 e 34: atribuí H3=2 contra 1 de Claude porque ainda há explicação recuperável no texto contaminado. Continua impraticável para aplicação direta e H4=1.
- Há concordância forte na reprovação dos scripts inteiramente inventados. Discordo apenas de considerar a classificação GC do laudo 5, por si só, necessariamente errada: a amostra CPU contém gc_collect_ms=17,94, portanto GC merece investigação, embora não justifique a implementação fictícia ou uma causalidade total.

## Qualidade e aperfeiçoamento da rubrica

A rubrica separa pertinência, preservação, didática e utilidade, evita zeros artificiais para ausência de código e exige atenção ao formato realmente entregue. Isso é útil. Porém H3 mistura clareza com correção técnica, H4 se sobrepõe aos demais critérios e falta um eixo explícito de fundamentação causal. Não define como combinar achados heterogêneos, qual o peso de uma regressão grave, nem quais notas intermediárias representam código relevante porém inutilizável.

Antes de usar as notas para comparar sistemas, recomendo operacionalizar: (1) quais efeitos e parâmetros devem permanecer funcionais; (2) se eliminação de trabalho sem resultado consumido é admissível; (3) limite de H1/H4 para código que não compila como entregue; (4) tratamento de exemplos ilustrativos versus patch; (5) regra de agregação por laudo; (6) grau de incerteza exigido para causa raiz; (7) semântica exata dos contadores e projeções. Convém oferecer exemplos de calibração para 2 e 4 e preservar separadamente indicadores de compilação, cobertura de campos e inferência não sustentada.

Uma resposta segura sem código pode ser mais útil que uma correção inventada; por outro lado, localizar Update e dizer para investigar não equivale a resolver o problema de um iniciante. As notas H4=3 para a maioria das abstenções refletem esse limite. Não há evidência de execução dos patches nem validação de ganho; as notas avaliam o conteúdo disponível e devem manter essa limitação.
