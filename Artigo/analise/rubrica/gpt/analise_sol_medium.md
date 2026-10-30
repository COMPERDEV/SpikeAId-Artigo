# Avaliação independente — Sol 6.1 medium

A avaliação foi feita lendo integralmente a rubrica e os 40 laudos, antes de abrir `notas_claude-opus-5.5.csv`. As notas foram salvas primeiro em `notas_sol_medium.csv` e não foram alteradas depois da comparação. Não consultei outros avaliadores nem tentei deduzir condições ocultas de geração. Esta é análise estática dos artefatos, sem compilação Unity nem medição de desempenho.

A conclusão é que os laudos frequentemente descrevem princípios úteis, mas poucos oferecem uma correção conservadora e aplicável. Há três defeitos recorrentes: substituir o jogo por exemplos inventados, eliminar funcionalidades expostas para baixar custo e transformar correlação de telemetria em causalidade demonstrada. As abstenções costumam ser mais seguras, embora raramente bastem para o iniciante resolver o problema.

## Como apliquei a rubrica

H1/H2 ficaram vazios nos 10 laudos sem código: 1, 3, 10, 11, 14, 18, 22, 23, 37 e 38. Um método Update parcial foi tratado como substituição desse método dentro do script original, sem exigir que cada trecho repetisse namespace e campos. Uma classe inteira apresentada como correção foi julgada como substituição da classe. Não considerei uma única linha, por si, erro de compilação: 16, 19, 20, 30, 36 e 39 são exemplos de código compactado cuja falha principal pode ser outra. Já os caracteres literais `\n` e fragmentos JSON são defeitos reais da entrega e pesam fortemente em H1/H4.

H2 considera funcionalidade e interface, não só a existência nominal dos campos. Manter `stressGc`, `gcArrayAllocs` e `cpuScansPerFrame` no Inspector enquanto deixa seus ramos vazios reduz preservação. O script não usa o conteúdo das buscas ou strings para uma mecânica visível além de KeepAlive; isso torna a eliminação defensável como otimização de trabalho morto em um jogo comum. Porém a instrução externa manda preservar o que ele faz e expõe. Atribuí crédito por remover o custo efetivo em H1, mas não nota máxima de preservação. Esta ambiguidade é relevante e deve ser resolvida antes de futuras avaliações.

H3 mede comunicação compreensível e tecnicamente responsável. Uma explicação genérica bem escrita pode receber 4 mesmo que o código inventado leve H1/H4 a 1. Em sentido inverso, uma explicação muito legível perde pontos quando ensina uma causalidade não comprovada ou uma propriedade técnica falsa. H4 exige aplicação útil com os limites relevantes: retirar funcionalidade ou entregar código inválido impede notas elevadas.

## Achados técnicos e exemplos

- **Correção no ramo errado:** 8 e 30 removem strings/arrays de `stressGc`, mas a cena indicada é CPU. Mantêm exatamente as chamadas repetidas a `FindObjectsOfType<GameObject>()`, que retornam novos arrays. O código não resolve as alocações observadas nessa cena. O fato de conhecer o código-fonte não informa quais flags do Inspector estavam ligadas. Uma resposta melhor apresentaria hipóteses condicionais e pediria confirmação no Profiler ou no Inspector.
- **Classes inventadas:** 5, 6, 13, 16, 17, 28, 33, 35, 36 e 39 trocam o script real por cálculo de senos, posições ou bytes. Pré-alocar buffers é uma técnica válida, mas nenhuma dessas rotinas implementa a lógica original. Os exemplos compiláveis continuam inadequados como refatoração. O 28 ainda oferece dois substitutos incompatíveis e uma tarefa de throttling vazia.
- **Código inválido como recebido:** 6, 7, 17, 25, 29, 33, 34 e 35 contêm escapes literais. Nos 25 e 34 há ainda conteúdo de serialização dentro do campo de código. Não assumi que o iniciante corrigiria automaticamente o transporte. H1=1 não afirma que a ideia subjacente nunca reduziria custo: afirma que a entrega não oferece a correção executável prometida.
- **Mudanças amplas e perda de funcionalidades:** 4, 7, 15, 24, 25, 26, 27, 29, 34 e 40 alteram defaults ou materiais e removem ramos além do necessário. O 15 deixa os campos de overdraw mas apaga sua construção. Alterar um inicializador não necessariamente altera um componente já serializado na cena, portanto não creditei a redução do custo à simples troca de defaults. O defeito é mudar o contrato para novas instâncias e realizar alterações desnecessárias.
- **Caches incompletos ou sem uso:** no 24, a lista é alimentada mas não substitui uma operação consumidora no Update. No 27, a coleção registra criações locais, não todos os objetos que a busca original poderia retornar, e não acompanha destruições ou objetos de outros scripts. No 34, `_spawnedObjects` nem recebe os objetos vazios responsáveis pelo volume da cena. Há melhoria possível de custo, mas a equivalência e a integração estão incompletas.
- **Materiais e cores:** MaterialPropertyBlock pode evitar a criação de um material individual, mas não é uma troca automaticamente equivalente a `renderer.material.color` para qualquer shader ou pipeline. `_Color` e `_BaseColor` dependem do shader. O 40 troca a propriedade e usa `Random.ColorHSV()`, assim como o 29 muda a distribuição de cores. Isso não preserva em geral as cores RGB uniformes do original. Não é possível prometer benefício universal ao batching sem conhecer a renderização.
- **Promessas exageradas:** StringBuilder reduz o custo de concatenação, mas não garante ausência completa de alocações em qualquer runtime e capacidade, nem resolve sozinho toda a CPU ou toda a coleta do jogo. Crescimento de buffers pode alocar. Arrays possuem elementos mutáveis: o 24 afirma equivocadamente que arrays criados em Update não podem ser modificados no mesmo local. A palavra `new` também não implica sempre alocação gerenciada — `new Vector3` é o exemplo presente nos próprios laudos.
- **Orientação operacional incompleta:** 37 recomenda corrotinas ou threads secundárias sem explicar que corrotinas não tornam automaticamente o trabalho paralelo e que operações Unity têm restrições de thread. Para um iniciante isso pode conduzir a outra implementação incorreta. Sua abstenção de código continua adequada, mas reduzi H4.

## Limites da inferência pela telemetria

A pilha localiza Update e, em alguns casos, GC.Alloc. Ela não prova qual ramo interno está ativo. O campo “Cena do caso” é verdade de referência para o avaliador, mas não estava disponível ao gerador. Isso permite julgar a eficácia real de 8/30, sem atribuir ao gerador intenção de ignorar informação que não tinha.

`gc_collect_ms > cpu_ms` em 3, 9, 11, 19, 31 e 38 não demonstra que a coleta “explica integralmente” o quadro. Sinaliza que os números exigem definições e bases de medição antes de serem decompostos. A telemetria informa projeção para alguns campos e apresenta tempos de stack com outra escala aparente. Não há contrato de exclusividade, sincronização ou soma desses valores. Não é seguro somar stack, GC e CPU, nem subtrair GC para nomear exatamente o “restante” como outros cálculos. Os 18 e 30 são melhores por rejeitar GC como explicação total, mas seus percentuais devem ser tratados como razões numéricas aproximadas entre métricas, não decomposição causal comprovada.

`GC.Alloc` identifica alocação, não uma coleta síncrona causada integralmente por aqueles bytes naquele frame. A coleta pode refletir pressão acumulada. Uma amostra extrema e contagens de violações não provam que o mesmo montante é criado em todos os frames ou que toda pausa é bloqueante. Mesmo uma correção efetiva para Update não prova que 60 FPS serão atingidos: no caso de CPU 402,50 ms, a amostra de stack de Update é 21,33 ms e não explica sozinha o total, especialmente sem esclarecer projeção. Os laudos deveriam recomendar nova captura e comparar antes/depois.

## Comparação com Claude Opus 5.5

| Critério | Notas comparáveis | Média Sol | Média Claude | Iguais | Diferença absoluta média |
|---|---:|---:|---:|---:|---:|
| H1 | 30 | 2,40 | 2,77 | 21/30 | 0,37 |
| H2 | 30 | 2,03 | 2,43 | 18/30 | 0,40 |
| H3 | 40 | 3,45 | 3,38 | 17/40 | 0,58 |
| H4 | 40 | 2,10 | 2,58 | 22/40 | 0,48 |

Essas médias são descrições de notas ordinais, não medidas precisas de distância de qualidade, e H1/H2 não têm o mesmo denominador de H3/H4. Não foi calculada uma nota global que imputasse zero às abstenções.

As principais divergências são interpretativas, não um desacordo sobre o conteúdo dos scripts:

1. **7 e 29:** Claude dá H1=3 e H4=2 a códigos que reconhece não compiláveis. Dei H1=1/H4=1, seguindo a regra explícita de avaliar como recebido. O 25 também cai de H1=2 para 1. Um protocolo deveria definir um teto para código inválido e permitir registrar separadamente qualidade da ideia.
2. **9, 19 e 31:** Claude dá 5/4/4/4. Dei 4/3/3/3: removem as alocações efetivas, mas esvaziam controles de GC e sustentam causalidade integral com tempos incompatíveis. A redução de custo merece crédito, porém “muda só o necessário” e “iniciante resolveria” exigem mais cautela.
3. **8 e 30:** ambos reconhecemos o ramo errado. Claude atribui H1=2 e H4=2 ou 3. Dei 1 e 1 porque o patch não reduz o custo medido na configuração real e ainda elimina uma funcionalidade independente.
4. **2, 12, 20, 21 e 32:** concordância em H1=4/H2=3/H3=4. H4 é 3 para mim e 4 em Claude: vejo ajuda incompleta porque o patch apaga modos e não demonstra que o total do quadro será resolvido.
5. **4, 24, 26 e 40:** sou mais estrito em H2 diante de mudanças no ramo GC/CPU e na renderização fora do escopo. Nos 15 e 40, o risco de perder overdraw ou cor também reduz H4.
6. **H3 em exemplos genéricos:** dei 4 a 5, 6, 13 e 16, contra 3 de Claude. A prosa é compreensível, embora não torne aplicável o código. Claude é mais severo com a clareza de classes inventadas e, inversamente, menos severo com causalidade nos laudos de GC. Essa diferença confirma sobreposição mal delimitada entre clareza, correção e utilidade.
7. **18 e 37:** o 18 recebe H4=3 em vez de 4 porque informa melhor a incerteza mas ainda não resolve. O 37 recebe 2 em vez de 3 pelas sugestões de paralelismo sem limites operacionais.

## Qualidade e melhorias da rubrica

A rubrica tem méritos: explicita o alvo iniciante, exige preservar campos e métodos, distingue abstenção legítima, fornece verdade de referência sobre as cenas e considera defeitos reais de formatação. O desenho permite identificar alucinação de implementação e uma correção plausível aplicada ao ramo inativo.

As âncoras precisam de maior precisão para aumentar concordância entre avaliadores. H1 mistura compilação, causalidade, eficácia e efeito colateral; H2 mistura identidade de campos com preservação funcional; H3 inclui clareza e verdade; H4 inclui praticamente todos os anteriores. Uma única nota por laudo também mascara que um achado resolve GC enquanto outro inventa CPU. Não há regra para combinar achados duplicados, contraditórios ou classes alternativas, nem para distinguir exemplo didático declarado de substituto pronto para colar.

Sugestão: conservar H1–H4 para continuidade, acompanhados de indicadores separados de compilabilidade, símbolos inventados, ramo ativo, preservação de interface/comportamento, sustentação causal e próximo passo verificável. Definir o que conta como preservação neste script de estresse, o teto de H1/H4 para código inválido, e uma política de agregação por achado. Acrescentar a opção “não demonstrável sem execução” a conclusões de ganho de desempenho. Dar à abstenção uma âncora positiva para orientação concreta e um teto explícito quando não há caminho de solução.

Para uma avaliação de estudo, seria útil uma revisão humana dos desacordos de dois pontos, um conjunto curto de exemplos calibrados e métricas de concordância ordinal. Os casos repetem telemetria e pertencem ao mesmo script: não devem ser tratados como 40 cenários técnicos independentes para inferência estatística ampla. As diferenças observadas não permitem atribuir vantagens a condições de geração não reveladas.
