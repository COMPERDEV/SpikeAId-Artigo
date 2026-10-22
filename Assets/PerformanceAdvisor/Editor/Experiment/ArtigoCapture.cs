using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PerformanceAdvisor.StressTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PerformanceAdvisor.Artigo
{
    /// <summary>
    /// Captures the article's test cases without anyone pressing Play: builds each stress
    /// scene, plays it, monitors it with the plugin, stops, and freezes the payload.
    /// </summary>
    /// <remarks>
    /// MOTIVO da máquina de estados em SessionState: entrar e sair do Play Mode recarrega o
    /// domínio, e todo campo estático some. O estado (qual caso, em que fase) mora no
    /// SessionState, que sobrevive ao recarregamento, e o [InitializeOnLoad] religa o laço.
    ///
    /// MOTIVO da intensidade adaptativa: o mesmo parâmetro estoura o orçamento numa máquina
    /// e não em outra. Se a sessão não produziu nenhuma amostra do tipo planejado, o caso é
    /// refeito com a intensidade multiplicada (até 3 vezes). O parâmetro que de fato gerou o
    /// caso fica gravado nele — o caso é reprodutível, e a regra está declarada.
    /// </remarks>
    [InitializeOnLoad]
    public static class ArtigoCapture
    {
        private const string STATE_KEY = "PA_Artigo_CaptureState";
        private const int    MAX_ATTEMPTS = 4;
        private const double SETTLE_SECONDS  = 1.0;   // depois de entrar em Play, antes de monitorar
        private const double MONITOR_SECONDS = 10.0;  // inclui os 2 s de aquecimento do Detetive
        private const string CUBE_PREFAB = "Assets/PerformanceAdvisor/Runtime/StressCube.prefab";

        // ── Plano dos casos capturados (G1–G4) ──────────────────────────────

        private class Plan
        {
            public string Grupo, Array, Cena; public float Level; public float Growth; public int Index;
            public string Id => $"{Grupo}_{Index:00}";
        }

        private static List<Plan> Plans()
        {
            var l = new List<Plan>();
            void Add(string g, int index, string arr, string cena, float growth, float level) =>
                l.Add(new Plan { Grupo = g, Index = index, Array = arr, Cena = cena, Growth = growth, Level = level });

            // MOTIVO de cena e array serem coisas separadas: o caso é a SELEÇÃO de um tipo de
            // snapshot, e o mesmo tipo pode vir de cenas diferentes. Os casos G2_04 e G2_05 são
            // as amostras de GC de sessões com carga de CPU — alocação alta e coleta pequena
            // perto do pico, em que a resposta certa é "a coleta NÃO explica o pico" (seção 5.2
            // do artigo). Os três primeiros são da cena de GC, em que a coleta é grande.
            for (int i = 0; i < 5; i++) Add("G1_CPU", i + 1, "cpu_samples", "cpu", 2.0f, new float[] { 16, 24, 32, 48, 64 }[i]); // varreduras/quadro
            Add("G2_GC", 1, "gc_samples", "gc",  1.5f, 32);   // arrays de ~80 KB por quadro
            Add("G2_GC", 2, "gc_samples", "gc",  1.5f, 48);
            Add("G2_GC", 3, "gc_samples", "gc",  1.5f, 64);
            Add("G2_GC", 4, "gc_samples", "cpu", 2.0f, 32);   // varreduras/quadro (carga de CPU)
            Add("G2_GC", 5, "gc_samples", "cpu", 2.0f, 64);
            for (int i = 0; i < 5; i++) Add("G3_DRAWCALLS", i + 1, "drawcalls_samples", "draw", 1.5f, new float[] { 3000, 3500, 4500, 6000, 8000 }[i]); // cubos únicos
            for (int i = 0; i < 5; i++) Add("G4_GPU", i + 1, "gpu_samples", "gpu", 2.0f, new float[] { 32, 48, 64, 96, 128 }[i]); // iterações/pixel
            return l;
        }

        // MOTIVO: cubos com material único na cena de GPU. Ficam ABAIXO do teto de draw calls
        // (a contagem do plugin dá ~4,7 por cubo nesta máquina; 400 cubos ≈ 1.900 contra o teto
        // de 2.579), então o gargalo continua sendo só de GPU — mas a amostra chega com muitas
        // chamadas de desenho, o que convida o modelo a prescrever batching num achado de GPU.
        private const int GPU_LURE_CUBES = 400;

        [Serializable]
        private class State
        {
            public int    index;
            public int    attempt;
            public string phase;          // open, playing, monitoring, exiting, done
            public double phaseStart;
            public string prevBuildJson;
            public int    prevLanguage;
            public string prevScene;
            public bool   quitWhenDone;
            public string grupos = "";    // vazio = todos; senão "G2_GC,G4_GPU" ou ids de caso
            public bool   prevRunInBackground;
            public string log = "";
        }

        private static List<Plan> Plans(State s)
        {
            var all = Plans();
            if (s == null || string.IsNullOrEmpty(s.grupos)) return all;
            // Aceita grupo (G2_GC) ou caso (G2_GC_05).
            var keep = new HashSet<string>(s.grupos.Split(','));
            return all.FindAll(p => keep.Contains(p.Grupo) || keep.Contains(p.Id));
        }

        // ── Diagnóstico por sessão ──────────────────────────────────────────
        // MOTIVO: quando um grupo não gera amostra, "cpu=2 gpu=0" não diz por quê. Os valores
        // brutos da sessão (pior quadro de CPU e de GPU, quadros com coleta de lixo) dizem se a
        // carga não chegou ao orçamento ou se o contador não a enxerga.
        private static int   _dFrames, _dCollectFrames;
        private static float _dCpuMax, _dGpuMax, _dCollectMax;
        private static double _dCpuSum, _dGpuSum;

        private static void ResetDiag()
        {
            _dFrames = _dCollectFrames = 0; _dCpuMax = _dGpuMax = _dCollectMax = 0f; _dCpuSum = _dGpuSum = 0;
        }

        private static void OnFrame(float cpuMs, long gcBytes, int drawCalls, long memBytes, float gpuMs)
        {
            _dFrames++; _dCpuSum += cpuMs; _dGpuSum += gpuMs;
            if (cpuMs > _dCpuMax) _dCpuMax = cpuMs;
            if (gpuMs > _dGpuMax) _dGpuMax = gpuMs;
            float c = TelemetryMonitorCore.LastGcCollectMs;
            if (c > 0f) { _dCollectFrames++; if (c > _dCollectMax) _dCollectMax = c; }
        }

        private static string Diag() =>
            $"diag: {_dFrames} quadros; CPU bruta média {(_dFrames > 0 ? _dCpuSum / _dFrames : 0):0.0} ms, pior {_dCpuMax:0.0} ms; " +
            $"GPU bruta média {(_dFrames > 0 ? _dGpuSum / _dFrames : 0):0.00} ms, pior {_dGpuMax:0.00} ms (fator {HardwareBuildSettings.GpuProjectionFactor:0.00}); " +
            $"quadros com coleta {_dCollectFrames}, pior coleta {_dCollectMax:0.00} ms";

        static ArtigoCapture()
        {
            if (Load() != null) EditorApplication.update += Tick;
        }

        [MenuItem("Tools/Performance Advisor/Artigo/Capturar casos de teste", false, 300)]
        public static void CaptureFromMenu()
        {
            if (!EditorUtility.DisplayDialog("Artigo",
                    "Captura os 20 casos (G1–G4) entrando em Play sozinho, e deriva os 10 dos grupos G5 e G6.\n\n" +
                    "Leva alguns minutos. Não mexa no Editor enquanto roda.", "Capturar", "Cancelar"))
                return;
            Begin(false);
        }

        /// <summary>Command-line entry: Unity.exe -projectPath ... -executeMethod PerformanceAdvisor.Artigo.ArtigoCapture.CaptureCli</summary>
        public static void CaptureCli()
        {
            // -artigoGrupos G2_GC,G4_GPU recaptura só esses grupos (os outros casos ficam como estão).
            string[] args = Environment.GetCommandLineArgs();
            string grupos = "";
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-artigoGrupos") grupos = args[i + 1];
            Begin(true, grupos);
        }

        private static void Begin(bool quitWhenDone, string grupos = "")
        {
            if (!ArtigoPaths.IsExperimentBranch) { Debug.LogError("[Artigo] Esta branch não tem Artigo/condicao.txt."); return; }
            if (!quitWhenDone && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var s = new State
            {
                index = 0, attempt = 0, phase = "open", phaseStart = EditorApplication.timeSinceStartup,
                prevBuildJson = JsonUtility.ToJson(HardwareBuildSettings.GetActiveCopy()),
                prevLanguage = (int)Loc.Current,
                prevScene = EditorSceneManager.GetActiveScene().path,
                quitWhenDone = quitWhenDone,
                grupos = grupos ?? ""
            };

            // MOTIVO: o texto do payload (hardware_budget) é localizado. O experimento é em
            // português — os dois prompts são — então a captura fixa o idioma, e devolve o
            // do usuário no fim.
            Loc.Current = UiLanguage.Portuguese;

            // MOTIVO: com a janela do Unity sem foco, o Play Mode roda estrangulado — no
            // diagnóstico, as sessões sem foco tiveram 200–250 quadros em 10 s e o contador de
            // GPU lendo zero; as com foco, 700–1.500. A captura não pode depender de onde o
            // autor está com o mouse.
            s.prevRunInBackground = PlayerSettings.runInBackground;
            PlayerSettings.runInBackground = true;

            // Alvo fixo de PC medido no Editor: o que o artigo descreve como orçamento de
            // hardware. As peças são as que o autor tem selecionadas; ficam gravadas no caso.
            var b = HardwareBuildSettings.GetActiveCopy();
            b.Platform = TargetPlatform.PC;
            b.Test = TestMode.NoEditor;
            b.TargetFps = 60;
            HardwareBuildSettings.Apply(b);

            Directory.CreateDirectory(ArtigoPaths.CasesDir);
            Save(s);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Log(s, $"Captura iniciada — {Plans(s).Count} casos planejados" +
                   (string.IsNullOrEmpty(grupos) ? "" : $" (grupos {grupos})") + $". Alvo: {HardwareBuildSettings.Description}");
        }

        // ── Laço ────────────────────────────────────────────────────────────

        private static void Tick()
        {
            State s = Load();
            if (s == null) { EditorApplication.update -= Tick; return; }
            var plans = Plans(s);
            double now = EditorApplication.timeSinceStartup;

            try
            {
                switch (s.phase)
                {
                    case "open":
                        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
                        if (s.index >= plans.Count) { Finish(s); return; }
                        OpenScene(plans[s.index], s.attempt);
                        s.phase = "playing"; s.phaseStart = now; Save(s);
                        EditorApplication.EnterPlaymode();
                        return;

                    case "playing":
                        if (!EditorApplication.isPlaying) return;
                        if (now - s.phaseStart < SETTLE_SECONDS) return;
                        ResetDiag();
                        TelemetryMonitorCore.OnFrameSampled -= OnFrame;
                        TelemetryMonitorCore.OnFrameSampled += OnFrame;
                        TelemetryMonitorCore.StartMonitoring();
                        s.phase = "monitoring"; s.phaseStart = now; Save(s);
                        return;

                    case "monitoring":
                        if (now - s.phaseStart < MONITOR_SECONDS) return;
                        TelemetryMonitorCore.StopMonitoring();
                        TelemetryMonitorCore.OnFrameSampled -= OnFrame;
                        bool ok = SaveCase(plans[s.index], s);
                        if (ok || s.attempt + 1 >= MAX_ATTEMPTS)
                        {
                            if (!ok) Log(s, $"{plans[s.index].Id}: nenhuma amostra após {MAX_ATTEMPTS} tentativas — caso NÃO gerado.");
                            s.index++; s.attempt = 0;
                        }
                        else
                        {
                            s.attempt++;
                            Log(s, $"{plans[s.index].Id}: sem amostra do tipo planejado — nova tentativa com intensidade × {Scale(plans[s.index], s.attempt):0.##}.");
                        }
                        s.phase = "open"; s.phaseStart = now; Save(s);
                        EditorApplication.ExitPlaymode();
                        return;
                }
            }
            catch (Exception ex)
            {
                Log(s, "ERRO: " + ex);
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
                s.phase = "open"; s.index++; s.attempt = 0; Save(s);
            }
        }

        private static float Scale(Plan p, int attempt) => Mathf.Pow(p.Growth, attempt);

        private static string Params(Plan p, int attempt)
        {
            float v = p.Level * Scale(p, attempt);
            switch (p.Cena)
            {
                case "cpu":  return $"cena cpu: stressCpu, cpuScansPerFrame={Mathf.RoundToInt(v)}, emptyObjects=10000";
                case "gc":   return $"cena gc: stressGc, gcArrayAllocs={Mathf.RoundToInt(v)}, gcConcatIterations=1000";
                case "draw": return $"cena draw: prepopulateCount={Mathf.RoundToInt(v)}, useUniqueMaterials, spawnRadius=15";
                default:     return $"cena gpu: overdrawLayers=16, _Iterations={Mathf.RoundToInt(v)}, prepopulateCount={GPU_LURE_CUBES}, useUniqueMaterials";
            }
        }

        private static void OpenScene(Plan p, int attempt)
        {
            EnsureFolder(ArtigoPaths.ScenesDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var go = new GameObject("StressTest");
            var st = go.AddComponent<StressTestScene>();
            st.stressCpu = false; st.stressGc = false; st.stressDraw = false;
            float v = p.Level * Scale(p, attempt);

            switch (p.Cena)
            {
                case "cpu":
                    st.stressCpu = true; st.cpuScansPerFrame = Mathf.RoundToInt(v); st.emptyObjects = 10000;
                    break;
                case "gc":
                    st.stressGc = true; st.gcArrayAllocs = Mathf.RoundToInt(v); st.gcConcatIterations = 1000;
                    break;
                case "draw":
                    st.testPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CUBE_PREFAB);
                    st.prepopulateCount = Mathf.RoundToInt(v); st.useUniqueMaterials = true; st.spawnRadius = 15f;
                    break;
                case "gpu":
                    st.testPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CUBE_PREFAB);
                    st.prepopulateCount = GPU_LURE_CUBES; st.useUniqueMaterials = true; st.spawnRadius = 15f;
                    var shader = Shader.Find("PerformanceAdvisor/StressOverdraw");
                    if (shader == null) throw new Exception("shader PerformanceAdvisor/StressOverdraw não encontrado");
                    string matPath = $"{ArtigoPaths.ScenesDir}/{p.Id}_overdraw.mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
                    mat.SetFloat("_Iterations", Mathf.RoundToInt(v));
                    EditorUtility.SetDirty(mat); AssetDatabase.SaveAssets();
                    st.overdrawLayers = 16; st.overdrawMaterial = mat;
                    break;
            }

            EditorSceneManager.SaveScene(scene, $"{ArtigoPaths.ScenesDir}/{p.Id}.unity");
        }

        private static bool SaveCase(Plan p, State s)
        {
            FrameSnapshot[] cpu  = SnapshotHeuristicsController.CopyCpuSamples();
            FrameSnapshot[] gc   = SnapshotHeuristicsController.CopyGcSamples();
            FrameSnapshot[] draw = SnapshotHeuristicsController.CopyDrawSamples();
            FrameSnapshot[] mem  = SnapshotHeuristicsController.CopyMemSamples();
            FrameSnapshot[] gpu  = SnapshotHeuristicsController.CopyGpuSamples();
            var none = System.Array.Empty<FrameSnapshot>();

            // MOTIVO: o caso é uma SELEÇÃO de snapshots, como o dev faz na aba Snapshots:
            // só o array do gargalo plantado vai ao laudo. Os outros tipos que a cena também
            // gerou continuam contados no violations_summary, como no plugin.
            FrameSnapshot[] chosen;
            switch (p.Array)
            {
                case "cpu_samples":       chosen = cpu;  break;
                case "gc_samples":        chosen = gc;   break;
                case "drawcalls_samples": chosen = draw; break;
                default:                  chosen = gpu;  break;
            }
            Log(s, $"{p.Id} [{Params(p, s.attempt)}]: cpu={cpu.Length} gc={gc.Length} draw={draw.Length} gpu={gpu.Length} mem={mem.Length} — {Diag()}");
            if (chosen.Length == 0) return false;

            var payload = GeminiRestBridge.CapturePayload(
                p.Array == "cpu_samples" ? chosen : none,
                p.Array == "gc_samples" ? chosen : none,
                p.Array == "drawcalls_samples" ? chosen : none,
                none,
                p.Array == "gpu_samples" ? chosen : none);

            var c = new ArtigoCase
            {
                id = p.Id, grupo = p.Grupo, origem = "captura",
                cena = $"{ArtigoPaths.ScenesDir}/{p.Id}.unity",
                parametros = Params(p, s.attempt),
                criadoEm = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                descricao = payload.Description, orcamentoMs = payload.CpuBudgetMs,
                tetoDrawCalls = payload.DrawCallBudget, tetoMemoriaBytes = payload.MemoryBudgetBytes,
                fatorCpu = payload.CpuFactor, fatorGpu = payload.GpuFactor,
                medidoEm = payload.MeasuredOn ?? "", contadorDraw = payload.DrawCounter, contadorMemoria = payload.MemoryCounter,
                memResolvido = payload.MemResolved, gpuResolvido = payload.GpuResolved,
                vCpu = payload.Summary.cpu, vGc = payload.Summary.gc, vDraw = payload.Summary.draw,
                vGpu = payload.Summary.gpu, vMem = payload.Summary.mem,
                vCpuMaxMs = payload.Summary.cpuMaxMs, vGcMaxBytes = payload.Summary.gcMaxBytes,
                vDrawMax = payload.Summary.drawMax, vGpuMaxMs = payload.Summary.gpuMaxMs, vMemMaxBytes = payload.Summary.memMaxBytes
            };
            foreach (var f in chosen) c.amostras.Add(ArtigoCase.FromSnapshot(p.Array, f));
            c.fontes = new List<ScriptSource>(payload.Sources ?? System.Array.Empty<ScriptSource>());
            c.esperadoAchados = c.amostras.Count;
            c.esperadoIndeterminacao = false;
            c.esperadoSemEstouro = !c.AnyMetricOverBudget();
            c.RebuildUserText();
            c.Save();
            return true;
        }

        // ── Casos derivados (G5 e G6) ───────────────────────────────────────

        /// <summary>
        /// Builds G5 (missing telemetry) and G6 (misleading telemetry) from captured cases.
        /// </summary>
        /// <remarks>
        /// MOTIVO: o plugin só captura quadro que ESTOURA o orçamento, então "telemetria
        /// faltante" e "enganosa" não saem de uma sessão — saem de uma transformação
        /// declarada sobre casos capturados (decisão do autor, 09/10/2026):
        ///  - o orçamento de quadro sobe até caber o pior tempo da seleção com 25% de folga
        ///    (alvo em FPS inteiro, como o plugin aceita), e o teto de draw calls idem;
        ///  - o resumo de violações zera, coerente com o novo orçamento;
        ///  - G5 remove a pilha de chamadas; G6 a mantém.
        /// G5_k e G6_k saem do MESMO caso-base, então a única diferença entre eles é a pilha
        /// — que é exatamente o contraste que o artigo quer medir (ausente × enganosa).
        /// </remarks>
        public static void DeriveCases()
        {
            var all = ArtigoCase.LoadAll();
            var bases = new List<ArtigoCase>();
            bool HasStack(ArtigoCase c) => c.amostras.Exists(a => !string.IsNullOrEmpty(a.culpritStack));
            // MOTIVO: G6 precisa de pilha para enganar, então os casos-base vêm de CPU e GC.
            // Ordem fixa e declarada, misturando os dois tipos e evitando os casos de pior
            // quadro muito longo: o orçamento derivado (pior tempo + 25%) de um quadro de 300 ms
            // daria "alvo 3 FPS", um alvo que nenhum projeto declara. O que faltar é completado
            // com qualquer caso capturado que tenha pilha.
            foreach (string id in new[] { "G1_CPU_01", "G2_GC_01", "G2_GC_02", "G2_GC_03", "G2_GC_04" })
            {
                var c = all.Find(x => x.id == id);
                if (c != null && HasStack(c)) bases.Add(c);
            }
            foreach (var c in all)
                if (bases.Count < 5 && !bases.Contains(c) && c.origem == "captura" && HasStack(c)) bases.Add(c);

            int k = 0;
            foreach (var b in bases)
            {
                k++;
                Derive(b, $"G5_FALTANTE_{k:00}", "G5_FALTANTE", keepStack: false).Save();
                Derive(b, $"G6_ENGANOSA_{k:00}", "G6_ENGANOSA", keepStack: true).Save();
            }
            Debug.Log($"[Artigo] {k * 2} casos derivados (G5 e G6) a partir de {k} casos-base.");
        }

        /// <summary>Command-line entry: rebuilds only G5 and G6 from the captured cases (no Play, batch mode).</summary>
        public static void DeriveCli()
        {
            try { DeriveCases(); EditorApplication.Exit(0); }
            catch (Exception ex) { Debug.LogError("[Artigo] " + ex); EditorApplication.Exit(1); }
        }

        /// <summary>
        /// Command-line entry: attaches the source of the scripts named in each case's stacks,
        /// keeping the captured telemetry as is (batch mode).
        /// </summary>
        public static void AttachSourcesCli()
        {
            try
            {
                int n = 0;
                foreach (var c in ArtigoCase.LoadAll())
                {
                    c.AttachSources();
                    c.Save();
                    if (c.fontes.Count > 0) n++;
                    Debug.Log($"[Artigo] {c.id}: {c.fontes.Count} script(s) anexado(s) " +
                              $"{string.Join(", ", c.fontes.ConvertAll(f => f.file))}");
                }
                Debug.Log($"[Artigo] Código anexado a {n} casos.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogError("[Artigo] " + ex); EditorApplication.Exit(1); }
        }

        private static ArtigoCase Derive(ArtigoCase b, string id, string grupo, bool keepStack)
        {
            var c = JsonUtility.FromJson<ArtigoCase>(JsonUtility.ToJson(b));
            c.id = id; c.grupo = grupo; c.origem = "derivado de " + b.id;
            c.criadoEm = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            float maxMs = 0f; int maxDraw = 0; long maxMem = 0;
            foreach (var s in c.amostras)
            {
                maxMs = Mathf.Max(maxMs, c.SentCpuMs(s));
                float g = c.SentGpuMs(s); if (!float.IsNaN(g)) maxMs = Mathf.Max(maxMs, g);
                maxDraw = Mathf.Max(maxDraw, s.drawCalls);
                maxMem = Math.Max(maxMem, s.memBytes);
            }
            int fps = Mathf.Max(1, Mathf.FloorToInt(1000f / (maxMs * 1.25f)));
            c.orcamentoMs = 1000f / fps;
            c.descricao = Regex.Replace(c.descricao ?? "", @"alvo \d+ FPS", $"alvo {fps} FPS");
            if (c.tetoDrawCalls > 0 && maxDraw >= c.tetoDrawCalls) c.tetoDrawCalls = Mathf.CeilToInt(maxDraw * 1.25f);
            if (c.tetoMemoriaBytes > 0 && maxMem >= c.tetoMemoriaBytes) c.tetoMemoriaBytes = (long)(maxMem * 1.25);

            c.vCpu = c.vGc = c.vDraw = c.vGpu = c.vMem = 0;
            c.vCpuMaxMs = c.vGpuMaxMs = 0f; c.vGcMaxBytes = c.vMemMaxBytes = 0; c.vDrawMax = 0;

            // Sem pilha não há o que anexar: o plugin só manda o código dos scripts da pilha.
            if (!keepStack) { foreach (var s in c.amostras) s.culpritStack = ""; c.fontes.Clear(); }

            c.esperadoAchados = c.amostras.Count;
            c.esperadoSemEstouro = true;
            c.esperadoIndeterminacao = !keepStack;
            if (c.AnyMetricOverBudget()) throw new Exception($"{id}: a derivação deixou métrica acima do teto");
            c.RebuildUserText();
            return c;
        }

        // ── Fim ─────────────────────────────────────────────────────────────

        private static void Finish(State s)
        {
            try { DeriveCases(); } catch (Exception ex) { Log(s, "ERRO na derivação: " + ex); }

            HardwareBuildSettings.Apply(JsonUtility.FromJson<HardwareBuild>(s.prevBuildJson));
            Loc.Current = (UiLanguage)s.prevLanguage;
            PlayerSettings.runInBackground = s.prevRunInBackground;
            Log(s, "Captura concluída. Casos em Artigo/casos/.");
            // Acumula: cada rodada de captura (completa ou só de alguns grupos) fica registrada.
            File.AppendAllText(Path.Combine(ArtigoPaths.CasesDir, "_captura_log.txt"), s.log + "\n", new UTF8Encoding(false));

            SessionState.EraseString(STATE_KEY);
            EditorApplication.update -= Tick;
            if (!string.IsNullOrEmpty(s.prevScene) && File.Exists(s.prevScene))
                EditorSceneManager.OpenScene(s.prevScene);
            if (s.quitWhenDone) EditorApplication.Exit(0);
        }

        // ── Utilidades ──────────────────────────────────────────────────────

        private static State Load()
        {
            string j = SessionState.GetString(STATE_KEY, "");
            return string.IsNullOrEmpty(j) ? null : JsonUtility.FromJson<State>(j);
        }

        private static void Save(State s) => SessionState.SetString(STATE_KEY, JsonUtility.ToJson(s));

        private static void Log(State s, string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            s.log += line + "\n";
            Save(s);
            Debug.Log("[Artigo] " + msg);
        }

        private static void EnsureFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
