# Resultados do experimento

Gerado em 2026-10-15 21:39. Respostas por condição: amador = 150, profissional = 150; sem resposta (erro de API): 0.

| Métrica | Casos | Amador | Profissional | p exato | p Holm | r |
|---|---|---|---|---|---|---|
| Violação de formato | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Quantidade de achados errada | 30 | 18.0% | 0.0% | 0.0039 | 0.0391 | 1.00 |
| Atribuição de tipo errada | 30 | 34.7% | 0.0% | 0.0005 | 0.0054 | 1.00 |
| Fabricação de símbolo (inexistente) | 30 | 1.3% | 0.0% | 0.5000 | 1.0000 | 1.00 |
| Símbolo fora da pilha (R4 estrita) | 30 | 100.0% | 0.0% | 0.0000 | 0.0000 | 1.00 |
| Fabricação de valor ou quadro | 30 | 12.0% | 2.7% | 0.0256 | 0.2307 | 0.65 |
| Teto ausente tratado como estourado | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Vazamento de escopo | 30 | 3.3% | 0.0% | 0.5000 | 1.0000 | 1.00 |
| Dicas de level design ausentes com achado DrawCalls | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Indeterminação correta (G5) | 5 | 0.0% | 28.0% | 0.5000 | 1.0000 | -1.00 |
| Indeterminação indevida (G1–G4) | 20 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Estouro afirmado sem estouro (G5–G6) | 10 | 30.0% | 2.0% | 0.0313 | 0.2500 | 1.00 |
| Válida porém errada | 30 | 55.3% | 15.3% | 0.0001 | 0.0013 | 0.92 |
| Abstenção de código (G1–G2, pós-hoc) | 10 | 0.0% | 100.0% | 0.0020 | — | -1.00 |

Taxas: média, entre os casos, da fração de repetições em que a falha ocorreu (para "indeterminação correta", a fração em que ocorreu o acerto). Teste: Wilcoxon pareado bilateral, p exato, α = 0.05, com ajuste de Holm entre as métricas testadas. r = correlação bisserial de postos (positivo = amador maior).
