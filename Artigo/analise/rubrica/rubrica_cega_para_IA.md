# Avaliação cega de laudos de desempenho — rubrica para avaliador de IA

Você é um avaliador independente num estudo sobre laudos de diagnóstico de desempenho gerados por um
modelo de linguagem para projetos Unity. Avalie os **40 laudos** abaixo com a rubrica. Não há
informação sobre como cada laudo foi produzido; não tente adivinhar, e julgue cada um só pelo que
está nele e pela telemetria que o acompanha.

## Contexto

Um plugin de Editor da Unity mede o jogo, captura os quadros que excederam o orçamento de hardware e
envia ao modelo a telemetria (o "turno do usuário"). Em parte dos laudos o modelo recebeu também o
código-fonte do script citado na pilha de chamadas; em outros, não. O laudo deve ter um achado por
amostra, com a causa raiz, o código C# refatorado (ou vazio, se não houver como corrigir sem inventar)
e uma explicação para um desenvolvedor **iniciante**.

O script real do projeto (StressTestScene.cs) é o abaixo — é o mesmo em todos os casos. É um script
de teste que cria gargalos de propósito; julgue as correções como se ele fosse código de jogo:
o comportamento a preservar é o que o script faz e expõe (campos do Inspector, métodos), e a
correção ideal reduz o custo apontado pela telemetria mudando só o necessário. Atenção: os valores
dos campos do Inspector de cada cena **não** vão na telemetria — o caso informa, em
"Cena do caso", quais opções estavam ligadas, mas o modelo que escreveu o laudo não sabia disso.

```csharp
using System;
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{

    public class StressTestScene : MonoBehaviour
    {

        public bool stressGc = true;

        public bool stressCpu = true;

        public bool stressDraw = false;

        public GameObject testPrefab;

        [Range(1, 20)]
        public float spawnRadius = 10f;

        [Range(1, 50)]
        public int cubesPerFrame = 1;

        public bool useUniqueMaterials = false;

        [Range(1, 512)]
        public int cpuScansPerFrame = 1;

        public int emptyObjects = 0;

        public int gcConcatIterations = 1000;

        public int gcArrayAllocs = 0;

        public int overdrawLayers = 0;

        public Material overdrawMaterial;

        [Range(0, 2000)]
        public int prepopulateCount = 0;

        private void Start()
        {

            if (prepopulateCount > 0 && testPrefab != null)
            {
                for (int i = 0; i < prepopulateCount; i++)
                {
                    SpawnOne();
                }
            }

            for (int i = 0; i < emptyObjects; i++) new GameObject("Empty_" + i);

            if (overdrawLayers > 0 && overdrawMaterial != null) BuildOverdrawLayers();
        }

        private void BuildOverdrawLayers()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            for (int i = 0; i < overdrawLayers; i++)
            {
                float d = cam.nearClipPlane + 0.05f + i * 0.001f;
                float h = 2f * d * Mathf.Tan(halfFov) * 1.1f;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                quad.name = "Overdraw_" + i;
                quad.transform.SetParent(cam.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, d);
                quad.transform.localScale = new Vector3(h * cam.aspect, h, 1f);
                quad.GetComponent<Renderer>().sharedMaterial = overdrawMaterial;
            }
        }

        private void Update()
        {
            if (stressGc)
            {

                string s = "";
                for (int i = 0; i < gcConcatIterations; i++) s += i.ToString();
                GC.KeepAlive(s);

                for (int i = 0; i < gcArrayAllocs; i++)
                {
                    var block = new object[10000];
                    GC.KeepAlive(block);
                }
            }

            if (stressCpu)
            {

                for (int k = 0; k < cpuScansPerFrame; k++)
                {
                    var allObjects = GameObject.FindObjectsOfType<GameObject>();
                    GC.KeepAlive(allObjects);
                }
            }

            if (stressDraw && testPrefab != null)
            {
                int n = Mathf.Max(1, cubesPerFrame);
                for (int i = 0; i < n; i++) SpawnOne();
            }
        }

        private void SpawnOne()
        {
            var clone = Instantiate(
                testPrefab,
                UnityEngine.Random.insideUnitSphere * spawnRadius,
                Quaternion.identity);

            if (useUniqueMaterials)
            {
                var renderer = clone.GetComponent<Renderer>();
                if (renderer != null)
                {

                    renderer.material.color = new Color(
                        UnityEngine.Random.value,
                        UnityEngine.Random.value,
                        UnityEngine.Random.value);
                }
            }
        }
    }
}

```

## Rubrica (notas inteiras de 1 a 5; 2 e 4 são intermediárias)

| Critério | 1 | 3 | 5 |
|---|---|---|---|
| **H1 Pertinência da correção** (só se o laudo traz código) | corrige outra coisa, ou nada | ataca a causa, mas incompleta ou com efeito colateral | ataca exatamente a causa que a telemetria aponta e reduziria o custo medido |
| **H2 Preservação do script** (só se o laudo traz código) | reescreve o script e perde funções ou campos | mantém o essencial, com mudanças não pedidas | muda só o necessário |
| **H3 Clareza didática** | jargão sem explicação, confuso ou errado | compreensível com esforço | um iniciante entende o porquê e o que fazer |
| **H4 Utilidade para o iniciante (global)** | levaria a erro: prescrição errada ou inventada | neutro: não ajuda nem atrapalha | o iniciante resolveria o problema com este laudo |

- Se o laudo **não traz código** em nenhum achado, deixe H1 e H2 vazios.
- Se o laudo tem vários achados, avalie o laudo inteiro (uma nota por critério).
- Um laudo que se abstém do código com motivo correto não é penalizado em H3; em H4, julgue se
  a abstenção ajuda mais ou menos do que uma correção ajudaria.
- Código entregue numa linha só, com `\n` literal no lugar das quebras de linha, é como o
  usuário o receberia: considere isso em H1/H4 (ele não compila colado assim).

## Formato da resposta (obrigatório)

Responda **apenas** com um CSV com separador ponto e vírgula, cabeçalho e exatamente 40 linhas,
nesta ordem de colunas (comentário curto, sem ponto e vírgula):

```
laudo;H1;H2;H3;H4;comentario
1;4;5;4;4;corrige a varredura e mantém os campos
2;;;3;2;sem código, diagnóstico não sustentado
```

Salve como `notas_<nome-do-avaliador>.csv` (ex.: `notas_codex.csv`) e informe o modelo usado.

---

## Laudo 1

**Caso:** G1_CPU_05 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=64, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:132, cpu_max_ms:240.29, gc_violations:132, gc_max_bytes:10352981, drawcalls_violations:0, drawcalls_max:0, gpu_violations:16, gpu_max_ms:29.54, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:88, cpu_ms:240.29, gc_bytes:10279872, gc_collect_ms:16.05, draw_calls:7, mem_bytes:4574236672, gpu_ms:13.05, t_ms:8258, culprit_stack:"Update.ScriptRunBehaviourUpdate (55.62ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (55.60ms) | UnityEngine.CoreModule.dll!UnityEngine.Rendering::RenderPipelineManager.DoRenderLoop_Internal() [Invoke] (0.62ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 1 · achado 1 · tipo CPU

**Causa raiz:** No frame 88, o tempo de CPU atingiu 240.29ms, excedendo significativamente o orcamento de 16.7ms/frame estabelecido para 60 FPS. A pilha de execucao aponta que o metodo StressTestScene.Update() consumiu 55.60ms dos 55.62ms gastos em Update.ScriptRunBehaviourUpdate. O codigo_refatorado foi deixado vazio porque a telemetria identifica apenas a classe StressTestScene e o metodo Update(), sem detalhar campos ou operacoes internas da rotina, tornando impossivel propor uma refatoracao sem inventar simbolos inexistentes na amostra.

**Código refatorado:** (vazio)

**Explicação didática:** O processador (CPU) precisa concluir todas as instrucoes do jogo a cada quadro (frame) dentro de uma fatia de tempo chamada orcamento de quadro, que e de 16.7 milissegundos (ms) para manter uma taxa de 60 quadros por segundo (FPS). No frame 88, a CPU demorou 240.29ms, causando um congelamento visivel na tela. Uma parte substancial do tempo de scripts foi gasta no metodo Update() da classe StressTestScene, que roda a cada quadro. Quando uma funcao executada no Update() realiza calculos muito pesados ou repetitivos de uma so vez, a CPU nao consegue entregar a imagem a tempo. E necessario investigar o interior de StressTestScene.Update() para distribuir essa carga ao longo do tempo ou otimizar seus algoritmos.

**Dicas de level design:** null

---

## Laudo 2

**Caso:** G1_CPU_02 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=24, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:335, cpu_max_ms:402.50, gc_violations:335, gc_max_bytes:3925405, drawcalls_violations:0, drawcalls_max:0, gpu_violations:12, gpu_max_ms:447.19, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:160, cpu_ms:402.50, gc_bytes:3868438, gc_collect_ms:16.69, draw_calls:7, mem_bytes:3396362240, gpu_ms:11.81, t_ms:6780, culprit_stack:"Update.ScriptRunBehaviourUpdate (21.36ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (21.33ms) | UnityEngine.CoreModule.dll!UnityEngine.Rendering::RenderPipelineManager.DoRenderLoop_Internal() [Invoke] (0.67ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 2 · achado 1 · tipo CPU

**Causa raiz:** No frame 160, o tempo de CPU atingiu 402.50ms, superando severamente o orcamento estabelecido de 16.7ms/frame (alvo de 60 FPS). O culprit_stack identifica que a execucao foi dominada pelo metodo StressTestScene.Update() (21.33ms na chamada interna monitorada), onde ocorrem buscas custosas na hierarquia por meio de GameObject.FindObjectsOfType<GameObject>() iteradas em laco (stressCpu), somadas a concatenacoes repetitivas de string em laco (stressGc).

**Código refatorado:**

```csharp
private void Update()
{
    if (stressGc)
    {
        // Evita concatenacao repetida de strings em laco que sobrecarrega a CPU e gera lixo na memoria
    }

    if (stressCpu)
    {
        // A busca exaustiva por GameObject.FindObjectsOfType foi removida do Update para nao bloquear a CPU a cada quadro
    }

    if (stressDraw && testPrefab != null)
    {
        int n = Mathf.Max(1, cubesPerFrame);
        for (int i = 0; i < n; i++) SpawnOne();
    }
}
```

**Explicação didática:** O tempo de CPU (a unidade central de processamento do computador que executa a logica do jogo) ficou muito acima do limite maximo de 16.7 milissegundos por quadro necessarios para alcancar uma taxa fluida de 60 FPS (quadros por segundo). O metodo Update e executado a cada quadro renderizado na tela; dentro dele, a chamada a GameObject.FindObjectsOfType forca a Unity a vasculhar todos os objetos ativos da cena, uma operacao extremamente lenta e pesada. Ao mesmo tempo, concatenar textos dentro de um laco de repeticao cria dezenas de copias temporarias na memoria, consumindo ciclos preciosos do processador. Para corrigir picos de processamento, buscas globais e operacoes repetitivas pesadas devem ser evitadas dentro de Update.

**Dicas de level design:** null

---

## Laudo 3

**Caso:** G2_GC_03 · **Cena do caso:** cena gc: stressGc, gcArrayAllocs=64, gcConcatIterations=1000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:47, cpu_max_ms:380.73, gc_violations:151, gc_max_bytes:8124711, drawcalls_violations:0, drawcalls_max:0, gpu_violations:41, gpu_max_ms:66.83, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:1915, cpu_ms:22.67, gc_bytes:7987847, gc_collect_ms:24.57, draw_calls:7, mem_bytes:5620846592, gpu_ms:9.17, t_ms:10309, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 3 · achado 1 · tipo GC

**Causa raiz:** No frame 1915, o tempo gasto com coleta de lixo (gc_collect_ms) atingiu 24.57 ms, superando o cpu_ms de 22.67 ms e o orcamento de 16.7 ms/frame, associado a uma alocacao de 7987847 bytes (gc_bytes). O gc_collect_ms explica totalmente o pico observado no quadro, tendo como origem a chamada indicada em culprit_stack: StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] -> GC.Alloc. Nao e possivel fornecer codigo_refatorado porque a pilha de execucao informa apenas o metodo Update() da classe StressTestScene e o evento GC.Alloc, sem detalhar os campos, variaveis locais ou instrucoes especificas que geram a alocacao, impedindo a refatoracao sem inventar simbolos nao presentes nos dados.

**Código refatorado:** (vazio)

**Explicação didática:** O Garbage Collector (GC, ou Coletor de Lixo) e um sistema automatico responsavel por encontrar e liberar memoria da memoria RAM que nao esta mais sendo utilizada pelo jogo. Quando o codigo cria novos objetos repetidamente dentro do metodo Update() (uma funcao que a Unity executa a cada quadro renderizado), ocorre o chamado GC.Alloc (alocacao de memoria gerenciada). Ao atingir um limite, o Garbage Collector precisa pausar a execucao normal da CPU (Unidade Central de Processamento) para varrer e limpar esses dados, gerando o tempo medido em gc_collect_ms. Como a coleta consumiu 24.57 ms no frame 1915, o jogo travou momentaneamente, ultrapassando a meta de 16.7 ms necessaria para manter 60 quadros por segundo.

**Dicas de level design:** null

---

## Laudo 4

**Caso:** G2_GC_03 · **Cena do caso:** cena gc: stressGc, gcArrayAllocs=64, gcConcatIterations=1000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:47, cpu_max_ms:380.73, gc_violations:151, gc_max_bytes:8124711, drawcalls_violations:0, drawcalls_max:0, gpu_violations:41, gpu_max_ms:66.83, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:1915, cpu_ms:22.67, gc_bytes:7987847, gc_collect_ms:24.57, draw_calls:7, mem_bytes:5620846592, gpu_ms:9.17, t_ms:10309, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 4 · achado 1 · tipo GC

**Causa raiz:** Concatenação excessiva de strings dentro do Update (s += i.ToString()) e instanciação repetida de arrays (new object[10000]), gerando cerca de 8 MB de lixo por frame e disparando pausas severas de Garbage Collection (24.57 ms).

**Código refatorado:**

```csharp
using System;
using System.Text;
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    public class StressTestScene : MonoBehaviour
    {
        public bool stressGc = false;
        public bool stressCpu = false;
        public bool stressDraw = false;
        public GameObject testPrefab;
        [Range(1, 20)] public float spawnRadius = 10f;
        [Range(1, 50)] public int cubesPerFrame = 1;
        public bool useUniqueMaterials = false;
        [Range(1, 512)] public int cpuScansPerFrame = 1;
        public int emptyObjects = 0;
        public int gcConcatIterations = 1000;
        public int gcArrayAllocs = 0;
        public int overdrawLayers = 0;
        public Material overdrawMaterial;
        [Range(0, 2000)] public int prepopulateCount = 0;

        private StringBuilder _sbCached = new StringBuilder(2048);
        private MaterialPropertyBlock _propBlock;
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (prepopulateCount > 0 && testPrefab != null)
            {
                for (int i = 0; i < prepopulateCount; i++)
                {
                    SpawnOne();
                }
            }

            for (int i = 0; i < emptyObjects; i++)
            {
                new GameObject("Empty_" + i);
            }

            if (overdrawLayers > 0 && overdrawMaterial != null)
            {
                BuildOverdrawLayers();
            }
        }

        private void BuildOverdrawLayers()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            for (int i = 0; i < overdrawLayers; i++)
            {
                float d = cam.nearClipPlane + 0.05f + i * 0.001f;
                float h = 2f * d * Mathf.Tan(halfFov) * 1.1f;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                quad.name = "Overdraw_" + i;
                quad.transform.SetParent(cam.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, d);
                quad.transform.localScale = new Vector3(h * cam.aspect, h, 1f);
                quad.GetComponent<Renderer>().sharedMaterial = overdrawMaterial;
            }
        }

        private void Update()
        {
            if (stressGc)
            {
                _sbCached.Clear();
                for (int i = 0; i < gcConcatIterations; i++)
                {
                    _sbCached.Append(i);
                }
            }

            if (stressCpu)
            {
                // Otimizado: evitar buscas exaustivas na hierarquia durante o Update
            }

            if (stressDraw && testPrefab != null)
            {
                int n = Mathf.Max(1, cubesPerFrame);
                for (int i = 0; i < n; i++)
                {
                    SpawnOne();
                }
            }
        }

        private void SpawnOne()
        {
            var clone = Instantiate(testPrefab, UnityEngine.Random.insideUnitSphere * spawnRadius, Quaternion.identity);
            if (useUniqueMaterials)
            {
                var renderer = clone.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.GetPropertyBlock(_propBlock);
                    _propBlock.SetColor(ColorProp, new Color(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value));
                    renderer.SetPropertyBlock(_propBlock);
                }
            }
        }
    }
}
```

**Explicação didática:** Strings em C# não podem ser alteradas depois de criadas. Toda vez que você faz 's += i', a Unity cria um novo pedaço de texto na memória e descarta o anterior. O Garbage Collector precisa parar o jogo para limpar essa sujeira, causando travamentos. Usar StringBuilder reaproveita o mesmo espaço sem criar lixo na memória.

### Laudo 4 · achado 2 · tipo CPU

**Causa raiz:** Chamada repetida de GameObject.FindObjectsOfType dentro do Update(), que varre recursivamente todos os nós da cena na CPU a cada quadro, resultando em picos de até 380 ms.

**Código refatorado:**

```csharp
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    public class CpuOptimizationExample : MonoBehaviour
    {
        // Em vez de FindObjectsOfType no Update, faça cache ou use eventos/registros no Start/Awake.
        private GameObject[] _cachedObjects;

        private void Start()
        {
            _cachedObjects = GameObject.FindObjectsOfType<GameObject>();
        }

        private void Update()
        {
            if (_cachedObjects == null) return;
            // Operações eficientes utilizando o cache pré-existente sem varrer a cena
        }
    }
}
```

**Explicação didática:** O comando FindObjectsOfType é como procurar uma chave revirando a casa inteira de ponta-cabeça. Fazer isso 60 vezes por segundo esgota o processador. O ideal é procurar apenas uma vez no início (Start) e guardar a referência em uma variável.

**Dicas de level design:** null

---

## Laudo 5

**Caso:** G1_CPU_01 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=16, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:495, cpu_max_ms:43.79, gc_violations:495, gc_max_bytes:2650856, drawcalls_violations:0, drawcalls_max:0, gpu_violations:12, gpu_max_ms:23.02, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:433, cpu_ms:43.79, gc_bytes:2631025, gc_collect_ms:17.94, draw_calls:7, mem_bytes:2949439488, gpu_ms:11.84, t_ms:9988, culprit_stack:"Update.ScriptRunBehaviourUpdate (16.86ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (16.85ms) | GC.Collect (3.06ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 5 · achado 1 · tipo GC

**Causa raiz:** Alocação excessiva de memória gerenciada dentro do método Update() (~2,6 MB por frame), resultando em coletas constantes do Garbage Collector (GC.Collect atingindo até 17,94 ms). Essas pausas do coletor de lixo somadas ao tempo de execução do script elevam o tempo de CPU para 43,79 ms, violando severamente a meta de 16,7 ms (60 FPS).

**Código refatorado:**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    public class StressTestScene : MonoBehaviour
    {
        // Capacidade pré-alocada para evitar redimensionamentos dinâmicos
        [SerializeField] private int bufferCapacity = 1024;

        // Buffers e coleções cacheadas para eliminar alocações no heap a cada frame
        private List<Vector3> _positionsCache;
        private float[] _dataBuffer;

        private void Awake()
        {
            _positionsCache = new List<Vector3>(bufferCapacity);
            _dataBuffer = new float[bufferCapacity];
        }

        private void Update()
        {
            // Limpa coleções reutilizando a memória já alocada em vez de criar novas instâncias
            _positionsCache.Clear();

            // Executa operações diretamente sobre buffers pré-alocados sem boxing ou LINQ
            for (int i = 0; i < bufferCapacity; i++)
            {
                _dataBuffer[i] = Mathf.Sin(Time.time + i);
                _positionsCache.Add(new Vector3(i, _dataBuffer[i], 0f));
            }
        }
    }
}
```

**Explicação didática:** Toda vez que criamos objetos temporários (como 'new List()', 'new int[]' ou concatenação de texto) dentro do método Update, o Unity reserva um novo espaço na memória RAM. Como o Update roda dezenas de vezes por segundo, essa memória enche rapidamente e a Unity é forçada a congelar o jogo momentaneamente para limpar esse lixo (o Garbage Collector). Para resolver isso, criamos os recipientes de dados apenas uma vez (no Awake/Start) e apenas limpamos e reutilizamos esses mesmos recipientes a cada frame, zerando a criação de lixo e eliminando os engasgos de desempenho.

**Dicas de level design:** null

---

## Laudo 6

**Caso:** G2_GC_01 · **Cena do caso:** cena gc: stressGc, gcArrayAllocs=32, gcConcatIterations=1000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:40, cpu_max_ms:102.01, gc_violations:128, gc_max_bytes:5557527, drawcalls_violations:0, drawcalls_max:0, gpu_violations:37, gpu_max_ms:40.48, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:1009, cpu_ms:23.60, gc_bytes:5424641, gc_collect_ms:25.84, draw_calls:15, mem_bytes:5257035776, gpu_ms:0.00, t_ms:6131, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 6 · achado 1 · tipo GC

**Causa raiz:** Alocação excessiva de memória gerenciada dentro do método Update() em StressTestScene (mais de 5.4 MB alocados em um único frame), disparando coletas de lixo síncronas frequentes do Garbage Collector com pausas de até 25.84 ms, ultrapassando amplamente o orçamento de 16.7 ms por frame.

**Código refatorado:**

_(entregue numa linha só, como abaixo)_

```csharp
using UnityEngine;\nusing System.Collections.Generic;\n\nnamespace PerformanceAdvisor.StressTest\n{\n    public class StressTestScene : MonoBehaviour\n    {\n        [SerializeField] private int bufferCapacity = 1024;\n        private List<Vector3> cachedPositionBuffer;\n        private byte[] reusableDataBuffer;\n\n        private void Awake()\n        {\n            cachedPositionBuffer = new List<Vector3>(bufferCapacity);\n            reusableDataBuffer = new byte[bufferCapacity];\n        }\n\n        private void Update()\n        {\n            cachedPositionBuffer.Clear();\n            ProcessDataNonAlloc(cachedPositionBuffer, reusableDataBuffer);\n        }\n\n        private void ProcessDataNonAlloc(List<Vector3> positions, byte[] buffer)\n        {\n            for (int i = 0; i < positions.Capacity; i++)\n            {\n                positions.Add(transform.position);\n                buffer[i] = (byte)(i % 256);\n            }\n        }\n    }\n}
```

**Explicação didática:** Toda vez que você cria listas, arrays ou novos objetos dentro do método Update(), o Unity aloca memória dinâmica temporária. Quando essa memória se acumula, o Garbage Collector (o coletor de lixo da memória) precisa pausar a execução do jogo para fazer a limpeza, gerando travamentos perceptíveis (stuttering). Para resolver isso, alocamos os buffers e listas uma única vez no Awake() ou Start() e apenas limpamos e reutilizamos esses mesmos recursos a cada frame usando métodos como List.Clear(), zerando a geração de lixo.

**Dicas de level design:** null

---

## Laudo 7

**Caso:** G1_CPU_05 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=64, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:132, cpu_max_ms:240.29, gc_violations:132, gc_max_bytes:10352981, drawcalls_violations:0, drawcalls_max:0, gpu_violations:16, gpu_max_ms:29.54, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:88, cpu_ms:240.29, gc_bytes:10279872, gc_collect_ms:16.05, draw_calls:7, mem_bytes:4574236672, gpu_ms:13.05, t_ms:8258, culprit_stack:"Update.ScriptRunBehaviourUpdate (55.62ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (55.60ms) | UnityEngine.CoreModule.dll!UnityEngine.Rendering::RenderPipelineManager.DoRenderLoop_Internal() [Invoke] (0.62ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 7 · achado 1 · tipo CPU

**Causa raiz:** O método GameObject.FindObjectsOfType<GameObject>() é executado repetidamente dentro do Update(). Essa chamada varre recursivamente toda a hierarquia de objetos carregados na cena e aloca um novo array no heap a cada invocação, consumindo dezenas de milissegundos de processamento CPU e saturando o Garbage Collector.

**Código refatorado:**

_(entregue numa linha só, como abaixo)_

```csharp
using System;\nusing UnityEngine;\n\nnamespace PerformanceAdvisor.StressTest\n{\n    public class StressTestScene : MonoBehaviour\n    {\n        public bool stressGc = false;\n        public bool stressCpu = false;\n        public bool stressDraw = false;\n        public GameObject testPrefab;\n        [Range(1, 20)] public float spawnRadius = 10f;\n        [Range(1, 50)] public int cubesPerFrame = 1;\n        public bool useUniqueMaterials = false;\n        [Range(1, 512)] public int cpuScansPerFrame = 1;\n        public int emptyObjects = 0;\n        public int gcConcatIterations = 0;\n        public int gcArrayAllocs = 0;\n        public int overdrawLayers = 0;\n        public Material overdrawMaterial;\n        [Range(0, 2000)] public int prepopulateCount = 0;\n\n        private static readonly MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();\n        private static readonly int ColorPropertyId = Shader.PropertyToID(\"_Color\");\n\n        private void Start()\n        {\n            if (prepopulateCount > 0 && testPrefab != null)\n            {\n                for (int i = 0; i < prepopulateCount; i++)\n                {\n                    SpawnOne();\n                }\n            }\n\n            for (int i = 0; i < emptyObjects; i++)\n            {\n                new GameObject(\"Empty_\" + i);\n            }\n\n            if (overdrawLayers > 0 && overdrawMaterial != null)\n            {\n                BuildOverdrawLayers();\n            }\n        }\n\n        private void BuildOverdrawLayers()\n        {\n            Camera cam = Camera.main;\n            if (cam == null) return;\n            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;\n            for (int i = 0; i < overdrawLayers; i++)\n            {\n                float d = cam.nearClipPlane + 0.05f + i * 0.001f;\n                float h = 2f * d * Mathf.Tan(halfFov) * 1.1f;\n                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);\n                var col = quad.GetComponent<Collider>();\n                if (col != null) Destroy(col);\n                quad.name = \"Overdraw_\" + i;\n                quad.transform.SetParent(cam.transform, false);\n                quad.transform.localPosition = new Vector3(0f, 0f, d);\n                quad.transform.localScale = new Vector3(h * cam.aspect, h, 1f);\n                quad.GetComponent<Renderer>().sharedMaterial = overdrawMaterial;\n            }\n        }\n\n        private void Update()\n        {\n            if (stressDraw && testPrefab != null)\n            {\n                int n = Mathf.Max(1, cubesPerFrame);\n                for (int i = 0; i < n; i++)\n                {\n                    SpawnOne();\n                }\n            }\n        }\n\n        private void SpawnOne()\n        {\n            var clone = Instantiate(testPrefab, UnityEngine.Random.insideUnitSphere * spawnRadius, Quaternion.identity);\n            if (useUniqueMaterials)\n            {\n                var rend = clone.GetComponent<Renderer>();\n                if (rend != null)\n                {\n                    PropertyBlock.SetColor(ColorPropertyId, new Color(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value));\n                    rend.SetPropertyBlock(PropertyBlock);\n                }\n            }\n        }\n    }\n}
```

**Explicação didática:** A função FindObjectsOfType obriga a CPU a vasculhar cada objeto da cena do zero toda vez que roda. Em vez de procurar objetos a cada frame dentro do Update, guarde referências diretas ou use listas gerenciadas. Também evitamos o uso de renderer.material (que duplica materiais na memória) trocando por MaterialPropertyBlock, que altera propriedades visuais sem clonar materiais.

### Laudo 7 · achado 2 · tipo GC

**Causa raiz:** Concatenação de strings em laço for (s += i.ToString()) e alocação contínua de novos arrays de objetos (new object[10000]) dentro do Update() gerando mais de 10 MB de lixo por frame, forçando pausas de coleta de 16ms.

**Código refatorado:**

_(entregue numa linha só, como abaixo)_

```csharp
// O lixo foi eliminado desativando as alocacoes desnecessarias e substituindo strings dinamicas por buffers reutilizaveis quando necessario.\n// Veja o script completo otimizado no finding de CPU acima.
```

**Explicação didática:** Em C#, somar strings com '+' cria um novo texto na memória a cada repetição. Quando isso roda centenas de vezes por segundo, a memória enche rapidamente de objetos descartados e o Garbage Collector trava o jogo temporariamente para limpar essa sujeira, provocando engasgos (stutters).

**Dicas de level design:** null

---

## Laudo 8

**Caso:** G2_GC_04 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=32, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:245, cpu_max_ms:125.67, gc_violations:245, gc_max_bytes:5219463, drawcalls_violations:0, drawcalls_max:0, gpu_violations:47, gpu_max_ms:26.02, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:170, cpu_ms:73.36, gc_bytes:5149014, gc_collect_ms:22.75, draw_calls:7, mem_bytes:5634785280, gpu_ms:15.17, t_ms:8354, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 8 · achado 1 · tipo GC

**Causa raiz:** No frame 170, o tempo total de CPU foi de 73.36 ms, bem acima do orcamento de 16.7 ms/frame. O tempo gasto com coleta de lixo (gc_collect_ms) foi de 22.75 ms, o que explica aproximadamente 31% do pico total de CPU do quadro e, isoladamente, ja ultrapassa todo o orcamento de 16.7 ms. Esse congelamento foi provocado por 5.149.014 bytes alocados (gc_bytes) oriundos do metodo StressTestScene.Update(), onde concatenacoes repetitivas de strings (s += i.ToString()) e criacao de novos arrays (new object[10000]) forcam a execucao do Garbage Collector.

**Código refatorado:**

```csharp
private void Update()
{
    // Removidas as alocacoes de strings e arrays temporarios que sobrecarregavam o Garbage Collector
    if (stressCpu)
    {
        for (int k = 0; k < cpuScansPerFrame; k++)
        {
            var allObjects = GameObject.FindObjectsOfType<GameObject>();
            GC.KeepAlive(allObjects);
        }
    }

    if (stressDraw && testPrefab != null)
    {
        int n = Mathf.Max(1, cubesPerFrame);
        for (int i = 0; i < n; i++) SpawnOne();
    }
}
```

**Explicação didática:** O Garbage Collector (GC, ou coletor de lixo) e um sistema automatico que varre a memoria gerenciada (heap) para descartar dados que nao estao mais sendo usados pelo jogo. Quando o codigo cria muitos textos (strings) ou objetos novos a cada frame (quadro de imagem), a memoria enche muito rapido. Para nao ficar sem espaco, o Garbage Collector pausa a execucao do jogo para limpar essa memoria. Essa pausa durou 22.75 ms no frame 170, fazendo o jogo engasgar. Evitar alocar novos objetos dentro do metodo Update() impede que essas pausas acontecam.

**Dicas de level design:** null

---

## Laudo 9

**Caso:** G2_GC_02 · **Cena do caso:** cena gc: stressGc, gcArrayAllocs=48, gcConcatIterations=1000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:48, cpu_max_ms:118.27, gc_violations:140, gc_max_bytes:6835851, drawcalls_violations:0, drawcalls_max:0, gpu_violations:33, gpu_max_ms:65.03, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:1643, cpu_ms:21.61, gc_bytes:6706563, gc_collect_ms:23.18, draw_calls:7, mem_bytes:5605183488, gpu_ms:9.02, t_ms:8815, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 9 · achado 1 · tipo GC

**Causa raiz:** No frame 1643, a coleta de lixo consumiu gc_collect_ms de 23.18 ms frente a um cpu_ms de 21.61 ms e um orçamento de 16.7 ms/frame, explicando a totalidade do pico que travou a execução do quadro. Esse tempo excessivo de coleta foi desencadeado pelo acúmulo de alocações transitórias registradas em gc_bytes (6706563 bytes no frame), originadas no método StressTestScene.Update() conforme identificado na pilha de chamadas (StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc), em particular pelas concatenações contínuas de string e instanciações repetidas de arrays a cada quadro.

**Código refatorado:**

```csharp
        private void Update()
        {
            if (stressGc)
            {
                // Alocações repetidas de string e arrays foram removidas para evitar disparos do Garbage Collector durante o Update.
            }

            if (stressCpu)
            {
                for (int k = 0; k < cpuScansPerFrame; k++)
                {
                    var allObjects = GameObject.FindObjectsOfType<GameObject>();
                    GC.KeepAlive(allObjects);
                }
            }

            if (stressDraw && testPrefab != null)
            {
                int n = Mathf.Max(1, cubesPerFrame);
                for (int i = 0; i < n; i++) SpawnOne();
            }
        }
```

**Explicação didática:** O Garbage Collector (GC, ou Coletor de Lixo) é o sistema responsável por encontrar e liberar blocos de memória que o jogo não está mais utilizando. Na Unity, quando você cria variáveis temporárias que não duram além daquele momento — como juntar textos repetidamente com o operador '+' ou criar novos blocos de arrays a cada quadro (função Update) —, essa memória fica acumulada na memória gerenciada (Heap). Quando a quantidade de lixo atinge um limite, o Garbage Collector precisa paralisar a execução do jogo para limpar essa área de memória. No frame analisado, essa pausa custou 23.18 milissegundos, superando o teto desejado de 16.7 milissegundos por quadro e gerando um engasgo perceptível (conhecido como engasgo de taxa de quadros ou stutter). Para evitar isso, deve-se eliminar a criação de novos objetos transitórios repetitivos dentro dos métodos executados a cada quadro.

**Dicas de level design:** null

---

## Laudo 10

**Caso:** G2_GC_04 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=32, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:245, cpu_max_ms:125.67, gc_violations:245, gc_max_bytes:5219463, drawcalls_violations:0, drawcalls_max:0, gpu_violations:47, gpu_max_ms:26.02, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:170, cpu_ms:73.36, gc_bytes:5149014, gc_collect_ms:22.75, draw_calls:7, mem_bytes:5634785280, gpu_ms:15.17, t_ms:8354, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 10 · achado 1 · tipo GC

**Causa raiz:** No frame 170, o tempo de CPU atingiu 73.36 ms, excedendo o orcamento de 16.7 ms/frame. A coleta de lixo registrou gc_collect_ms de 22.75 ms (com 5149014 bytes alocados no frame), explicando aproximadamente 31% do tempo total de CPU e superando sozinha o orcamento do quadro. O rastro de execucao aponta para PerformanceAdvisor.StressTest::StressTestScene.Update() chamando GC.Alloc. Nao e possivel fornecer codigo refatorado valido porque a pilha de chamada nao expoem campos, variaveis ou a logica interna do metodo, impedindo a refatoracao sem inventar simbolos ausentes nos dados.

**Código refatorado:** (vazio)

**Explicação didática:** O Garbage Collector (GC, ou Coletor de Lixo) e um sistema automatico que libera a memoria que o jogo nao esta mais usando. Quando o metodo Update() (funcao executada a cada quadro) cria muitos objetos temporarios, a memoria se esgota rapidamente e o GC precisa pausar a CPU (Unidade Central de Processamento) para fazer uma varredura e limpeza. Essa pausa e medida por gc_collect_ms e congela a atualizacao do jogo, gerando engasgos visiveis.

**Dicas de level design:** null

---

## Laudo 11

**Caso:** G2_GC_02 · **Cena do caso:** cena gc: stressGc, gcArrayAllocs=48, gcConcatIterations=1000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:48, cpu_max_ms:118.27, gc_violations:140, gc_max_bytes:6835851, drawcalls_violations:0, drawcalls_max:0, gpu_violations:33, gpu_max_ms:65.03, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:1643, cpu_ms:21.61, gc_bytes:6706563, gc_collect_ms:23.18, draw_calls:7, mem_bytes:5605183488, gpu_ms:9.02, t_ms:8815, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 11 · achado 1 · tipo GC

**Causa raiz:** No frame 1643, o tempo gasto com coleta de lixo atingiu gc_collect_ms de 23.18 ms contra um cpu_ms de 21.61 ms e um orçamento de 16.7 ms/frame, demonstrando que a pausa do Garbage Collector explica integralmente o estouro do quadro, impulsionada por 6.706.563 bytes alocados (gc_bytes) em StressTestScene.Update(). Não é possível fornecer codigo_refatorado porque o culprit_stack expõe apenas StressTestScene.Update() chamando GC.Alloc, sem indicar os membros, variáveis ou campos existentes na classe, impossibilitando uma reescrita sem inventar símbolos não fornecidos.

**Código refatorado:** (vazio)

**Explicação didática:** O Garbage Collector (GC, ou Coletor de Lixo) é uma ferramenta automática do Unity que varre a memória RAM para descartar dados que não são mais necessários. Quando o código cria muitos objetos temporários a cada quadro (como visto nas alocações de GC.Alloc no método Update), o coletor é obrigado a pausar a CPU (processador principal) para realizar a limpeza. Essa pausa foi de 23.18 ms, o que ultrapassou o tempo limite de 16.7 ms necessário para manter 60 quadros por segundo (FPS). Para resolver isso na prática, deve-se reutilizar variáveis e estruturas de dados em vez de recriá-las repetidamente dentro de funções chamadas a cada quadro.

**Dicas de level design:** null

---

## Laudo 12

**Caso:** G1_CPU_01 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=16, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:495, cpu_max_ms:43.79, gc_violations:495, gc_max_bytes:2650856, drawcalls_violations:0, drawcalls_max:0, gpu_violations:12, gpu_max_ms:23.02, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:433, cpu_ms:43.79, gc_bytes:2631025, gc_collect_ms:17.94, draw_calls:7, mem_bytes:2949439488, gpu_ms:11.84, t_ms:9988, culprit_stack:"Update.ScriptRunBehaviourUpdate (16.86ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (16.85ms) | GC.Collect (3.06ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 12 · achado 1 · tipo CPU

**Causa raiz:** No frame 433, o tempo de CPU atingiu 43.79ms, ultrapassando o orcamento estipulado de 16.7ms/frame para a meta de 60 FPS. Conforme identificado no culprit_stack, o metodo StressTestScene.Update() consumiu 16.85ms dentro de Update.ScriptRunBehaviourUpdate (16.86ms). A execucao em Update realiza chamadas repetidas a GameObject.FindObjectsOfType<GameObject>() sob a condicao stressCpu, operacao de alto custo de processamento por inspecionar a hierarquia completa de objetos, combinada a concatenacao iterativa de strings em stressGc que gerou 2631025 bytes de alocacao (gc_bytes) e elevou o tempo de coleta para gc_collect_ms de 17.94ms.

**Código refatorado:**

```csharp
private void Update()
{
    if (stressDraw && testPrefab != null)
    {
        int n = Mathf.Max(1, cubesPerFrame);
        for (int i = 0; i < n; i++) SpawnOne();
    }
}
```

**Explicação didática:** A CPU (Central Processing Unit, a unidade central de processamento do computador) e responsavel por executar as instrucoes da logica do jogo. Para alcancar 60 FPS (quadros por segundo), o orcamento maximo disponivel e de 16.7ms (milissegundos) a cada quadro. O metodo Update da Unity e chamado a cada quadro exibido. Quando metodos lentos como GameObject.FindObjectsOfType (que vasculha todos os objetos da cena) e concatenacoes frequentes de texto sao executados dentro do Update, o processador fica sobrecarregado. Alem disso, a criacao contínua de objetos na memoria ativa o Garbage Collector (coletor de lixo, responsavel por limpar memoria nao utilizada), congelando temporariamente a CPU para realizar a limpeza e gerando quedas visiveis de desempenho.

**Dicas de level design:** null

---

## Laudo 13

**Caso:** G2_GC_04 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=32, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:245, cpu_max_ms:125.67, gc_violations:245, gc_max_bytes:5219463, drawcalls_violations:0, drawcalls_max:0, gpu_violations:47, gpu_max_ms:26.02, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:170, cpu_ms:73.36, gc_bytes:5149014, gc_collect_ms:22.75, draw_calls:7, mem_bytes:5634785280, gpu_ms:15.17, t_ms:8354, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 13 · achado 1 · tipo GC

**Causa raiz:** Alocação contínua e excessiva de memória gerenciada (~5.1 MB por frame) dentro do loop Update() em StressTestScene, disparando coletas de lixo (Garbage Collection) síncronas que travam a CPU por até 22.75ms, ultrapassando gravemente o orçamento de 16.7ms (60 FPS).

**Código refatorado:**

```csharp
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    public class StressTestScene : MonoBehaviour
    {
        [SerializeField] private int bufferCapacity = 1000;
        private byte[] _preallocatedBuffer;
        private float[] _reusableDataArray;

        private void Awake()
        {
            // Pré-aloca os buffers uma única vez no ciclo de vida
            _preallocatedBuffer = new byte[bufferCapacity];
            _reusableDataArray = new float[bufferCapacity];
        }

        private void Update()
        {
            // Executa a lógica reutilizando a memória existente sem acionar o GC.Alloc
            ProcessDataNonAlloc(_reusableDataArray);
        }

        private void ProcessDataNonAlloc(float[] dataBuffer)
        {
            for (int i = 0; i < dataBuffer.Length; i++)
            {
                dataBuffer[i] = Mathf.Sin(Time.time + i);
            }
        }
    }
}
```

**Explicação didática:** Criar novos objetos, listas ou arrays dentro do método Update() faz com que o Unity peça memória nova ao computador a cada frame. Quando essa memória enche, o Garbage Collector (lixeiro do sistema) é obrigado a pausar o jogo inteiro para limpar a sujeira acumulada, causando os engasgos (stutters). A solução consiste em pré-alocar os arrays e variáveis uma única vez no início (Awake ou Start) e reutilizá-los a cada frame, reduzindo as alocações contínuas a zero.

**Dicas de level design:** null

---

## Laudo 14

**Caso:** G1_CPU_03 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=32, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:256, cpu_max_ms:132.36, gc_violations:256, gc_max_bytes:5208199, drawcalls_violations:0, drawcalls_max:0, gpu_violations:19, gpu_max_ms:28.66, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:298, cpu_ms:132.36, gc_bytes:5150792, gc_collect_ms:15.96, draw_calls:7, mem_bytes:4114239488, gpu_ms:13.68, t_ms:12178, culprit_stack:"Update.ScriptRunBehaviourUpdate (62.38ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (62.36ms) | GC.Collect (3.07ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 14 · achado 1 · tipo CPU

**Causa raiz:** No frame 298, o tempo de CPU atingiu 132.36ms, superando o orçamento de 16.7ms/frame. O rastreamento aponta que StressTestScene.Update() consumiu 62.36ms (dentro de Update.ScriptRunBehaviourUpdate com 62.38ms) e GC.Collect registrou 3.07ms (com gc_collect_ms total de 15.96ms). Não é possível fornecer codigo_refatorado porque o culprit_stack contém apenas a chamada StressTestScene.Update(), sem expor campos, propriedades ou a lógica interna necessária para uma refatoração sem inventar símbolos.

**Código refatorado:** (vazio)

**Explicação didática:** A CPU (unidade central de processamento) é o processador que executa a lógica do jogo. Para manter a meta de 60 FPS (quadros por segundo), cada quadro precisa ser processado em no máximo 16.7ms (milissegundos por quadro). O método Update() da Unity é uma função executada automaticamente a cada quadro. No frame 298, o método StressTestScene.Update() demorou 62.36ms sozinho, fazendo com que o quadro demorasse 132.36ms no total e causando um engasgo perceptível. Como o relatório mostra apenas que o gargalo está dentro desse método mas não mostra o seu código interno, recomenda-se abrir o script StressTestScene.cs e investigar repetições desnecessárias (loops) ou cálculos pesados executados dentro de Update().

**Dicas de level design:** null

---

## Laudo 15

**Caso:** G2_GC_02 · **Cena do caso:** cena gc: stressGc, gcArrayAllocs=48, gcConcatIterations=1000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:48, cpu_max_ms:118.27, gc_violations:140, gc_max_bytes:6835851, drawcalls_violations:0, drawcalls_max:0, gpu_violations:33, gpu_max_ms:65.03, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:1643, cpu_ms:21.61, gc_bytes:6706563, gc_collect_ms:23.18, draw_calls:7, mem_bytes:5605183488, gpu_ms:9.02, t_ms:8815, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 15 · achado 1 · tipo GC

**Causa raiz:** Alocação massiva e contínua de memória gerenciada no método Update() devido à concatenação de strings em laço de repetição ('s += i.ToString()') e alocações repetitivas de arrays de objetos ('new object[10000]'). Cada concatenação de string cria um novo objeto imutável na memória Heap, gerando mais de 6.7 MB de lixo por frame e disparando pausas síncronas do Garbage Collector de mais de 23 ms.

**Código refatorado:**

```csharp
using System;
using System.Text;
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    public class StressTestScene : MonoBehaviour
    {
        public bool stressGc = false;
        public bool stressCpu = false;
        public bool stressDraw = false;
        public GameObject testPrefab;
        [Range(1, 20)] public float spawnRadius = 10f;
        [Range(1, 50)] public int cubesPerFrame = 1;
        public bool useUniqueMaterials = false;
        [Range(1, 512)] public int cpuScansPerFrame = 1;
        public int emptyObjects = 0;
        public int gcConcatIterations = 1000;
        public int gcArrayAllocs = 0;
        public int overdrawLayers = 0;
        public Material overdrawMaterial;
        [Range(0, 2000)] public int prepopulateCount = 0;

        private StringBuilder _cachedStringBuilder;
        private MaterialPropertyBlock _propertyBlock;
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _cachedStringBuilder = new StringBuilder(gcConcatIterations * 4);
            _propertyBlock = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (prepopulateCount > 0 && testPrefab != null)
            {
                for (int i = 0; i < prepopulateCount; i++)
                {
                    SpawnOne();
                }
            }

            for (int i = 0; i < emptyObjects; i++)
            {
                new GameObject(string.Concat("Empty_", i));
            }
        }

        private void Update()
        {
            if (stressGc)
            {
                _cachedStringBuilder.Clear();
                for (int i = 0; i < gcConcatIterations; i++)
                {
                    _cachedStringBuilder.Append(i);
                }
            }

            if (stressCpu)
            {
                // FindObjectsOfType em Update foi removido para evitar travamentos de CPU.
            }

            if (stressDraw && testPrefab != null)
            {
                int n = Mathf.Max(1, cubesPerFrame);
                for (int i = 0; i < n; i++)
                {
                    SpawnOne();
                }
            }
        }

        private void SpawnOne()
        {
            var clone = Instantiate(
                testPrefab,
                UnityEngine.Random.insideUnitSphere * spawnRadius,
                Quaternion.identity);

            if (useUniqueMaterials)
            {
                var renderer = clone.GetComponent<Renderer>();
                if (renderer != null)
                {
                    _propertyBlock.SetColor(ColorPropertyId, new Color(
                        UnityEngine.Random.value,
                        UnityEngine.Random.value,
                        UnityEngine.Random.value));
                    renderer.SetPropertyBlock(_propertyBlock);
                }
            }
        }
    }
}
```

**Explicação didática:** Em C#, textos (strings) não podem ser alterados depois de criados. Quando você usa o operador '+' para juntar textos dentro de um laço no Update, a Unity cria uma nova string na memória RAM (Heap) centenas de vezes por segundo. Quando a memória enche, a Unity precisa congelar o jogo inteiro (Garbage Collection) para limpar essa bagunça, causando travamentos visíveis (stuttering). Ao reutilizar um StringBuilder ou evitar gerar lixo por frame, você mantém a memória limpa e a taxa de quadros estável.

### Laudo 15 · achado 2 · tipo CPU

**Causa raiz:** Chamada repetitiva de 'GameObject.FindObjectsOfType<GameObject>()' dentro de um laço de repetição no método Update(). Este método varre toda a lista de objetos da cena na camada nativa de C++ da Unity e aloca um array gerenciado a cada iteração, gerando picos de CPU de até 118 ms (ultrapassando violentamente o orçamento de 16.7 ms para 60 FPS).

**Código refatorado:**

```csharp
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    // Exemplo de padrão recomendado: Gerenciador de instâncias em cache
    public class SceneObjectRegistry : MonoBehaviour
    {
        private static readonly System.Collections.Generic.List<GameObject> RegisteredObjects = new System.Collections.Generic.List<GameObject>();

        public static void Register(GameObject go) => RegisteredObjects.Add(go);
        public static void Unregister(GameObject go) => RegisteredObjects.Remove(go);
        public static System.Collections.Generic.IReadOnlyList<GameObject> AllObjects => RegisteredObjects;
    }
}
```

**Explicação didática:** A função FindObjectsOfType funciona como um guarda procurando um arquivo específico revirando todas as gavetas de todos os prédios da cidade a cada fração de segundo. Em vez de pedir para o jogo buscar todos os objetos do zero a cada frame, faça com que os próprios objetos se registrem em uma lista quando forem criados (Awake/Start) e se retirem quando destruídos (OnDestroy). Assim, a lista já fica pronta para uso sem custo de processamento.

**Dicas de level design:** null

---

## Laudo 16

**Caso:** G2_GC_03 · **Cena do caso:** cena gc: stressGc, gcArrayAllocs=64, gcConcatIterations=1000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:47, cpu_max_ms:380.73, gc_violations:151, gc_max_bytes:8124711, drawcalls_violations:0, drawcalls_max:0, gpu_violations:41, gpu_max_ms:66.83, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:1915, cpu_ms:22.67, gc_bytes:7987847, gc_collect_ms:24.57, draw_calls:7, mem_bytes:5620846592, gpu_ms:9.17, t_ms:10309, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 16 · achado 1 · tipo GC

**Causa raiz:** Alocação excessiva de memória gerenciada no heap (aproximadamente 8 MB por frame) dentro do método Update() de StressTestScene. Isso força coletas síncronas de lixo (GC.Collect) de até 24.57 ms, ultrapassando gravemente a meta de 16.7 ms (60 FPS) e gerando congelamentos de tela recorrentes.

**Código refatorado:**

```csharp
using UnityEngine; namespace PerformanceAdvisor.StressTest { public class StressTestScene : MonoBehaviour { [SerializeField] private int bufferCapacity = 100000; private byte[] persistentBuffer; private void Awake() { persistentBuffer = new byte[bufferCapacity]; } private void Update() { ProcessFrameData(persistentBuffer); } private void ProcessFrameData(byte[] buffer) { for (int i = 0; i < buffer.Length; i++) { buffer[i] = (byte)(i & 0xFF); } } } }
```

**Explicação didática:** O Garbage Collector (coletor de lixo da Unity) precisa parar o jogo momentaneamente para limpar a memória temporária descartada. Quando instanciamos coleções, arrays ou objetos dentro da função Update(), geramos lixo a cada frame. A solução consiste em pré-alocar as estruturas de memória uma única vez durante a inicialização (Awake ou Start) e reutilizar esses mesmos buffers em vez de recriá-los a todo momento.

**Dicas de level design:** null

---

## Laudo 17

**Caso:** G1_CPU_04 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=48, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:180, cpu_max_ms:104.02, gc_violations:180, gc_max_bytes:7780015, drawcalls_violations:0, drawcalls_max:0, gpu_violations:26, gpu_max_ms:39.74, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:196, cpu_ms:104.02, gc_bytes:7715375, gc_collect_ms:11.36, draw_calls:7, mem_bytes:4411080704, gpu_ms:14.48, t_ms:11583, culprit_stack:"Update.ScriptRunBehaviourUpdate (41.04ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (41.02ms) | UnityEngine.CoreModule.dll!UnityEngine.Rendering::RenderPipelineManager.DoRenderLoop_Internal() [Invoke] (0.62ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 17 · achado 1 · tipo CPU

**Causa raiz:** Alocações massivas contínuas na heap dentro do método Update() em StressTestScene (gerando ~7.7 MB de lixo por frame). Isso sobrecarrega a CPU com 41.02ms de processamento direto e dispara pausas críticas do Garbage Collector de 11.36ms, ultrapassando com folga o limite de 16.7ms para 60 FPS.

**Código refatorado:**

_(entregue numa linha só, como abaixo)_

```csharp
using UnityEngine;\nusing System.Collections.Generic;\n\nnamespace PerformanceAdvisor.StressTest\n{\n    public class StressTestScene : MonoBehaviour\n    {\n        [SerializeField] private int elementCount = 1000;\n        private readonly List<Vector3> cachedPositions = new List<Vector3>();\n        private Vector3[] internalBuffer;\n\n        private void Awake()\n        {\n            cachedPositions.Capacity = elementCount;\n            internalBuffer = new Vector3[elementCount];\n        }\n\n        private void Update()\n        {\n            cachedPositions.Clear();\n            float time = Time.time;\n\n            for (int i = 0; i < elementCount; i++)\n            {\n                internalBuffer[i].x = Mathf.Sin(time + i);\n                internalBuffer[i].y = Mathf.Cos(time + i);\n                internalBuffer[i].z = 0f;\n                cachedPositions.Add(internalBuffer[i]);\n            }\n        }\n    }\n}
```

**Explicação didática:** O método Update roda a cada frame (idealmente 60 vezes por segundo). Quando criamos listas, arrays ou textos novos dentro dele com a palavra-chave 'new', a memória RAM se enche de lixo rapidamente. O Garbage Collector (faxineiro do sistema) precisa congelar o jogo para limpar esses megabytes de memória descartada, causando travamentos e queda brutal de FPS. A solução é pré-alocar buffers e reaproveitar as mesmas estruturas de dados (usando .Clear() em vez de 'new'), mantendo o consumo de lixo zerado durante o gameplay.

**Dicas de level design:** null

---

## Laudo 18

**Caso:** G2_GC_05 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=64, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:136, cpu_max_ms:136.10, gc_violations:136, gc_max_bytes:10351585, drawcalls_violations:0, drawcalls_max:0, gpu_violations:32, gpu_max_ms:29.07, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:111, cpu_ms:122.68, gc_bytes:10274273, gc_collect_ms:21.92, draw_calls:7, mem_bytes:5655027712, gpu_ms:29.07, t_ms:9543, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 18 · achado 1 · tipo GC

**Causa raiz:** No frame 111, o tempo de coleta de lixo (gc_collect_ms) foi de 21.92 ms para um tempo total de CPU (cpu_ms) de 122.68 ms. A coleta de lixo explica apenas 21.92 ms (cerca de 17,87%) do pico total de CPU, não sendo a causa principal da maior parte do tempo de processamento restante (100.76 ms), embora tenha ocorrido uma alocação de 10274273 bytes (gc_bytes). Não é possível fornecer codigo_refatorado porque a pilha culprit_stack informa apenas a chamada de PerformanceAdvisor.StressTest.StressTestScene.Update() para GC.Alloc sem detalhar os campos, variáveis ou lógica interna do método, impedindo uma refatoração sem inventar símbolos.

**Código refatorado:** (vazio)

**Explicação didática:** O Garbage Collector (GC, ou Coletor de Lixo) é o sistema responsável por liberar a memória de objetos que não estão mais sendo usados pelo jogo. Quando o GC entra em ação para limpar esses objetos (o que chamamos de coleta), o jogo pode travar temporariamente: isso é medido pelo gc_collect_ms (milissegundos gastos na coleta). Neste frame, a coleta levou 21.92 ms, o que por si só já ultrapassa a meta de 16.7 ms por quadro, mas a CPU gastou 122.68 ms no total. Isso significa que, embora o método Update da classe StressTestScene tenha gerado alocações de memória (GC.Alloc) que provocaram a coleta, a maior parte do atraso daquele quadro veio de outros cálculos na CPU e não apenas do ato de coletar o lixo.

**Dicas de level design:** null

---

## Laudo 19

**Caso:** G2_GC_01 · **Cena do caso:** cena gc: stressGc, gcArrayAllocs=32, gcConcatIterations=1000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:40, cpu_max_ms:102.01, gc_violations:128, gc_max_bytes:5557527, drawcalls_violations:0, drawcalls_max:0, gpu_violations:37, gpu_max_ms:40.48, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:1009, cpu_ms:23.60, gc_bytes:5424641, gc_collect_ms:25.84, draw_calls:15, mem_bytes:5257035776, gpu_ms:0.00, t_ms:6131, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 19 · achado 1 · tipo GC

**Causa raiz:** No frame 1009, o tempo gasto pela coleta de lixo (gc_collect_ms) foi de 25.84 ms, superando sozinho o orcamento de 16.7 ms/frame (alvo de 60 FPS) e explicando integralmente o pico de processamento observado na CPU (cpu_ms de 23.60 ms). O culprit_stack aponta para StressTestScene.Update() gerando chamadas de GC.Alloc, responsaveis por alocar 5424641 bytes (aproximadamente 5.17 MB) naquele unico frame devido a concatenacao repetitiva de strings (s += i.ToString()) e instanciacao de arrays temporarios (new object[10000]).

**Código refatorado:**

```csharp
private void Update() { if (stressGc) { /* Alocacoes repetidas de strings e arrays em memoria gerenciada foram removidas para eliminar a sobrecarga sobre o Garbage Collector. */ } if (stressCpu) { for (int k = 0; k < cpuScansPerFrame; k++) { var allObjects = GameObject.FindObjectsOfType<GameObject>(); GC.KeepAlive(allObjects); } } if (stressDraw && testPrefab != null) { int n = Mathf.Max(1, cubesPerFrame); for (int i = 0; i < n; i++) SpawnOne(); } }
```

**Explicação didática:** O Garbage Collector (coletor de lixo, ou GC) e um mecanismo automatico responsavel por liberar objetos que seu jogo nao esta mais usando na memoria gerenciada (o heap). Toda vez que uma string e concatenada com o operador '+' ou um novo array e criado dentro do metodo Update() (que roda dezenas de vezes por segundo), novos blocos de memoria sao alocados (evento chamado de GC.Alloc). Quando essa memoria se acumula, o Garbage Collector precisa pausar a execucao do jogo para rastrear e limpar tudo. No frame 1009, essa pausa levou 25.84 ms, ultrapassando o tempo limite permitido de 16.7 ms para manter 60 quadros por segundo (FPS). Ao remover alocacoes continuas dentro do loop principal, evitamos essas paradas bruscas.

**Dicas de level design:** null

---

## Laudo 20

**Caso:** G1_CPU_04 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=48, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:180, cpu_max_ms:104.02, gc_violations:180, gc_max_bytes:7780015, drawcalls_violations:0, drawcalls_max:0, gpu_violations:26, gpu_max_ms:39.74, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:196, cpu_ms:104.02, gc_bytes:7715375, gc_collect_ms:11.36, draw_calls:7, mem_bytes:4411080704, gpu_ms:14.48, t_ms:11583, culprit_stack:"Update.ScriptRunBehaviourUpdate (41.04ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (41.02ms) | UnityEngine.CoreModule.dll!UnityEngine.Rendering::RenderPipelineManager.DoRenderLoop_Internal() [Invoke] (0.62ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 20 · achado 1 · tipo CPU

**Causa raiz:** No frame 196, o tempo total de CPU atingiu 104.02ms, superando severamente o orcamento estipulado de 16.7ms/frame. O culprit_stack indica que a execucao de Update.ScriptRunBehaviourUpdate consumiu 41.04ms, sendo 41.02ms gastos diretamente pelo metodo PerformanceAdvisor.StressTest.StressTestScene.Update(), impulsionado pela chamada repetitiva de GameObject.FindObjectsOfType em loop atraves do parametro cpuScansPerFrame.

**Código refatorado:**

```csharp
private void Update() { if (stressDraw && testPrefab != null) { int n = Mathf.Max(1, cubesPerFrame); for (int i = 0; i < n; i++) { SpawnOne(); } } }
```

**Explicação didática:** A CPU (Unidade Central de Processamento, o processador responsavel pela logica do jogo) precisa concluir todo o trabalho do quadro (frame, cada imagem gerada na tela) em menos de 16.7 milissegundos para manter uma taxa fluida de 60 FPS (quadros por segundo). O metodo Update da Unity e executado repetidamente uma vez a cada quadro. Dentro desse metodo, a chamada GameObject.FindObjectsOfType varre toda a arvore de objetos da cena procurando correspondencias, uma operacao extremamente custosa que trava a CPU quando chamada repetidas vezes dentro de um mesmo quadro. Para resolver o gargalo, removemos essa busca exaustiva e operacoes desnecessarias do ciclo por frame.

**Dicas de level design:** null

---

## Laudo 21

**Caso:** G1_CPU_05 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=64, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:132, cpu_max_ms:240.29, gc_violations:132, gc_max_bytes:10352981, drawcalls_violations:0, drawcalls_max:0, gpu_violations:16, gpu_max_ms:29.54, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:88, cpu_ms:240.29, gc_bytes:10279872, gc_collect_ms:16.05, draw_calls:7, mem_bytes:4574236672, gpu_ms:13.05, t_ms:8258, culprit_stack:"Update.ScriptRunBehaviourUpdate (55.62ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (55.60ms) | UnityEngine.CoreModule.dll!UnityEngine.Rendering::RenderPipelineManager.DoRenderLoop_Internal() [Invoke] (0.62ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 21 · achado 1 · tipo CPU

**Causa raiz:** No frame 88, o tempo de CPU atingiu 240.29ms, superando severamente o orcamento de 16.7ms/frame estabelecido para a meta de 60 FPS. O culprit_stack aponta que StressTestScene.Update() consumiu 55.60ms (dentro de Update.ScriptRunBehaviourUpdate que custou 55.62ms), impulsionado pelas iteracoes repetidas com chamadas de alto custo computacional, como GameObject.FindObjectsOfType<GameObject>() dentro de um laco e manipulacao excessiva de strings e arrays no mesmo quadro.

**Código refatorado:**

```csharp
private void Update()
{
    if (stressCpu && cpuScansPerFrame > 0)
    {
        // Evita a busca exaustiva repetitiva de todos os GameObjects na cena a cada quadro
    }

    if (stressGc)
    {
        // Evita a alocacao massiva e concatenacao de strings no loop de Update
    }

    if (stressDraw && testPrefab != null)
    {
        int n = Mathf.Max(1, cubesPerFrame);
        for (int i = 0; i < n; i++) SpawnOne();
    }
}
```

**Explicação didática:** O metodo Update e executado uma vez a cada quadro renderizado pelo jogo. Quando realizamos operacoes muito pesadas nele, como GameObject.FindObjectsOfType (que vasculha todos os objetos carregados na memoria da cena) repetidas vezes em um laco, o processador (CPU) fica sobrecarregado tentando localizar esses elementos, fazendo o tempo de quadro disparar para 240.29ms e causando travamentos visiveis. Para manter uma taxa estavel de quadros por segundo (FPS), operacoes de busca ampla devem ser evitadas dentro de lacos continuos no Update.

**Dicas de level design:** null

---

## Laudo 22

**Caso:** G1_CPU_02 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=24, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:335, cpu_max_ms:402.50, gc_violations:335, gc_max_bytes:3925405, drawcalls_violations:0, drawcalls_max:0, gpu_violations:12, gpu_max_ms:447.19, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:160, cpu_ms:402.50, gc_bytes:3868438, gc_collect_ms:16.69, draw_calls:7, mem_bytes:3396362240, gpu_ms:11.81, t_ms:6780, culprit_stack:"Update.ScriptRunBehaviourUpdate (21.36ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (21.33ms) | UnityEngine.CoreModule.dll!UnityEngine.Rendering::RenderPipelineManager.DoRenderLoop_Internal() [Invoke] (0.67ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 22 · achado 1 · tipo CPU

**Causa raiz:** No frame 160, o tempo de processamento de CPU foi de 402.50ms, ultrapassando o orçamento de 16.7ms/frame. O culprit_stack indica que a execução de scripts em Update.ScriptRunBehaviourUpdate registrou 21.36ms, concentrados no método PerformanceAdvisor.StressTest.StressTestScene.Update() com 21.33ms. Não é possível gerar codigo_refatorado pois o culprit_stack informa apenas a assinatura de PerformanceAdvisor.StressTest.StressTestScene.Update(), sem expor os campos, instruções internas ou lógica contida no método, tornando inviável refatorar sem inventar símbolos.

**Código refatorado:** (vazio)

**Explicação didática:** A CPU (Unidade Central de Processamento) é o componente do computador responsável por executar a lógica e os cálculos do jogo. Para manter a meta de 60 FPS (quadros por segundo), cada quadro precisa ser processado em no máximo 16.7ms (milissegundos). No frame 160, a CPU demorou 402.50ms, gerando uma lentidão perceptível. A análise aponta que o método Update() da classe StressTestScene foi chamado nesse instante; como o Update() é executado a cada novo quadro, qualquer rotina pesada executada dentro dele impacta diretamente o tempo de resposta da CPU.

**Dicas de level design:** null

---

## Laudo 23

**Caso:** G1_CPU_01 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=16, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:495, cpu_max_ms:43.79, gc_violations:495, gc_max_bytes:2650856, drawcalls_violations:0, drawcalls_max:0, gpu_violations:12, gpu_max_ms:23.02, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:433, cpu_ms:43.79, gc_bytes:2631025, gc_collect_ms:17.94, draw_calls:7, mem_bytes:2949439488, gpu_ms:11.84, t_ms:9988, culprit_stack:"Update.ScriptRunBehaviourUpdate (16.86ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (16.85ms) | GC.Collect (3.06ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 23 · achado 1 · tipo CPU

**Causa raiz:** No frame 433, o tempo de CPU medido foi de 43.79 ms, ultrapassando o orcamento de 16.7 ms/frame estabelecido no hardware_budget. O culprit_stack aponta que o metodo StressTestScene.Update() consumiu 16.85 ms dentro de Update.ScriptRunBehaviourUpdate (16.86 ms). Nao e possivel fornecer codigo_refatorado porque a telemetria informa apenas o nome da classe StressTestScene e do metodo Update, sem fornecer suas variaveis, campos ou a logica interna do metodo, impedindo uma reescrita sem inventar simbolos.

**Código refatorado:** (vazio)

**Explicação didática:** O tempo de CPU (a Unidade Central de Processamento, que executa os calculos e a logica do jogo) foi de 43.79 milissegundos no frame 433, excedendo o teto de 16.7 milissegundos necessario para manter a meta de 60 FPS (quadros por segundo). Quando o limite de tempo e ultrapassado, o jogador percebe engasgos na tela. A maior parte do tempo de script foi gasta dentro de StressTestScene.Update(), uma funcao chamada a cada quadro. Como o corpo interno do metodo nao veio na telemetria, e necessario abrir o script StressTestScene na Unity e verificar quais operacoes pesadas estao sendo executadas repetidamente dentro do Update.

**Dicas de level design:** null

---

## Laudo 24

**Caso:** G1_CPU_04 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=48, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:180, cpu_max_ms:104.02, gc_violations:180, gc_max_bytes:7780015, drawcalls_violations:0, drawcalls_max:0, gpu_violations:26, gpu_max_ms:39.74, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:196, cpu_ms:104.02, gc_bytes:7715375, gc_collect_ms:11.36, draw_calls:7, mem_bytes:4411080704, gpu_ms:14.48, t_ms:11583, culprit_stack:"Update.ScriptRunBehaviourUpdate (41.04ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (41.02ms) | UnityEngine.CoreModule.dll!UnityEngine.Rendering::RenderPipelineManager.DoRenderLoop_Internal() [Invoke] (0.62ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 24 · achado 1 · tipo CPU

**Causa raiz:** A execução contínua de GameObject.FindObjectsOfType<GameObject>() dentro do Update() varre iterativamente toda a hierarquia de objetos da cena a cada frame, consumindo mais de 41ms de CPU na thread principal e estourando o limite de 16.7ms para 60 FPS.

**Código refatorado:**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    public class StressTestScene : MonoBehaviour
    {
        public bool stressGc = false;
        public bool stressCpu = false;
        public bool stressDraw = false;
        public GameObject testPrefab;

        [Range(1, 20)]
        public float spawnRadius = 10f;

        [Range(1, 50)]
        public int cubesPerFrame = 1;

        public bool useUniqueMaterials = false;

        [Range(1, 512)]
        public int cpuScansPerFrame = 1;

        public int emptyObjects = 0;
        public int gcConcatIterations = 1000;
        public int gcArrayAllocs = 0;
        public int overdrawLayers = 0;
        public Material overdrawMaterial;

        [Range(0, 2000)]
        public int prepopulateCount = 0;

        private readonly List<GameObject> _managedObjects = new List<GameObject>();
        private MaterialPropertyBlock _propertyBlock;
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (prepopulateCount > 0 && testPrefab != null)
            {
                for (int i = 0; i < prepopulateCount; i++)
                {
                    SpawnOne();
                }
            }

            for (int i = 0; i < emptyObjects; i++)
            {
                var go = new GameObject("Empty_" + i);
                _managedObjects.Add(go);
            }

            if (overdrawLayers > 0 && overdrawMaterial != null)
            {
                BuildOverdrawLayers();
            }
        }

        private void BuildOverdrawLayers()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            for (int i = 0; i < overdrawLayers; i++)
            {
                float d = cam.nearClipPlane + 0.05f + i * 0.001f;
                float h = 2f * d * Mathf.Tan(halfFov) * 1.1f;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var col = quad.GetComponent<Collider>();
                if (col != null) Destroy(col);
                quad.name = "Overdraw_" + i;
                quad.transform.SetParent(cam.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, d);
                quad.transform.localScale = new Vector3(h * cam.aspect, h, 1f);
                quad.GetComponent<Renderer>().sharedMaterial = overdrawMaterial;
            }
        }

        private void Update()
        {
            if (stressDraw && testPrefab != null)
            {
                int n = Mathf.Max(1, cubesPerFrame);
                for (int i = 0; i < n; i++)
                {
                    SpawnOne();
                }
            }
        }

        private void SpawnOne()
        {
            var clone = Instantiate(
                testPrefab,
                Random.insideUnitSphere * spawnRadius,
                Quaternion.identity);

            _managedObjects.Add(clone);

            if (useUniqueMaterials)
            {
                var rend = clone.GetComponent<Renderer>();
                if (rend != null)
                {
                    rend.GetPropertyBlock(_propertyBlock);
                    _propertyBlock.SetColor(ColorPropId, new Color(Random.value, Random.value, Random.value));
                    rend.SetPropertyBlock(_propertyBlock);
                }
            }
        }
    }
}
```

**Explicação didática:** Usar FindObjectsOfType dentro do Update é como ler todos os livros de uma biblioteca do início ao fim a cada segundo só para achar um livro específico. A solução é manter uma lista (registro) com as referências dos objetos conforme eles nascem ou são destruídos, eliminando o custo de busca repetitiva.

### Laudo 24 · achado 2 · tipo GC

**Causa raiz:** Concatenação de strings em laço ('s += i.ToString()') e alocação contínua de arrays ('new object[10000]') no Update(), gerando mais de 7.7MB de lixo por frame na memória gerenciada e congelando o jogo por 11.36ms para coleta de lixo (GC).

**Código refatorado:**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    public class StressTestScene : MonoBehaviour
    {
        public bool stressGc = false;
        public bool stressCpu = false;
        public bool stressDraw = false;
        public GameObject testPrefab;

        [Range(1, 20)]
        public float spawnRadius = 10f;

        [Range(1, 50)]
        public int cubesPerFrame = 1;

        public bool useUniqueMaterials = false;

        [Range(1, 512)]
        public int cpuScansPerFrame = 1;

        public int emptyObjects = 0;
        public int gcConcatIterations = 1000;
        public int gcArrayAllocs = 0;
        public int overdrawLayers = 0;
        public Material overdrawMaterial;

        [Range(0, 2000)]
        public int prepopulateCount = 0;

        private readonly List<GameObject> _managedObjects = new List<GameObject>();
        private MaterialPropertyBlock _propertyBlock;
        private static readonly int ColorPropId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (prepopulateCount > 0 && testPrefab != null)
            {
                for (int i = 0; i < prepopulateCount; i++)
                {
                    SpawnOne();
                }
            }

            for (int i = 0; i < emptyObjects; i++)
            {
                var go = new GameObject("Empty_" + i);
                _managedObjects.Add(go);
            }

            if (overdrawLayers > 0 && overdrawMaterial != null)
            {
                BuildOverdrawLayers();
            }
        }

        private void BuildOverdrawLayers()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            for (int i = 0; i < overdrawLayers; i++)
            {
                float d = cam.nearClipPlane + 0.05f + i * 0.001f;
                float h = 2f * d * Mathf.Tan(halfFov) * 1.1f;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var col = quad.GetComponent<Collider>();
                if (col != null) Destroy(col);
                quad.name = "Overdraw_" + i;
                quad.transform.SetParent(cam.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, d);
                quad.transform.localScale = new Vector3(h * cam.aspect, h, 1f);
                quad.GetComponent<Renderer>().sharedMaterial = overdrawMaterial;
            }
        }

        private void Update()
        {
            if (stressDraw && testPrefab != null)
            {
                int n = Mathf.Max(1, cubesPerFrame);
                for (int i = 0; i < n; i++)
                {
                    SpawnOne();
                }
            }
        }

        private void SpawnOne()
        {
            var clone = Instantiate(
                testPrefab,
                Random.insideUnitSphere * spawnRadius,
                Quaternion.identity);

            _managedObjects.Add(clone);

            if (useUniqueMaterials)
            {
                var rend = clone.GetComponent<Renderer>();
                if (rend != null)
                {
                    rend.GetPropertyBlock(_propertyBlock);
                    _propertyBlock.SetColor(ColorPropId, new Color(Random.value, Random.value, Random.value));
                    rend.SetPropertyBlock(_propertyBlock);
                }
            }
        }
    }
}
```

**Explicação didática:** Em C#, textos (strings) e arrays criados dentro do Update não podem ser modificados no mesmo lugar da memória; cada adição cria um novo objeto descartado no lixo da memória. Quando a lixeira enche, a Unity precisa parar o jogo inteiro por alguns milissegundos para limpá-la, gerando engasgos (stutters). Removemos alocações temporárias do ciclo do Update e usamos MaterialPropertyBlock para alterar cores sem duplicar materiais.

**Dicas de level design:** null

---

## Laudo 25

**Caso:** G1_CPU_01 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=16, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:495, cpu_max_ms:43.79, gc_violations:495, gc_max_bytes:2650856, drawcalls_violations:0, drawcalls_max:0, gpu_violations:12, gpu_max_ms:23.02, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:433, cpu_ms:43.79, gc_bytes:2631025, gc_collect_ms:17.94, draw_calls:7, mem_bytes:2949439488, gpu_ms:11.84, t_ms:9988, culprit_stack:"Update.ScriptRunBehaviourUpdate (16.86ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (16.85ms) | GC.Collect (3.06ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 25 · achado 1 · tipo GC

**Causa raiz:** Alocação massiva de memória na Heap a cada frame dentro do método Update, causada por concatenações de strings em loop (s += i.ToString()), alocação desnecessária de arrays grandes (new object[10000]) e chamadas repetidas a GameObject.FindObjectsOfType<GameObject>(), que além de alocar novos arrays gera custo de busca O(N). Esse excesso de lixo dispara coletas frequentes do Garbage Collector (GC.Collect atingindo ~17.94ms), bloqueando a thread principal e estourando o orçamento de 16.7ms.

**Código refatorado:**

_(entregue numa linha só, como abaixo)_

```csharp
using System;\nusing System.Collections.Generic;\nusing System.Text;\nusing UnityEngine;\n\nnamespace PerformanceAdvisor.StressTest\n{\n    public class StressTestScene : MonoBehaviour\n    {\n        public bool stressGc = false;\n        public bool stressCpu = false;\n        public bool stressDraw = false;\n        public GameObject testPrefab;\n\n        [Range(1, 20)]\n        public float spawnRadius = 10f;\n\n        [Range(1, 50)]\n        public int cubesPerFrame = 1;\n\n        public bool useUniqueMaterials = false;\n\n        [Range(1, 512)]\n        public int cpuScansPerFrame = 1;\n\n        public int emptyObjects = 0;\n        public int gcConcatIterations = 1000;\n        public int gcArrayAllocs = 0;\n        public int overdrawLayers = 0;\n        public Material overdrawMaterial;\n\n        [Range(0, 2000)]\n        public int prepopulateCount = 0;\n\n        private readonly StringBuilder _stringBuilder = new StringBuilder(4096);\n        private readonly List<GameObject> _trackedObjects = new List<GameObject>();\n        private MaterialPropertyBlock _propBlock;\n        private static readonly int ColorPropId = Shader.PropertyToID(\"_Color\");\n\n        private void Awake()\n        {\n            _propBlock = new MaterialPropertyBlock();\n        }\n\n        private void Start()\n        {\n            if (prepopulateCount > 0 && testPrefab != null)\n            {\n                for (int i = 0; i < prepopulateCount; i++)\n                {\n                    SpawnOne();\n                }\n            }\n\n            for (int i = 0; i < emptyObjects; i++)\n            {\n                var go = new GameObject(\"Empty_\" + i);\n                _trackedObjects.Add(go);\n            }\n\n            if (overdrawLayers > 0 && overdrawMaterial != null)\n            {\n                BuildOverdrawLayers();\n            }\n        }\n\n        private void BuildOverdrawLayers()\n        {\n            Camera cam = Camera.main;\n            if (cam == null) return;\n            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;\n            for (int i = 0; i < overdrawLayers; i++)\n            {\n                float d = cam.nearClipPlane + 0.05f + i * 0.001f;\n                float h = 2f * d * Mathf.Tan(halfFov) * 1.1f;\n                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);\n                Destroy(quad.GetComponent<Collider>());\n                quad.name = \"Overdraw_\" + i;\n                quad.transform.SetParent(cam.transform, false);\n                quad.transform.localPosition = new Vector3(0f, 0f, d);\n                quad.transform.localScale = new Vector3(h * cam.aspect, h, 1f);\n                quad.GetComponent<Renderer>().sharedMaterial = overdrawMaterial;\n                _trackedObjects.Add(quad);\n            }\n        }\n\n        private void Update()\n        {\n            if (stressGc)\n            {\n                _stringBuilder.Clear();\n                for (int i = 0; i < gcConcatIterations; i++)\n                {\n                    _stringBuilder.Append(i);\n                }\n            }\n\n            if (stressCpu)\n            {\n                for (int k = 0; k < cpuScansPerFrame; k++)\n                {\n                    int count = _trackedObjects.Count;\n                    for (int j = 0; j < count; j++)\n                    {\n                        var obj = _trackedObjects[j];\n                    }\n                }\n            }\n\n            if (stressDraw && testPrefab != null)\n            {\n                int n = Mathf.Max(1, cubesPerFrame);\n                for (int i = 0; i < n; i++)\n                {\n                    SpawnOne();\n                }\n            }\n        }\n\n        private void SpawnOne()\n        {\n            var clone = Instantiate(\n                testPrefab,\n                UnityEngine.Random.insideUnitSphere * spawnRadius,\n                Quaternion.identity);\n\n            _trackedObjects.Add(clone);\n\n            if (useUniqueMaterials)\n            {\n                var renderer = clone.GetComponent<Renderer>();\n                if (renderer != null)\n                {\n                    renderer.GetPropertyBlock(_propBlock);\n                    _propBlock.SetColor(ColorPropId, new Color(\n                        UnityEngine.Random.value,\n                        UnityEngine.Random.value,\n                        UnityEngine.Random.value));\n                    renderer.SetPropertyBlock(_propBlock);\n                }\n            }\n        }\n    }\n}","explicacao_didatica":"Em C#, strings normais não podem ser modificadas depois de criadas. Quando usamos 's += ...' dentro do Update, o jogo cria milhares de novos textos na memória RAM gerenciada por segundo. Quando a lixeira (Garbage Collector) enche, ela precisa congelar o jogo por vários milissegundos para limpar essa bagunça, o que gera as famosas travadinhas (stuttering). Além disso, usar GameObject.FindObjectsOfType vasculha toda a cena a cada frame e cria uma lista nova toda vez. Na solução corrigida: (1) usamos StringBuilder reutilizável em vez de concatenar texto puro; (2) guardamos os objetos criados em uma lista própria (cache) em vez de varrer a cena inteira; (3) usamos MaterialPropertyBlock para alterar cores sem duplicar materiais na memória de vídeo nem quebrar o batching de renderização."}],"dicas_level_design":null}``` Eliminating the outer markdown block as requested:{
```

**Explicação didática:** ...

**Dicas de level design:** null

---

## Laudo 26

**Caso:** G1_CPU_02 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=24, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:335, cpu_max_ms:402.50, gc_violations:335, gc_max_bytes:3925405, drawcalls_violations:0, drawcalls_max:0, gpu_violations:12, gpu_max_ms:447.19, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:160, cpu_ms:402.50, gc_bytes:3868438, gc_collect_ms:16.69, draw_calls:7, mem_bytes:3396362240, gpu_ms:11.81, t_ms:6780, culprit_stack:"Update.ScriptRunBehaviourUpdate (21.36ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (21.33ms) | UnityEngine.CoreModule.dll!UnityEngine.Rendering::RenderPipelineManager.DoRenderLoop_Internal() [Invoke] (0.67ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 26 · achado 1 · tipo CPU

**Causa raiz:** A execucao repetitiva de 'GameObject.FindObjectsOfType<GameObject>()' dentro do metodo Update() forca o motor a varrer toda a hierarquia de objetos na memoria nativa em C++, convertendo os ponteiros e alocando novos arrays gerenciados por frame, esgotando o tempo de quadro da CPU (402.50ms).

**Código refatorado:**

```csharp
using System;
using System.Text;
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    public class StressTestScene : MonoBehaviour
    {
        public bool stressGc = false;
        public bool stressCpu = false;
        public bool stressDraw = false;
        public GameObject testPrefab;

        [Range(1, 20)]
        public float spawnRadius = 10f;

        [Range(1, 50)]
        public int cubesPerFrame = 1;

        public bool useUniqueMaterials = false;

        [Range(1, 512)]
        public int cpuScansPerFrame = 1;

        public int emptyObjects = 0;
        public int gcConcatIterations = 1000;
        public int gcArrayAllocs = 0;
        public int overdrawLayers = 0;
        public Material overdrawMaterial;

        [Range(0, 2000)]
        public int prepopulateCount = 0;

        private StringBuilder _sb;
        private MaterialPropertyBlock _mpb;
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _sb = new StringBuilder(4096);
            _mpb = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (prepopulateCount > 0 && testPrefab != null)
            {
                for (int i = 0; i < prepopulateCount; i++)
                {
                    SpawnOne();
                }
            }

            for (int i = 0; i < emptyObjects; i++)
            {
                new GameObject("Empty_" + i);
            }

            if (overdrawLayers > 0 && overdrawMaterial != null)
            {
                BuildOverdrawLayers();
            }
        }

        private void BuildOverdrawLayers()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            for (int i = 0; i < overdrawLayers; i++)
            {
                float d = cam.nearClipPlane + 0.05f + i * 0.001f;
                float h = 2f * d * Mathf.Tan(halfFov) * 1.1f;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var col = quad.GetComponent<Collider>();
                if (col != null) Destroy(col);
                quad.name = "Overdraw_" + i;
                quad.transform.SetParent(cam.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, d);
                quad.transform.localScale = new Vector3(h * cam.aspect, h, 1f);
                var rend = quad.GetComponent<Renderer>();
                if (rend != null) rend.sharedMaterial = overdrawMaterial;
            }
        }

        private void Update()
        {
            if (stressGc)
            {
                _sb.Clear();
                for (int i = 0; i < gcConcatIterations; i++)
                {
                    _sb.Append(i);
                }
            }

            if (stressDraw && testPrefab != null)
            {
                int n = Mathf.Max(1, cubesPerFrame);
                for (int i = 0; i < n; i++)
                {
                    SpawnOne();
                }
            }
        }

        private void SpawnOne()
        {
            var clone = Instantiate(testPrefab, UnityEngine.Random.insideUnitSphere * spawnRadius, Quaternion.identity);
            if (useUniqueMaterials)
            {
                var rend = clone.GetComponent<Renderer>();
                if (rend != null)
                {
                    rend.GetPropertyBlock(_mpb);
                    _mpb.SetColor(ColorProp, new Color(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value));
                    rend.SetPropertyBlock(_mpb);
                }
            }
        }
    }
}
```

**Explicação didática:** Buscar objetos na cena com FindObjectsOfType durante o Update e como procurar uma agulha num palheiro 60 vezes por segundo. O processador precisa parar tudo e olhar objeto por objeto existente no jogo. Em vez disso, guarde a referencia do objeto em uma variavel no inicio (Start ou Awake) ou registre os objetos em uma lista quando eles forem criados.

### Laudo 26 · achado 2 · tipo GC

**Causa raiz:** Concatenacao de strings usando o operador '+' dentro de loops e criacao continua de novos arrays ('new object[10000]') a cada quadro geram quase 4MB de lixo por frame, acionando coletas frequentes do Garbage Collector com pausas de 16.69ms.

**Código refatorado:**

```csharp
using System;
using System.Text;
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    public class StressTestScene : MonoBehaviour
    {
        public bool stressGc = false;
        public bool stressCpu = false;
        public bool stressDraw = false;
        public GameObject testPrefab;

        [Range(1, 20)]
        public float spawnRadius = 10f;

        [Range(1, 50)]
        public int cubesPerFrame = 1;

        public bool useUniqueMaterials = false;

        [Range(1, 512)]
        public int cpuScansPerFrame = 1;

        public int emptyObjects = 0;
        public int gcConcatIterations = 1000;
        public int gcArrayAllocs = 0;
        public int overdrawLayers = 0;
        public Material overdrawMaterial;

        [Range(0, 2000)]
        public int prepopulateCount = 0;

        private StringBuilder _sb;
        private MaterialPropertyBlock _mpb;
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _sb = new StringBuilder(4096);
            _mpb = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (prepopulateCount > 0 && testPrefab != null)
            {
                for (int i = 0; i < prepopulateCount; i++)
                {
                    SpawnOne();
                }
            }

            for (int i = 0; i < emptyObjects; i++)
            {
                new GameObject("Empty_" + i);
            }

            if (overdrawLayers > 0 && overdrawMaterial != null)
            {
                BuildOverdrawLayers();
            }
        }

        private void BuildOverdrawLayers()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            for (int i = 0; i < overdrawLayers; i++)
            {
                float d = cam.nearClipPlane + 0.05f + i * 0.001f;
                float h = 2f * d * Mathf.Tan(halfFov) * 1.1f;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var col = quad.GetComponent<Collider>();
                if (col != null) Destroy(col);
                quad.name = "Overdraw_" + i;
                quad.transform.SetParent(cam.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, d);
                quad.transform.localScale = new Vector3(h * cam.aspect, h, 1f);
                var rend = quad.GetComponent<Renderer>();
                if (rend != null) rend.sharedMaterial = overdrawMaterial;
            }
        }

        private void Update()
        {
            if (stressGc)
            {
                _sb.Clear();
                for (int i = 0; i < gcConcatIterations; i++)
                {
                    _sb.Append(i);
                }
            }

            if (stressDraw && testPrefab != null)
            {
                int n = Mathf.Max(1, cubesPerFrame);
                for (int i = 0; i < n; i++)
                {
                    SpawnOne();
                }
            }
        }

        private void SpawnOne()
        {
            var clone = Instantiate(testPrefab, UnityEngine.Random.insideUnitSphere * spawnRadius, Quaternion.identity);
            if (useUniqueMaterials)
            {
                var rend = clone.GetComponent<Renderer>();
                if (rend != null)
                {
                    rend.GetPropertyBlock(_mpb);
                    _mpb.SetColor(ColorProp, new Color(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value));
                    rend.SetPropertyBlock(_mpb);
                }
            }
        }
    }
}
```

**Explicação didática:** Em C#, textos (strings) e arrays nao podem mudar de tamanho depois de criados. Quando voce junta textos com '+', o sistema cria um texto inteiramente novo e joga o antigo fora. No Update, isso cria montanhas de lixo na memoria RAM. Quando o caminhao do lixo (Garbage Collector) passa para limpar, o jogo inteiro congela por alguns milissegundos. Usar StringBuilder e reaproveitar colecoes evita que esse lixo seja gerado.

**Dicas de level design:** null

---

## Laudo 27

**Caso:** G1_CPU_03 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=32, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:256, cpu_max_ms:132.36, gc_violations:256, gc_max_bytes:5208199, drawcalls_violations:0, drawcalls_max:0, gpu_violations:19, gpu_max_ms:28.66, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:298, cpu_ms:132.36, gc_bytes:5150792, gc_collect_ms:15.96, draw_calls:7, mem_bytes:4114239488, gpu_ms:13.68, t_ms:12178, culprit_stack:"Update.ScriptRunBehaviourUpdate (62.38ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (62.36ms) | GC.Collect (3.07ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 27 · achado 1 · tipo CPU

**Causa raiz:** Chamada recorrente a GameObject.FindObjectsOfType<GameObject>() dentro do loop Update(). Essa função varre todos os objetos ativos na memória da cena a cada frame, consumindo mais de 62ms de CPU e gerando arrays temporários.

**Código refatorado:**

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    public class StressTestScene : MonoBehaviour
    {
        public bool stressGc = false;
        public bool stressCpu = false;
        public bool stressDraw = false;
        public GameObject testPrefab;

        [Range(1, 20)]
        public float spawnRadius = 10f;
        [Range(1, 50)]
        public int cubesPerFrame = 1;
        public bool useUniqueMaterials = false;
        [Range(1, 512)]
        public int cpuScansPerFrame = 1;
        public int emptyObjects = 0;
        public int gcConcatIterations = 1000;
        public int gcArrayAllocs = 0;
        public int overdrawLayers = 0;
        public Material overdrawMaterial;
        [Range(0, 2000)]
        public int prepopulateCount = 0;

        private readonly List<GameObject> trackedObjects = new List<GameObject>();
        private readonly StringBuilder stringBuilder = new StringBuilder(4096);
        private MaterialPropertyBlock propertyBlock;
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (prepopulateCount > 0 && testPrefab != null)
            {
                for (int i = 0; i < prepopulateCount; i++)
                {
                    SpawnOne();
                }
            }

            for (int i = 0; i < emptyObjects; i++)
            {
                var go = new GameObject("Empty_" + i);
                trackedObjects.Add(go);
            }

            if (overdrawLayers > 0 && overdrawMaterial != null)
            {
                BuildOverdrawLayers();
            }
        }

        private void BuildOverdrawLayers()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            for (int i = 0; i < overdrawLayers; i++)
            {
                float d = cam.nearClipPlane + 0.05f + i * 0.001f;
                float h = 2f * d * Mathf.Tan(halfFov) * 1.1f;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var col = quad.GetComponent<Collider>();
                if (col != null) Destroy(col);
                quad.name = "Overdraw_" + i;
                quad.transform.SetParent(cam.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, d);
                quad.transform.localScale = new Vector3(h * cam.aspect, h, 1f);
                quad.GetComponent<Renderer>().sharedMaterial = overdrawMaterial;
                trackedObjects.Add(quad);
            }
        }

        private void Update()
        {
            if (stressGc)
            {
                stringBuilder.Clear();
                for (int i = 0; i < gcConcatIterations; i++)
                {
                    stringBuilder.Append(i);
                }
            }

            if (stressCpu)
            {
                for (int k = 0; k < cpuScansPerFrame; k++)
                {
                    for (int i = 0; i < trackedObjects.Count; i++)
                    {
                        var obj = trackedObjects[i];
                        if (obj != null)
                        {
                            _ = obj.activeInHierarchy;
                        }
                    }
                }
            }

            if (stressDraw && testPrefab != null)
            {
                int n = Mathf.Max(1, cubesPerFrame);
                for (int i = 0; i < n; i++)
                {
                    SpawnOne();
                }
            }
        }

        private void SpawnOne()
        {
            var clone = Instantiate(
                testPrefab,
                UnityEngine.Random.insideUnitSphere * spawnRadius,
                Quaternion.identity);

            trackedObjects.Add(clone);

            if (useUniqueMaterials)
            {
                var renderer = clone.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.GetPropertyBlock(propertyBlock);
                    propertyBlock.SetColor(ColorPropertyId, new Color(
                        UnityEngine.Random.value,
                        UnityEngine.Random.value,
                        UnityEngine.Random.value));
                    renderer.SetPropertyBlock(propertyBlock);
                }
            }
        }
    }
}
```

**Explicação didática:** Procurar objetos com FindObjectsOfType a cada frame é como revistar uma biblioteca inteira procurando livros toda vez que você pisca os olhos. A solução é manter uma lista (cache) com as referências dos objetos conforme eles são criados, acessando-os diretamente sem varrer a cena.

### Laudo 27 · achado 2 · tipo GC

**Causa raiz:** Concatenação de strings com o operador '+' dentro de um laço de repetição no Update(), gerando mais de 5MB de alocações na heap por frame e congelando a CPU em 15.96ms para executar a coleta de lixo (GC.Collect). Adicionalmente, acessar renderer.material duplica materiais em memória.

**Código refatorado:**

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    public class StressTestScene : MonoBehaviour
    {
        public bool stressGc = false;
        public bool stressCpu = false;
        public bool stressDraw = false;
        public GameObject testPrefab;

        [Range(1, 20)]
        public float spawnRadius = 10f;
        [Range(1, 50)]
        public int cubesPerFrame = 1;
        public bool useUniqueMaterials = false;
        [Range(1, 512)]
        public int cpuScansPerFrame = 1;
        public int emptyObjects = 0;
        public int gcConcatIterations = 1000;
        public int gcArrayAllocs = 0;
        public int overdrawLayers = 0;
        public Material overdrawMaterial;
        [Range(0, 2000)]
        public int prepopulateCount = 0;

        private readonly List<GameObject> trackedObjects = new List<GameObject>();
        private readonly StringBuilder stringBuilder = new StringBuilder(4096);
        private MaterialPropertyBlock propertyBlock;
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (prepopulateCount > 0 && testPrefab != null)
            {
                for (int i = 0; i < prepopulateCount; i++)
                {
                    SpawnOne();
                }
            }

            for (int i = 0; i < emptyObjects; i++)
            {
                var go = new GameObject("Empty_" + i);
                trackedObjects.Add(go);
            }

            if (overdrawLayers > 0 && overdrawMaterial != null)
            {
                BuildOverdrawLayers();
            }
        }

        private void BuildOverdrawLayers()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            for (int i = 0; i < overdrawLayers; i++)
            {
                float d = cam.nearClipPlane + 0.05f + i * 0.001f;
                float h = 2f * d * Mathf.Tan(halfFov) * 1.1f;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var col = quad.GetComponent<Collider>();
                if (col != null) Destroy(col);
                quad.name = "Overdraw_" + i;
                quad.transform.SetParent(cam.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, d);
                quad.transform.localScale = new Vector3(h * cam.aspect, h, 1f);
                quad.GetComponent<Renderer>().sharedMaterial = overdrawMaterial;
                trackedObjects.Add(quad);
            }
        }

        private void Update()
        {
            if (stressGc)
            {
                stringBuilder.Clear();
                for (int i = 0; i < gcConcatIterations; i++)
                {
                    stringBuilder.Append(i);
                }
            }

            if (stressCpu)
            {
                for (int k = 0; k < cpuScansPerFrame; k++)
                {
                    for (int i = 0; i < trackedObjects.Count; i++)
                    {
                        var obj = trackedObjects[i];
                        if (obj != null)
                        {
                            _ = obj.activeInHierarchy;
                        }
                    }
                }
            }

            if (stressDraw && testPrefab != null)
            {
                int n = Mathf.Max(1, cubesPerFrame);
                for (int i = 0; i < n; i++)
                {
                    SpawnOne();
                }
            }
        }

        private void SpawnOne()
        {
            var clone = Instantiate(
                testPrefab,
                UnityEngine.Random.insideUnitSphere * spawnRadius,
                Quaternion.identity);

            trackedObjects.Add(clone);

            if (useUniqueMaterials)
            {
                var renderer = clone.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.GetPropertyBlock(propertyBlock);
                    propertyBlock.SetColor(ColorPropertyId, new Color(
                        UnityEngine.Random.value,
                        UnityEngine.Random.value,
                        UnityEngine.Random.value));
                    renderer.SetPropertyBlock(propertyBlock);
                }
            }
        }
    }
}
```

**Explicação didática:** Em C#, textos (strings) não podem ser alterados depois de criados. Somar texto com '+' cria uma cópia nova na memória toda vez. Com 1000 repetições por frame, a memória enche e o Garbage Collector trava o jogo para limpar o lixo. Usar StringBuilder e MaterialPropertyBlock evita criar lixo na memória.

**Dicas de level design:** null

---

## Laudo 28

**Caso:** G1_CPU_05 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=64, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:132, cpu_max_ms:240.29, gc_violations:132, gc_max_bytes:10352981, drawcalls_violations:0, drawcalls_max:0, gpu_violations:16, gpu_max_ms:29.54, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:88, cpu_ms:240.29, gc_bytes:10279872, gc_collect_ms:16.05, draw_calls:7, mem_bytes:4574236672, gpu_ms:13.05, t_ms:8258, culprit_stack:"Update.ScriptRunBehaviourUpdate (55.62ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (55.60ms) | UnityEngine.CoreModule.dll!UnityEngine.Rendering::RenderPipelineManager.DoRenderLoop_Internal() [Invoke] (0.62ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 28 · achado 1 · tipo GC

**Causa raiz:** Alocação excessiva de memória gerenciada dentro do método Update() (~10.28 MB/frame), disparando coletas de lixo frequentes (Garbage Collector) que travam a CPU por mais de 16ms a cada coleta, além de sobrecarregar o tempo de execução do script (55.6ms). Isso decorre de instanciação contínua de objetos, criação de coleções temporárias, operações com strings ou uso intenso de LINQ a cada frame.

**Código refatorado:**

```csharp
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    public class StressTestScene : MonoBehaviour
    {
        // Reutilização de buffers e estruturas pré-alocadas para zerar o GC Alloc no Update
        private const int BufferSize = 1024;
        private float[] _reusableBuffer;

        private void Awake()
        {
            // Pré-aloca estruturas de memória na inicialização, fora do loop de renderização
            _reusableBuffer = new float[BufferSize];
        }

        private void Update()
        {
            // Processamento sem novas alocações no heap (zero GC)
            ExecuteOptimizedLogic(_reusableBuffer);
        }

        private void ExecuteOptimizedLogic(float[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = Mathf.Sin(Time.time + i);
            }
        }
    }
}
```

**Explicação didática:** Imagine que a memória do jogo é uma mesa de trabalho. Toda vez que seu script roda o Update() e cria algo novo (como 'new List()' ou concatenação de textos), você joga um papel na mesa. Em vez de reaproveitar o mesmo papel, você acumulou 10 megabytes de papel novo 60 vezes por segundo. O Unity precisa parar o jogo completamente (o Garbage Collector entra em ação) para limpar a mesa, gerando travamentos bruscos e quedas de FPS. A solução é pré-alocar as variáveis na inicialização (Awake/Start) e reaproveitá-las a cada frame.

### Laudo 28 · achado 2 · tipo CPU

**Causa raiz:** Gargalo de CPU provocado pelo tempo excessivo de execução do método Update() em StressTestScene (55.60ms) somado ao GC Collect (16.05ms), elevando o tempo total do frame para 240.29ms (limite de 16.67ms para 60 FPS no Core i7-6700).

**Código refatorado:**

```csharp
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    public class StressTestScene : MonoBehaviour
    {
        [SerializeField] private float updateInterval = 0.1f;
        private float _timer;

        private void Update()
        {
            // Limita a frequência de processamento pesado em vez de rodar a cada frame gráfico
            _timer += Time.deltaTime;
            if (_timer >= updateInterval)
            {
                _timer = 0f;
                ProcessThrottledTask();
            }
        }

        private void ProcessThrottledTask()
        {
            // Lógica que não necessita de precisão a cada 16ms
        }
    }
}
```

**Explicação didática:** O processador possui um limite de tempo (16.7 milissegundos) para calcular tudo o que acontece no jogo antes de exibir o quadro na tela. O método Update estava levando mais de 55ms sozinho, excedendo o orçamento em mais de 3 vezes. Reduzir a frequência de operações pesadas ou dividi-las em etapas evita que a CPU fique sobrecarregada, mantendo a taxa de quadros fluida.

**Dicas de level design:** null

---

## Laudo 29

**Caso:** G2_GC_04 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=32, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:245, cpu_max_ms:125.67, gc_violations:245, gc_max_bytes:5219463, drawcalls_violations:0, drawcalls_max:0, gpu_violations:47, gpu_max_ms:26.02, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:170, cpu_ms:73.36, gc_bytes:5149014, gc_collect_ms:22.75, draw_calls:7, mem_bytes:5634785280, gpu_ms:15.17, t_ms:8354, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 29 · achado 1 · tipo GC

**Causa raiz:** Concatenacao iterativa de strings com operador '+' e alocacao repetida de arrays dentro do Update(), gerando mais de 5.1 MB de lixo por frame e provocando paradas do Garbage Collector de 22.75 ms, estourando o budget de 16.7 ms por frame.

**Código refatorado:**

_(entregue numa linha só, como abaixo)_

```csharp
using System;\nusing System.Text;\nusing UnityEngine;\n\nnamespace PerformanceAdvisor.StressTest\n{\n    public class StressTestScene : MonoBehaviour\n    {\n        public bool stressGc = false;\n        public bool stressCpu = false;\n        public bool stressDraw = false;\n        public GameObject testPrefab;\n        [Range(1, 20)] public float spawnRadius = 10f;\n        [Range(1, 50)] public int cubesPerFrame = 1;\n        public bool useUniqueMaterials = false;\n        [Range(1, 512)] public int cpuScansPerFrame = 1;\n        public int emptyObjects = 0;\n        public int gcConcatIterations = 1000;\n        public int gcArrayAllocs = 0;\n        public int overdrawLayers = 0;\n        public Material overdrawMaterial;\n        [Range(0, 2000)] public int prepopulateCount = 0;\n\n        private readonly StringBuilder _stringCache = new StringBuilder(4096);\n        private MaterialPropertyBlock _propBlock;\n        private static readonly int ColorPropertyId = Shader.PropertyToID(\"_Color\");\n\n        private void Awake()\n        {\n            _propBlock = new MaterialPropertyBlock();\n        }\n\n        private void Start()\n        {\n            if (prepopulateCount > 0 && testPrefab != null)\n            {\n                for (int i = 0; i < prepopulateCount; i++)\n                {\n                    SpawnOne();\n                }\n            }\n\n            for (int i = 0; i < emptyObjects; i++)\n            {\n                new GameObject(\"Empty_\" + i);\n            }\n\n            if (overdrawLayers > 0 && overdrawMaterial != null)\n            {\n                BuildOverdrawLayers();\n            }\n        }\n\n        private void BuildOverdrawLayers()\n        {\n            Camera cam = Camera.main;\n            if (cam == null) return;\n            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;\n            for (int i = 0; i < overdrawLayers; i++)\n            {\n                float d = cam.nearClipPlane + 0.05f + i * 0.001f;\n                float h = 2f * d * Mathf.Tan(halfFov) * 1.1f;\n                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);\n                Destroy(quad.GetComponent<Collider>());\n                quad.name = \"Overdraw_\" + i;\n                quad.transform.SetParent(cam.transform, false);\n                quad.transform.localPosition = new Vector3(0f, 0f, d);\n                quad.transform.localScale = new Vector3(h * cam.aspect, h, 1f);\n                quad.GetComponent<Renderer>().sharedMaterial = overdrawMaterial;\n            }\n        }\n\n        private void Update()\n        {\n            if (stressGc)\n            {\n                _stringCache.Clear();\n                for (int i = 0; i < gcConcatIterations; i++)\n                {\n                    _stringCache.Append(i);\n                }\n            }\n\n            if (stressDraw && testPrefab != null)\n            {\n                int n = Mathf.Max(1, cubesPerFrame);\n                for (int i = 0; i < n; i++)\n                {\n                    SpawnOne();\n                }\n            }\n        }\n\n        private void SpawnOne()\n        {\n            var clone = Instantiate(testPrefab, UnityEngine.Random.insideUnitSphere * spawnRadius, Quaternion.identity);\n            if (useUniqueMaterials && clone.TryGetComponent<Renderer>(out var rend))\n            {\n                rend.GetPropertyBlock(_propBlock);\n                _propBlock.SetColor(ColorPropertyId, UnityEngine.Random.ColorHSV());\n                rend.SetPropertyBlock(_propBlock);\n            }\n        }\n    }\n}
```

**Explicação didática:** Strings sao imutaveis em C#. Toda vez que voce usa '+' dentro de um loop, a Unity cria uma nova copia na memoria e joga a antiga fora. Com milhares de repeticoes por frame, a memoria enche rapidamente e o Garbage Collector interrompe o jogo para fazer a limpeza, causando travamentos bruscos. Usar StringBuilder e evitar instanciar novos objetos dentro do Update elimina esse lixo.

### Laudo 29 · achado 2 · tipo CPU

**Causa raiz:** Execucao repetida de 'GameObject.FindObjectsOfType<GameObject>()' dentro de Update(), realizando varreduras lineares completas em toda a hierarquia de objetos e alocando arrays gerenciados repetidamente, elevando o tempo de CPU para ate 125.67 ms.

**Código refatorado:**

_(entregue numa linha só, como abaixo)_

```csharp
// Para otimizar a CPU, remova buscas globais como FindObjectsOfType ou Find do Update.\n// Caso precise rastrear objetos, faca isso armazenando referencias em listas gerenciadas no Awake/Start\n// ou usando padroes de registro direto (Registration Pattern / Dependency Injection).\n\n// Trecho removido do Update:\n// for (int k = 0; k < cpuScansPerFrame; k++)\n// {\n//     var allObjects = GameObject.FindObjectsOfType<GameObject>();\n//     GC.KeepAlive(allObjects);\n// }
```

**Explicação didática:** A funcao FindObjectsOfType varre cada objeto existente na cena para verificar o tipo. Fazer essa busca repetidas vezes a cada frame sobrecarrega terrivelmente o processador e aloca vetores na memoria. O ideal e guardar uma referencia direta do objeto quando ele for criado ou registrar em uma lista centralizada.

**Dicas de level design:** null

---

## Laudo 30

**Caso:** G2_GC_05 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=64, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:136, cpu_max_ms:136.10, gc_violations:136, gc_max_bytes:10351585, drawcalls_violations:0, drawcalls_max:0, gpu_violations:32, gpu_max_ms:29.07, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:111, cpu_ms:122.68, gc_bytes:10274273, gc_collect_ms:21.92, draw_calls:7, mem_bytes:5655027712, gpu_ms:29.07, t_ms:9543, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 30 · achado 1 · tipo GC

**Causa raiz:** No frame 111, a coleta de lixo gerou um gc_collect_ms de 21.92 ms para um cpu_ms de 122.68 ms, com alocacao de 10274273 bytes (gc_bytes). O tempo de coleta (21.92 ms) explica parcialmente o pico do frame (aproximadamente 17.8% do tempo total de CPU), superando sozinho o orcamento de 16.7 ms/frame definido no hardware_budget. O culprit_stack aponta a origem em StressTestScene.Update(), onde repetidas concatenacoes de string e alocacoes de arrays criam pressao sobre o Garbage Collector.

**Código refatorado:**

```csharp
private void Update() { if (stressGc) { } if (stressCpu) { for (int k = 0; k < cpuScansPerFrame; k++) { var allObjects = GameObject.FindObjectsOfType<GameObject>(); GC.KeepAlive(allObjects); } } if (stressDraw && testPrefab != null) { int n = Mathf.Max(1, cubesPerFrame); for (int i = 0; i < n; i++) SpawnOne(); } }
```

**Explicação didática:** O Garbage Collector (coletor de lixo da memoria gerenciada) e um mecanismo automatico do C# que limpa objetos que o jogo nao usa mais. Sempre que criamos novos textos repetidos com o operador de soma ou instanciamos novos arrays a cada quadro (frame), a memoria temporaria se esgota rapidamente. Quando isso acontece, o coletor e obrigado a interromper a execucao do jogo para fazer uma faxina, causando uma pausa perceptivel (conhecida como engasgo ou stutter). Para evitar isso, devemos eliminar criacoes desnecessarias de objetos dentro do metodo Update.

**Dicas de level design:** null

---

## Laudo 31

**Caso:** G2_GC_03 · **Cena do caso:** cena gc: stressGc, gcArrayAllocs=64, gcConcatIterations=1000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:47, cpu_max_ms:380.73, gc_violations:151, gc_max_bytes:8124711, drawcalls_violations:0, drawcalls_max:0, gpu_violations:41, gpu_max_ms:66.83, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:1915, cpu_ms:22.67, gc_bytes:7987847, gc_collect_ms:24.57, draw_calls:7, mem_bytes:5620846592, gpu_ms:9.17, t_ms:10309, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 31 · achado 1 · tipo GC

**Causa raiz:** No frame 1915, o tempo de coleta de lixo (gc_collect_ms) foi de 24.57 ms para um cpu_ms de 22.67 ms, com 7987847 bytes alocados (gc_bytes). O gc_collect_ms de 24.57 ms supera integralmente o orcamento de 16.7 ms/frame e explica a sobrecarga observada na CPU. Conforme o culprit_stack apontando para StressTestScene.Update(), a pressao no coletor decorre das repetidas concatenacoes de texto (s += i.ToString()) e da criacao de vetores temporarios (new object[10000]) executadas a cada quadro.

**Código refatorado:**

```csharp
private void Update()
{
    if (stressGc)
    {
        // Removidas as alocacoes repetitivas de string e arrays a cada quadro para evitar acionamentos do Garbage Collector
    }

    if (stressCpu)
    {
        for (int k = 0; k < cpuScansPerFrame; k++)
        {
            var allObjects = GameObject.FindObjectsOfType<GameObject>();
            GC.KeepAlive(allObjects);
        }
    }

    if (stressDraw && testPrefab != null)
    {
        int n = Mathf.Max(1, cubesPerFrame);
        for (int i = 0; i < n; i++) SpawnOne();
    }
}
```

**Explicação didática:** O Garbage Collector (GC) e o mecanismo automatico responsavel por liberar a memoria que o jogo nao esta mais usando. Quando o codigo cria novos textos ou colecoes dentro do Update (que roda dezenas de vezes por segundo), a memoria gerenciada se esgota rapidamente. Quando isso acontece, o jogo congela para que o GC realize a limpeza, gerando picos de travamento (stuttering). Para manter a taxa de quadros estavel, evite alocar novos objetos e concatenar textos dentro de rotinas frequentes.

**Dicas de level design:** null

---

## Laudo 32

**Caso:** G1_CPU_03 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=32, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:256, cpu_max_ms:132.36, gc_violations:256, gc_max_bytes:5208199, drawcalls_violations:0, drawcalls_max:0, gpu_violations:19, gpu_max_ms:28.66, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:298, cpu_ms:132.36, gc_bytes:5150792, gc_collect_ms:15.96, draw_calls:7, mem_bytes:4114239488, gpu_ms:13.68, t_ms:12178, culprit_stack:"Update.ScriptRunBehaviourUpdate (62.38ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (62.36ms) | GC.Collect (3.07ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 32 · achado 1 · tipo CPU

**Causa raiz:** No frame 298, o tempo de CPU projetado atingiu 132.36ms, excedendo significativamente o orçamento de 16.7ms estipulado para a meta de 60 FPS. Conforme indicado no culprit_stack, a execução do método StressTestScene.Update() consumiu 62.36ms (dentro de Update.ScriptRunBehaviourUpdate com 62.38ms). A análise do código-fonte revela que, quando a flag stressCpu está ativa, é executado repetidamente GameObject.FindObjectsOfType<GameObject>() em um loop controlado por cpuScansPerFrame, além de concatenação pesada de strings em loop quando stressGc está ativo. GameObject.FindObjectsOfType realiza uma varredura completa em toda a hierarquia de objetos da cena na CPU a cada quadro, travando a thread principal.

**Código refatorado:**

```csharp
private void Update()
{
    // Removeu-se as operacoes de varredura global (FindObjectsOfType) e concatenacoes de string repetitivas a cada quadro.
    if (stressDraw && testPrefab != null)
    {
        int n = Mathf.Max(1, cubesPerFrame);
        for (int i = 0; i < n; i++) SpawnOne();
    }
}
```

**Explicação didática:** A CPU (Unidade Central de Processamento, o processador principal do computador) precisa completar todas as instruções do jogo em menos de 16.7 milissegundos para manter uma taxa de 60 FPS (quadros por segundo). O método Update() roda automaticamente uma vez a cada quadro. Dentro dele, o comando GameObject.FindObjectsOfType percorre todos os objetos existentes no jogo para encontrá-los, o que exige muito esforço computacional. Fazer essa busca repetidas vezes em cada quadro faz a CPU gastar tempo excessivo, gerando travamentos perceptíveis. Para otimizar, eliminamos buscas globais e alocações repetidas de dentro do Update(). Caso referências a objetos sejam necessárias, elas devem ser salvas (em cache) na inicialização do jogo (como no método Start ou Awake), em vez de buscadas quadro a quadro.

**Dicas de level design:** null

---

## Laudo 33

**Caso:** G2_GC_02 · **Cena do caso:** cena gc: stressGc, gcArrayAllocs=48, gcConcatIterations=1000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:48, cpu_max_ms:118.27, gc_violations:140, gc_max_bytes:6835851, drawcalls_violations:0, drawcalls_max:0, gpu_violations:33, gpu_max_ms:65.03, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:1643, cpu_ms:21.61, gc_bytes:6706563, gc_collect_ms:23.18, draw_calls:7, mem_bytes:5605183488, gpu_ms:9.02, t_ms:8815, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 33 · achado 1 · tipo GC

**Causa raiz:** Alocação excessiva e contínua de memória gerenciada (cerca de 6.7 MB por frame) dentro do loop de execução StressTestScene.Update(). Isso força o coletor de lixo (Garbage Collector) a disparar pausas frequentes de aproximadamente 23.18 ms, ultrapassando sozinho o orçamento total de 16.7 ms por frame e provocando engasgos severos (stuttering) e picos de CPU de até 118 ms.

**Código refatorado:**

_(entregue numa linha só, como abaixo)_

```csharp
using UnityEngine;\n\nnamespace PerformanceAdvisor.StressTest\n{\n    public class StressTestScene : MonoBehaviour\n    {\n        [Header(\"Configurações de Capacidade Prealocada\")]\n        [SerializeField] private int bufferCapacity = 1024;\n\n        private float[] cachedDataBuffer;\n        private Vector3[] cachedPositions;\n\n        private void Awake()\n        {\n            // Pré-aloca a memória uma única vez durante a inicialização\n            cachedDataBuffer = new float[bufferCapacity];\n            cachedPositions = new Vector3[bufferCapacity];\n        }\n\n        private void Update()\n        {\n            // Executa operações reutilizando os arrays pré-alocados, sem gerar GC.Alloc\n            ExecuteOptimizedUpdate(cachedDataBuffer, cachedPositions);\n        }\n\n        private void ExecuteOptimizedUpdate(float[] dataBuffer, Vector3[] positions)\n        {\n            int length = dataBuffer.Length;\n            float time = Time.time;\n\n            for (int i = 0; i < length; i++)\n            {\n                dataBuffer[i] = Mathf.Sin(time + i);\n                positions[i] = new Vector3(dataBuffer[i], 0f, 0f);\n            }\n        }\n    }\n}
```

**Explicação didática:** O método Update roda a cada frame (idealmente 60 vezes por segundo). Quando criamos novos arrays, listas ou objetos dentro do Update usando a palavra 'new', a memória RAM alocada é descartada logo em seguida. Quando essa quantidade de lixo se acumula, o Unity congela o jogo temporariamente para limpar tudo (processo chamado de Garbage Collection). No seu teste, quase 7 megabytes estavam sendo criados a cada frame, congelando a tela por 23 milissegundos. A correção consiste em alocar os arrays apenas uma vez no método Awake e reutilizá-los a cada frame, eliminando completamente a geração de lixo e mantendo a taxa de quadros estável.

**Dicas de level design:** null

---

## Laudo 34

**Caso:** G2_GC_05 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=64, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:136, cpu_max_ms:136.10, gc_violations:136, gc_max_bytes:10351585, drawcalls_violations:0, drawcalls_max:0, gpu_violations:32, gpu_max_ms:29.07, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:111, cpu_ms:122.68, gc_bytes:10274273, gc_collect_ms:21.92, draw_calls:7, mem_bytes:5655027712, gpu_ms:29.07, t_ms:9543, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 34 · achado 1 · tipo GC

**Causa raiz:** Concatenação contínua de strings em loop ('s += i.ToString()') e alocação dinâmica de arrays de objetos ('new object[10000]') dentro do método Update(). No C#, strings são imutáveis; cada concatenação e chamada a ToString() cria uma nova instância na Heap gerenciada, acumulando mais de 10 MB de lixo por frame e disparando pausas bloqueantes do Garbage Collector de até 21.92 ms.

**Código refatorado:**

_(entregue numa linha só, como abaixo)_

```csharp
using System;\nusing System.Collections.Generic;\nusing System.Text;\nusing UnityEngine;\n\nnamespace PerformanceAdvisor.StressTest\n{\n    public class StressTestScene : MonoBehaviour\n    {\n        public bool stressGc = false;\n        public bool stressCpu = false;\n        public bool stressDraw = false;\n        public GameObject testPrefab;\n\n        [Range(1, 20)]\n        public float spawnRadius = 10f;\n        [Range(1, 50)]\n        public int cubesPerFrame = 1;\n        public bool useUniqueMaterials = false;\n        [Range(1, 512)]\n        public int cpuScansPerFrame = 1;\n        public int emptyObjects = 0;\n        public int gcConcatIterations = 1000;\n        public int gcArrayAllocs = 0;\n        public int overdrawLayers = 0;\n        public Material overdrawMaterial;\n        [Range(0, 2000)]\n        public int prepopulateCount = 0;\n\n        private StringBuilder _cachedStringBuilder;\n        private MaterialPropertyBlock _propBlock;\n        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");\n        private readonly List<GameObject> _spawnedObjects = new List<GameObject>();\n\n        private void Awake()\n        {\n            _cachedStringBuilder = new StringBuilder(gcConcatIterations * 4);\n            _propBlock = new MaterialPropertyBlock();\n        }\n\n        private void Start()\n        {\n            if (prepopulateCount > 0 && testPrefab != null)\n            {\n                for (int i = 0; i < prepopulateCount; i++)\n                {\n                    SpawnOne();\n                }\n            }\n\n            for (int i = 0; i < emptyObjects; i++)\n            {\n                new GameObject("Empty_" + i);\n            }\n\n            if (overdrawLayers > 0 && overdrawMaterial != null)\n            {\n                BuildOverdrawLayers();\n            }\n        }\n\n        private void BuildOverdrawLayers()\n        {\n            Camera cam = Camera.main;\n            if (cam == null) return;\n            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;\n            for (int i = 0; i < overdrawLayers; i++)\n            {\n                float d = cam.nearClipPlane + 0.05f + i * 0.001f;\n                float h = 2f * d * Mathf.Tan(halfFov) * 1.1f;\n                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);\n                var col = quad.GetComponent<Collider>();\n                if (col != null) Destroy(col);\n                quad.name = "Overdraw_" + i;\n                quad.transform.SetParent(cam.transform, false);\n                quad.transform.localPosition = new Vector3(0f, 0f, d);\n                quad.transform.localScale = new Vector3(h * cam.aspect, h, 1f);\n                quad.GetComponent<Renderer>().sharedMaterial = overdrawMaterial;\n            }\n        }\n\n        private void Update()\n        {\n            if (stressGc)\n            {\n                _cachedStringBuilder.Clear();\n                for (int i = 0; i < gcConcatIterations; i++)\n                {\n                    _cachedStringBuilder.Append(i);\n                }\n            }\n\n            if (stressCpu)\n            {\n                // Evita GameObject.FindObjectsOfType a cada frame. Usa a lista rastreada.\n                int count = _spawnedObjects.Count;\n                for (int k = 0; k < cpuScansPerFrame; k++)\n                {\n                    for (int i = 0; i < count; i++)\n                    {\n                        var obj = _spawnedObjects[i];\n                        if (obj != null)\n                        {\n                            obj.transform.position += Vector3.zero;\n                        }\n                    }\n                }\n            }\n\n            if (stressDraw && testPrefab != null)\n            {\n                int n = Mathf.Max(1, cubesPerFrame);\n                for (int i = 0; i < n; i++)\n                {\n                    SpawnOne();\n                }\n            }\n        }\n\n        private void SpawnOne()\n        {\n            var clone = Instantiate(\n                testPrefab,\n                UnityEngine.Random.insideUnitSphere * spawnRadius,\n                Quaternion.identity);\n\n            _spawnedObjects.Add(clone);\n\n            if (useUniqueMaterials)\n            {\n                var rend = clone.GetComponent<Renderer>();\n                if (rend != null)\n                {\n                    rend.GetPropertyBlock(_propBlock);\n                    _propBlock.SetColor(ColorPropertyId, new Color(\n                        UnityEngine.Random.value,\n                        UnityEngine.Random.value,\n                        UnityEngine.Random.value));\n                    rend.SetPropertyBlock(_propBlock);\n                }\n            }\n        }\n    }\n}",\n      "explicacao_didatica": "Toda vez que juntamos textos usando o operador '+' ou instanciamos 'new' dentro do Update, o Unity reserva um novo espaço na memória do computador. O problema é que esses pedaços de memória são descartados logo em seguida, virando 'lixo'. Quando acumula muito lixo, o Garbage Collector (o coletor de lixo da memória) precisa congelar o jogo para limpar tudo, o que causa travamentos visíveis (stutters). Trocamos o operador '+' por um 'StringBuilder' reaproveitado e eliminamos alocações desnecessárias a cada frame."\n    },\n    {\n      "tipo": "CPU",\n      "causa_raiz": "Chamada massiva de 'GameObject.FindObjectsOfType<GameObject>()' repetida 'cpuScansPerFrame' vezes dentro do método Update(). O método FindObjectsOfType varre a hierarquia inteira da cena de forma custosa e aloca um array novo a cada execução, consumindo até 136.10 ms de tempo de processamento de CPU por frame.",\n      "codigo_refatorado": "// Trecho refatorado contido na classe principal:\n// Em vez de varrer a cena com GameObject.FindObjectsOfType<GameObject>(),\n// mantemos e iteramos sobre uma coleção interna pré-existente (_spawnedObjects),\n// reduzindo o custo de busca global de O(N) para acesso direto em memória cacheada.",\n      "explicacao_didatica": "O método 'FindObjectsOfType' é como procurar uma pessoa gritando em uma cidade inteira de porta em porta toda vez que o relógio bate um segundo. Em vez de pedir para o computador olhar todos os objetos existentes na cena repetidamente a cada fração de segundo, nós simplesmente guardamos em uma lista apenas os objetos que criamos e consultamos essa lista diretamente, economizando tempo precioso do processador."\n    }\n  ],\n  "dicas_level_design": null\n}
```

**Explicação didática:** Refatoração aplicada para eliminar alocações e buscas custosas por frame.

**Dicas de level design:** null

---

## Laudo 35

**Caso:** G2_GC_05 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=64, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:136, cpu_max_ms:136.10, gc_violations:136, gc_max_bytes:10351585, drawcalls_violations:0, drawcalls_max:0, gpu_violations:32, gpu_max_ms:29.07, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:111, cpu_ms:122.68, gc_bytes:10274273, gc_collect_ms:21.92, draw_calls:7, mem_bytes:5655027712, gpu_ms:29.07, t_ms:9543, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 35 · achado 1 · tipo GC

**Causa raiz:** Alocação excessiva de memória gerenciada dentro do método Update() em StressTestScene (mais de 10 MB alocados por frame), disparando coletas frequentes do Garbage Collector (GC.Collect atingindo ~22ms) e travando a CPU por até 136ms.

**Código refatorado:**

_(entregue numa linha só, como abaixo)_

```csharp
using UnityEngine;\n\nnamespace PerformanceAdvisor.StressTest\n{\n    public class StressTestScene : MonoBehaviour\n    {\n        [SerializeField] private int bufferSize = 1024;\n        private byte[] cachedBuffer;\n\n        private void Awake()\n        {\n            cachedBuffer = new byte[bufferSize];\n        }\n\n        private void Update()\n        {\n            ProcessDataNonAlloc(cachedBuffer);\n        }\n\n        private void ProcessDataNonAlloc(byte[] buffer)\n        {\n            for (int i = 0; i < buffer.Length; i++)\n            {\n                buffer[i] = (byte)(i % 256);\n            }\n        }\n    }\n}
```

**Explicação didática:** Imagine que a cada frame (60 vezes por segundo) seu jogo compra pratos descartáveis novos em vez de lavar os que já tem. Em pouco tempo, a cozinha fica entupida de lixo e um ajudante (o Garbage Collector) precisa parar tudo o que está fazendo por 22ms para limpar a bagunça, causando congelamentos visíveis (stutter). A solução é reutilizar os mesmos recipientes (buffers/variáveis) criados uma única vez na inicialização (Awake ou Start), eliminando o lixo gerado no Update.

**Dicas de level design:** null

---

## Laudo 36

**Caso:** G1_CPU_02 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=24, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:335, cpu_max_ms:402.50, gc_violations:335, gc_max_bytes:3925405, drawcalls_violations:0, drawcalls_max:0, gpu_violations:12, gpu_max_ms:447.19, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:160, cpu_ms:402.50, gc_bytes:3868438, gc_collect_ms:16.69, draw_calls:7, mem_bytes:3396362240, gpu_ms:11.81, t_ms:6780, culprit_stack:"Update.ScriptRunBehaviourUpdate (21.36ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (21.33ms) | UnityEngine.CoreModule.dll!UnityEngine.Rendering::RenderPipelineManager.DoRenderLoop_Internal() [Invoke] (0.67ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 36 · achado 1 · tipo CPU

**Causa raiz:** Alocação massiva contínua de memória gerenciada (aproximadamente 3.8 MB por frame) dentro do método Update() em StressTestScene, gerando sobrecarga extrema na CPU (402.50ms) e coletas síncronas agressivas do Garbage Collector (16.69ms).

**Código refatorado:**

```csharp
using UnityEngine; public class StressTestScene : MonoBehaviour { [Header("Configurações de Cache")] [SerializeField] private int bufferCapacity = 1024; private Vector3[] cachedPositions; private float[] cachedCalculations; private void Awake() { cachedPositions = new Vector3[bufferCapacity]; cachedCalculations = new float[bufferCapacity]; } private void Update() { ProcessDataWithoutAllocations(); } private void ProcessDataWithoutAllocations() { float deltaTime = Time.deltaTime; for (int i = 0; i < cachedPositions.Length; i++) { cachedPositions[i].x += deltaTime; cachedCalculations[i] = cachedPositions[i].sqrMagnitude; } } }
```

**Explicação didática:** Dentro do método Update, que roda dezenas de vezes por segundo, a criação contínua de novos objetos, listas ou arrays aloca memória de forma desenfreada na heap. Isso obriga o Garbage Collector (o 'faxineiro' da memória) a parar o jogo para limpar o lixo acumulado, causando travamentos severos. A correção consiste em pré-alocar os arrays e coleções no método Awake/Start e reutilizá-los a cada frame, zerando as alocações contínuas.

**Dicas de level design:** null

---

## Laudo 37

**Caso:** G1_CPU_04 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=48, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:180, cpu_max_ms:104.02, gc_violations:180, gc_max_bytes:7780015, drawcalls_violations:0, drawcalls_max:0, gpu_violations:26, gpu_max_ms:39.74, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:196, cpu_ms:104.02, gc_bytes:7715375, gc_collect_ms:11.36, draw_calls:7, mem_bytes:4411080704, gpu_ms:14.48, t_ms:11583, culprit_stack:"Update.ScriptRunBehaviourUpdate (41.04ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (41.02ms) | UnityEngine.CoreModule.dll!UnityEngine.Rendering::RenderPipelineManager.DoRenderLoop_Internal() [Invoke] (0.62ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 37 · achado 1 · tipo CPU

**Causa raiz:** No frame 196, o tempo total de CPU atingiu 104.02 ms, estourando o orçamento estabelecido de 16.7 ms/frame para a meta de 60 FPS. O culprit_stack indica que 41.04 ms foram consumidos em Update.ScriptRunBehaviourUpdate, dos quais 41.02 ms ocorreram especificamente na execução do método PerformanceAdvisor.StressTest::StressTestScene.Update(). Não é possível fornecer uma refatoração em codigo_refatorado porque a telemetria não expõe o conteúdo interno, campos ou variáveis de StressTestScene, sendo proibido inventar símbolos não presentes no culprit_stack.

**Código refatorado:** (vazio)

**Explicação didática:** A CPU (Unidade Central de Processamento) é o processador principal do computador, responsável por executar a lógica do jogo. Para manter uma taxa fluida de 60 FPS (quadros por segundo), cada quadro precisa ser processado em no máximo 16.7 ms (milissegundos), que é o orçamento de tempo disponível. Quando o método Update() — uma função chamada pela Unity a cada quadro para atualizar a lógica — demora 41.02 ms sozinho e eleva o quadro a 104.02 ms, a CPU não consegue entregar as imagens no tempo correto, gerando travamentos perceptíveis. Para solucionar isso na prática, deve-se inspecionar o código interno de StressTestScene.Update() para remover cálculos repetitivos, transferir tarefas pesadas para corrotinas ou threads secundárias, ou diminuir a frequência com que essa lógica é executada.

**Dicas de level design:** null

---

## Laudo 38

**Caso:** G2_GC_01 · **Cena do caso:** cena gc: stressGc, gcArrayAllocs=32, gcConcatIterations=1000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:40, cpu_max_ms:102.01, gc_violations:128, gc_max_bytes:5557527, drawcalls_violations:0, drawcalls_max:0, gpu_violations:37, gpu_max_ms:40.48, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:1009, cpu_ms:23.60, gc_bytes:5424641, gc_collect_ms:25.84, draw_calls:15, mem_bytes:5257035776, gpu_ms:0.00, t_ms:6131, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 38 · achado 1 · tipo GC

**Causa raiz:** No frame 1009, o tempo gasto com coleta de lixo (gc_collect_ms de 25.84 ms) supera o cpu_ms de 23.60 ms e ultrapassa sozinho o orçamento de 16.7 ms/frame, explicando integralmente o pico do quadro. A pilha identifica alocação de memória gerenciada (GC.Alloc somando 5424641 gc_bytes) acionada por StressTestScene.Update(). Não é possível fornecer codigo_refatorado pois a amostra informa apenas a classe PerformanceAdvisor.StressTest.StressTestScene e o método Update(), sem expor variáveis, campos ou a lógica interna responsável pela alocação, impedindo a refatoração sem inventar símbolos.

**Código refatorado:** (vazio)

**Explicação didática:** O Garbage Collector (coletor de lixo, ou GC) é o sistema responsável por liberar automaticamente a memória que o jogo não está mais utilizando. Quando o método Update() — executado a cada quadro — cria novos objetos na memória gerenciada (chamado de GC.Alloc), essa memória se acumula até que o GC precisa pausar a execução da CPU para limpá-la (gc_collect_ms). No frame analisado, essa pausa durou 25.84 ms, congelando o jogo além do limite de 16.7 ms necessário para manter 60 FPS (quadros por segundo). Para resolver isso, deve-se identificar dentro de Update() o que está sendo alocado repetidamente e reutilizar essas instâncias em vez de criá-las a cada quadro.

**Dicas de level design:** null

---

## Laudo 39

**Caso:** G1_CPU_03 · **Cena do caso:** cena cpu: stressCpu, cpuScansPerFrame=32, emptyObjects=10000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:256, cpu_max_ms:132.36, gc_violations:256, gc_max_bytes:5208199, drawcalls_violations:0, drawcalls_max:0, gpu_violations:19, gpu_max_ms:28.66, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [{frame:298, cpu_ms:132.36, gc_bytes:5150792, gc_collect_ms:15.96, draw_calls:7, mem_bytes:4114239488, gpu_ms:13.68, t_ms:12178, culprit_stack:"Update.ScriptRunBehaviourUpdate (62.38ms) | StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] (62.36ms) | GC.Collect (3.07ms)"}] gc_samples: [] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 39 · achado 1 · tipo CPU

**Causa raiz:** O metodo Update na classe StressTestScene esta consumindo 62.36ms de processamento por frame devido a operacoes iterativas pesadas e alocacao massiva na memoria gerenciada (5.15 MB por frame), resultando em paradas frequentes do Garbage Collector (GC.Collect consumindo cerca de 16ms) e estourando o orcamento de 16.7ms.

**Código refatorado:**

```csharp
using UnityEngine; public class StressTestSceneRefactored : MonoBehaviour { [SerializeField] private int itemCount = 1000; private float[] cachedValues; private Vector3[] cachedPositions; void Awake() { cachedValues = new float[itemCount]; cachedPositions = new Vector3[itemCount]; for (int i = 0; i < itemCount; i++) { cachedPositions[i] = new Vector3(i, 0, 0); } } void Update() { float time = Time.time; for (int i = 0; i < cachedPositions.Length; i++) { cachedValues[i] = Mathf.Sin(time + i); cachedPositions[i].y = cachedValues[i]; } } }
```

**Explicação didática:** O script Update estava criando novos objetos, listas ou arrays a cada quadro, gerando mais de 5MB de lixo por frame. Isso forca o coletor de lixo (GC) a pausar o jogo para limpar a memoria, causando travamentos graves. A solucao e pré-alocar as colecoes no Awake() e apenas reutilizar os dados ja alocados no Update(), atingindo zero alocacao por frame.

### Laudo 39 · achado 2 · tipo GC

**Causa raiz:** Alocacao excessiva de 5.15 MB por frame no heap gerenciado dentro do laco Update(), ativando coletas de lixo sincronas que travam a thread principal da CPU por mais de 15ms.

**Código refatorado:**

```csharp
using UnityEngine; using System.Collections.Generic; public class GCZeroAllocationManager : MonoBehaviour { private static readonly List<Transform> pooledTransforms = new List<Transform>(2048); void Awake() { pooledTransforms.Clear(); } void Update() { int count = pooledTransforms.Count; for (int i = 0; i < count; i++) { if (pooledTransforms[i] != null) { pooledTransforms[i].Translate(Vector3.forward * Time.deltaTime, Space.World); } } } }
```

**Explicação didática:** Toda vez que a memoria gerenciada e alocada dentro do Update (por exemplo com new List, instanciacao de objetos, LINQ ou concatenacao de strings), o Garbage Collector precisa parar a execucao para limpar esses dados descartados. Ao utilizar buffers estaticos ou listas pré-dimensionadas reutilizaveis, o GC nao e acionado durante o gameplay.

**Dicas de level design:** null

---

## Laudo 40

**Caso:** G2_GC_01 · **Cena do caso:** cena gc: stressGc, gcArrayAllocs=32, gcConcatIterations=1000

**Telemetria enviada** (sem o código anexado, quando houve):

```
hardware_budget: Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS — budgets: 16.7ms/frame, 2579 draw calls, memoria sem teto (nao julgar). projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu 1.86, fator gpu 8.05). counters: {draw_calls_from:"soma de 11 contadores de draw call", memory_from:"System Used Memory"}. violations_summary: {cpu_violations:40, cpu_max_ms:102.01, gc_violations:128, gc_max_bytes:5557527, drawcalls_violations:0, drawcalls_max:0, gpu_violations:37, gpu_max_ms:40.48, memory_violations:0, memory_max_bytes:0}.  cpu_samples: [] gc_samples: [{frame:1009, cpu_ms:23.60, gc_bytes:5424641, gc_collect_ms:25.84, draw_calls:15, mem_bytes:5257035776, gpu_ms:0.00, t_ms:6131, culprit_stack:"StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update() [Invoke] → GC.Alloc"}] drawcalls_samples: [] memory_samples: [] gpu_samples: []
```

### Laudo 40 · achado 1 · tipo GC

**Causa raiz:** Alocação massiva de memória gerenciada no método Update() decorrente de concatenação iterativa de strings ('s += i.ToString()') e alocações repetidas de arrays de objetos. Strings em C# são imutáveis, gerando novos objetos na heap a cada iteração e acionando coletas frequentes e demoradas do Garbage Collector (GC.Collect atingindo ~25.8ms).

**Código refatorado:**

```csharp
using System;
using System.Text;
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    public class StressTestScene : MonoBehaviour
    {
        public bool stressGc = false;
        public bool stressCpu = false;
        public bool stressDraw = false;
        public GameObject testPrefab;

        [Range(1, 20)]
        public float spawnRadius = 10f;

        [Range(1, 50)]
        public int cubesPerFrame = 1;

        public bool useUniqueMaterials = false;

        [Range(1, 512)]
        public int cpuScansPerFrame = 1;

        public int emptyObjects = 0;
        public int gcConcatIterations = 1000;
        public int gcArrayAllocs = 0;
        public int overdrawLayers = 0;
        public Material overdrawMaterial;

        [Range(0, 2000)]
        public int prepopulateCount = 0;

        private StringBuilder _cachedStringBuilder;
        private MaterialPropertyBlock _propertyBlock;
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            _cachedStringBuilder = new StringBuilder(4096);
            _propertyBlock = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (prepopulateCount > 0 && testPrefab != null)
            {
                for (int i = 0; i < prepopulateCount; i++)
                {
                    SpawnOne();
                }
            }

            for (int i = 0; i < emptyObjects; i++)
            {
                new GameObject("Empty_" + i);
            }

            if (overdrawLayers > 0 && overdrawMaterial != null)
            {
                BuildOverdrawLayers();
            }
        }

        private void BuildOverdrawLayers()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            for (int i = 0; i < overdrawLayers; i++)
            {
                float d = cam.nearClipPlane + 0.05f + i * 0.001f;
                float h = 2f * d * Mathf.Tan(halfFov) * 1.1f;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                quad.name = "Overdraw_" + i;
                quad.transform.SetParent(cam.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, d);
                quad.transform.localScale = new Vector3(h * cam.aspect, h, 1f);
                quad.GetComponent<Renderer>().sharedMaterial = overdrawMaterial;
            }
        }

        private void Update()
        {
            if (stressGc)
            {
                _cachedStringBuilder.Clear();
                for (int i = 0; i < gcConcatIterations; i++)
                {
                    _cachedStringBuilder.Append(i);
                }
            }

            if (stressDraw && testPrefab != null)
            {
                int n = Mathf.Max(1, cubesPerFrame);
                for (int i = 0; i < n; i++)
                {
                    SpawnOne();
                }
            }
        }

        private void SpawnOne()
        {
            var clone = Instantiate(
                testPrefab,
                UnityEngine.Random.insideUnitSphere * spawnRadius,
                Quaternion.identity);

            if (useUniqueMaterials)
            {
                var renderer = clone.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.GetPropertyBlock(_propertyBlock);
                    _propertyBlock.SetColor(BaseColorPropertyId, UnityEngine.Random.ColorHSV());
                    renderer.SetPropertyBlock(_propertyBlock);
                }
            }
        }
    }
}
```

**Explicação didática:** Em C#, toda vez que você junta textos usando '+' ou chama '.ToString()', um novo pedaço de memória gerenciada é criado e o anterior é jogado fora. Em uma cena rodando a 60 quadros por segundo, fazer isso centenas ou milhares de vezes por frame cria gigabytes de lixo virtual. O Garbage Collector precisa congelar o jogo para limpar essa sujeira, causando travamentos visíveis (engasgos). Substituímos a concatenação contínua por StringBuilder reutilizável e removemos alocações vazias e scans desnecessários no Update.

### Laudo 40 · achado 2 · tipo CPU

**Causa raiz:** Chamada repetitiva de 'GameObject.FindObjectsOfType<GameObject>()' em todos os quadros dentro de um loop de Update(). Esta operação varre toda a árvore de objetos da cena nativa e converte para instâncias gerenciadas, gerando sobrecarga massiva na CPU (até 102ms/frame) e alocando arrays que sobrecarregam o Garbage Collector.

**Código refatorado:**

```csharp
// A varredura contínua foi totalmente eliminada do Update(). Para arquiteturas funcionais que precisam de referências a múltiplos objetos, utilize registro no Awake/OnEnable através de coleções estáticas ou Singletons de serviço:
/*
public static readonly HashSet<GameObject> ActiveObjects = new HashSet<GameObject>();
private void OnEnable() => ActiveObjects.Add(gameObject);
private void OnDisable() => ActiveObjects.Remove(gameObject);
*/
```

**Explicação didática:** Métodos de busca como 'FindObjectsOfType' vasculham todos os objetos existentes na memória da fase inteira, um por um. Fazer isso a cada frame é equivalente a ler uma enciclopédia inteira toda vez que você pisca os olhos. A melhor abordagem é manter uma lista prévia com os objetos necessários ou registrá-los no momento em que forem criados.

**Dicas de level design:** null

---
