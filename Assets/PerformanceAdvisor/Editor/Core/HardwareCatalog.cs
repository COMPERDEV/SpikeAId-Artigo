using System;

namespace PerformanceAdvisor
{
    public enum CpuClass  { I5, I7, I9, CoreUltra, Ryzen5, Ryzen7, Ryzen9 }
    public enum GpuVendor { Nvidia, Amd, Integrada }
    public enum RamType   { DDR2, DDR3, DDR4, DDR5 }
    public enum TargetPlatform { PC, Mobile, VR }

    /// <summary>Where the measurement comes from: the Editor process, or a real build.</summary>
    /// <remarks>
    /// MOTIVO: a primeira versão disto era <c>VrTestMode</c>, com os valores
    /// "OrcamentoPublicado" e "MedicaoNoHeadset". O nome estava errado sobre o próprio
    /// eixo. Orçamento publicado não é um jeito de MEDIR — é contra o que se compara, e
    /// isso é propriedade da PLATAFORMA, não do modo. O que muda entre os dois modos é de
    /// onde o número vem.
    ///
    /// E o eixo certo vale para as três plataformas, com o mesmo mecanismo
    /// (<c>ProfilerDriver</c> lendo um player conectado) e destinos diferentes:
    ///
    ///  - **PC:** build standalone de Windows rodando ao lado do Editor.
    ///  - **VR:** a build no headset, por adb.
    ///  - **Mobile:** a build no aparelho, por adb. Único modo válido.
    ///
    /// A diferença entre os dois modos não é conveniência — é o que o número significa:
    ///
    ///  - <see cref="NoEditor"/> mede o processo do EDITOR. A Unity inteira está nele:
    ///    janelas, inspector, cache de assets, o domínio C# do Editor. Medido, deu 3,2 a
    ///    4,2 GB contra um teto de 1,4 GB — 230% a 298% de forma permanente. Para
    ///    memória isso não é limiar mal calibrado, é a grandeza errada, e por isso este
    ///    modo não emite veredito de memória.
    ///  - <see cref="NaBuild"/> mede um processo que contém só o jogo.
    ///
    /// Contagem de draw call sobrevive nos dois, porque é propriedade da CENA e do
    /// pipeline — geometria, materiais, batching — e não da máquina que renderiza.
    /// </remarks>
    public enum TestMode
    {
        /// <summary>Play Mode inside the Editor. Fast, but the process is the Editor.</summary>
        NoEditor,
        /// <summary>A development build connected to the Profiler. The process is the game.</summary>
        NaBuild
    }


    [Serializable]
    public struct CpuSpec
    {
        public string Id;
        public string DisplayName;
        public CpuClass Class;
        public int Year;

        /// <summary>
        /// PassMark single-thread rating, as published on cpubenchmark.net.
        /// </summary>
        /// <remarks>
        /// MOTIVO: é o índice que permite projetar um custo de draw call MEDIDO na máquina
        /// do desenvolvedor para a CPU alvo, em vez de inventar um teto. Draw call é custo
        /// de CPU e a submissão é majoritariamente single-thread, então é este índice — e
        /// não o multi-thread — que representa a grandeza certa.
        /// </remarks>
        public int SingleThreadRating;

        public string IntegratedGpuId; // null = sem iGPU
        // MOTIVO: compatibilidade real CPU×RAM — um Ryzen 7000 (AM5) só aceita
        // DDR5; um Q6600 nunca rodou com DDR4. A UI filtra por esta lista.
        public RamType[] SupportedRam;

        public CpuSpec(string id, string name, CpuClass cls, int year, int singleThread, string igpu, RamType[] ram)
        { Id = id; DisplayName = name; Class = cls; Year = year; SingleThreadRating = singleThread;
          IntegratedGpuId = igpu; SupportedRam = ram; }
    }

    [Serializable]
    public struct GpuSpec
    {
        public string Id;
        public string DisplayName;
        public GpuVendor Vendor;
        public int Year;

        /// <summary>
        /// PassMark G3D Mark, as published on videocardbenchmark.net.
        /// </summary>
        /// <remarks>
        /// MOTIVO: substitui o antigo <c>DrawCallBudget</c>, que era inventado e ainda por cima
        /// media a grandeza errada — draw call é custo de CPU. O que a placa
        /// determina é TEMPO DE GPU, e este índice é o que permite projetar o tempo medido
        /// na máquina do dev para o tempo no hardware alvo.
        /// </remarks>
        public int G3dRating;

        public bool IsIntegrated;

        /// <summary>
        /// Position of this part in the Steam Hardware Survey, 1 = most common.
        /// </summary>
        /// <remarks>
        /// MOTIVO: guarda POR QUE esta placa está na lista, separado de quanto ela
        /// processa. São perguntas distintas com fontes distintas — a Steam diz o que as
        /// pessoas têm, a PassMark diz o que cada peça entrega. O catálogo antigo
        /// confundia as duas e a banca apontou. Aqui o número da Steam é só ordenação e
        /// rótulo na UI; nenhum veredito é calculado a partir dele.
        /// </remarks>
        public int SteamRank;

        public GpuSpec(string id, string name, GpuVendor vendor, int year, int g3d, bool integrated, int steamRank)
        { Id = id; DisplayName = name; Vendor = vendor; Year = year; G3dRating = g3d; IsIntegrated = integrated;
          SteamRank = steamRank; }
    }

    [Serializable]
    public struct RamGenSpec
    {
        public RamType Type;
        public int[] MhzOptions;
        public int BaseMhz;

        public RamGenSpec(RamType type, int[] mhz, int baseMhz)
        { Type = type; MhzOptions = mhz; BaseMhz = baseMhz; }

    }
    [Serializable]
    public struct VrHeadsetSpec
    {
        public string Id;
        public string DisplayName;
        public int Year;
        public int RefreshHz;

        public int DrawCallBudget;
        public int RamGb;

        /// <summary>
        /// Memory the platform actually allows one immersive app to use, when the vendor
        /// publishes it. Zero means "not published" — fall back to the RAM fraction.
        /// </summary>
        /// <remarks>
        /// MOTIVO: a RAM total do aparelho não é o teto do app — o SO reserva parte para
        /// tracking e apps de fundo. A Meta publica o limite real por app e o mede em PSS
        /// (Proportional Set Size), matando o app sem aviso quando estoura. Usar o número
        /// publicado é procedência 3; multiplicar a RAM total por uma fração inventada
        /// não é — e errava feio: 6 GB × 0,25 dava 1,5 GB no Quest 2, contra os 4,4 GiB
        /// que a Meta documenta.
        /// </remarks>
        public long AppMemoryBytes;

        /// <summary>Human-readable provenance of this entry's budgets, shown in the UI.</summary>
        public string BudgetSource;

        public VrHeadsetSpec(string id, string name, int year, int hz, int draws, int ramGb,
                             long appMemoryBytes, string budgetSource)
        { Id = id; DisplayName = name; Year = year; RefreshHz = hz;
          DrawCallBudget = draws; RamGb = ramGb; AppMemoryBytes = appMemoryBytes; BudgetSource = budgetSource; }

    }

    /// <summary>
    /// Catálogo estático de hardware — quais peças e aparelhos o dev pode escolher
    /// como alvo, e os budgets associados a cada um.
    /// </summary>
    /// <remarks>
    /// MOTIVO: estático e offline, sem dependência de rede.
    ///
    /// **Procedência das listas**: os líderes históricos da Steam Hardware Survey
    /// (github.com/jdegene/steamHWsurvey, github.com/myagues/steam-hss-data) e listas
    /// públicas de mais vendidos (mobile/VR).
    ///
    /// **Correção importante (2026-09-18)**: este comentário afirmava que "cada número
    /// de budget é direto e auditável" citando aquelas duas fontes. **A afirmação era
    /// falsa.** A Steam Hardware Survey registra *quais GPUs as pessoas possuem* e não
    /// diz nada sobre quanto cada uma processa — ela sustenta a LISTA de aparelhos, não
    /// os NÚMEROS pendurados neles.
    ///
    /// Estado real da procedência de cada budget (ver CLAUDE.md, "números não se inventam"):
    ///  - <b>Frame (ms)</b> — aritmética: <c>1000 / FPS alvo</c>, com o alvo escolhido pelo
    ///    dev. Em VR o alvo é limitado por <see cref="VrHeadsetSpec.RefreshHz"/>, que é
    ///    especificação de fabricante. ✅ procedência válida.
    ///  - <b>RamGb</b> — especificação do fabricante. ✅
    ///  - <b>DrawCallBudget (Mobile)</b> — REMOVIDO em 2026-09-20 junto com os iPhones.
    ///    Era estimativa sem lastro e já estava morto: Mobile mede no aparelho.
    ///  - <b>DrawCallBudget (VR)</b> — publicado pela Meta por aparelho. ✅
    ///  - <b>Budget de GC</b> — REMOVIDO em 2026-09-19. Era 1 MB/frame inventado. O custo
    ///    de uma coleta já aparece no tempo de CPU, que é portão; o GC explica picos em
    ///    vez de julgar, e a captura dele dispara junto com a violação de CPU.
    ///  - <b>PC_MEMORY_FRACTION / MOBILE_MEMORY_FRACTION</b> — REMOVIDOS em 2026-09-20.
    ///    Eram RAM total × número sem fonte. O teto de memória passou a ser publicado
    ///    (Quest), perguntado ao aparelho (Android, etapa 2), declarado pelo dev (PC) ou
    ///    inexistente — ver o comentário em RamGbOptions.
    ///
    /// Os marcados com ❌ não devem ser apresentados como medição em lugar nenhum —
    /// nem na UI, nem no laudo, nem no SDD — enquanto não tiverem procedência.
    /// </remarks>
    public static class HardwareCatalog
    {
        private const long KB = 1024L;
        private const long MB = 1024L * 1024L;
        private const long GB = 1024L * 1024L * 1024L;

        /// <summary>
        /// Selectable frame-rate targets. The frame budget in ms is <c>1000 / target</c>.
        /// </summary>
        /// <remarks>
        /// MOTIVO: são as taxas usadas na prática — 30 e 60 em PC/mobile, 72/90/120 nas
        /// taxas de atualização dos headsets atuais. O alvo é decisão do desenvolvedor;
        /// o plugin só faz a conta.
        /// </remarks>
        public static readonly int[] TargetFpsOptions = { 30, 60, 72, 90, 120 };

        /// <summary>
        /// Fraction of the frame budget a mobile target should actually aim for, leaving
        /// headroom for thermal throttling.
        /// </summary>
        /// <remarks>
        /// MOTIVO: o orçamento aritmético (1000 / FPS) é o tempo do frame, não o tempo
        /// que o aparelho sustenta. A Unity publica que em mobile não se deve usar o
        /// orçamento inteiro — "the device can overheat and the OS can thermal throttle
        /// the CPU and GPU" — e recomenda mirar em ~65% dele, o que dá ~22 ms a 30 FPS e
        /// ~11 ms a 60 FPS contra os 33,3 e 16,6 teóricos.
        ///
        /// Procedência 3: guia publicado do fornecedor, com o motivo declarado. Fonte:
        /// unity.com/blog/games/optimize-your-mobile-game-performance-tips-on-profiling-
        /// memory-and-code-architecture-from — consultado em 2026-09-20.
        ///
        /// Sem isso o plugin estava OTIMISTA EM 35% no mobile: aprovaria cena que na
        /// prática engasga assim que o aparelho esquenta.
        ///
        /// NÃO aplicado a VR, de propósito. O Quest é hardware móvel e também faz
        /// throttling, mas esta recomendação é da Unity sobre celular, e a Meta publica
        /// orientação própria para o Quest (travar cpuLevel/gpuLevel). Estender o número
        /// de um contexto para o outro sem fonte seria inventar — que é o que esta regra
        /// existe para impedir.
        /// </remarks>
        public const float MOBILE_THERMAL_HEADROOM = 0.65f;

        /// <summary>
        /// Clamps a target frame rate to what the platform can actually present.
        /// </summary>
        /// <remarks>
        /// MOTIVO: em VR mirar acima da taxa de atualização do headset não significa nada —
        /// o compositor apresenta na taxa do aparelho. O teto vem do <c>RefreshHz</c>, que é
        /// especificação de fabricante.
        /// </remarks>
        public static int ClampTargetFps(int fps, TargetPlatform platform, string vrId)
        {
            if (fps <= 0) fps = 60;

            if (platform == TargetPlatform.VR && TryGetVr(vrId, out var headset) && headset.RefreshHz > 0)
                return fps > headset.RefreshHz ? headset.RefreshHz : fps;

            return fps;
        }

        private static readonly RamType[] DDR4_ONLY  = { RamType.DDR4 };
        private static readonly RamType[] DDR4_DDR5  = { RamType.DDR4, RamType.DDR5 };
        private static readonly RamType[] DDR5_ONLY  = { RamType.DDR5 };

        // ── CPUs ─ sem orçamento de frame: ele é 1000/FPS alvo, escolha do dev ──
        //
        // MOTIVO da LISTA (quais CPUs existem aqui): **a Steam não publica modelo de
        // processador.** A Hardware Survey divulga só fabricante (Intel 54,5% / AMD
        // 45,5%), contagem de núcleos físicos e faixa de clock — nada que nomeie uma peça.
        // Então a lista vem da tabela "Common CPUs" da própria PassMark
        // (cpubenchmark.net/common_cpus.html), que ordena modelos por FREQUÊNCIA DE
        // SUBMISSÃO ao benchmark.
        //
        // RESSALVA que precisa ficar dita: frequência de submissão NÃO é participação de
        // mercado. Quem roda benchmark é um recorte entusiasta, enviesado para peças
        // recentes e de topo. A tabela responde "quais modelos aparecem muito", não
        // "quantas máquinas têm cada um". É a melhor fonte pública POR MODELO que existe,
        // e é usada só para escolher QUEM entra na lista — nunca para calcular nada.
        //
        // Conferência contra o que a Steam publica: a distribuição de núcleos da survey
        // (8 núcleos 28,0%, 6 núcleos 27,4%, 4 núcleos 12,5%, 10 núcleos 7,5%) é coberta
        // por esta lista, que vai de 4 núcleos (i5-9400F) a 16 (Ryzen 9 9950X3D).
        //
        // MOTIVO do SingleThreadRating (quanto cada CPU entrega): PassMark single thread,
        // cpubenchmark.net/singleThread.html. Submissão de draw call e lógica de main
        // thread são trabalho majoritariamente single-thread — é este índice, e não o
        // CPU Mark multi-thread, que representa a grandeza certa.
        //
        // AUSÊNCIAS DECLARADAS: as CPUs de notebook que aparecem alto na tabela de
        // frequência (i7-11800H, i7-12700H, i7-13620H, Ryzen 7 5800H, i7-10750H,
        // i7-9750H) ficaram de fora — a PassMark não expõe o single thread delas na
        // tabela pública, só o CPU Mark multi-thread, e derivar um do outro seria
        // inventar. A exceção é o i5-1135G7, cuja página traz o valor. Entram quando a
        // medição em dispositivo cobrir esse caso.
        //
        // Consultado em 2026-09-20.
        public static readonly CpuSpec[] Cpus =
        {
            // Intel — desktop
            new CpuSpec("i7-6700",     "Intel Core i7-6700 (2015)",        CpuClass.I7,        2015, 2273, null, DDR4_ONLY),
            new CpuSpec("i7-8700",     "Intel Core i7-8700 (2017)",        CpuClass.I7,        2017, 2624, null, DDR4_ONLY),
            new CpuSpec("i7-8700k",    "Intel Core i7-8700K (2017)",       CpuClass.I7,        2017, 2714, null, DDR4_ONLY),
            new CpuSpec("i5-9400f",    "Intel Core i5-9400F (2019)",       CpuClass.I5,        2019, 2574, null, DDR4_ONLY),
            new CpuSpec("i7-9700k",    "Intel Core i7-9700K (2018)",       CpuClass.I7,        2018, 2857, null, DDR4_ONLY),
            new CpuSpec("i9-9900k",    "Intel Core i9-9900K (2018)",       CpuClass.I9,        2018, 2908, null, DDR4_ONLY),
            new CpuSpec("i5-10400f",   "Intel Core i5-10400F (2020)",      CpuClass.I5,        2020, 2540, null, DDR4_ONLY),
            new CpuSpec("i5-12400f",   "Intel Core i5-12400F (2022)",      CpuClass.I5,        2022, 3485, null, DDR4_DDR5),
            new CpuSpec("i7-12700k",   "Intel Core i7-12700K (2021)",      CpuClass.I7,        2021, 4003, null, DDR4_DDR5),
            new CpuSpec("i9-12900k",   "Intel Core i9-12900K (2021)",      CpuClass.I9,        2021, 4127, null, DDR4_DDR5),
            new CpuSpec("i7-13700k",   "Intel Core i7-13700K (2022)",      CpuClass.I7,        2022, 4325, null, DDR4_DDR5),
            new CpuSpec("i9-13900k",   "Intel Core i9-13900K (2022)",      CpuClass.I9,        2022, 4595, null, DDR4_DDR5),
            new CpuSpec("i7-14700k",   "Intel Core i7-14700K (2023)",      CpuClass.I7,        2023, 4454, null, DDR4_DDR5),
            new CpuSpec("i9-14900k",   "Intel Core i9-14900K (2023)",      CpuClass.I9,        2023, 4690, null, DDR4_DDR5),
            new CpuSpec("ultra7-265k", "Intel Core Ultra 7 265K (2024)",   CpuClass.CoreUltra, 2024, 4927, null, DDR5_ONLY),
            new CpuSpec("ultra9-285k", "Intel Core Ultra 9 285K (2024)",   CpuClass.CoreUltra, 2024, 5086, null, DDR5_ONLY),
            // Intel — notebook
            new CpuSpec("i5-1135g7",   "Intel Core i5-1135G7 (2020)",      CpuClass.I5,        2020, 2595, "iris-xe", DDR4_ONLY),

            // AMD — AM4 (DDR4)
            new CpuSpec("r5-2600",     "AMD Ryzen 5 2600 (2018)",          CpuClass.Ryzen5,    2018, 2234, null, DDR4_ONLY),
            new CpuSpec("r7-2700x",    "AMD Ryzen 7 2700X (2018)",         CpuClass.Ryzen7,    2018, 2413, null, DDR4_ONLY),
            new CpuSpec("r5-3600",     "AMD Ryzen 5 3600 (2019)",          CpuClass.Ryzen5,    2019, 2557, null, DDR4_ONLY),
            new CpuSpec("r7-3700x",    "AMD Ryzen 7 3700X (2019)",         CpuClass.Ryzen7,    2019, 2654, null, DDR4_ONLY),
            new CpuSpec("r9-3900x",    "AMD Ryzen 9 3900X (2019)",         CpuClass.Ryzen9,    2019, 2702, null, DDR4_ONLY),
            new CpuSpec("r5-5500",     "AMD Ryzen 5 5500 (2022)",          CpuClass.Ryzen5,    2022, 3061, null, DDR4_ONLY),
            new CpuSpec("r5-5600g",    "AMD Ryzen 5 5600G (2021)",         CpuClass.Ryzen5,    2021, 3178, null, DDR4_ONLY),
            new CpuSpec("r5-5600",     "AMD Ryzen 5 5600 (2022)",          CpuClass.Ryzen5,    2022, 3253, null, DDR4_ONLY),
            new CpuSpec("r5-5600x",    "AMD Ryzen 5 5600X (2020)",         CpuClass.Ryzen5,    2020, 3366, null, DDR4_ONLY),
            new CpuSpec("r7-5700g",    "AMD Ryzen 7 5700G (2021)",         CpuClass.Ryzen7,    2021, 3281, null, DDR4_ONLY),
            new CpuSpec("r7-5700x3d",  "AMD Ryzen 7 5700X3D (2024)",       CpuClass.Ryzen7,    2024, 2968, null, DDR4_ONLY),
            new CpuSpec("r7-5800x3d",  "AMD Ryzen 7 5800X3D (2022)",       CpuClass.Ryzen7,    2022, 3233, null, DDR4_ONLY),
            new CpuSpec("r7-5700x",    "AMD Ryzen 7 5700X (2022)",         CpuClass.Ryzen7,    2022, 3386, null, DDR4_ONLY),
            new CpuSpec("r7-5800x",    "AMD Ryzen 7 5800X (2020)",         CpuClass.Ryzen7,    2020, 3448, null, DDR4_ONLY),
            new CpuSpec("r9-5900x",    "AMD Ryzen 9 5900X (2020)",         CpuClass.Ryzen9,    2020, 3465, null, DDR4_ONLY),
            new CpuSpec("r9-5950x",    "AMD Ryzen 9 5950X (2020)",         CpuClass.Ryzen9,    2020, 3476, null, DDR4_ONLY),
            // AMD — AM5 (DDR5)
            new CpuSpec("r7-7800x3d",  "AMD Ryzen 7 7800X3D (2023)",       CpuClass.Ryzen7,    2023, 3759, null, DDR5_ONLY),
            new CpuSpec("r5-7600x",    "AMD Ryzen 5 7600X (2022)",         CpuClass.Ryzen5,    2022, 4129, null, DDR5_ONLY),
            new CpuSpec("r7-7700x",    "AMD Ryzen 7 7700X (2022)",         CpuClass.Ryzen7,    2022, 4176, null, DDR5_ONLY),
            new CpuSpec("r9-7900x",    "AMD Ryzen 9 7900X (2022)",         CpuClass.Ryzen9,    2022, 4226, null, DDR5_ONLY),
            new CpuSpec("r9-7950x",    "AMD Ryzen 9 7950X (2022)",         CpuClass.Ryzen9,    2022, 4252, null, DDR5_ONLY),
            new CpuSpec("r7-9800x3d",  "AMD Ryzen 7 9800X3D (2024)",       CpuClass.Ryzen7,    2024, 4421, null, DDR5_ONLY),
            new CpuSpec("r5-9600x",    "AMD Ryzen 5 9600X (2024)",         CpuClass.Ryzen5,    2024, 4570, null, DDR5_ONLY),
            new CpuSpec("r7-9700x",    "AMD Ryzen 7 9700X (2024)",         CpuClass.Ryzen7,    2024, 4643, null, DDR5_ONLY),
            new CpuSpec("r9-9950x3d",  "AMD Ryzen 9 9950X3D (2025)",       CpuClass.Ryzen9,    2025, 4737, null, DDR5_ONLY),
        };

        // ── GPUs: as 50 mais presentes na Steam Hardware Survey ────────────
        // MOTIVO da LISTA (quais placas existem aqui): Steam Hardware Survey,
        // agosto/2026 — a pesquisa publica a porcentagem de máquinas que rodam cada
        // placa. É a resposta pública para "o que o mercado tem de fato na máquina",
        // que é a pergunta certa para escolher alvos. A lista anterior era montada por
        // "líder de cada época" e tinha peças de 2007–2012 que praticamente não existem
        // mais em uso, enquanto faltavam as placas realmente difundidas hoje.
        // O campo SteamRank guarda a POSIÇÃO na survey, não uma nota nossa.
        //
        // MOTIVO do G3dRating (quanto cada placa processa): PassMark G3D Mark,
        // videocardbenchmark.net. São duas fontes para duas perguntas diferentes, e é
        // exatamente o erro que a banca apontou no TIC1 — o catálogo antigo citava a
        // Steam como se ela justificasse os budgets. Ela justifica a LISTA; quem
        // justifica o NÚMERO é a PassMark.
        //
        // AUSÊNCIAS DECLARADAS: cinco das 50 posições não entraram por não terem índice
        // atribuível. Posições 18 e 30 são "AMD Radeon Graphics" e "AMD Radeon(TM)
        // Graphics" — nomes genéricos que a AMD usa para toda uma família de integradas,
        // sem placa única a que associar um G3D. A 38 é repetição de "Intel UHD
        // Graphics". A 39 (RTX 3050 6GB Laptop) e a 47 (RX 580 2048SP) são variantes sem
        // página própria de índice. Ficam de fora em vez de receber o número do parente
        // mais próximo.
        //
        // Consultado em 2026-09-20.
        public static readonly GpuSpec[] Gpus =
        {
            new GpuSpec("rtx3060",        "NVIDIA GeForce RTX 3060 12GB",      GpuVendor.Nvidia, 2021, 16705, false,  1),
            new GpuSpec("rtx4060-laptop", "NVIDIA GeForce RTX 4060 Laptop",    GpuVendor.Nvidia, 2023, 17335, false,  2),
            new GpuSpec("rtx5070",        "NVIDIA GeForce RTX 5070",           GpuVendor.Nvidia, 2025, 28647, false,  3),
            new GpuSpec("rtx4060",        "NVIDIA GeForce RTX 4060",           GpuVendor.Nvidia, 2023, 19486, false,  4),
            new GpuSpec("rtx3050",        "NVIDIA GeForce RTX 3050 6GB",       GpuVendor.Nvidia, 2022, 10740, false,  5),
            new GpuSpec("rtx5060",        "NVIDIA GeForce RTX 5060",           GpuVendor.Nvidia, 2025, 20629, false,  6),
            new GpuSpec("rtx5060-laptop", "NVIDIA GeForce RTX 5060 Laptop",    GpuVendor.Nvidia, 2025, 16732, false,  7),
            new GpuSpec("rtx5060ti",      "NVIDIA GeForce RTX 5060 Ti 16GB",   GpuVendor.Nvidia, 2025, 22610, false,  8),
            new GpuSpec("gtx1650",        "NVIDIA GeForce GTX 1650",           GpuVendor.Nvidia, 2019,  7866, false,  9),
            new GpuSpec("rtx4060ti",      "NVIDIA GeForce RTX 4060 Ti",        GpuVendor.Nvidia, 2023, 22596, false, 10),
            new GpuSpec("rtx3060ti",      "NVIDIA GeForce RTX 3060 Ti",        GpuVendor.Nvidia, 2020, 20219, false, 11),
            new GpuSpec("rtx3070",        "NVIDIA GeForce RTX 3070",           GpuVendor.Nvidia, 2020, 22074, false, 12),
            new GpuSpec("rtx3060-laptop", "NVIDIA GeForce RTX 3060 Laptop",    GpuVendor.Nvidia, 2021, 13102, false, 13),
            new GpuSpec("rtx5070ti",      "NVIDIA GeForce RTX 5070 Ti",        GpuVendor.Nvidia, 2025, 32324, false, 14),
            new GpuSpec("rtx4070",        "NVIDIA GeForce RTX 4070",           GpuVendor.Nvidia, 2023, 26857, false, 15),
            new GpuSpec("iris-xe",        "Intel Iris Xe Graphics",            GpuVendor.Integrada, 2020, 2589, true, 16),
            new GpuSpec("rtx4050-laptop", "NVIDIA GeForce RTX 4050 Laptop",    GpuVendor.Nvidia, 2023, 14207, false, 17),
            new GpuSpec("rtx5080",        "NVIDIA GeForce RTX 5080",           GpuVendor.Nvidia, 2025, 35629, false, 19),
            new GpuSpec("rtx2060",        "NVIDIA GeForce RTX 2060",           GpuVendor.Nvidia, 2019, 14074, false, 20),
            new GpuSpec("gtx1060",        "NVIDIA GeForce GTX 1060",           GpuVendor.Nvidia, 2016, 10022, false, 21),
            new GpuSpec("rtx4070s",       "NVIDIA GeForce RTX 4070 SUPER",     GpuVendor.Nvidia, 2024, 29945, false, 22),
            new GpuSpec("gtx1660s",       "NVIDIA GeForce GTX 1660 SUPER",     GpuVendor.Nvidia, 2019, 12660, false, 23),
            new GpuSpec("rx9070xt",       "AMD Radeon RX 9070 XT",             GpuVendor.Amd,    2025, 26908, false, 24),
            new GpuSpec("rtx3080",        "NVIDIA GeForce RTX 3080",           GpuVendor.Nvidia, 2020, 24981, false, 25),
            new GpuSpec("intel-uhd",      "Intel UHD Graphics",                GpuVendor.Integrada, 2017, 1540, true, 26),
            new GpuSpec("gtx1050ti",      "NVIDIA GeForce GTX 1050 Ti",        GpuVendor.Nvidia, 2016,  6366, false, 27),
            new GpuSpec("rtx4070-laptop", "NVIDIA GeForce RTX 4070 Laptop",    GpuVendor.Nvidia, 2023, 19474, false, 28),
            new GpuSpec("rtx4070ti",      "NVIDIA GeForce RTX 4070 Ti",        GpuVendor.Nvidia, 2023, 31517, false, 29),
            new GpuSpec("rtx3070ti",      "NVIDIA GeForce RTX 3070 Ti",        GpuVendor.Nvidia, 2021, 23170, false, 31),
            new GpuSpec("gtx1660ti",      "NVIDIA GeForce GTX 1660 Ti",        GpuVendor.Nvidia, 2019, 12569, false, 32),
            new GpuSpec("rx6600",         "AMD Radeon RX 6600",                GpuVendor.Amd,    2021, 15049, false, 33),
            new GpuSpec("rx9060xt",       "AMD Radeon RX 9060 XT 16GB",        GpuVendor.Amd,    2025, 20118, false, 34),
            new GpuSpec("rtx2060s",       "NVIDIA GeForce RTX 2060 SUPER",     GpuVendor.Nvidia, 2019, 16456, false, 35),
            new GpuSpec("rtx4070tis",     "NVIDIA GeForce RTX 4070 Ti SUPER",  GpuVendor.Nvidia, 2024, 31842, false, 36),
            new GpuSpec("rtx4090",        "NVIDIA GeForce RTX 4090",           GpuVendor.Nvidia, 2022, 38036, false, 37),
            new GpuSpec("rtx5070-laptop", "NVIDIA GeForce RTX 5070 Laptop",    GpuVendor.Nvidia, 2025, 19094, false, 40),
            new GpuSpec("rtx2070s",       "NVIDIA GeForce RTX 2070 SUPER",     GpuVendor.Nvidia, 2019, 18124, false, 41),
            new GpuSpec("rtx4080",        "NVIDIA GeForce RTX 4080",           GpuVendor.Nvidia, 2022, 34431, false, 42),
            new GpuSpec("rtx4080s",       "NVIDIA GeForce RTX 4080 SUPER",     GpuVendor.Nvidia, 2024, 34201, false, 43),
            new GpuSpec("rtx3050ti-laptop","NVIDIA GeForce RTX 3050 Ti Laptop", GpuVendor.Nvidia, 2021, 10063, false, 44),
            new GpuSpec("rx7800xt",       "AMD Radeon RX 7800 XT",             GpuVendor.Amd,    2023, 24466, false, 45),
            new GpuSpec("gtx1070",        "NVIDIA GeForce GTX 1070",           GpuVendor.Nvidia, 2016, 13509, false, 46),
            new GpuSpec("rx580",          "AMD Radeon RX 580",                 GpuVendor.Amd,    2017,  8782, false, 48),
            new GpuSpec("rx6700xt",       "AMD Radeon RX 6700 XT",             GpuVendor.Amd,    2021, 19720, false, 49),
            new GpuSpec("gtx1050",        "NVIDIA GeForce GTX 1050",           GpuVendor.Nvidia, 2016,  5051, false, 50),
        };

        /// <summary>
        /// Selectable render resolutions for PC targets, as width × height.
        /// </summary>
        /// <remarks>
        /// MOTIVO: tempo de GPU escala com quantidade de pixels, então projetar do
        /// desenvolvedor para o alvo exige saber a resolução dos dois lados. Em mobile e
        /// VR ela é especificação do aparelho; em PC é escolha do projeto, igual ao FPS
        /// alvo — por isso entra como lista selecionável e não como número no catálogo.
        /// </remarks>
        public static readonly (string label, int width, int height)[] PcResolutions =
        {
            ("1280×720 (HD)",     1280,  720),
            ("1920×1080 (FHD)",   1920, 1080),
            ("2560×1440 (QHD)",   2560, 1440),
            ("3840×2160 (4K)",    3840, 2160),
        };

        // ── Gerações de RAM ────────────────────────────────────────────────
        // MOTIVO: DDR2 e DDR3 saíram junto com as CPUs que as usavam. Nenhuma CPU da
        // lista atual aceita essas gerações, então deixá-las como opção só produziria
        // combinações que não existem. As frequências são as especificações JEDEC e de
        // XMP/EXPO das respectivas gerações.
        public static readonly RamGenSpec[] RamGens =
        {
            new RamGenSpec(RamType.DDR4, new[] { 2133, 2666, 3200, 3600 },  2666),
            new RamGenSpec(RamType.DDR5, new[] { 4800, 5600, 6000 },        5600),
        };

        /// <summary>
        /// Selectable system RAM capacities, ordered as the Steam Hardware Survey ranks
        /// them by share of machines.
        /// </summary>
        /// <remarks>
        /// MOTIVO: a Steam publica a distribuição de RAM total instalada, e é uma das
        /// poucas coisas que ela publica com número por faixa — 16 GB em 41,20% das
        /// máquinas, 32 GB em 37,45%, 8 GB em 7,46%, 64 GB em 3,97%, 12 GB em 2,41%,
        /// 24 GB em 2,22%, 4 GB em 1,38%, 48 GB em 1,20%. A lista antiga tinha 2 GB
        /// (abaixo de qualquer faixa publicada) e omitia 12, 24 e 48 GB, que juntos
        /// respondem por quase 6% do parque — mais que os 64 GB que ela listava.
        ///
        /// Consultado em 2026-09-20 (survey de agosto/2026).
        /// </remarks>
        public static readonly int[] RamGbOptions = { 16, 32, 8, 64, 12, 24, 4, 48 };

        // MOTIVO da REMOÇÃO: aqui moravam PC_MEMORY_FRACTION = 0,35 e
        // MOBILE_MEMORY_FRACTION = 0,25 — a RAM total da máquina multiplicada por um
        // número que não veio de lugar nenhum. Era o último número inventado no caminho
        // do veredito, e sobreviveu a todas as limpezas anteriores.
        //
        // O teto de memória agora vem de uma hierarquia, do mais forte ao mais fraco, e
        // quem resolve está em HardwareBuildSettings.Recompute:
        //   1. PUBLICADO pelo fabricante  — Quest, que a Meta documenta em PSS.
        //   2. DECLARADO pelo desenvolvedor — PC e Android. Mesma natureza do TargetFps: é
        //      alvo de projeto, e a aritmética em cima dele é honesta porque a escolha é
        //      dele. Pode sair do pico MEDIDO na sessão (botão "pico + 20%").
        //   3. SEM TETO                     — default. Mostra o valor, sem semáforo.
        //
        // DESCARTADO: perguntar ao Android via ActivityManager.getMemoryClass(). Chegou a
        // ser planejado, mas esse valor é o limite do heap JAVA (Dalvik/ART), e a memória
        // de um jogo Unity é quase toda NATIVA, fora desse heap. Usá-lo como teto do jogo
        // seria citar uma fonte que justifica outra coisa — exatamente o erro que a
        // regra de procedência proíbe. O Android mata app por pressão de memória do
        // sistema, não por um número publicado por app.
        //
        // Ninguém publica "quanto de RAM um jogo pode ocupar no Windows", e é por isso
        // que o PC cai no item 2 em vez de ganhar uma constante nova.

        // ── Mobile: sem catálogo ───────────────────────────────────────────
        // MOTIVO da REMOÇÃO: aqui havia 6 aparelhos Android (e antes, 4 iPhones junto).
        // Eles carregavam nome, ano e RAM de fábrica — nada inventado — mas também nada
        // que decidisse qualquer coisa: nenhum fabricante de celular publica orçamento
        // por modelo, então nenhum limiar saía dali.
        //
        // Com a medição no aparelho, quem responde é o que estiver conectado. Um seletor
        // que não muda resultado nenhum é pior que ausência: o dev troca de modelo,
        // espera outro número, e recebe o mesmo.
        //
        // Quando a etapa 2 ler a identificação do aparelho pelo Profiler, o alvo passa a
        // ser MEDIDO e não escolhido — e aí ele volta a aparecer na tela, vindo do cabo.

        // ── VR standalone (PCVR fica fora — usa a build PC) ────────────────
        // ── VR ────────────────────────────────────────────────────────────
        // Draw calls e memória dos Quest vêm da documentação da Meta, não de estimativa:
        //  - developers.meta.com/horizon/resources/device-optimization-comparison/
        //    Quest 2: < 100 draw calls, < 750K triângulos. Quest 3: < 200, < 1,5M.
        //  - developers.meta.com/horizon/documentation/unity/po-memory-ram/
        //    Limite por app, medido em PSS: Quest 2 e Quest Pro 4,4 GiB; Quest 3 e 3S 5,75 GiB.
        //    Estourar faz o SO matar o app sem aviso.
        //
        // CUIDADO com a fonte errada: a página /native/pc/dg-performance-guidelines/ cita
        // 500–1.000 draw calls, mas ela é de **PC VR**, não de Quest standalone. Usá-la aqui
        // inflaria o teto em 5×.
        public static readonly VrHeadsetSpec[] VrHeadsets =
        {
            new VrHeadsetSpec("quest2",        "Meta Quest 2 (2020)",      2020, 72, 100,  6,
                (long)(4.4 * GB),  "Meta (publicado): < 100 draw calls; 4,4 GiB por app (PSS)"),

            new VrHeadsetSpec("quest3",        "Meta Quest 3 (2023)",      2023, 90, 200,  8,
                (long)(5.75 * GB), "Meta (publicado): < 200 draw calls; 5,75 GiB por app (PSS)"),

            // MOTIVO: a Meta publica o limite de MEMÓRIA do 3S (5,75 GiB, mesmo do Quest 3),
            // mas não publica draw calls dele separadamente. O 200 abaixo é INFERÊNCIA a
            // partir do Quest 3, com quem ele compartilha o SoC — está marcado como tal e
            // não deve ser apresentado como número publicado.
            new VrHeadsetSpec("quest3s",       "Meta Quest 3S (2024)",     2024, 90, 200,  8,
                (long)(5.75 * GB), "Meta (publicado): 5,75 GiB por app (PSS). Draw calls: inferido do Quest 3 (mesmo SoC)"),

            // ❌ SEM FONTE PUBLICADA — não foi encontrado budget oficial de draw calls nem
            // limite de memória por app da Pico nem da HTC. Valores herdados da estimativa
            // original; AppMemoryBytes = 0 faz cair na fração de RAM, que também é estimativa.

        };


        // ── Listas ordenadas para a UI ─────────────────────────────────────
        // MOTIVO: o dev monta a build escolhendo o PIOR hardware que pretende suportar,
        // e procurar isso numa lista ordenada por outra coisa é trabalho manual. Então
        // as listas vão do menor para o maior desempenho.
        //
        // Onde há índice publicado a ordem é literal: G3D nas GPUs, single thread nas
        // CPUs, capacidade nas memórias. A ordem por fatia da Steam continua existindo no
        // DADO (o SteamRank está no rótulo de cada placa), só não manda mais na ordenação.
        //
        // RESSALVA nos aparelhos Mobile: eles NÃO têm índice comparável — é a mesma
        // lacuna que impede o veredito de CPU/GPU ali. Ordenar por ano é cronologia, não
        // desempenho, e um aparelho mais novo pode ser mais fraco que um mais velho de
        // linha superior. Está ordenado por ano e DITO na UI, em vez de insinuar um
        // ranking que não temos como sustentar.

        private static GpuSpec[] _gpusAsc;
        private static CpuSpec[] _cpusAsc;
        private static int[] _ramAsc;
        private static VrHeadsetSpec[] _vrAsc;

        /// <summary>GPUs from weakest to strongest, by published G3D Mark.</summary>
        public static GpuSpec[] GpusAscending
        {
            get
            {
                if (_gpusAsc == null)
                {
                    _gpusAsc = (GpuSpec[])Gpus.Clone();
                    Array.Sort(_gpusAsc, (a, b) => a.G3dRating.CompareTo(b.G3dRating));
                }
                return _gpusAsc;
            }
        }

        /// <summary>CPUs from weakest to strongest, by published single thread rating.</summary>
        public static CpuSpec[] CpusAscending
        {
            get
            {
                if (_cpusAsc == null)
                {
                    _cpusAsc = (CpuSpec[])Cpus.Clone();
                    Array.Sort(_cpusAsc, (a, b) => a.SingleThreadRating.CompareTo(b.SingleThreadRating));
                }
                return _cpusAsc;
            }
        }

        /// <summary>RAM capacities from smallest to largest.</summary>
        public static int[] RamGbAscending
        {
            get
            {
                if (_ramAsc == null)
                {
                    _ramAsc = (int[])RamGbOptions.Clone();
                    Array.Sort(_ramAsc);
                }
                return _ramAsc;
            }
        }

        /// <summary>Headsets from weakest to strongest, by the draw-call budget Meta publishes.</summary>
        /// <remarks>
        /// MOTIVO: aqui existe um número publicado que ordena — o Quest 2 recebe menos de
        /// 100 draw calls e o Quest 3 menos de 200, e isso é a Meta dizendo qual aguenta
        /// mais. O Quest 3S empata com o Quest 3 porque compartilha o SoC, e o desempate
        /// por ano só serve para a ordem ficar estável.
        /// </remarks>
        public static VrHeadsetSpec[] VrHeadsetsAscending
        {
            get
            {
                if (_vrAsc == null)
                {
                    _vrAsc = (VrHeadsetSpec[])VrHeadsets.Clone();
                    Array.Sort(_vrAsc, (a, b) =>
                    {
                        int byBudget = a.DrawCallBudget.CompareTo(b.DrawCallBudget);
                        return byBudget != 0 ? byBudget : a.Year.CompareTo(b.Year);
                    });
                }
                return _vrAsc;
            }
        }
        // ── Lookups (linear é suficiente pra ~25 entradas; roda só na UI/load)
        public static bool TryGetCpu(string id, out CpuSpec spec)
        {
            for (int i = 0; i < Cpus.Length; i++)
                if (Cpus[i].Id == id) { spec = Cpus[i]; return true; }
            spec = default; return false;
        }

        public static bool TryGetGpu(string id, out GpuSpec spec)
        {
            for (int i = 0; i < Gpus.Length; i++)
                if (Gpus[i].Id == id) { spec = Gpus[i]; return true; }
            spec = default; return false;
        }

        public static bool TryGetRamGen(RamType type, out RamGenSpec spec)
        {
            for (int i = 0; i < RamGens.Length; i++)
                if (RamGens[i].Type == type) { spec = RamGens[i]; return true; }
            spec = default; return false;
        }

        public static bool TryGetVr(string id, out VrHeadsetSpec spec)
        {
            for (int i = 0; i < VrHeadsets.Length; i++)
                if (VrHeadsets[i].Id == id) { spec = VrHeadsets[i]; return true; }
            spec = default; return false;
        }
    }
}
