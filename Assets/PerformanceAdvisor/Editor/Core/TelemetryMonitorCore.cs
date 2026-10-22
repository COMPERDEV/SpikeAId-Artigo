using System;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace PerformanceAdvisor
{
    /// <summary>
    /// "Porteiro" — drives the native ProfilerRecorders during Play Mode and
    /// emits a per-frame sample event. Runs entirely in the Editor and tears
    /// itself down on Stop to avoid leaking the underlying counters.
    /// </summary>
    /// <remarks>
    /// Hot-path rules (see "Proibições absolutas" in the spec):
    ///  - No allocations inside <see cref="SampleFrame"/>.
    ///  - No <c>Debug.Log</c> on the per-frame path.
    ///  - All recorders are pre-allocated in <see cref="StartMonitoring"/>.
    ///
    /// Also reads GPU Frame Time — the only counter that measures the graphics card
    /// rather than the CPU.
    ///
    /// Known limitation: every counter below describes the *Editor process*, not
    /// the build. Comparing it against a device budget from the catalog is only
    /// an approximation until the remote-profiler work lands.
    /// </remarks>
    [InitializeOnLoad]
    public static class TelemetryMonitorCore
    {
        // MOTIVO: nanos → ms factor. Pre-computed const so the conversion
        // inside SampleFrame is a single multiply with no boxing.
        private const float NS_TO_MS = 1e-6f;

        // MOTIVO: medido com ProfilerCounterProbe em Unity 6.4 + URP + DX12 — os nomes
        // "Draw Calls Count", "Batches Count" e "Total Batches Count" que versões antigas
        // expunham **não existem mais**. A Unity 6 fatiou draw calls por CAMINHO DE
        // BATCHING, e cada caminho tem seu próprio contador. Num projeto URP com SRP
        // Batcher praticamente tudo cai em "SRP Batcher Draw Calls Count" e todos os
        // outros ficam zerados — foi por isso que o indicador marcava 0 e nunca estourava.
        //
        // Portanto NÃO se escolhe um contador: soma-se todos os caminhos, que é o que
        // a janela Stats da Unity mostra como "Draw Calls".
        private static readonly string[] DRAW_CALL_COUNTERS =
        {
            "Standard Draw Calls Count",
            "Standard Indirect Draw Calls Count",
            "Standard Instanced Draw Calls Count",
            "SRP Batcher Draw Calls Count",
            "BRG Draw Calls Count",
            "BRG Indirect Draw Calls Count",
            "Dynamic Batched Draw Calls Count",
            "Static Batched Draw Calls Count",
            "Instanced Batched Draw Calls Count",
            "Null Geometry Draw Calls Count",
            "Null Geometry Indirect Draw Calls Count"
        };

        private const string CPU_COUNTER     = "Main Thread";
        private const string GC_COUNTER      = "GC Allocated In Frame";
        private const string MEMORY_COUNTER  = "System Used Memory";

        // MOTIVO: o unico contador que mede o trabalho da PLACA. Existia, estava populado
        // e era ignorado — medido na StressTestAllIn deu pico de 28,65 ms num orcamento de
        // 16,7 ms, e nenhum dos cinco indicadores podia enxergar isso.
        private const string GPU_COUNTER = "GPU Frame Time";

        // MOTIVO: marcadores da COLETA de lixo, confirmados na lista de marcadores desta
        // Unity (categoria GC). O incremental é o padrão no Unity 6: a coleta vem em
        // fatias pequenas espalhadas pelos frames, e ler só GC.Collect perderia quase
        // todo o custo.
        private const string GC_COLLECT_MARKER     = "GC.Collect";
        private const string GC_INCREMENTAL_MARKER = "GarbageCollector.CollectIncremental";

        /// <summary>Time garbage collection cost in the last sampled frame, in ms.</summary>
        /// <remarks>
        /// Preenchido antes de OnFrameSampled disparar, para não mudar a assinatura do
        /// evento (três assinantes). Leitura de campo — segura no hot-path (RN-01).
        /// </remarks>
        public static float LastGcCollectMs { get; private set; }

        /// <summary>When the last sampled frame happened, in seconds since the session began.</summary>
        /// <remarks>
        /// MOTIVO: o aquecimento era contado em FRAMES (2 × FPS alvo), e o nome dizia
        /// "2 segundos". Só coincidem se o jogo atinge o alvo. Numa cena pesada a 10 FPS,
        /// 120 frames são 12 s descartados — e cena pesada é justamente a que precisa de
        /// diagnóstico. Com o relógio do próprio frame, 2 s são 2 s.
        ///
        /// Remoto, o relógio é o do PLAYER (início do frame na linha do tempo dele), não
        /// o do Editor: os frames chegam em lote, e o horário de chegada não diz quando
        /// eles aconteceram.
        /// </remarks>
        public static double LastFrameSeconds { get; private set; }

        /// <summary>Time the main thread spent waiting (frame cap, vsync) in the last remote frame, in ms.</summary>
        public static float LastWaitMs { get; private set; }


        // MOTIVO: "System Used Memory" é a pegada do processo vista pelo SO — a
        // grandeza que decide se o sistema mata o app por falta de memória, e a
        // única comparável ao RamGb do catálogo. O antigo "Total Reserved Memory"
        // media o que a Unity reservou do SO (pools livres inclusos), o que fazia
        // o semáforo estourar continuamente em aparelhos que nunca travavam.

        private static ProfilerRecorder _cpuRecorder;
        private static ProfilerRecorder _gcRecorder;
        private static ProfilerRecorder _memoryRecorder;
        private static ProfilerRecorder _gpuRecorder;
        private static ProfilerRecorder _gcCollectRecorder;
        private static ProfilerRecorder _gcIncrementalRecorder;


        private static readonly ProfilerRecorder[] _drawRecorders =
            new ProfilerRecorder[DRAW_CALL_COUNTERS.Length];


        private static bool _isMonitoring;
        private static int _frameIndex;

        /// <summary>Fired right after the recorders are started.</summary>
        public static event Action OnMonitoringStarted;

        /// <summary>Fired right after the recorders are disposed.</summary>
        public static event Action OnMonitoringStopped;

        /// <summary>
        /// Per-frame sample event. Args: cpuMs, gcBytes, drawCalls, memoryBytes, gpuMs.
        /// Subscribers MUST stay allocation-free.
        /// </summary>
        public static event Action<float, long, int, long, float> OnFrameSampled;

        /// <summary>Read-only frame counter since the last <c>StartMonitoring</c>.</summary>
        public static int FrameIndex => _frameIndex;

        /// <summary>True while the recorders are active.</summary>
        public static bool IsMonitoring => _isMonitoring;

        /// <summary>
        /// Describes where the draw-call reading comes from, so the UI never shows a
        /// number whose meaning the user cannot check.
        /// </summary>
        /// <summary>How many draw-call counters the plugin knows about.</summary>
        public static int DrawCallCounterCount => DRAW_CALL_COUNTERS.Length;

        public static string DrawCallsCounterName =>
            $"soma de {DRAW_CALL_COUNTERS.Length} contadores de draw call";

        /// <summary>
        /// Counter contributing the most draw calls right now, e.g. "SRP Batcher".
        /// Diagnostic on its own: it names the batching path the project actually uses.
        /// </summary>
        /// <remarks>Called from OnGUI, never from the per-frame path — allocates.</remarks>
        public static string DominantDrawCallSource
        {
            get
            {
                if (!_isMonitoring) return "—";

                // MOTIVO: no modo remoto os ProfilerRecorder locais NUNCA são criados — a
                // leitura vem do RawFrameDataView. Ler LastValue deles lançava
                // "ProfilerRecorder object is not initialized" a cada repintura do
                // Monitor; o console enchia de exceções durante o primeiro teste que
                // funcionou no aparelho. Em remoto a fonte é o array preenchido na
                // amostragem; em local, só recorders válidos.
                long best = 0;
                int bestIdx = -1;
                for (int i = 0; i < DRAW_CALL_COUNTERS.Length; i++)
                {
                    long v;
                    if (IsRemote)                     v = _remoteDrawValues[i];
                    else if (_drawRecorders[i].Valid) v = _drawRecorders[i].LastValue;
                    else                              continue;

                    if (v > best) { best = v; bestIdx = i; }
                }
                if (bestIdx < 0) return "nenhum caminho ativo";
                // Corta o sufixo " Draw Calls Count" pra sobrar só o nome do caminho.
                string n = DRAW_CALL_COUNTERS[bestIdx];
                int cut = n.IndexOf(" Draw Calls Count", System.StringComparison.Ordinal);
                return cut > 0 ? n.Substring(0, cut) : n;
            }
        }


        /// <summary>Name of the Profiler counter feeding the memory reading.</summary>
        public static string MemoryCounterName => MEMORY_COUNTER;

        // [InitializeOnLoad] requires this static ctor — runs once per domain reload.
        static TelemetryMonitorCore()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // MOTIVO: não inicia mais automaticamente no EnteredPlayMode — o dev
            // dispara via botão "Monitorar" no Dashboard (feedback do orientador).
            // Mantemos o StopMonitoring no ExitingPlayMode como safety net: se o
            // dev sair do Play com recorders ativos, libera tudo.
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                StopMonitoring();
            }
        }
        /// <summary>
        /// Starts sampling and emitting <see cref="OnFrameSampled"/>, reading either this
        /// process or a connected player.
        /// </summary>
        /// <remarks>
        /// MOTIVO da escolha de fonte: a build pode declarar que mede "na build", e aí o
        /// processo que interessa não é este. <c>ProfilerRecorder</c> é local por design
        /// e devolveria os números do Editor — foi exatamente o que aconteceu no teste,
        /// com a build standalone rodando, conectada, e o plugin mostrando 0 em tudo.
        ///
        /// A decisão não é só do modo declarado: exige também que exista player
        /// conectado. Declarar "na build" sem build rodando não deve virar leitura
        /// remota de nada — cai no local, e o Monitor avisa.
        /// </remarks>
        public static void StartMonitoring()
        {
            if (_isMonitoring) return;

            IsRemote = HardwareBuildSettings.ActiveTestMode == TestMode.NaBuild
                       && HasConnectedPlayer();
            SessionTargetName = IsRemote ? RemoteTargetName : "";
            _sessionStart = EditorApplication.timeSinceStartup;

            _frameIndex = 0;
            // MOTIVO: a leitura remota agora processa TODOS os frames desde o último lido.
            // Começar de -1 faria a primeira passada varrer o histórico inteiro do
            // Profiler — frames de antes do botão, inclusive da abertura de outra sessão.
            _lastRemoteFrame = ProfilerDriver.lastFrameIndex;
            _remoteSessionStartNs = 0;
            _remoteFramesLost = 0;
            LastFrameSeconds = 0;
            LastWaitMs = 0f;
            _isMonitoring = true;

            // MOTIVO: o fator de GPU depende da resolução em que a medição acontece, e ela
            // pode ter mudado desde que o fator foi calculado (Game view redimensionada,
            // modo de teste trocado). Recalcular aqui fixa o fator da sessão no estado real.
            HardwareBuildSettings.RefreshForSession();

            if (IsRemote)
            {
                // MOTIVO: sem ProfilerDriver.enabled não chega frame nenhum do player, e
                // a tela ficaria idêntica à de aparelho desconectado.
                ProfilerStackResolver.EnableForSession();
                EditorApplication.update += SampleFrameRemote;
            }
            else
            {
                // MOTIVO: capacity=1 — the moving-window logic lives in the Detetive,
                // here we only need LastValue to keep memory at minimum.
                _cpuRecorder     = ProfilerRecorder.StartNew(ProfilerCategory.Internal, CPU_COUNTER, 1);
                _gcRecorder      = ProfilerRecorder.StartNew(ProfilerCategory.Memory,   GC_COUNTER, 1);
                _memoryRecorder  = ProfilerRecorder.StartNew(ProfilerCategory.Memory,   MEMORY_COUNTER, 1);
                _gpuRecorder     = ProfilerRecorder.StartNew(ProfilerCategory.Render,   GPU_COUNTER, 1);
                _gcCollectRecorder     = ProfilerRecorder.StartNew(new ProfilerCategory("GC"), GC_COLLECT_MARKER, 1);
                _gcIncrementalRecorder = ProfilerRecorder.StartNew(new ProfilerCategory("GC"), GC_INCREMENTAL_MARKER, 1);

                for (int i = 0; i < DRAW_CALL_COUNTERS.Length; i++)
                {
                    _drawRecorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Render, DRAW_CALL_COUNTERS[i], 1);
                }

                EditorApplication.update += SampleFrame;

                // Liga o Profiler interno do Unity para que o ProfilerStackResolver
                // tenha frames pra ler. Restaura o estado anterior no Stop.
                ProfilerStackResolver.EnableForSession();
            }

            OnMonitoringStarted?.Invoke();
        }

        // Per-frame hot path. Must remain allocation-free.
        private static void SampleFrame()
        {
            if (!_isMonitoring) return;

            float cpuMs    = _cpuRecorder.LastValue * NS_TO_MS;
            long  gcBytes  = _gcRecorder.LastValue;
            long  memBytes = _memoryRecorder.LastValue;
            float gpuMs    = _gpuRecorder.LastValue * NS_TO_MS;
            // Coleta de lixo: soma completa + incremental. Recorder inválido conta zero.
            long collectNs = (_gcCollectRecorder.Valid ? _gcCollectRecorder.LastValue : 0L)
                           + (_gcIncrementalRecorder.Valid ? _gcIncrementalRecorder.LastValue : 0L);
            LastGcCollectMs = collectNs * NS_TO_MS;

            // Soma de todos os caminhos de batching. Loop sobre array fixo — zero alloc.
            long drawSum = 0;
            for (int i = 0; i < _drawRecorders.Length; i++)
                drawSum += _drawRecorders[i].LastValue;
            int draws = (int)drawSum;

            LastFrameSeconds = EditorApplication.timeSinceStartup - _sessionStart;
            _frameIndex++;
            OnFrameSampled?.Invoke(cpuMs, gcBytes, draws, memBytes, gpuMs);
        }

        // ── Leitura remota (modo "Na build") ──────────────────────────────
        // MOTIVO: ProfilerRecorder é LOCAL por design — enxerga só o processo que o
        // chama, que no Editor é o Editor. Medido na prática: com a build standalone
        // rodando e o Profiler da Unity conectado a ela ("Profiler connected to
        // DESKTOP-… - FlightLog"), o plugin mostrava 0 em tudo. A build estava ali,
        // conectada, e o Porteiro olhava para o processo errado.
        //
        // ProfilerDriver segura a conexão (local ou remota) e RawFrameDataView lê os
        // frames que chegaram por ela. Os contadores viajam no mesmo fluxo dos markers,
        // que é como a janela Profiler mostra Draw Calls de um player conectado.
        //
        // RN-01 (zero alocação no caminho por frame) NÃO se aplica com o mesmo peso
        // aqui, e a razão importa: a regra existe pra que o custo do Porteiro não
        // contamine o que ele está medindo. Medindo remoto, o processo medido é o
        // PLAYER e este custo cai no Editor — ele não entra na conta. Abrir um
        // RawFrameDataView por tick é mais caro que ler LastValue, e é aceitável
        // justamente por isso.
        private static int _lastRemoteFrame = -1;

        /// <summary>True when the current or most recent session read a connected player.</summary>
        public static bool IsRemote { get; private set; }

        /// <summary>Name of the player the current or most recent session measured.</summary>
        /// <remarks>
        /// MOTIVO: o rótulo "Medido em X" lia o nome da conexão ATUAL. Quando a build
        /// desconecta, o Profiler volta para "Editor", e a tela passou a dizer "Medido em
        /// Editor — os números são do processo do jogo, sem Editor no meio": a frase se
        /// contradiz, e o nome estava errado sobre dados que estavam certos. O nome é
        /// capturado quando a sessão começa, junto com IsRemote, e descreve os DADOS — não
        /// a conexão de agora. Para a conexão de agora continua existindo RemoteTargetName.
        /// </remarks>
        public static string SessionTargetName { get; private set; } = "";

        private static double _sessionStart;

        /// <summary>How long the most recent monitoring session lasted, in seconds.</summary>
        /// <remarks>
        /// MOTIVO: o teto de memória automático nasce do pico de UMA sessão, e uma sessão
        /// de 17 s (caso real no tablet) pode não chegar ao consumo que o jogo atinge em
        /// uso normal. Fixar uma duração mínima seria inventar um número; mostrar quanto a
        /// sessão de origem durou deixa o dev julgar se ela representa o jogo.
        /// </remarks>
        public static double LastSessionSeconds { get; private set; }

        /// <summary>Name of the connected profiler target, or empty when it is the Editor.</summary>
        public static string RemoteTargetName
        {
            get
            {
                string id = ProfilerDriver.GetConnectionIdentifier(ProfilerDriver.connectedProfiler);
                return string.IsNullOrEmpty(id) ? "" : id;
            }
        }

        /// <summary>
        /// Is the Profiler currently attached to something other than this Editor?
        /// </summary>
        /// <remarks>
        /// MOTIVO: é o que permite o botão "Monitorar" funcionar FORA do Play Mode. No
        /// modo build o Editor não está em Play — quem está rodando é a build. Exigir
        /// Play ali travava justamente o caso que a etapa 2 existe para atender.
        /// </remarks>
        public static bool HasConnectedPlayer() => IsPlayerIdentifier(RemoteTargetName);

        /// <summary>True when a Profiler connection name refers to a built player.</summary>
        /// <remarks>
        /// MOTIVO: a primeira versão testava `id.Contains("Play") == false` para descartar
        /// o alvo "Play Mode". Era um bug grave e silencioso: o identificador de um player
        /// standalone é "WindowsPlayer(MAQUINA)", e o de um aparelho é "AndroidPlayer(...)"
        /// — **"Player" contém "Play"**. O teste rejeitava exatamente aquilo que deveria
        /// aceitar, em todas as plataformas.
        ///
        /// Foi por isso que a conexão automática nunca funcionou e o botão manual dizia
        /// "nenhuma build encontrada" com o Profiler visivelmente conectado ao player e
        /// recebendo frames.
        ///
        /// Agora a lista de alvos LOCAIS é explícita e comparada por igualdade. Qualquer
        /// outra coisa é player externo. Inverter a regra — procurar "Player" no nome —
        /// seria trocar um palpite por outro: nem todo alvo remoto se chama assim, e um
        /// nome novo voltaria a cair fora sem ninguém perceber. É mais seguro enumerar os
        /// dois casos locais, que são conhecidos e estáveis, e aceitar o resto.
        /// </remarks>
        public static bool IsPlayerIdentifier(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;

            string t = id.Trim();
            for (int i = 0; i < LOCAL_TARGETS.Length; i++)
                if (t.Equals(LOCAL_TARGETS[i], StringComparison.OrdinalIgnoreCase)) return false;

            return true;
        }

        // Como o Editor se identifica no dropdown de alvo do Profiler.
        private static readonly string[] LOCAL_TARGETS = { "Editor", "Play Mode", "EditorPlayMode" };

        // ── Diagnóstico da leitura remota ─────────────────────────────────
        // MOTIVO: no caminho remoto o plugin NÃO controla quais contadores existem —
        // eles mudam por plataforma e pipeline. Se nenhum resolver, somar zeros produz
        // "0 draw calls", que se lê como "a cena não desenha nada" quando significa
        // "não consegui ler" — e o aparelho não está aqui para conferir a olho.
        // Por isso a contagem de contadores RESOLVIDOS é rastreada e exposta: a UI mostra
        // "—" em vez de 0 quando a leitura falhou, e o dev sabe que precisa rodar a sonda.
        private static int  _remoteDrawResolved;
        private static bool _remoteCpuFromRoot;

        // MOTIVO: "reconhecido ou não" existia só para draw call. GPU, GC e memória, se
        // o aparelho não expusesse o contador, viravam ZERO silencioso — e zero de GPU
        // passa por veredito verde, zero de GC se lê como "não aloca nada". No primeiro
        // teste que funcionou no aparelho, o GC apareceu como 0 KB/frame e não havia
        // como saber pela tela se era real ou contador ausente. O mesmo cuidado que já
        // salvou a CPU (o contador "Main Thread" não existe no Android) vale para todos.
        //
        // Começam true: antes da primeira amostra remota não há o que declarar ausente.
        private static bool _remoteGpuResolved = true;
        private static bool _remoteGcResolved  = true;
        private static bool _remoteMemResolved = true;

        // Valor de cada caminho de batching no último frame remoto — é a fonte de
        // DominantDrawCallSource em modo remoto, onde não existem recorders locais.
        private static readonly long[] _remoteDrawValues = new long[DRAW_CALL_COUNTERS.Length];

        /// <summary>How many draw-call counters the connected player actually exposes.</summary>
        public static int RemoteDrawCountersResolved => _remoteDrawResolved;

        /// <summary>True when main-thread time came from the frame root instead of a counter.</summary>
        public static bool RemoteCpuFromRoot => _remoteCpuFromRoot;

        /// <summary>False when a remote session could not find the GPU time counter.</summary>
        public static bool RemoteGpuResolved => !IsRemote || _remoteGpuResolved;

        /// <summary>False when a remote session could not find the GC allocation counter.</summary>
        public static bool RemoteGcResolved => !IsRemote || _remoteGcResolved;

        /// <summary>False when a remote session could not find the memory counter.</summary>
        public static bool RemoteMemResolved => !IsRemote || _remoteMemResolved;

        private static void SampleFrameRemote()
        {
            if (!_isMonitoring) return;

            // MOTIVO: fechar a build não avisa ninguém. Sem isto o monitoramento seguiria
            // "ativo" lendo frame nenhum, e a tela ficaria congelada nos últimos valores
            // como se a medição continuasse — pior que parar, porque parece que está indo.
            if (!HasConnectedPlayer())
            {
                UnityEngine.Debug.Log("[Performance Advisor] " +
                    Loc.T("A build desconectou — monitoramento encerrado.", "The build disconnected — monitoring stopped.",
                          "La build se desconectó: monitoreo finalizado."));
                StopMonitoring();
                return;
            }

            int last = ProfilerDriver.lastFrameIndex;
            // MOTIVO: sem o guarda de frame repetido, o mesmo frame do player seria
            // emitido várias vezes entre chegadas — o Detetive contaria violações que
            // aconteceram uma vez como se fossem muitas.
            if (last < 0 || last <= _lastRemoteFrame) return;

            // MOTIVO: antes só o ÚLTIMO frame de cada tick era lido, e os que chegaram entre
            // dois ticks eram descartados. O tick do Editor não acompanha o player: com a
            // build de PC em primeiro plano, o Editor fica em segundo plano e é atualizado
            // bem menos vezes por segundo. O plugin via uma fração dos frames — e a fração
            // perdida é justamente onde mora a travada de um frame só, que é o que o laudo
            // existe para achar. A contagem de violações enviada à IA saía menor que a real.
            //
            // Agora todos os frames desde o último lido são processados, em ordem.
            int first = ProfilerDriver.firstFrameIndex;
            int from = _lastRemoteFrame + 1;
            if (from < first)
            {
                // O histórico do Profiler já descartou frames que o plugin não leu (Editor
                // travado por mais tempo que o histórico cobre). Eles se perderam: contar,
                // não esconder.
                _remoteFramesLost += first - from;
                from = first;
            }

            // MOTIVO do teto por tick: depois de um travamento longo do Editor, o atraso
            // pode ser de centenas de frames. Processar tudo de uma vez travaria o Editor de
            // novo; o que não couber neste tick fica para o próximo, sem ser descartado.
            int to = Math.Min(last, from + MAX_REMOTE_FRAMES_PER_TICK - 1);
            for (int frame = from; frame <= to && _isMonitoring; frame++)
            {
                _lastRemoteFrame = frame;
                ProcessRemoteFrame(frame);
            }
        }

        private const int MAX_REMOTE_FRAMES_PER_TICK = 240;

        // Início (relógio do player) do primeiro frame da sessão; 0 = ainda não visto.
        private static ulong _remoteSessionStartNs;
        private static int   _remoteFramesLost;

        /// <summary>Frames the Profiler discarded before the plugin could read them.</summary>
        public static int RemoteFramesLost => _remoteFramesLost;

        // MOTIVO: marcadores de ESPERA da main thread. Sem contador "Main Thread" (caso do
        // Android), o tempo de CPU sai da duração da raiz do frame — e a raiz inclui o
        // tempo parado esperando o limite de FPS e a apresentação na tela. Num celular, o
        // Unity limita o jogo a 30 FPS por padrão: o frame dura 33,3 ms mesmo que o
        // trabalho de CPU seja de 5 ms. Sem descontar, o plugin mediria o limitador e
        // daria vermelho numa cena leve. O valor validado no tablet foi exatamente
        // 33,3 ms, o que é suspeito justamente por isso.
        //
        // Os nomes são os marcadores de espera documentados pela Unity ("Common Profiler
        // markers"). Gfx.WaitForPresentOnGfxThread é a main thread esperando a render
        // thread/GPU: tempo de GPU, que já tem métrica própria.
        private static readonly string[] WAIT_MARKERS =
        {
            "WaitForTargetFPS",
            "Gfx.WaitForPresentOnGfxThread",
            "Gfx.WaitForRenderThread",
            "Gfx.WaitForPresent",
        };
        private static readonly int[] _waitIds = new int[WAIT_MARKERS.Length];

        private static void ProcessRemoteFrame(int frame)
        {
            // threadIndex 0 = main thread, do PLAYER.
            using (RawFrameDataView view = ProfilerDriver.GetRawFrameDataView(frame, 0))
            {
                if (!view.valid) return;

                if (_remoteSessionStartNs == 0) _remoteSessionStartNs = view.frameStartTimeNs;
                LastFrameSeconds = (view.frameStartTimeNs - _remoteSessionStartNs) / 1e9;

                // ── CPU ──────────────────────────────────────────────────
                // MOTIVO: "Main Thread" é lido localmente por ProfilerRecorder na
                // categoria Internal, mas no fluxo de frames ele nem sempre viaja como
                // CONTADOR — em várias plataformas o tempo da main thread é a duração da
                // amostra RAIZ da thread 0 (o PlayerLoop). Procurar só o contador
                // devolveria 0 em silêncio, e 0 ms de CPU passaria por veredito verde.
                //
                // Tenta o contador; se ele não existir, usa a raiz do frame, que é a
                // mesma grandeza medida de outro jeito. Qual dos dois foi usado fica
                // exposto, porque muda o que o número significa.
                long cpuNs = ReadCounter(view, CPU_COUNTER, out bool cpuOk);
                _remoteCpuFromRoot = !cpuOk;
                LastWaitMs = 0f;
                if (!cpuOk && view.sampleCount > 0)
                {
                    cpuNs = (long)view.GetSampleTimeNs(0);
                    // Desconta as esperas que estão DENTRO da raiz — só elas fazem parte da
                    // duração lida. Uma espera fora do intervalo da raiz não estava somada.
                    long waitNs = WaitInsideRootNs(view);
                    LastWaitMs = waitNs * NS_TO_MS;
                    cpuNs = Math.Max(0L, cpuNs - waitNs);
                }

                long gcBytes  = ReadCounter(view, GC_COUNTER,     out _remoteGcResolved);
                long memBytes = ReadCounter(view, MEMORY_COUNTER, out _remoteMemResolved);
                long gpuNs    = ReadCounter(view, GPU_COUNTER,    out _remoteGpuResolved);

                // MOTIVO: coleta de lixo não é contador, é MARCADOR — aparece como amostra
                // com duração na thread principal. Soma o tempo das amostras de coleta
                // (completa e incremental) deste frame. As duas são caminhos alternativos
                // do coletor, não aninhados, então somar não conta o mesmo tempo duas vezes.
                int collectId = view.GetMarkerId(GC_COLLECT_MARKER);
                int incrId    = view.GetMarkerId(GC_INCREMENTAL_MARKER);
                long collectNs = 0;
                if (collectId != FrameDataView.invalidMarkerId || incrId != FrameDataView.invalidMarkerId)
                {
                    int n = view.sampleCount;
                    for (int s = 0; s < n; s++)
                    {
                        int mid = view.GetSampleMarkerId(s);
                        if (mid == collectId || mid == incrId)
                            collectNs += (long)view.GetSampleTimeNs(s);
                    }
                }
                LastGcCollectMs = collectNs * NS_TO_MS;

                // ── Draw calls ───────────────────────────────────────────
                long drawSum = 0;
                int resolved = 0;
                for (int i = 0; i < DRAW_CALL_COUNTERS.Length; i++)
                {
                    long v = ReadCounter(view, DRAW_CALL_COUNTERS[i], out bool ok);
                    _remoteDrawValues[i] = ok ? v : 0L;
                    if (ok) { resolved++; drawSum += v; }
                }
                _remoteDrawResolved = resolved;

                _frameIndex++;
                OnFrameSampled?.Invoke(cpuNs * NS_TO_MS, gcBytes, (int)drawSum,
                                       memBytes, gpuNs * NS_TO_MS);
            }
        }

        /// <summary>Reads one counter from a frame; <paramref name="resolved"/> says if it existed.</summary>
        /// <remarks>
        /// MOTIVO do flag: "não existe" e "existe e vale zero" são coisas diferentes, e
        /// só quem chama sabe qual das duas importa. Draw call ausente precisa virar "—";
        /// GC igual a zero é informação legítima.
        ///
        /// Os nomes de contador MUDAM por plataforma e pipeline — no Windows/DX12 os três
        /// nomes antigos de draw call davam 0 e os reais eram outros onze; no Android com
        /// Vulkan a lista muda de novo. Quem diz o que existe de fato é a
        /// RemoteCounterProbe, e por isso um contador ausente não pode derrubar a coleta
        /// inteira: ele se declara ausente e o resto continua.
        /// </remarks>
        private static long ReadCounter(RawFrameDataView view, string name, out bool resolved)
        {
            int id = view.GetMarkerId(name);
            if (id == FrameDataView.invalidMarkerId || !view.HasCounterValue(id))
            {
                resolved = false;
                return 0L;
            }
            resolved = true;
            return view.GetCounterValueAsLong(id);
        }

        /// <summary>Total wait time (frame cap, present) inside the frame's root sample, in ns.</summary>
        /// <remarks>
        /// MOTIVO de não somar tudo: uma espera pode conter outra (WaitForPresent dentro de
        /// WaitForPresentOnGfxThread, conforme a plataforma). As amostras vêm em ordem de
        /// início, então uma espera que começa antes de a anterior terminar está aninhada
        /// nela e já foi contada.
        /// </remarks>
        private static long WaitInsideRootNs(RawFrameDataView view)
        {
            for (int i = 0; i < WAIT_MARKERS.Length; i++)
                _waitIds[i] = view.GetMarkerId(WAIT_MARKERS[i]);

            ulong rootStart = view.GetSampleStartTimeNs(0);
            ulong rootEnd   = rootStart + view.GetSampleTimeNs(0);
            ulong countedUntil = 0;
            long total = 0;

            int n = view.sampleCount;
            for (int s = 1; s < n; s++)
            {
                int mid = view.GetSampleMarkerId(s);
                bool isWait = false;
                for (int w = 0; w < _waitIds.Length; w++)
                    if (_waitIds[w] != FrameDataView.invalidMarkerId && mid == _waitIds[w]) { isWait = true; break; }
                if (!isWait) continue;

                ulong start = view.GetSampleStartTimeNs(s);
                if (start < rootStart || start >= rootEnd) continue; // fora da raiz
                if (start < countedUntil) continue;                  // aninhada em espera já contada

                ulong dur = view.GetSampleTimeNs(s);
                total += (long)dur;
                countedUntil = start + dur;
            }
            return total;
        }

        /// <summary>
        /// Libera os ProfilerRecorders e para de emitir samples.
        /// Chamado pelo botão "Parar Monitoramento" do Dashboard ou automaticamente
        /// no ExitingPlayMode. No-op se já estiver parado.
        /// </summary>
        public static void StopMonitoring()
        {
            if (!_isMonitoring) return;
            _isMonitoring = false;
            EditorApplication.update -= SampleFrame;
            EditorApplication.update -= SampleFrameRemote;
            // MOTIVO: IsRemote NÃO é zerado no Stop. Ele descreve de onde vieram os
            // números da sessão, e esses números continuam na tela (e nos snapshots) depois
            // de parar. Zerar aqui fazia o aviso "a fonte é o Editor" sumir no momento em
            // que o dev ia olhar o resultado — os valores ficavam com o rótulo de aparelho
            // sem ter vindo dele. Ele é redefinido no próximo StartMonitoring.

            // MOTIVO: ProfilerRecorder is a struct — Dispose() releases the native handle.
            // Skipping this leaks the counter inside the Editor process until next domain reload.

            if (_cpuRecorder.Valid)     _cpuRecorder.Dispose();
            if (_gcRecorder.Valid)      _gcRecorder.Dispose();
            if (_memoryRecorder.Valid)  _memoryRecorder.Dispose();
            if (_gpuRecorder.Valid)     _gpuRecorder.Dispose();
            if (_gcCollectRecorder.Valid)     _gcCollectRecorder.Dispose();
            if (_gcIncrementalRecorder.Valid) _gcIncrementalRecorder.Dispose();

            // Libera TODOS os candidatos, inclusive os que não foram escolhidos —
            // todos seguraram um handle nativo durante a sessão.
            for (int i = 0; i < _drawRecorders.Length; i++)
            {
                if (_drawRecorders[i].Valid) _drawRecorders[i].Dispose();
            }

            ProfilerStackResolver.RestoreSession();

            // Duração da sessão, lida por quem reage ao Stop (ex.: teto automático).
            LastSessionSeconds = EditorApplication.timeSinceStartup - _sessionStart;
            OnMonitoringStopped?.Invoke();
        }
    }
}
