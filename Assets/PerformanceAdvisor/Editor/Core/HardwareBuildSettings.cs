using System;
using UnityEditor;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Estado serializável da build selecionada. Campos de plataformas não-ativas
    /// são preservados (trocar PC→Mobile→PC não perde a seleção de peças).
    /// </summary>
    [Serializable]
    public class HardwareBuild
    {
        public TargetPlatform Platform = TargetPlatform.PC;

        // MOTIVO: o orçamento de frame em ms é função do FPS ALVO, não do hardware —
        // uma Q6600 e um i9 têm os mesmos 33,3 ms se o alvo é 30 FPS; o que muda é o
        // que cabe dentro deles. Escolher o alvo é decisão do desenvolvedor, então o
        // catálogo não tem o que opinar aqui. Antes cada CPU carregava um ms "realista"
        // inventado (50,0 / 40,0 / 28,0 / 22,0…, que dão 20, 25, 35,7 e 45,5 FPS);
        // agora o alvo é explícito e o ms sai de 1000/alvo — aritmética pura.
        //
        // Default 60: é o padrão da indústria em PC e console hoje. 30 sobrevive em AAA
        // pesado e hardware legado, e 60 é inaceitável em VR (mínimo de 72 exigido pela
        // Meta no Quest) — por isso o alvo de VR é limitado pela taxa do headset em
        // HardwareCatalog.ClampTargetFps.
        public int TargetFps = 60;

        // PC
        public string CpuId  = "r5-3600";
        public bool   UseIntegratedGpu = false;
        public string GpuId  = "rtx3060";
        public RamType RamType = RamType.DDR4;
        public int    RamMhz = 3200;
        public int    RamGb  = 16;
        // MOTIVO: resolução é decisão de projeto em PC (em mobile/VR é spec do aparelho),
        // e o tempo de GPU escala com pixels — sem ela não há projeção de GPU.
        public int    TargetResolutionIndex = 1; // 1920×1080
        // Mobile / VR
        // MOTIVO: MobileId removido. Era o modelo escolhido num seletor que não decidia
        // nada — nenhum limiar saía dele, porque nenhum fabricante de celular publica
        // orçamento por modelo. Com a medição no aparelho, quem responde é o que está
        // plugado, e um campo que não muda resultado nenhum só sugere que muda.
        public string VrId     = "quest2";

        // MOTIVO: vale para as três plataformas, não só pro VR como na primeira versão.
        // Ver TestMode. Default no Editor porque é o que funciona sem build nenhuma;
        // Mobile é forçado pra NaBuild no Sanitize, porque lá não existe outro jeito.
        public TestMode Test = TestMode.NoEditor;

        /// <summary>Memory ceiling the developer declares, in MB. 0 = no ceiling.</summary>
        /// <remarks>
        /// MOTIVO: substitui PC_MEMORY_FRACTION = 0,35, que multiplicava a RAM total da
        /// máquina por um número sem fonte. Ninguém publica quanto de RAM um jogo pode
        /// ocupar no Windows — mas o desenvolvedor sabe pra quem está fazendo o jogo, e
        /// a declaração dele é procedência legítima.
        ///
        /// É a mesma natureza do TargetFps, que já funciona assim: alvo de projeto
        /// declarado, com aritmética honesta em cima. A diferença entre isso e a fração
        /// inventada é de quem é a escolha — lá era nossa, e sem fundamento; aqui é dele,
        /// sobre o próprio jogo.
        ///
        /// Vale para PC e Android. No VR não: ali a Meta PUBLICA o limite por app, e
        /// número publicado ganha de número declarado. Em Android ninguém publica, então
        /// a declaração do dev é a melhor procedência disponível — e, com a medição no
        /// aparelho ligada, ela pode sair do pico real que o jogo DELE atingiu.
        ///
        /// Zero é o default e significa SEM TETO: mostra o valor e não julga, mesma regra
        /// que o teto de draw calls já segue quando não há calibração.
        /// </remarks>
        public int DeclaredMemoryCeilingMb = 0;

        // MOTIVO: o teto pertence a UMA plataforma. O pico de um tablet e o de uma build
        // standalone de PC não têm nada a ver um com o outro, e com um campo só, trocar de
        // Mobile para PC fazia o teto do tablet julgar o PC. Quem não bate com a plataforma
        // ativa é tratado como "sem teto" — ver CeilingFor.
        //
        // PlatformSet existe para migrar builds salvas antes desta regra: JsonUtility
        // deixa o enum em PC (valor 0) quando o campo não existe no JSON, o que faria um
        // teto declarado no Android sumir. Com o flag falso, o Sanitize adota a
        // plataforma ativa em vez de assumir PC.
        public TargetPlatform MemoryCeilingPlatform;
        public bool   MemoryCeilingPlatformSet;

        /// <summary>True when the ceiling was set by the plugin from a measured session.</summary>
        public bool   MemoryCeilingIsAuto;

        /// <summary>Player the automatic ceiling was measured on. Empty for manual ceilings.</summary>
        public string MemoryCeilingSource = "";

        /// <summary>Length of the session an automatic ceiling came from, in seconds.</summary>
        public int    MemoryCeilingSessionSeconds;
    }

    /// <summary>
    /// Build ativa do plugin: persistência (EditorPrefs/JSON), cálculo e CACHE dos
    /// 4 budgets, builds prontas e evento de mudança.
    /// MOTIVO: budgets são cacheados em campos — o Detetive lê por propriedade
    /// estática no hot-path sem recomputar nem alocar (RN-01).
    /// </summary>
    public static class HardwareBuildSettings
    {
        private const string BUILD_PREF = "PerformanceAdvisor_Build";

        private static HardwareBuild _active;
        private static float _cpuMs;
        private static int   _drawCalls;
        private static long  _memBytes;
        private static string _buildId;      // ex.: "q6600+ddr3-1333-4gb+gtx750ti"
        private static string _description;  // legível pra UI e payload do Gemini
        private static string _verdictSource = "";  // procedência da projeção do veredito
        private static float  _cpuFactor;           // 0 = sem projeção possível
        private static float  _gpuFactor;

        /// <summary>Disparado após qualquer mudança aplicada na build.</summary>
        public static event Action OnBuildChanged;

        // Budgets cacheados — leitura é só um campo (hot-path safe).
        public static float CpuBudgetMs       => _cpuMs;

        /// <summary>The frame rate the developer declared, independent of any headroom factor.</summary>
        /// <remarks>
        /// MOTIVO: o resumo do painel derivava o FPS de volta como 1000/CpuBudgetMs. Isso
        /// só funcionava enquanto orçamento era exatamente 1000/FPS. Com a folga térmica
        /// da Unity em mobile (65%), a conta inversa daria 92 FPS para um alvo de 60 —
        /// número que nunca foi pedido por ninguém. O alvo é um dado, não um derivado.
        /// </remarks>
        public static int   TargetFps           => _active != null ? _active.TargetFps : 60;

        /// <summary>Peak process memory from the last monitored session, or 0 if none.</summary>
        /// <remarks>
        /// MOTIVO: é o que permite o teto de memória sair de MEDIÇÃO em vez de chute. O
        /// painel pedia megabytes crus num IntField, e ninguém sabe de cabeça quanto o
        /// próprio jogo ocupa — o campo ficava em 0 e o veredito nunca ligava.
        ///
        /// Vem de EditorPrefs porque a sessão de Play termina com domain reload, e o
        /// painel é lido depois disso.
        /// </remarks>
        public static long LastMeasuredPeakBytes
        {
            get
            {
                string raw = EditorPrefs.GetString(SnapshotHeuristicsController.PEAK_PREF, "");
                return long.TryParse(raw, out long v) ? v : 0L;
            }
        }

        /// <summary>Where the stored memory peak was measured. Empty means the Editor.</summary>
        public static string LastMeasuredPeakSource =>
            EditorPrefs.GetString(SnapshotHeuristicsController.PEAK_SOURCE_PREF, "");

        public static int   DrawCallBudget    => _drawCalls;
        // MOTIVO: se o aparelho conectado não expõe o contador de memória, a leitura
        // vira zero — e zero contra um teto pinta verde. Contador ausente desliga o
        // veredito em vez de aprová-lo.
        public static long  MemoryBudgetBytes => TelemetryMonitorCore.RemoteMemResolved ? _memBytes : 0L;
        public static string ActiveBuildId    => _buildId;
        public static string Description      => _description;

        /// <summary>Platform of the active build. Lets the UI explain the right absence.</summary>
        public static TargetPlatform ActivePlatform => _active != null ? _active.Platform : TargetPlatform.PC;

        /// <summary>Where the active build declares its measurement comes from.</summary>
        public static TestMode ActiveTestMode => _active != null ? _active.Test : TestMode.NoEditor;
        /// <summary>Where the current verdict projection comes from — shown in the UI and the report.</summary>
        public static string VerdictSource => _verdictSource;

        /// <summary>True when the active build cannot produce a full time verdict.</summary>
        // MOTIVO: a UI decidia a cor procurando "sem veredito" no texto. Com o texto
        // traduzido isso quebraria em silêncio — o estado agora é um campo, não uma frase.
        public static bool VerdictIncomplete => _verdictIncomplete;
        private static bool _verdictIncomplete;
        /// <summary>
        /// Factor that converts CPU time measured here into time on the target. 0 = no verdict.
        /// </summary>
        /// <remarks>
        /// MOTIVO: em Mobile e VR o fator 1,0 do modo "na build" significa "o tempo medido
        /// JÁ É o do aparelho". Isso só é verdade se a sessão leu de fato um player
        /// remoto — e antes ninguém conferia. No teste, o autor fechou o APK e deu Play
        /// com o Unity Remote, que roda o jogo NO PC e usa o celular só como tela e
        /// touch. O plugin aplicou fator 1,0 ao tempo do PC contra o orçamento do celular
        /// e mostrou "CPU Crítico, 165%" como se fosse medição do aparelho.
        ///
        /// Agora o fator do modo build vale só quando a fonte da sessão é remota. Sem
        /// isso, é 0 — sem veredito — em vez de um veredito sobre a máquina errada.
        /// No PC nada muda: lá a projeção PassMark já parte desta máquina de propósito.
        /// </remarks>
        public static float CpuProjectionFactor => EffectiveFactor(_cpuFactor);

        /// <summary>Same as <see cref="CpuProjectionFactor"/>, for GPU time.</summary>
        // MOTIVO: mesma regra da memória. GPU ausente no aparelho viraria 0 ms e
        // veredito verde; sem o contador, não há veredito de GPU.
        public static float GpuProjectionFactor =>
            TelemetryMonitorCore.RemoteGpuResolved ? EffectiveFactor(_gpuFactor) : 0f;

        private static float EffectiveFactor(float f)
        {
            // Leitura de campo e bool — segura no hot-path do Detetive (RN-01).
            if (_active != null && _active.Platform != TargetPlatform.PC && !TelemetryMonitorCore.IsRemote)
                return 0f;
            return f;
        }


        // ── Teto de memória: por plataforma, manual ou automático ─────────

        /// <summary>The ceiling that applies to this build's platform, in MB. 0 = none.</summary>
        public static int CeilingFor(HardwareBuild b)
        {
            if (b == null || b.DeclaredMemoryCeilingMb <= 0) return 0;
            return b.MemoryCeilingPlatform == b.Platform ? b.DeclaredMemoryCeilingMb : 0;
        }

        /// <summary>Sets the ceiling for the build's current platform.</summary>
        public static void SetCeiling(HardwareBuild b, int mb, bool isAuto, string source,
                                      int sessionSeconds = 0)
        {
            b.DeclaredMemoryCeilingMb  = mb > 0 ? mb : 0;
            b.MemoryCeilingPlatform    = b.Platform;
            b.MemoryCeilingPlatformSet = true;
            b.MemoryCeilingIsAuto      = mb > 0 && isAuto;
            b.MemoryCeilingSource      = mb > 0 && isAuto ? (source ?? "") : "";
            b.MemoryCeilingSessionSeconds = mb > 0 && isAuto ? sessionSeconds : 0;
        }

        /// <summary>Message about a ceiling the plugin set on its own this session, or null.</summary>
        /// <remarks>Session-only: cleared when a new monitoring session starts.</remarks>
        public static string AutoCeilingNotice { get; private set; }

        /// <summary>Clears the one-shot automatic-ceiling message.</summary>
        public static void ClearAutoCeilingNotice() => AutoCeilingNotice = null;

        /// <summary>
        /// Sets the memory ceiling from a finished session's peak, when nothing is set yet.
        /// </summary>
        /// <remarks>
        /// MOTIVO: o autor pediu que o teto se definisse sozinho, sem pausar e apertar
        /// botão. Automatizar DENTRO da sessão seria circular: um teto derivado do pico da
        /// própria sessão nunca é ultrapassado por ela — o semáforo de memória ficaria
        /// verde por construção, e não mediria nada.
        ///
        /// O que tem sentido é o teto nascer da PRIMEIRA sessão medida e julgar as
        /// SEGUINTES. Aí ele detecta a coisa que um teto de memória existe para detectar
        /// sem fonte publicada: o jogo passar a ocupar mais do que ocupava — regressão e
        /// vazamento entre uma build e a próxima.
        ///
        /// Regras, cada uma com um porquê:
        ///  - Só sessão REMOTA. Pico do Editor é a Unity inteira e daria um teto que nunca
        ///    estoura.
        ///  - Só quando não há teto para esta plataforma. Se já existe (manual ou
        ///    automático), ele NÃO é substituído: subir o teto a cada sessão apagaria
        ///    exatamente o sinal de regressão que ele serve para dar. Recalcular é ação
        ///    explícita do dev, pelo botão no painel Build.
        ///  - Nunca no VR: lá a Meta publica o limite, e número publicado ganha.
        ///  - +20% de folga, pela mesma razão do botão: colado no pico, qualquer variação
        ///    normal entre execuções acusaria problema.
        /// </remarks>
        public static bool TryAutoMemoryCeiling(long peakBytes, string sourceName)
        {
            EnsureLoaded();
            HardwareBuild b = _active;

            if (b.Platform == TargetPlatform.VR) return false;
            if (b.Test != TestMode.NaBuild)      return false;
            if (string.IsNullOrEmpty(sourceName) || peakBytes <= 0) return false;
            if (CeilingFor(b) > 0)               return false;

            int peakMb = Mathf.CeilToInt(peakBytes / (1024f * 1024f));
            int mb     = Mathf.CeilToInt(peakMb * 1.2f);

            HardwareBuild copy = GetActiveCopy();
            int secs = Mathf.RoundToInt((float)TelemetryMonitorCore.LastSessionSeconds);
            SetCeiling(copy, mb, true, sourceName, secs);
            Apply(copy);

            AutoCeilingNotice = Loc.T(
                $"Teto de memória definido automaticamente: {mb} MB (pico de {peakMb} MB em " +
                $"{sourceName}, numa sessão de {secs} s, + 20%). As próximas sessões neste alvo " +
                "serão comparadas a ele. Se essa sessão foi curta demais para representar o jogo, " +
                "recalcule depois de uma sessão longa (painel Build).",
                $"Memory ceiling set automatically: {mb} MB (peak of {peakMb} MB on " +
                $"{sourceName}, in a {secs} s session, + 20%). The next sessions on this target " +
                "will be compared against it. If that session was too short to represent the game, " +
                "recalculate after a long session (Build panel).",
                $"Techo de memoria definido automáticamente: {mb} MB (pico de {peakMb} MB en " +
                $"{sourceName}, en una sesión de {secs} s, + 20%). Las próximas sesiones en este objetivo " +
                "se compararán con él. Si esa sesión fue demasiado corta para representar el juego, " +
                "recalcula después de una sesión larga (panel Build).");
            Debug.Log("[Performance Advisor] " + AutoCeilingNotice);
            return true;
        }

        /// <summary>Cópia da build ativa pra UI editar; aplique com <see cref="Apply"/>.</summary>
        public static HardwareBuild GetActiveCopy()
        {
            EnsureLoaded();
            return JsonUtility.FromJson<HardwareBuild>(JsonUtility.ToJson(_active));
        }

        public static void Apply(HardwareBuild build)
        {
            _active = build;
            Sanitize(_active);
            Recompute();
            EditorPrefs.SetString(BUILD_PREF, JsonUtility.ToJson(_active));
            OnBuildChanged?.Invoke();
        }
        // MOTIVO da REMOÇÃO das ReadyBuilds: eram combinações montadas por nós — esta CPU
        // com esta GPU com esta RAM. A Steam publica a distribuição de CADA peça
        // SEPARADAMENTE, nunca a conjunta, então não existia fonte dizendo que qualquer
        // uma dessas máquinas existe no mundo. Cada peça tinha procedência; o CONJUNTO
        // não tinha, e o atalho dava a ele uma autoridade que as peças não emprestam.
        //
        // Montar peça por peça é mais trabalho e mais honesto.

        /// <summary>Recomputes budgets and projection factors from the current state.</summary>
        /// <remarks>
        /// MOTIVO: o fator de GPU usa a resolução da medição, que não é constante — a Game
        /// view muda de tamanho, o dev troca o modo de teste. Ele era calculado só ao
        /// carregar e ao salvar a build, e ficava valendo para sessões medidas em outra
        /// resolução. Chamado no início de cada sessão de monitoramento.
        /// </remarks>
        public static void RefreshForSession()
        {
            EnsureLoaded();
            Recompute();
        }

        private static void EnsureLoaded()
        {
            if (_active != null) return;
            string json = EditorPrefs.GetString(BUILD_PREF, "");
            _active = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<HardwareBuild>(json);
            if (_active == null) _active = new HardwareBuild();
            Sanitize(_active);
            Recompute();
        }

        // MOTIVO: build salva pode referenciar Id removido em versão futura do
        // catálogo — cai pro default com aviso, nunca fica num estado inválido.
        private static void Sanitize(HardwareBuild b)
        {
            var fallback = new HardwareBuild();
            if (!HardwareCatalog.TryGetCpu(b.CpuId, out _))
            {
                Debug.LogWarning("[PerformanceAdvisor] " + Loc.T($"CPU '{b.CpuId}' não está no catálogo — usando {fallback.CpuId}.", $"CPU '{b.CpuId}' is not in the catalog — using {fallback.CpuId}.", $"La CPU '{b.CpuId}' no está en el catálogo: usando {fallback.CpuId}."));
                b.CpuId = fallback.CpuId;
            }
            if (!b.UseIntegratedGpu && !HardwareCatalog.TryGetGpu(b.GpuId, out _))
            {
                Debug.LogWarning("[PerformanceAdvisor] " + Loc.T($"GPU '{b.GpuId}' não está no catálogo — usando {fallback.GpuId}.", $"GPU '{b.GpuId}' is not in the catalog — using {fallback.GpuId}.", $"La GPU '{b.GpuId}' no está en el catálogo: usando {fallback.GpuId}."));
                b.GpuId = fallback.GpuId;
            }
            if (!HardwareCatalog.TryGetVr(b.VrId, out _))         b.VrId = fallback.VrId;

            // GPU integrada exige CPU com iGPU.
            HardwareCatalog.TryGetCpu(b.CpuId, out var cpu);
            if (b.UseIntegratedGpu && string.IsNullOrEmpty(cpu.IntegratedGpuId))
                b.UseIntegratedGpu = false;

            // MOTIVO: compatibilidade real CPU×RAM — trocar pra uma CPU que não
            // aceita a geração atual cai pra MELHOR geração que ela suporta.
            if (cpu.SupportedRam != null && Array.IndexOf(cpu.SupportedRam, b.RamType) < 0)
            {
                b.RamType = cpu.SupportedRam[cpu.SupportedRam.Length - 1];
                if (HardwareCatalog.TryGetRamGen(b.RamType, out var bestGen))
                    b.RamMhz = bestGen.BaseMhz;
            }

            // MHz/GB precisam existir nas opções da geração.
            if (HardwareCatalog.TryGetRamGen(b.RamType, out var gen))
            {
                if (Array.IndexOf(gen.MhzOptions, b.RamMhz) < 0) b.RamMhz = gen.BaseMhz;
            }
            if (Array.IndexOf(HardwareCatalog.RamGbOptions, b.RamGb) < 0) b.RamGb = 8;

            // Alvo precisa existir na lista e, em VR, não passar da taxa do headset.
            if (Array.IndexOf(HardwareCatalog.TargetFpsOptions, b.TargetFps) < 0)
                b.TargetFps = 60;
            b.TargetFps = HardwareCatalog.ClampTargetFps(b.TargetFps, b.Platform, b.VrId);
            // MOTIVO: Android só produz número com sentido medindo no aparelho — o tempo
            // do Editor é o deste PC e não existe índice que o converta para um SoC
            // móvel. A UI já esconde a escolha, mas uma build salva de antes desta regra
            // (ou editada à mão no EditorPrefs) chegaria aqui em NoEditor e daria uma
            // tela inteira de zeros sem explicação. A regra mora aqui, não só na tela.
            if (b.Platform == TargetPlatform.Mobile) b.Test = TestMode.NaBuild;

            // MOTIVO: migra teto salvo antes de ele ter plataforma. Sem o flag, o JSON
            // antigo carregaria MemoryCeilingPlatform = PC (valor 0) e um teto declarado
            // no Android sumiria. Adota a plataforma ativa, onde ele foi declarado.
            if (b.DeclaredMemoryCeilingMb > 0 && !b.MemoryCeilingPlatformSet)
            {
                b.MemoryCeilingPlatform    = b.Platform;
                b.MemoryCeilingPlatformSet = true;
            }
        }

        private static void Recompute()
        {
            HardwareBuild b = _active;
            switch (b.Platform)
            {
                case TargetPlatform.Mobile:
                {
                    // Procedência: aritmética (1000 / FPS alvo) × a fração publicada pela
                    // Unity para mobile. O orçamento cheio é o tempo do frame; o que o
                    // aparelho SUSTENTA é menor, porque ele esquenta e o SO reduz o clock.
                    // Ver MOBILE_THERMAL_HEADROOM para a fonte.
                    _cpuMs = (1000f / Mathf.Max(1, b.TargetFps)) * HardwareCatalog.MOBILE_THERMAL_HEADROOM;
                    // MOTIVO: medindo NA BUILD não existe projeção a fazer — o tempo sai
                    // do próprio aparelho, então o fator é 1,0 e o veredito é direto.
                    // Medindo no Editor o tempo é o DESTE PC, e não existe índice que
                    // converta isso para um SoC móvel; aí o fator é 0 e não se emite
                    // veredito, em vez de emitir um errado.
                    //
                    // Isto entra JUNTO com a leitura remota, nunca antes: com a coleta
                    // ainda local, fator 1,0 estaria afirmando que o tempo do PC é o
                    // tempo do celular.
                    bool medindoNoAlvo = b.Test == TestMode.NaBuild;
                    _cpuFactor = medindoNoAlvo ? 1f : 0f;
                    _gpuFactor = medindoNoAlvo ? 1f : 0f;
                    _drawCalls = 0;
                    // MOTIVO: a redação anterior afirmava que "não existe índice publicado
                    // que compare SoC móvel com desktop". É FALSO — o 3DMark Wild Life
                    // Extreme é oficialmente cross-platform, e a pesquisa de 20/09/2026
                    // desmentiu a frase. O que falta é cobertura curada do lado do DEV:
                    // nenhuma fonte publica Wild Life para GPU de desktop moderna, e o
                    // plugin roda na máquina de quem instalar, que é qualquer uma.
                    //
                    // Afirmar impossibilidade onde há só lacuna é o tipo de frase que o
                    // avaliador derruba com uma busca. A mensagem diz o que de fato falta.
                    _verdictIncomplete = b.Test != TestMode.NaBuild;
                    _verdictSource = b.Test == TestMode.NaBuild
                        ? Loc.T("Mobile: medindo no aparelho conectado — os tempos são dele, então não há " +
                                "projeção nem índice envolvidos.",
                                "Mobile: measuring on the connected device — the times are its own, so no " +
                                "projection or index is involved.",
                                "Mobile: midiendo en el dispositivo conectado: los tiempos son suyos, así que no hay " +
                                "proyección ni índice de por medio.")
                        : Loc.T("Mobile: sem veredito no Editor — o tempo medido é o do seu PC. Os índices " +
                                "deste catálogo são PassMark, que não cobre SoC móvel. Existe régua " +
                                "cross-platform (3DMark Wild Life Extreme), mas os valores por aparelho " +
                                "não foram levantados. Medir na build resolve sem precisar deles.",
                                "Mobile: no verdict in the Editor — the measured time is your PC's. This " +
                                "catalog's indices are PassMark, which does not cover mobile SoCs. A cross-platform " +
                                "benchmark exists (3DMark Wild Life Extreme), but per-device values were not " +
                                "collected. Measuring on the build solves it without them.",
                                "Mobile: sin veredicto en el Editor: el tiempo medido es el de tu PC. Los índices " +
                                "de este catálogo son PassMark, que no cubre SoC móviles. Existe una referencia " +
                                "multiplataforma (3DMark Wild Life Extreme), pero no se recopilaron los valores por " +
                                "dispositivo. Medir en la build lo resuelve sin necesitarlos.");
                    // MOTIVO: a RAM total do aparelho NÃO é o teto do app — o SO reserva
                    // parte para si e para o que roda em segundo plano. A versão anterior
                    // multiplicava esse total por 0,25, número sem fonte, e depois ficou
                    // sem teto nenhum — o que deixava a memória sem veredito no Android
                    // MESMO medindo no aparelho, que era o caso todo.
                    //
                    // Nenhum fabricante de celular publica o limite por app, então quem
                    // declara é o dev, igual ao PC. E com a medição em dispositivo ligada
                    // essa declaração pode sair do pico REAL que o jogo dele atingiu no
                    // aparelho — procedência 5, a mais forte, e não mais um palpite.
                    _memBytes = (b.Test == TestMode.NaBuild && CeilingFor(b) > 0)
                        ? CeilingFor(b) * 1024L * 1024L
                        : 0L;
                    // MOTIVO: a build mobile não nomeia mais um aparelho. O modelo saiu do
                    // painel porque nenhum limiar saía dele, e quem responde é o que
                    // estiver conectado. Quando a etapa 2 ler a identificação do aparelho
                    // pelo Profiler, é ela que entra aqui — medida, não escolhida.
                    _buildId = "android";
                    float cheio = 1000f / Mathf.Max(1, b.TargetFps);
                    _description = Loc.T(
                        $"Android — alvo {b.TargetFps} FPS (orçamento {_cpuMs:F1} ms = 65% de {cheio:F1} ms, " +
                        "folga térmica recomendada pela Unity para mobile)",
                        $"Android — target {b.TargetFps} FPS (budget {_cpuMs:F1} ms = 65% of {cheio:F1} ms, " +
                        "thermal headroom recommended by Unity for mobile)",
                        $"Android — objetivo {b.TargetFps} FPS (presupuesto {_cpuMs:F1} ms = 65% de {cheio:F1} ms, " +
                        "margen térmico recomendado por Unity para móviles)");
                    return;
                }
                case TargetPlatform.VR:
                {
                    HardwareCatalog.TryGetVr(b.VrId, out var v);
                    // Procedência: aritmética sobre especificação — o alvo é limitado
                    // pela taxa de atualização do headset, que é spec de fabricante.
                    _cpuMs = 1000f / Mathf.Max(1, b.TargetFps);

                    // Mesma regra do Mobile: medindo NA BUILD (no headset) o tempo já é o
                    // do alvo e o fator é 1,0 — não há projeção a fazer. No Editor o tempo
                    // é o deste PC e o SoC do headset não tem índice na escala desktop,
                    // então não se emite veredito de tempo.
                    bool medindoNoHeadset = b.Test == TestMode.NaBuild;
                    _cpuFactor = medindoNoHeadset ? 1f : 0f;
                    _gpuFactor = medindoNoHeadset ? 1f : 0f;

                    // MOTIVO: o teto de draw calls que a Meta publica estava no catálogo e
                    // NÃO ERA USADO — o ramo VR zerava `_drawCalls`, então o único budget
                    // de draw call com procedência real no projeto inteiro ficava
                    // ignorado. Ligado aqui, e nos DOIS modos: um número publicado não
                    // deixa de ser publicado porque a medição mudou de lugar.
                    //
                    // Vale mesmo medindo no Editor, porque contagem de draw call é
                    // propriedade da CENA e do pipeline (geometria, materiais, batching),
                    // não da máquina que renderiza — é justamente por isso que a Meta
                    // consegue publicar um número desses.
                    _drawCalls = v.DrawCallBudget;

                    _verdictIncomplete = false;
                    if (b.Test == TestMode.NaBuild)
                    {
                        // Procedência: limite por app publicado pelo fabricante. A Meta
                        // documenta em PSS e mata o app ao estourar. Só vale aqui porque
                        // só aqui o processo medido é o jogo.
                        _memBytes = v.AppMemoryBytes;
                        _verdictSource = Loc.T(
                            "VR / medindo na build do headset: tempos e memória vêm do aparelho, " +
                            "então o veredito é direto — sem projeção e sem orçamento emprestado. " +
                            "Exige Development Build + Autoconnect Profiler rodando no headset.",
                            "VR / measuring the build on the headset: times and memory come from the device, " +
                            "so the verdict is direct — no projection and no borrowed budget. " +
                            "Requires Development Build + Autoconnect Profiler running on the headset.",
                            "VR / midiendo la build en el visor: los tiempos y la memoria vienen del dispositivo, " +
                            "así que el veredicto es directo, sin proyección ni presupuesto prestado. " +
                            "Requiere Development Build + Autoconnect Profiler ejecutándose en el visor.");
                    }
                    else
                    {
                        // MOTIVO: o limite da Meta é por APP, e no Editor o processo é a
                        // Unity inteira. Comparar um contra o outro daria vermelho
                        // permanente sem dizer nada sobre o jogo — foi exatamente o
                        // sintoma relatado pelo orientador. Sem teto neste modo.
                        _memBytes = 0;
                        _verdictSource = Loc.T(
                            "VR / medindo no Editor: draw calls comparados ao que a Meta publica para " +
                            "este headset (contagem é da cena, não da máquina). Sem veredito de memória " +
                            "nem de tempo — o processo medido é o Editor e os ms são desta máquina. " +
                            "Para os dois, mude para medição na build.",
                            "VR / measuring in the Editor: draw calls compared with what Meta publishes for " +
                            "this headset (the count belongs to the scene, not the machine). No memory or time " +
                            "verdict — the measured process is the Editor and the ms are this machine's. " +
                            "For both, switch to measuring on the build.",
                            "VR / midiendo en el Editor: draw calls comparados con lo que Meta publica para " +
                            "este visor (el conteo es de la escena, no de la máquina). Sin veredicto de memoria " +
                            "ni de tiempo: el proceso medido es el Editor y los ms son de esta máquina. " +
                            "Para ambos, cambia a medir en la build.");
                    }

                    _buildId = v.Id + (b.Test == TestMode.NaBuild ? "+medido" : "");
                    _description = Loc.T(
                        $"{v.DisplayName} — painel {v.RefreshHz}Hz, {v.RamGb}GB RAM, alvo {b.TargetFps} FPS " +
                        $"[procedência dos budgets: {v.BudgetSource}]",
                        $"{v.DisplayName} — {v.RefreshHz}Hz panel, {v.RamGb}GB RAM, target {b.TargetFps} FPS " +
                        $"[budget source: {v.BudgetSource}]",
                        $"{v.DisplayName} — panel {v.RefreshHz}Hz, {v.RamGb}GB RAM, objetivo {b.TargetFps} FPS " +
                        $"[fuente de los presupuestos: {v.BudgetSource}]");
                    return;
                }
                default: // PC
                {
                    HardwareCatalog.TryGetCpu(b.CpuId, out var cpu);
                    string gpuId = b.UseIntegratedGpu ? cpu.IntegratedGpuId : b.GpuId;
                    HardwareCatalog.TryGetGpu(gpuId, out var gpu);
                    HardwareCatalog.TryGetRamGen(b.RamType, out var gen);

                    // Procedência: aritmética — 1000 / FPS alvo declarado pelo dev.
                    _cpuMs = 1000f / Mathf.Max(1, b.TargetFps);

                    // ── Fatores de projeção ───────────────────────────────────────
                    // Cacheados aqui, em campo, porque o Detetive os lê todo frame e o
                    // hot-path não pode consultar catálogo nem alocar (RN-01).
                    int devCpu = CpuPerformanceIndex.DevRating;
                    _cpuFactor = (devCpu > 0 && cpu.SingleThreadRating > 0)
                        ? (float)devCpu / cpu.SingleThreadRating
                        : 0f;

                    int  devGpu = GpuPerformanceIndex.DevRating;
                    long devPx  = PerformanceProjection.DevPixels();
                    var  res    = HardwareCatalog.PcResolutions[
                                      Mathf.Clamp(b.TargetResolutionIndex, 0, HardwareCatalog.PcResolutions.Length - 1)];
                    long tgtPx  = (long)res.width * res.height;

                    // Tempo de GPU escala com PIXELS e com a potência da placa. O de CPU
                    // não escala com pixels — por isso o fator existe só aqui.
                    _gpuFactor = (devGpu > 0 && gpu.G3dRating > 0 && devPx > 0)
                        ? ((float)tgtPx / devPx) * ((float)devGpu / gpu.G3dRating)
                        : 0f;

                    _verdictIncomplete = !(_cpuFactor > 0f && _gpuFactor > 0f);
                    string px = PerformanceProjection.DevPixelsSource();
                    _verdictSource = !_verdictIncomplete
                        ? Loc.T($"projetado: CPU ×{_cpuFactor:F2} ({devCpu}÷{cpu.SingleThreadRating} PassMark), " +
                                $"GPU ×{_gpuFactor:F2} (medido em {px} → alvo {res.label}, {devGpu}÷{gpu.G3dRating} G3D)",
                                $"projected: CPU ×{_cpuFactor:F2} ({devCpu}÷{cpu.SingleThreadRating} PassMark), " +
                                $"GPU ×{_gpuFactor:F2} (measured at {px} → target {res.label}, {devGpu}÷{gpu.G3dRating} G3D)",
                                $"proyectado: CPU ×{_cpuFactor:F2} ({devCpu}÷{cpu.SingleThreadRating} PassMark), " +
                                $"GPU ×{_gpuFactor:F2} (medido en {px} → objetivo {res.label}, {devGpu}÷{gpu.G3dRating} G3D)")
                        : Loc.T("sem veredito completo — falta índice desta máquina ou da peça alvo",
                                "no full verdict — missing an index for this machine or the target part",
                                "sin veredicto completo: falta el índice de esta máquina o de la pieza objetivo");

                    // Teto de draw calls = tempo disponivel para submissao / custo por draw
                    // no alvo. Zero = sem calibracao, e entao sem teto (nao se inventa um).
                    _drawCalls = DrawCallBudgetModel.Compute(_cpuMs, cpu.Id);

                    // MOTIVO: substitui `b.RamGb * PC_MEMORY_FRACTION`, que pegava a RAM
                    // da máquina alvo e multiplicava por 0,35 — número sem fonte. Agora o
                    // teto é o que o dev DECLARA pro jogo dele, e só vale medindo a build:
                    // no Editor o processo é a Unity inteira, e comparar isso contra um
                    // teto de jogo dá vermelho permanente sem dizer nada (medido: 3,2 a
                    // 4,2 GB contra teto de 1,4 GB, 230% a 298% o tempo todo).
                    _memBytes = (b.Test == TestMode.NaBuild && CeilingFor(b) > 0)
                        ? CeilingFor(b) * 1024L * 1024L
                        : 0L;
                    _buildId = $"{cpu.Id}+{b.RamType.ToString().ToLowerInvariant()}-{b.RamMhz}-{b.RamGb}gb+{gpu.Id}";
                    _description = $"{cpu.DisplayName}, {b.RamType} {b.RamMhz}MHz {b.RamGb}GB, {gpu.DisplayName}, " +
                                   Loc.T($"alvo {b.TargetFps} FPS", $"target {b.TargetFps} FPS", $"objetivo {b.TargetFps} FPS");
                    return;
                }
            }
        }

        // MOTIVO: [InitializeOnLoadMethod] garante budgets carregados antes do
        // primeiro frame monitorado, mesmo sem a janela aberta.
        [InitializeOnLoadMethod]
        private static void Bootstrap()
        {
            EnsureLoaded();
            // MOTIVO: descrição e procedência são textos calculados no Recompute e guardados
            // em campo. Sem refazer ao trocar o idioma, a aba Build ficaria no idioma antigo.
            Loc.Changed += Recompute;
        }
    }
}
