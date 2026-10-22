# Critérios objetivos de qualidade do código — G1_CPU e G2_GC (10 casos × 5 repetições por célula)

| Critério | A sem código | P sem código | A com código | P com código |
|---|---|---|---|---|
| Achados (com código) | 56 (56) | 50 (0) | 93 (93) | 50 (49) |
| Achado traz código | 100.0% | 0.0% | 100.0% | 98.0% |
| Código numa linha só, com quebras escapadas | 25.6% | — | 16.7% | 0.0% |
| Compila aplicado ao projeto | 72.4% | — | 84.3% | 98.0% |
| Tira o defeito plantado do caminho por quadro | 100.0% | — | 91.6% | 80.0% |
| Preserva os campos públicos do script | 0.0% | — | 91.4% | 98.0% |
| Correção aplicável (as três) | 7.6% | — | 68.9% | 78.0% |
| Achados com correção aplicável (sobre todos os achados) | 7.6% | 0.0% | 68.9% | 78.0% |

### A com código × P com código

| Critério | Casos | A com código | P com código | p exato | p Holm | r |
|---|---|---|---|---|---|---|
| Achado traz código | 10 | 100.0% | 98.0% | 1,0000 | — | 1,00 |
| Código numa linha só, com quebras escapadas | 10 | 16.7% | 0.0% | 0,0156 | — | 1,00 |
| Compila aplicado ao projeto | 10 | 84.3% | 98.0% | 0,0391 | 0,1563 | -0,83 |
| Tira o defeito plantado do caminho por quadro | 10 | 91.6% | 80.0% | 0,9063 | 1,0000 | -0,07 |
| Preserva os campos públicos do script | 10 | 91.4% | 98.0% | 0,2500 | 0,7500 | -0,80 |
| Correção aplicável (as três) | 10 | 68.9% | 78.0% | 0,5547 | 1,0000 | -0,24 |
| Achados com correção aplicável (sobre todos os achados) | 10 | 68.9% | 78.0% | 0,5547 | — | -0,24 |

### A sem código × A com código

| Critério | Casos | A sem código | A com código | p exato | p Holm | r |
|---|---|---|---|---|---|---|
| Achado traz código | 10 | 100.0% | 100.0% | 1,0000 | — | 0,00 |
| Código numa linha só, com quebras escapadas | 10 | 25.6% | 16.7% | 0,4375 | — | 0,33 |
| Compila aplicado ao projeto | 10 | 72.4% | 84.3% | 0,3672 | 0,3672 | -0,39 |
| Tira o defeito plantado do caminho por quadro | 10 | 100.0% | 91.6% | 0,0313 | 0,0625 | 1,00 |
| Preserva os campos públicos do script | 10 | 0.0% | 91.4% | 0,0020 | 0,0078 | -1,00 |
| Correção aplicável (as três) | 10 | 7.6% | 68.9% | 0,0020 | 0,0078 | -1,00 |
| Achados com correção aplicável (sobre todos os achados) | 10 | 7.6% | 68.9% | 0,0020 | — | -1,00 |

## Forma do código entregue

| Célula | classe_original | metodo | trecho | outra_classe |
|---|---|---|---|---|
| sem_amador | 50 | 0 | 0 | 6 |
| sem_profissional | 0 | 0 | 0 | 0 |
| com_amador | 73 | 0 | 6 | 14 |
| com_profissional | 7 | 42 | 0 | 0 |

## Erros de compilação (primeiro erro de cada trecho)

- com_amador CS0116: 1
- com_amador CS0246: 1
- com_amador CS1056: 12
- com_profissional CS0103: 1
- sem_amador CS0116: 10
- sem_amador CS1056: 4
- sem_amador CS1061: 1

## Defeitos que ficaram no caminho por quadro

- com_amador array: 1
- com_amador concat+array: 3
- com_amador cpu: 4
- com_profissional cpu: 9

## Campos públicos perdidos (quando o código reescreve StressTestScene)

- com_amador cpuScansPerFrame: 5
- com_amador cubesPerFrame: 2
- com_amador emptyObjects: 5
- com_amador gcArrayAllocs: 7
- com_amador gcConcatIterations: 7
- com_amador overdrawLayers: 6
- com_amador overdrawMaterial: 5
- com_amador prepopulateCount: 5
- com_amador spawnRadius: 2
- com_amador stressCpu: 1
- com_amador stressDraw: 3
- com_amador stressGc: 3
- com_amador testPrefab: 2
- com_amador useUniqueMaterials: 2
- com_profissional cpuScansPerFrame: 1
- com_profissional cubesPerFrame: 1
- com_profissional emptyObjects: 1
- com_profissional gcArrayAllocs: 1
- com_profissional gcConcatIterations: 1
- com_profissional overdrawLayers: 1
- com_profissional overdrawMaterial: 1
- com_profissional prepopulateCount: 1
- com_profissional spawnRadius: 1
- com_profissional stressCpu: 1
- com_profissional stressDraw: 1
- com_profissional stressGc: 1
- com_profissional testPrefab: 1
- com_profissional useUniqueMaterials: 1
- sem_amador cpuScansPerFrame: 50
- sem_amador cubesPerFrame: 50
- sem_amador emptyObjects: 50
- sem_amador gcArrayAllocs: 50
- sem_amador gcConcatIterations: 50
- sem_amador overdrawLayers: 50
- sem_amador overdrawMaterial: 50
- sem_amador prepopulateCount: 50
- sem_amador spawnRadius: 50
- sem_amador stressCpu: 50
- sem_amador stressDraw: 50
- sem_amador stressGc: 50
- sem_amador testPrefab: 50
- sem_amador useUniqueMaterials: 50
