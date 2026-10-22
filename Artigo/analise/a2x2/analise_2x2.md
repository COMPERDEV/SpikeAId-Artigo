# Análise 2×2 — prompt × código anexado (gemini-3.8-flash, 30 casos × 5 repetições por célula)

## Taxas por célula

| Metric | Amateur, no code | Professional, no code | Amateur, with code | Professional, with code |
|---|---|---|---|---|
| Format violation | 0.0% | 0.0% | 0.0% | 0.0% |
| Wrong finding count | 18.0% | 0.0% | 40.0% | 0.0% |
| Wrong type attribution | 34.7% | 0.0% | 48.0% | 0.0% |
| Symbol fabrication (non-existent) | 1.3% | 0.0% | 0.7% | 0.0% |
| Symbol outside the stack (strict R4) | 100.0% | 0.0% | 100.0% | 3.3% |
| Value or frame fabrication | 12.0% | 2.7% | 14.0% | 0.0% |
| Missing ceiling treated as exceeded | 0.0% | 0.0% | 0.7% | 0.0% |
| Scope leakage | 3.3% | 0.0% | 4.7% | 0.0% |
| Missing level-design tips with DrawCalls finding | 0.0% | 0.0% | 0.0% | 0.0% |
| Correct indeterminacy (G5) | 0.0% | 28.0% | 0.0% | 20.0% |
| Undue indeterminacy (G1–G4) | 0.0% | 0.0% | 0.0% | 0.0% |
| Overrun claimed without overrun (G5–G6) | 30.0% | 2.0% | 22.0% | 6.0% |
| Valid but wrong | 55.3% | 15.3% | 69.3% | 15.3% |
| Code abstention (G1–G2, post hoc) | 0.0% | 100.0% | 0.0% | 2.0% |

## Efeito do prompt (pré-registrado), dentro de cada condição de código

### Without attached code: amateur vs professional

| Metric | Cases | Amateur | Professional | p exact | p Holm | r |
|---|---|---|---|---|---|---|
| Format violation | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Wrong finding count | 30 | 18.0% | 0.0% | 0.0039 | 0.0391 | 1.00 |
| Wrong type attribution | 30 | 34.7% | 0.0% | 0.0005 | 0.0054 | 1.00 |
| Symbol fabrication (non-existent) | 30 | 1.3% | 0.0% | 0.5000 | 1.0000 | 1.00 |
| Symbol outside the stack (strict R4) | 30 | 100.0% | 0.0% | <0.0001 | <0.0001 | 1.00 |
| Value or frame fabrication | 30 | 12.0% | 2.7% | 0.0256 | 0.2307 | 0.65 |
| Missing ceiling treated as exceeded | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Scope leakage | 30 | 3.3% | 0.0% | 0.5000 | 1.0000 | 1.00 |
| Missing level-design tips with DrawCalls finding | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Correct indeterminacy (G5) | 5 | 0.0% | 28.0% | 0.5000 | 1.0000 | -1.00 |
| Undue indeterminacy (G1–G4) | 20 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Overrun claimed without overrun (G5–G6) | 10 | 30.0% | 2.0% | 0.0313 | 0.2500 | 1.00 |
| Valid but wrong | 30 | 55.3% | 15.3% | 0.0001 | 0.0013 | 0.92 |
| Code abstention (G1–G2, post hoc) | 10 | 0.0% | 100.0% | 0.0020 | — | -1.00 |

### With attached code: amateur vs professional

| Metric | Cases | Amateur | Professional | p exact | p Holm | r |
|---|---|---|---|---|---|---|
| Format violation | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Wrong finding count | 30 | 40.0% | 0.0% | <0.0001 | 0.0006 | 1.00 |
| Wrong type attribution | 30 | 48.0% | 0.0% | <0.0001 | 0.0002 | 1.00 |
| Symbol fabrication (non-existent) | 30 | 0.7% | 0.0% | 1.0000 | 1.0000 | 1.00 |
| Symbol outside the stack (strict R4) | 30 | 100.0% | 3.3% | <0.0001 | <0.0001 | 1.00 |
| Value or frame fabrication | 30 | 14.0% | 0.0% | 0.0010 | 0.0088 | 1.00 |
| Missing ceiling treated as exceeded | 30 | 0.7% | 0.0% | 1.0000 | 1.0000 | 1.00 |
| Scope leakage | 30 | 4.7% | 0.0% | 0.2500 | 1.0000 | 1.00 |
| Missing level-design tips with DrawCalls finding | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Correct indeterminacy (G5) | 5 | 0.0% | 20.0% | 1.0000 | 1.0000 | -1.00 |
| Undue indeterminacy (G1–G4) | 20 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Overrun claimed without overrun (G5–G6) | 10 | 22.0% | 6.0% | 0.1250 | 1.0000 | 0.81 |
| Valid but wrong | 30 | 69.3% | 15.3% | <0.0001 | <0.0001 | 1.00 |
| Code abstention (G1–G2, post hoc) | 10 | 0.0% | 2.0% | 1.0000 | — | -1.00 |

## Efeito de anexar o código (exploratório, pós-hoc), dentro de cada prompt

### Amateur prompt: no code vs with code

| Metric | Cases | No code | With code | p exact | p Holm | r |
|---|---|---|---|---|---|---|
| Format violation | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Wrong finding count | 30 | 18.0% | 40.0% | 0.0101 | 0.1317 | -0.74 |
| Wrong type attribution | 30 | 34.7% | 48.0% | 0.1328 | 1.0000 | -0.52 |
| Symbol fabrication (non-existent) | 30 | 1.3% | 0.7% | 1.0000 | 1.0000 | 0.33 |
| Symbol outside the stack (strict R4) | 30 | 100.0% | 100.0% | 1.0000 | 1.0000 | 0.00 |
| Value or frame fabrication | 30 | 12.0% | 14.0% | 0.7383 | 1.0000 | -0.13 |
| Missing ceiling treated as exceeded | 30 | 0.0% | 0.7% | 1.0000 | 1.0000 | -1.00 |
| Scope leakage | 30 | 3.3% | 4.7% | 0.5000 | 1.0000 | -1.00 |
| Missing level-design tips with DrawCalls finding | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Correct indeterminacy (G5) | 5 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Undue indeterminacy (G1–G4) | 20 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Overrun claimed without overrun (G5–G6) | 10 | 30.0% | 22.0% | 0.6250 | 1.0000 | 0.40 |
| Valid but wrong | 30 | 55.3% | 69.3% | 0.0605 | 0.7266 | -0.64 |
| Code abstention (G1–G2, post hoc) | 10 | 0.0% | 0.0% | 1.0000 | — | 0.00 |

### Professional prompt: no code vs with code

| Metric | Cases | No code | With code | p exact | p Holm | r |
|---|---|---|---|---|---|---|
| Format violation | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Wrong finding count | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Wrong type attribution | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Symbol fabrication (non-existent) | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Symbol outside the stack (strict R4) | 30 | 0.0% | 3.3% | 0.1250 | 1.0000 | -1.00 |
| Value or frame fabrication | 30 | 2.7% | 0.0% | 0.5000 | 1.0000 | 1.00 |
| Missing ceiling treated as exceeded | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Scope leakage | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Missing level-design tips with DrawCalls finding | 30 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Correct indeterminacy (G5) | 5 | 28.0% | 20.0% | 1.0000 | 1.0000 | 1.00 |
| Undue indeterminacy (G1–G4) | 20 | 0.0% | 0.0% | 1.0000 | 1.0000 | 0.00 |
| Overrun claimed without overrun (G5–G6) | 10 | 2.0% | 6.0% | 1.0000 | 1.0000 | -0.33 |
| Valid but wrong | 30 | 15.3% | 15.3% | 1.0000 | 1.0000 | 0.00 |
| Code abstention (G1–G2, post hoc) | 10 | 100.0% | 2.0% | 0.0020 | — | 1.00 |

## Válida porém errada, por grupo

| Group | A no code | P no code | A with code | P with code |
|---|---|---|---|---|
| G1_CPU | 84.0% | 4.0% | 84.0% | 0.0% |
| G2_GC | 4.0% | 12.0% | 96.0% | 0.0% |
| G3_DRAWCALLS | 8.0% | 0.0% | 12.0% | 0.0% |
| G4_GPU | 88.0% | 0.0% | 88.0% | 0.0% |
| G5_FALTANTE | 100.0% | 72.0% | 100.0% | 92.0% |
| G6_ENGANOSA | 48.0% | 4.0% | 36.0% | 0.0% |
