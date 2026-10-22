using UnityEditor;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Derives a draw-call ceiling for the target hardware from measured cost and
    /// published performance indices, instead of a hand-written number.
    /// </summary>
    /// <remarks>
    /// MOTIVO: draw call é custo de **CPU**, não propriedade da GPU. Pendurar um teto de
    /// draw calls numa placa de vídeo é o mesmo erro que pendurar orçamento de frame numa
    /// CPU — a placa não é quem paga a conta.
    ///
    /// Pesquisa feita em 2026-09-18: **só a Meta publica teto de draw call por aparelho**,
    /// e só porque o Quest é plataforma fechada com certificação de app. Todos os outros se
    /// recusam de propósito e mandam derivar do tempo:
    ///  - Unity, docs.unity3d.com/Manual/optimizing-draw-calls.html — sem número universal;
    ///    manda manter os draw calls dentro do orçamento de tempo de frame.
    ///  - Arm, "GPU Processing Budget Approach to Game Development" — calcular o orçamento
    ///    do aparelho alvo, não consultar tabela.
    ///  - Qualcomm, Snapdragon Game Toolkit — boas práticas, sem números por tier.
    ///  - Apple — os "4.000 draw calls" do Metal são TETO DE API, não budget saudável.
    ///
    /// Então o número não existe por decisão da indústria, e o método correto é o que os
    /// próprios fornecedores prescrevem:
    ///
    ///     teto = (ms do frame × fatia de submissão) ÷ custo por draw call no alvo
    ///     custo no alvo = custo medido no dev × (índice do dev ÷ índice do alvo)
    ///
    /// Procedência de cada insumo:
    ///  - ms do frame ....... aritmética, 1000/FPS alvo
    ///  - custo por draw .... MEDIDO pelo plugin na máquina do dev
    ///  - índices ........... PassMark single-thread, publicado
    ///  - fatia de submissão. assunção DECLARADA (constante abaixo, exibida na UI)
    ///
    /// Sem calibração medida o modelo devolve 0, que significa "sem teto" — o indicador
    /// mostra o número e fica sem semáforo. Um campo honestamente vazio é preferível a um
    /// limiar inventado.
    /// </remarks>
    public static class DrawCallBudgetModel
    {
        /// <summary>
        /// Share of the frame assumed to be available for submitting draw calls.
        /// </summary>
        /// <remarks>
        /// MOTIVO: é a única ASSUNÇÃO do modelo, e por isso fica explícita, nomeada e
        /// visível na UI em vez de embutida numa constante anônima. 25% parte de que o
        /// frame ainda precisa acomodar lógica de jogo, física, animação e o resto da
        /// renderização. Quem discordar pode ajustar sabendo exatamente o que muda.
        /// </remarks>
        public const float DRAW_SUBMISSION_SHARE = 0.25f;

        private const string COST_PREF    = "PerformanceAdvisor_DrawCostUs";

        /// <summary>
        /// Measured CPU cost of one draw call on this machine, in microseconds.
        /// Zero means "never calibrated".
        /// </summary>
        public static float MeasuredCostUs
        {
            get => EditorPrefs.GetFloat(COST_PREF, 0f);
            set => EditorPrefs.SetFloat(COST_PREF, value);
        }

        /// <summary>
        /// Ceiling for any counted indicator whose unit cost was measured: how many of it
        /// fit in the share of the frame reserved for submission, projected onto the target.
        /// </summary>
        /// <remarks>
        /// MOTIVO: o limiar de um indicador contado nunca é a contagem em si — é quanto
        /// do frame aquela contagem consome.
        /// </remarks>
        public static int ComputeFromUnitCost(float frameBudgetMs, string targetCpuId, float unitCostUs)
        {
            int devRating = CpuPerformanceIndex.DevRating;
            if (unitCostUs <= 0f || devRating <= 0) return 0;
            if (!HardwareCatalog.TryGetCpu(targetCpuId, out var target)) return 0;
            if (target.SingleThreadRating <= 0) return 0;

            float costOnTargetUs = unitCostUs * ((float)devRating / target.SingleThreadRating);
            if (costOnTargetUs <= 0f) return 0;

            return (int)(frameBudgetMs * 1000f * DRAW_SUBMISSION_SHARE / costOnTargetUs);
        }

        /// <summary>True when there is enough data to derive a ceiling.</summary>
        public static bool IsCalibrated =>
            MeasuredCostUs > 0f && CpuPerformanceIndex.DevRating > 0;

        /// <summary>
        /// Draw-call ceiling for <paramref name="targetCpuId"/>, or 0 when uncalibrated.
        /// </summary>
        public static int Compute(float frameBudgetMs, string targetCpuId)
        {
            int devRating = CpuPerformanceIndex.DevRating;
            if (MeasuredCostUs <= 0f || devRating <= 0) return 0;
            if (!HardwareCatalog.TryGetCpu(targetCpuId, out var target)) return 0;
            if (target.SingleThreadRating <= 0) return 0;

            // Índice maior = CPU mais rápida, então o custo escala pelo INVERSO da razão.
            float costOnTargetUs = MeasuredCostUs * ((float)devRating / target.SingleThreadRating);
            if (costOnTargetUs <= 0f) return 0;

            float budgetUs = frameBudgetMs * 1000f * DRAW_SUBMISSION_SHARE;
            return (int)(budgetUs / costOnTargetUs);
        }

        /// <summary>
        /// One-line provenance of a derived ceiling. Only meaningful when
        /// <see cref="Compute"/> returned a value — see <see cref="WhyNotDerived"/> otherwise.
        /// </summary>
        public static string Explain(string targetCpuId)
        {
            if (!HardwareCatalog.TryGetCpu(targetCpuId, out var target)) return "";

            string conta = $"({CpuPerformanceIndex.DevRating} ÷ {target.SingleThreadRating} PassMark)";
            float share = DRAW_SUBMISSION_SHARE * 100;
            return Loc.T($"derivado: {MeasuredCostUs:F2} µs/draw medidos aqui × {conta}, em {share:F0}% do frame",
                         $"derived: {MeasuredCostUs:F2} µs/draw measured here × {conta}, in {share:F0}% of the frame",
                         $"derivado: {MeasuredCostUs:F2} µs/draw medidos aquí × {conta}, en el {share:F0}% del frame");
        }

        /// <summary>
        /// Why no ceiling could be derived, phrased as the missing step — never as
        /// "no ceiling", because the caller still falls back to an estimate.
        /// </summary>
        /// <remarks>
        /// MOTIVO: antes esta string começava com "sem teto", e o chamador a concatenava
        /// depois de "ESTIMATIVA sem fonte". O resultado na tela dizia ter e não ter teto
        /// na mesma frase. Aqui devolve só o passo que falta; quem compõe é o chamador.
        /// </remarks>
        public static string WhyNotDerived(string targetCpuId)
        {
            if (CpuPerformanceIndex.DevRating <= 0)
                return Loc.T($"não reconheci a CPU desta máquina ({CpuPerformanceIndex.RawProcessorName}); " +
                             "informe o índice dela acima",
                             $"I did not recognize this machine's CPU ({CpuPerformanceIndex.RawProcessorName}); " +
                             "enter its index above",
                             $"no reconocí la CPU de esta máquina ({CpuPerformanceIndex.RawProcessorName}); " +
                             "indica su índice arriba");
            if (MeasuredCostUs <= 0f)
                return Loc.T("a calibração automática não produziu resultado nesta máquina; tente o botão Recalibrar",
                             "automatic calibration produced no result on this machine; try the Recalibrate button",
                             "la calibración automática no produjo resultado en esta máquina; prueba el botón Recalibrar");
            if (!HardwareCatalog.TryGetCpu(targetCpuId, out _))
                return Loc.T("a CPU alvo não está no catálogo", "the target CPU is not in the catalog",
                             "la CPU objetivo no está en el catálogo");

            return "";
        }
    }
}
