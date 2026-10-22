# Análise 2×2 e qualidade do código

Análise das 600 respostas do experimento (prompt amador × profissional, sem × com o código
anexado). Lê só o que está em `Artigo/`; nada aqui chama a API.

## Passos

Requisitos: Node.js 18 ou mais novo e, para compilar os trechos, a Unity 6000.4.6f1 instalada
(Git Bash no Windows).

```
node Artigo/analise/preparar.js                 # copia respostas, métricas e casos para as pastas de trabalho
node Artigo/analise/a2x2/analise2x2.js          # 2×2: taxas, Wilcoxon exato, Holm, r → a2x2/analise_2x2.md
node Artigo/analise/qual/prep.js                # aplica o código de G1/G2 ao script real → qual/comp/
bash Artigo/analise/qual/compile.sh             # compila cada trecho com o Roslyn da Unity → qual/compile.tsv
node Artigo/analise/qual/analise_qual.js        # critérios Q1–Q3 por célula → qual/qualidade_codigo.md
node Artigo/analise/qual/build_review.js Artigo/analise/Revisao_resultados.html
node Artigo/analise/qual/build_packet.js Artigo/analise/rubrica
```

Se a Unity estiver em outro lugar: `UNITY="D:/Unity/6000.4.6f1/Editor" bash .../compile.sh`.

## O que cada saída é

| Arquivo | Conteúdo |
|---|---|
| `a2x2/analise_2x2.md` | taxas por célula, efeito do prompt dentro de cada condição de código (pré-registrado) e efeito do código dentro de cada prompt (exploratório) |
| `a2x2/taxas_por_caso_2x2.csv` | taxa de cada métrica por caso nas quatro células |
| `qual/compile.tsv` | resultado da compilação de cada trecho (id, código de saída, nº de erros, primeiro erro) |
| `qual/items_avaliados.json` | cada achado de G1/G2 com forma do código, Q1 compila, Q2 defeito fora do Update, Q3 campos preservados |
| `qual/qualidade_codigo.md` | critérios por célula e comparações pareadas (Wilcoxon, Holm sobre os quatro critérios) |
| `Revisao_resultados.html` | visualizador: as quatro células lado a lado por caso, avaliação às cegas com exportação de CSV, tabelas |
| `rubrica/rubrica_cega_para_IA.md` | pacote cego dos 40 laudos da rubrica, para avaliadores de IA |
| `rubrica/notas_*.csv` | notas de cada avaliador (H1–H4) |
| `qual/chave_rubrica.csv` | número do laudo → caso e célula (não vai para os avaliadores) |

## Regras que valem aqui

- A taxa por caso é calculada entre as repetições de formato válido (a violação de formato,
  sobre todas), como no `ArtigoAnalysis.cs`; os scripts reproduzem os dois `resumo.md`.
- O código é avaliado como o laudo o entrega ao usuário (o plugin lê o JSON uma vez): quebras
  de linha escapadas duas vezes ficam como `\n` literal e não compilam.
- A amostra da rubrica (40 laudos: G1/G2, repetição 1, quatro células) e a ordem embaralhada
  (semente 20261022) são as mesmas no visualizador e no pacote das IAs.
- Os critérios Q1–Q3 e a rubrica foram definidos depois da coleta: são exploratórios.
