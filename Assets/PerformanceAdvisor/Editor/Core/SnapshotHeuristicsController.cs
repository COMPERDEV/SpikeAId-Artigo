using System;
using UnityEditor;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// "Detetive" — receives every frame sample from the Porteiro, applies a
    /// 5-frame moving-average gate to filter background noise, and stores
    /// anomalous frames in a fixed-size circular buffer.
    /// </summary>
    /// <remarks>
    /// Allocation discipline: the buffer and the moving-window slots are
    /// pre-allocated arrays sized at compile time. The only allocation on the
    /// hot path is the optional culprit stack string, captured strictly when
    /// an anomaly is detected (rare event amortizes the cost — RN-01).
    /// </remarks>
    [InitializeOnLoad]
    public static class SnapshotHeuristicsController
    {
        // MOTIVO: 5 buffers separados (10 slots cada) garantem que cada tipo de
        // gargalo tenha amostras representativas, sem que o gargalo mais frequente
        // sobrescreva os outros.
        private const int PER_TYPE_BUFFER  = 10;
        private const int MOVING_WINDOW    = 5;

        private static readonly FrameSnapshot[] _cpuBuf     = new FrameSnapshot[PER_TYPE_BUFFER];
        private static readonly FrameSnapshot[] _gcBuf      = new FrameSnapshot[PER_TYPE_BUFFER];
        private static readonly FrameSnapshot[] _drawBuf    = new FrameSnapshot[PER_TYPE_BUFFER];
        private static readonly FrameSnapshot[] _gpuBuf     = new FrameSnapshot[PER_TYPE_BUFFER];
        private static readonly FrameSnapshot[] _memBuf     = new FrameSnapshot[PER_TYPE_BUFFER];

        private static int _cpuHead,  _gcHead,  _drawHead,  _gpuHead,  _memHead;
        private static int _cpuCount, _gcCount, _drawCount, _gpuCount, _memCount;

        private static readonly float[] _cpuWindow = new float[MOVING_WINDOW];
        private static int _windowIdx;
        private static int _windowFilled;

        private static int _frameIndex;

        // Contador monotônico global de Ids de snapshot.
        // Persiste entre sessões dentro do mesmo domínio do Editor — não reseta no
        // Reset() porque a aba Snapshots precisa de Ids únicos pra rastrear seleção
        // estável mesmo se o dev rodar várias sessões de Play.
        private static int _nextSnapshotId;

        // Throttle do ProfilerStackResolver — lendo RawFrameDataView todo frame
        // anômalo seguraria o Editor. 10 frames ~= 1 stack resolve por 0.16s a 60fps,
        // suficiente pra dar contexto sem custo perceptível. Frames dentro da janela
        // reusam o último resolve.
        private const int STACK_RESOLVE_INTERVAL = 10;
        private static int    _lastStackResolveFrame = -STACK_RESOLVE_INTERVAL;
        private static string _cachedCpuCulprit;
        private static string _cachedGcCulprit;
        // Chaves de causa, vindas prontas do resolver — ver WriteSnapshot.
        private static string _cachedCpuKey;
        private static string _cachedGcKey;

        // Resumo agregado de violações durante toda a sessão.
        // Conta CADA violação independente do buffer estar cheio.
        private static int   _cpuViolations,   _gcViolations,   _drawViolations,   _gpuViolations,   _memViolations;
        private static float _cpuMaxMs;
        private static long  _gcMaxBytes;
        private static int   _drawMax;
        private static float _gpuMaxMs;
        private static long  _memMaxBytes;
        private static long  _memPeakBytes;
        private static float _frameGcCollectMs;

        /// <summary>Fired whenever a new snapshot lands in the buffer. Arg: total snapshots stored.</summary>
        public static event Action<int> OnSnapshotCaptured;

        /// <summary>Total snapshots across the 5 type-buffers.</summary>
        public static int CapturedCount => _cpuCount + _gcCount + _drawCount + _gpuCount + _memCount;

        /// <summary>Per-type breakdown for UI display.</summary>
        public static int CpuCount     => _cpuCount;
        public static int GcCount      => _gcCount;
        public static int DrawCount    => _drawCount;
        public static int GpuCount     => _gpuCount;
        public static int MemCount     => _memCount;

        // [InitializeOnLoad] — wires the Detetive to the Porteiro on every domain reload
        // so the user never has to remember to call Subscribe().
        static SnapshotHeuristicsController()
        {
            TelemetryMonitorCore.OnMonitoringStarted += Reset;
            TelemetryMonitorCore.OnFrameSampled += OnFrameSampled;
            // MOTIVO: o pico da sessão precisa sobreviver ao domain reload pra aparecer
            // no painel de build depois que o Play parou — por isso vai pra EditorPrefs
            // no fim, e não fica só em memória.
            TelemetryMonitorCore.OnMonitoringStopped += PersistPeak;
        }

        /// <summary>Clears all 5 buffers and the moving window. Called at every PlayMode start.</summary>
        /// <summary>Key under which the session memory peak survives a domain reload.</summary>
        public const string PEAK_PREF = "PerformanceAdvisor.LastMemoryPeakBytes";

        /// <summary>Writes the session memory peak so the Build panel can propose a ceiling.</summary>
        private static void PersistPeak()
        {
            if (_memPeakBytes <= 0) return;

            EditorPrefs.SetString(PEAK_PREF, _memPeakBytes.ToString());
            // MOTIVO: o pico serve para PROPOR o teto de memória do jogo, e ele era salvo de
            // qualquer sessão — inclusive de Play Mode no Editor, onde a memória medida é a
            // da Unity inteira (3 a 4 GB). O botão "pico + 20%" proporia esse número como
            // teto do jogo. Guardar a origem deixa o painel recusar um pico que não é do
            // jogo. Vazio = medido no Editor.
            string source = TelemetryMonitorCore.IsRemote ? TelemetryMonitorCore.SessionTargetName : "";
            EditorPrefs.SetString(PEAK_SOURCE_PREF, source);

            // Primeira sessão medida no aparelho sem teto definido: vira a referência das
            // próximas. As regras (e por que não dentro da mesma sessão) estão em
            // HardwareBuildSettings.TryAutoMemoryCeiling.
            HardwareBuildSettings.TryAutoMemoryCeiling(_memPeakBytes, source);
        }

        /// <summary>Key holding where the stored memory peak was measured. Empty = Editor.</summary>
        public const string PEAK_SOURCE_PREF = "PerformanceAdvisor.LastMemoryPeakSource";

        public static void Reset()
        {
            _cpuHead = _gcHead = _drawHead = _gpuHead = _memHead = 0;
            _cpuCount = _gcCount = _drawCount = _gpuCount = _memCount = 0;
            _windowIdx = 0;
            _windowFilled = 0;
            _frameIndex = 0;
            for (int i = 0; i < MOVING_WINDOW; i++) _cpuWindow[i] = 0f;

            _cpuViolations = _gcViolations = _drawViolations = _gpuViolations = _memViolations = 0;
            _cpuMaxMs = 0f;
            _gcMaxBytes = 0L;
            _drawMax = 0;
            _gpuMaxMs = 0f;
            _memMaxBytes = 0L;
            _memPeakBytes = 0L;

            _lastStackResolveFrame = -STACK_RESOLVE_INTERVAL;
            _cachedCpuCulprit = null;
            _cachedGcCulprit = null;
            _cachedCpuKey = null;
            _cachedGcKey = null;
        }

        /// <summary>Read-only summary of every budget violation seen during this session.</summary>
        public static (int cpu, int gc, int draw, int gpu, int mem,
                       float cpuMaxMs, long gcMaxBytes, int drawMax, float gpuMaxMs, long memMaxBytes)
            GetViolationSummary() =>
            (_cpuViolations, _gcViolations, _drawViolations, _gpuViolations, _memViolations,
             _cpuMaxMs, _gcMaxBytes, _drawMax, _gpuMaxMs, _memMaxBytes);

        /// <summary>Chronologically-ordered copy of the CPU-violation snapshots (oldest first).</summary>
        public static FrameSnapshot[] CopyCpuSamples()  => CopyBuffer(_cpuBuf,  _cpuHead,  _cpuCount);
        /// <summary>Chronologically-ordered copy of the GC-violation snapshots (oldest first).</summary>
        public static FrameSnapshot[] CopyGcSamples()   => CopyBuffer(_gcBuf,   _gcHead,   _gcCount);
        /// <summary>Chronologically-ordered copy of the DrawCalls-violation snapshots (oldest first).</summary>
        public static FrameSnapshot[] CopyDrawSamples() => CopyBuffer(_drawBuf, _drawHead, _drawCount);
        /// <summary>Chronologically-ordered copy of the memory-violation snapshots (oldest first).</summary>
        public static FrameSnapshot[] CopyMemSamples() => CopyBuffer(_memBuf, _memHead, _memCount);
        /// <summary>Chronologically-ordered copy of the GPU-violation snapshots (oldest first).</summary>
        public static FrameSnapshot[] CopyGpuSamples() => CopyBuffer(_gpuBuf, _gpuHead, _gpuCount);

        /// <summary>
        /// Concatena os 5 tipos em um único array ordenado por <see cref="FrameSnapshot.Id"/>
        /// (ordem de captura). Usado pela aba Snapshots para exibir a lista unificada.
        /// </summary>
        public static FrameSnapshot[] CopyAllSamples()
        {
            int total = _cpuCount + _gcCount + _drawCount + _gpuCount + _memCount;
            var all = new FrameSnapshot[total];
            int w = 0;
            CopyInto(_cpuBuf,     _cpuHead,     _cpuCount,     all, ref w);
            CopyInto(_gcBuf,      _gcHead,      _gcCount,      all, ref w);
            CopyInto(_drawBuf,    _drawHead,    _drawCount,    all, ref w);
            CopyInto(_gpuBuf,     _gpuHead,     _gpuCount,     all, ref w);
            CopyInto(_memBuf,     _memHead,     _memCount,     all, ref w);
            // Sort por Id pra mostrar em ordem cronológica de captura.
            System.Array.Sort(all, (a, b) => a.Id.CompareTo(b.Id));
            return all;
        }

        private static void CopyInto(FrameSnapshot[] buf, int head, int count, FrameSnapshot[] dest, ref int destIdx)
        {
            int start = count == PER_TYPE_BUFFER ? head : 0;
            for (int i = 0; i < count; i++)
                dest[destIdx++] = buf[(start + i) % PER_TYPE_BUFFER];
        }

        private static FrameSnapshot[] CopyBuffer(FrameSnapshot[] buf, int head, int count)
        {
            var ordered = new FrameSnapshot[count];
            // Cheio: o mais antigo está em head; senão, está no índice 0.
            int start = count == PER_TYPE_BUFFER ? head : 0;
            for (int i = 0; i < count; i++)
                ordered[i] = buf[(start + i) % PER_TYPE_BUFFER];
            return ordered;
        }

        // Hot path — keep allocation-free except inside the anomaly branch.
        // ── Aquecimento: os primeiros frames não representam o jogo ───────
        // MOTIVO: abrir o jogo custa caro e esse custo não é do jogo. Carga de cena,
        // compilação/warm-up de shader e primeira alocação de assets produzem picos
        // enormes de CPU, GC e memória nos primeiros frames. Capturados como snapshot,
        // eles viram "problema" no laudo — e a IA diagnosticaria gargalo olhando para o
        // loading, que ninguém otimiza tirando código de gameplay.
        //
        // O caso ficou óbvio na medição em build, onde o Monitor liga assim que o player
        // conecta — ou seja, durante a abertura. Mas vale igual em Play Mode: entrar em
        // Play tem exatamente os mesmos primeiros frames caros.
        //
        // PROCEDÊNCIA do número: aritmética sobre um alvo DECLARADO, igual ao orçamento
        // de frame. WARMUP_SECONDS é escolha nossa, não medição — e é por isso que ela
        // aparece na tela enquanto está em curso, em vez de descartar frames em silêncio.
        // Dois segundos cobrem carga de cena e warm-up de shader sem comer playtest útil.
        //
        // O que é descartado é o JULGAMENTO, não a leitura: o Monitor continua mostrando
        // os valores ao vivo (é o que prova que a coleta está funcionando), só não abre
        // snapshot. Um buffer de 10 causas cheio de picos de loading expulsaria as causas
        // reais — foi o mesmo mecanismo que já mordeu antes.
        private const float WARMUP_SECONDS = 2f;

        // MOTIVO: era contado em frames (2 × FPS alvo), o que só dá 2 s se o jogo atinge o
        // alvo. A 10 FPS com alvo de 60, eram 12 s descartados — e a cena lenta é a que
        // mais precisa de diagnóstico. Agora é o relógio do próprio frame (o do player,
        // quando a leitura é remota). Ver TelemetryMonitorCore.LastFrameSeconds.

        /// <summary>Seconds still being ignored before anomalies start counting.</summary>
        public static float WarmupSecondsRemaining
        {
            get
            {
                double left = WARMUP_SECONDS - TelemetryMonitorCore.LastFrameSeconds;
                return left > 0 ? (float)left : 0f;
            }
        }

        /// <summary>True while the opening seconds are still being discarded.</summary>
        public static bool IsWarmingUp => TelemetryMonitorCore.LastFrameSeconds < WARMUP_SECONDS;

        private static void OnFrameSampled(float cpuMs, long gcBytes, int drawCalls, long memBytes, float gpuMs)
        {
            // Lido uma vez por frame: o Porteiro preenche antes de disparar o evento.
            _frameGcCollectMs = TelemetryMonitorCore.LastGcCollectMs;

            // 1) Update the 5-frame CPU moving window.
            _cpuWindow[_windowIdx] = cpuMs;
            _windowIdx = (_windowIdx + 1) % MOVING_WINDOW;
            if (_windowFilled < MOVING_WINDOW) _windowFilled++;

            float sum = 0f;
            for (int i = 0; i < _windowFilled; i++) sum += _cpuWindow[i];
            float avgCpu = sum / _windowFilled;

            // 2) Budgets vêm do cache da build ativa (leitura de campo, zero-alloc).
            float budgetCpu     = HardwareBuildSettings.CpuBudgetMs;
            int   budgetDraw    = HardwareBuildSettings.DrawCallBudget;
            long  budgetMem     = HardwareBuildSettings.MemoryBudgetBytes;

            // MOTIVO: tempos são MEDIDOS nesta máquina e comparados no ALVO, então passam
            // pelo fator de projeção cacheado. Fator 0 significa que não há índice
            // publicado que permita projetar (caso de Mobile e VR, cujos SoCs não têm
            // escala comparável à de desktop) — e aí não se emite veredito, em vez de
            // emitir um errado.
            float cpuFactor = HardwareBuildSettings.CpuProjectionFactor;
            float gpuFactor = HardwareBuildSettings.GpuProjectionFactor;

            float cpuOnTarget   = avgCpu * cpuFactor;   // média de 5 frames
            float frameOnTarget = cpuMs  * cpuFactor;   // este frame
            float gpuOnTarget   = gpuMs  * gpuFactor;

            // 3) Anomaly gate + global violation tally.
            //
            // MOTIVO: o portão de CPU era SEMPRE a média de 5 frames. A média existe para
            // filtrar ruído do sistema operacional quando o processo medido é o próprio
            // Editor — mas ela também apaga o engasgo de um frame só: um frame de 80 ms no
            // meio de frames de 16 ms quase não move a média. E o engasgo é exatamente o
            // que a fundamentação do trabalho define como o problema (stuttering é
            // variância entre quadros, não FPS médio).
            //
            // Na leitura remota não há Editor no processo medido, então não há o ruído que
            // a média filtrava: cada frame acima do orçamento é um prazo perdido de fato
            // (aritmética: 1000 / FPS alvo), e conta. No Editor a média continua.
            bool cpuSpike     = cpuFactor > 0f && frameOnTarget > budgetCpu;
            bool cpuSustained = cpuFactor > 0f && cpuOnTarget   > budgetCpu;
            bool cpuViolated  = TelemetryMonitorCore.IsRemote ? cpuSpike : cpuSustained;
            bool gpuViolated  = gpuFactor > 0f && gpuOnTarget > budgetCpu;

            // MOTIVO: budget 0 = sem teto com procedência, então não julga — draw calls
            // sem calibração mostram o número e calam a boca, em vez de inventar limiar.
            bool drawViolated    = budgetDraw    > 0 && drawCalls    > budgetDraw;

            // MOTIVO: o GC não tem limiar próprio, e não é por descuido — não existe fonte
            // que converta bytes alocados em milissegundos de coleta. O custo de uma coleta
            // trava a main thread e JÁ É MEDIDO no tempo de CPU, que é portão; então o GC
            // não julga, EXPLICA picos de CPU.
            //
            // A primeira versão capturava quando a CPU estourou e houve QUALQUER alocação no
            // frame. No aparelho isso deu "snapshot de GC" com 24 bytes — alocação que não
            // custa nada perto de um frame de 33 ms. Numa cena que estoura a CPU em todo
            // frame, qualquer byte virava suspeito, e a IA podia culpar o GC por um pico que
            // ele não causou.
            //
            // Agora o critério é MEDIDO: captura quando a CPU estourou E uma coleta de fato
            // rodou naquele frame (marcadores GC.Collect / CollectIncremental com tempo > 0).
            // Aí o snapshot carrega quanto a coleta custou, e o laudo sabe se o GC explica o
            // pico ou é irrelevante. Continua sem número inventado: o limiar é o da CPU, e o
            // resto é medição (procedência 5).
            //
            // MOTIVO de usar o FRAME e não a média, também no Editor: a travada de GC é por
            // natureza um evento de um frame só, e exigir que a média estourasse deixava
            // escapar justamente o caso clássico. Aqui o ruído do SO não é risco: a coleta
            // foi MEDIDA naquele frame, então o pico tem causa registrada.
            bool gcViolated  = cpuSpike && _frameGcCollectMs > 0f;
            // MOTIVO: teto 0 = sem procedência, então não julga — mesma regra dos draw
            // calls. Sem isso, medindo no Editor a memória estouraria todo frame (o
            // processo é a Unity inteira) e o buffer de snapshots encheria de ruído,
            // expulsando as causas reais. Era o sintoma que o orientador relatou.
            bool memViolated = budgetMem > 0 && memBytes > budgetMem;
            // MOTIVO: o pico de memória é rastreado ANTES do aquecimento, de propósito.
            // Ele serve pra propor teto, e o app precisa caber na memória TAMBÉM durante
            // a carga — se o pico é o loading, o teto tem que cobrir o loading. O que o
            // aquecimento descarta é julgamento de gargalo, não o fato de a memória ter
            // chegado naquele valor.
            if (memBytes > _memPeakBytes) _memPeakBytes = memBytes;

            // MOTIVO: o contador de violações alimenta o resumo que vai pra IA, então ele
            // tem que respeitar o aquecimento igual à captura. Sem isso o laudo leria
            // "12 violações de CPU" sendo que todas aconteceram durante a carga da cena.
            // O aquecimento é medido pelo relógio do frame (TelemetryMonitorCore.LastFrameSeconds).
            _frameIndex++;
            if (IsWarmingUp) return;

            // Garante que mesmo se o buffer só conservar GC-anomalies, sabemos que
            // os outros tipos também estouraram durante a sessão.
            // MOTIVO: o máximo enviado à IA era o da MÉDIA, menor que o pior frame real. O
            // laudo dimensionava a gravidade por um pico atenuado.
            if (cpuViolated)     { _cpuViolations++;     if (frameOnTarget > _cpuMaxMs)   _cpuMaxMs    = frameOnTarget; }
            if (gpuViolated)     { _gpuViolations++;     if (gpuOnTarget  > _gpuMaxMs)    _gpuMaxMs    = gpuOnTarget; }
            if (gcViolated)      { _gcViolations++;      if (gcBytes      > _gcMaxBytes)  _gcMaxBytes  = gcBytes; }
            if (drawViolated)    { _drawViolations++;    if (drawCalls    > _drawMax)     _drawMax     = drawCalls; }
            if (memViolated)     { _memViolations++;     if (memBytes     > _memMaxBytes) _memMaxBytes = memBytes; }

            if (!cpuViolated && !gpuViolated && !gcViolated && !drawViolated && !memViolated) return;

            // 4) Resolve culprits do Profiler (throttled). Só ocorre dentro do
            //    branch de anomalia — alocação permitida aqui (RN-01).
            if (_frameIndex - _lastStackResolveFrame >= STACK_RESOLVE_INTERVAL)
            {
                ProfilerStackResolver.ResolveCulprits(out _cachedCpuCulprit, out _cachedGcCulprit,
                                                      out _cachedCpuKey, out _cachedGcKey);
                _lastStackResolveFrame = _frameIndex;
            }

            // 5) Store the snapshot in each buffer where the corresponding metric violated.
            //    Same frame can land in multiple buffers if multiple budgets blew up.
            //    Indexed struct-copy keeps the hot path heap-free.
            long timestamp = (long)(Time.realtimeSinceStartupAsDouble * 1000.0);

            if (cpuViolated)     WriteSnapshot(_cpuBuf,     ref _cpuHead,     ref _cpuCount,     SnapshotKind.Cpu,       cpuMs, gcBytes, drawCalls, memBytes, gpuMs, timestamp, _cachedCpuCulprit, _cachedCpuKey);
            if (gcViolated)      WriteSnapshot(_gcBuf,      ref _gcHead,      ref _gcCount,      SnapshotKind.Gc,        cpuMs, gcBytes, drawCalls, memBytes, gpuMs, timestamp, _cachedGcCulprit, _cachedGcKey);
            // DrawCalls, GPU e memória não têm culprit de script — fica null.
            if (drawViolated)    WriteSnapshot(_drawBuf,    ref _drawHead,    ref _drawCount,    SnapshotKind.DrawCalls, cpuMs, gcBytes, drawCalls, memBytes, gpuMs, timestamp, null, null);
            if (gpuViolated)     WriteSnapshot(_gpuBuf,     ref _gpuHead,     ref _gpuCount,     SnapshotKind.Gpu,       cpuMs, gcBytes, drawCalls, memBytes, gpuMs, timestamp, null, null);
            if (memViolated)     WriteSnapshot(_memBuf,     ref _memHead,     ref _memCount,     SnapshotKind.Memory,    cpuMs, gcBytes, drawCalls, memBytes, gpuMs, timestamp, null, null);

            OnSnapshotCaptured?.Invoke(CapturedCount);
        }

        /// <summary>
        /// Stores an anomalous frame, merging it into an existing slot when it repeats a
        /// cause already captured.
        /// </summary>
        /// <remarks>
        /// MOTIVO: antes isto era um anel puro — cada frame anômalo consumia um slot. Numa
        /// cena onde o problema acontece todo frame, os 10 slots enchiam com os 10 ÚLTIMOS
        /// frames consecutivos e **despejavam qualquer causa diferente vista antes**. Uma
        /// causa rara no frame 50 era sobrescrita por dez repetições da causa comum no
        /// frame 420. O buffer separado por tipo impedia um tipo de sufocar o outro, mas
        /// não impedia uma causa de sufocar outra dentro do mesmo tipo.
        ///
        /// Agora os 10 slots são 10 CAUSAS distintas: repetição atualiza o slot existente
        /// (conta ocorrências e mantém o pior frame), e só causa inédita ocupa slot novo.
        ///
        /// Zero-alloc preservado: a chave vem pronta do resolver, e a comparação é
        /// ordinal sobre strings já existentes — na prática referência igual, porque o
        /// resolver é throttled e reusa a mesma string por vários frames.
        /// </remarks>
        private static void WriteSnapshot(
            FrameSnapshot[] buf, ref int head, ref int count, SnapshotKind kind,
            float cpuMs, long gcBytes, int drawCalls, long memBytes, float gpuMs, long timestamp,
            string culprit, string causeKey)
        {
            // 1) Repetição de causa já capturada? Atualiza no lugar.
            for (int i = 0; i < count; i++)
            {
                ref FrameSnapshot existing = ref buf[i];
                if (!string.Equals(existing.CauseKey, causeKey, System.StringComparison.Ordinal)) continue;

                existing.Occurrences++;
                if (_frameIndex > existing.LastFrame) existing.LastFrame = _frameIndex;

                // Mantém o PIOR frame como representante — o caso extremo diagnostica
                // melhor que o limítrofe, e é ele que vai ao laudo.
                if (IsWorseThan(kind, cpuMs, gcBytes, drawCalls, memBytes, gpuMs, existing))
                {
                    int keptOcc   = existing.Occurrences;
                    int keptFirst = existing.FirstFrame;
                    int keptLast  = existing.LastFrame;

                    Fill(ref existing, kind, cpuMs, gcBytes, drawCalls, memBytes, gpuMs,
                         timestamp, culprit, causeKey);

                    existing.Occurrences = keptOcc;
                    existing.FirstFrame  = keptFirst;
                    existing.LastFrame   = keptLast;
                }
                return;
            }

            // 2) Causa inédita: ocupa slot.
            ref FrameSnapshot slot = ref buf[head];
            slot.Id = ++_nextSnapshotId;
            Fill(ref slot, kind, cpuMs, gcBytes, drawCalls, memBytes, gpuMs,
                 timestamp, culprit, causeKey);
            slot.Occurrences = 1;
            slot.FirstFrame  = _frameIndex;
            slot.LastFrame   = _frameIndex;

            head = (head + 1) % PER_TYPE_BUFFER;
            if (count < PER_TYPE_BUFFER) count++;
        }

        /// <summary>Copies the frame's readings into a slot, leaving Id and counters alone.</summary>
        private static void Fill(
            ref FrameSnapshot slot, SnapshotKind kind,
            float cpuMs, long gcBytes, int drawCalls, long memBytes, float gpuMs,
            long timestamp, string culprit, string causeKey)
        {
            slot.Kind              = kind;
            slot.FrameIndex        = _frameIndex;
            slot.CpuLatencyMs      = cpuMs;
            slot.GcAllocatedBytes  = gcBytes;
            slot.GcCollectMs       = _frameGcCollectMs;
            slot.DrawCallsCount    = drawCalls;
            slot.MemoryUsedBytes   = memBytes;
            slot.GpuMs             = gpuMs;
            // MOTIVO: atribuição de string é só cópia de referência — zero alloc.
            slot.CulpritCallStack  = culprit;
            slot.CauseKey          = causeKey;
            slot.BuildId           = HardwareBuildSettings.ActiveBuildId;
            slot.TimestampMs       = timestamp;
        }

        /// <summary>True when this frame's reading beats what the slot already holds.</summary>
        private static bool IsWorseThan(
            SnapshotKind kind,
            float cpuMs, long gcBytes, int drawCalls, long memBytes, float gpuMs,
            in FrameSnapshot slot)
        {
            switch (kind)
            {
                case SnapshotKind.Cpu:       return cpuMs        > slot.CpuLatencyMs;
                // MOTIVO: pior GC é a coleta mais CARA, não a que alocou mais bytes.
                case SnapshotKind.Gc:        return _frameGcCollectMs > slot.GcCollectMs;
                case SnapshotKind.DrawCalls: return drawCalls    > slot.DrawCallsCount;
                case SnapshotKind.Gpu:       return gpuMs        > slot.GpuMs;
                case SnapshotKind.Memory:    return memBytes     > slot.MemoryUsedBytes;
                default:                     return false;
            }
        }
    }
}
