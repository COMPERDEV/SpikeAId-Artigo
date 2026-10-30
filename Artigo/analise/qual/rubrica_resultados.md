# Rubrica H1–H4 — 40 laudos (G1/G2, repetição 1)

Mediana dos seis avaliadores independentes (3 GPT, 3 Claude cegos) por laudo; por célula, média e mediana dessas medianas (10 laudos).

| Critério | Ref. sem código | Estr. sem código | Ref. com código | Estr. com código |
|---|---|---|---|---|
| H1 | 1,0 (md 1,0, n=10) | n/a | 3,0 (md 3,3, n=10) | 3,2 (md 3,5, n=10) |
| H2 | 1,0 (md 1,0, n=10) | n/a | 2,0 (md 2,0, n=10) | 2,9 (md 3,0, n=10) |
| H3 | 3,0 (md 3,0, n=10) | 3,7 (md 3,5, n=10) | 3,4 (md 3,8, n=10) | 3,9 (md 4,0, n=10) |
| H4 | 1,0 (md 1,0, n=10) | 2,9 (md 3,0, n=10) | 2,2 (md 2,0, n=10) | 2,8 (md 3,0, n=10) |

## Mesmo resumo com as notas do Claude (não independente do desenho)

| Critério | Ref. sem código | Estr. sem código | Ref. com código | Estr. com código |
|---|---|---|---|---|
| H1 | 1,0 | n/a | 3,4 | 3,9 |
| H2 | 1,0 | n/a | 2,8 | 3,5 |
| H3 | 3,0 | 3,9 | 2,6 | 4,0 |
| H4 | 1,1 | 3,1 | 2,4 | 3,7 |

## Concordância entre pares (kappa ponderado quadrático)

| Par | H1 | H2 | H3 | H4 |
|---|---|---|---|---|
| astra × sol | 0,86 | 0,81 | 0,57 | 0,82 |
| astra × luna | 0,73 | 0,88 | 0,27 | 0,68 |
| astra × opus | 0,72 | 0,81 | 0,50 | 0,51 |
| astra × sonnet | 0,88 | 0,86 | 0,21 | 0,49 |
| astra × haiku | 0,53 | 0,58 | 0,07 | 0,33 |
| astra × claude | 0,70 | 0,70 | 0,57 | 0,50 |
| sol × luna | 0,69 | 0,88 | 0,20 | 0,81 |
| sol × opus | 0,91 | 0,92 | 0,47 | 0,82 |
| sol × sonnet | 0,80 | 0,90 | 0,24 | 0,77 |
| sol × haiku | 0,68 | 0,77 | 0,14 | 0,57 |
| sol × claude | 0,89 | 0,81 | 0,39 | 0,78 |
| luna × opus | 0,73 | 0,86 | 0,56 | 0,81 |
| luna × sonnet | 0,79 | 0,87 | 0,37 | 0,70 |
| luna × haiku | 0,61 | 0,66 | 0,08 | 0,65 |
| luna × claude | 0,71 | 0,71 | 0,50 | 0,77 |
| opus × sonnet | 0,75 | 0,84 | 0,56 | 0,75 |
| opus × haiku | 0,79 | 0,81 | 0,27 | 0,84 |
| opus × claude | 0,99 | 0,86 | 0,75 | 0,94 |
| sonnet × haiku | 0,70 | 0,67 | 0,24 | 0,64 |
| sonnet × claude | 0,77 | 0,78 | 0,44 | 0,71 |
| haiku × claude | 0,87 | 0,93 | 0,24 | 0,86 |

## Kappa médio por grupo de pares (seis avaliadores independentes)

| Grupo | H1 | H2 | H3 | H4 |
|---|---|---|---|---|
| dentro da OpenAI | 0,76 | 0,86 | 0,34 | 0,77 |
| dentro da Anthropic | 0,75 | 0,78 | 0,35 | 0,74 |
| OpenAI × Anthropic | 0,74 | 0,80 | 0,29 | 0,63 |

## Laudos com amplitude de 2 ou mais entre os seis

2 (H2, H4); 4 (H2); 7 (H1, H4); 8 (H1, H4); 9 (H1, H3, H4); 12 (H2, H4); 15 (H1); 19 (H1, H4); 20 (H2, H4); 21 (H2, H4); 22 (H3); 24 (H1, H3, H4); 25 (H1, H3); 26 (H1, H4); 27 (H4); 28 (H3); 29 (H1, H4); 30 (H1, H4); 31 (H1, H4); 32 (H2, H4); 34 (H1); 36 (H3); 39 (H3); 40 (H1, H4)

## Comparações pareadas por caso (mediana dos seis; Wilcoxon exato, sem ajuste)

| Comparação | H1 | H2 | H3 | H4 |
|---|---|---|---|---|
| com_amador × com_profissional | 3,0 × 3,2 (p=0,445) | 2,0 × 2,9 (p=0,004) | 3,4 × 3,9 (p=0,094) | 2,2 × 2,8 (p=0,031) |
| sem_amador × sem_profissional | n/a | n/a | 3,0 × 3,7 (p=0,002) | 1,0 × 2,9 (p=0,002) |
| sem_amador × com_amador | 1,0 × 3,0 (p=0,002) | 1,0 × 2,0 (p=0,002) | 3,0 × 3,4 (p=0,289) | 1,0 × 2,2 (p=0,004) |
| sem_profissional × com_profissional | n/a | n/a | 3,7 × 3,9 (p=0,125) | 2,9 × 2,8 (p=1,000) |
