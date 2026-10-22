using UnityEditor;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Projects times measured on the developer's machine onto the target hardware.
    /// </summary>
    /// <remarks>
    /// MOTIVO: a pergunta do plugin é "cabe em X ms no alvo?". A resposta direta disso é
    /// TEMPO, e o alvo é hardware que o desenvolvedor não possui. Então se mede aqui e se
    /// projeta, usando índices de desempenho publicados como fator de conversão.
    ///
    ///     CPU: t_alvo = t_medido × (índice CPU dev ÷ índice CPU alvo)
    ///     GPU: t_alvo = t_medido × (pixels alvo ÷ pixels dev) × (G3D dev ÷ G3D alvo)
    ///
    /// O fator de pixels existe só na GPU porque só o trabalho dela escala com resolução;
    /// lógica de jogo e submissão de draw não.
    ///
    /// **LIMITE (2026-09-19, revisado em 2026-09-20):** esta projeção só vale para alvos
    /// **PC**, mas o motivo é mais estreito do que a primeira versão deste comentário
    /// afirmava.
    ///
    /// A afirmação original era que "não existe índice publicado que compare SoC móvel com
    /// desktop". **Isso está errado e a pesquisa desmentiu:** o 3DMark Wild Life é
    /// explicitamente cross-platform — roda em Android, iOS e Windows, e a UL declara que
    /// os scores são comparáveis entre plataformas. Tabelas públicas mostram Mali e Adreno
    /// no mesmo eixo de placas de desktop.
    ///
    /// O que falta é **cobertura curada**, não escala. A PassMark publica ranking por peça;
    /// a UL publica resultados por SUBMISSÃO, que variam com aparelho, driver e estado
    /// térmico. E há duas variantes de escalas incompatíveis (Wild Life e Wild Life
    /// Extreme), fáceis de misturar.
    ///
    /// **Decisão do autor, revisada em 2026-09-20: o G3D FICA.** Uma decisão anterior deste
    /// mesmo dia dizia "migrar tudo para Wild Life"; ela foi tomada antes da reconstrução
    /// do catálogo e não sobrevive a ela. O catálogo passou a ter 45 GPUs vindas do top 50
    /// da Steam Hardware Survey, e a PassMark publica G3D para 45 delas. A UL não publica
    /// nada equivalente para esse conjunto — trocar a régua custaria 45 lacunas para
    /// resolver 13, o que é pior do que o problema.
    ///
    /// O Wild Life continua sendo a saída certa para Mobile e VR, porque lá não existe
    /// alternativa. Mas migrar as duas pontas para ele exigiria valores por aparelho que
    /// hoje não estão levantados. Enquanto não estiverem:
    ///
    ///  - **PC:** veredito de CPU e GPU por projeção, régua PassMark nos dois lados.
    ///  - **Mobile e VR:** **sem veredito de CPU nem de GPU.** O de memória continua
    ///    valendo, porque ali o limite é publicado pelo fabricante e não depende de
    ///    projeção nenhuma.
    ///
    /// Prever tempo em aparelho móvel exige medir no aparelho — que é exatamente a etapa
    /// do profiler em dispositivo, e é ela que fecha esse buraco sem depender de régua
    /// emprestada.
    ///
    /// Aproximações declaradas, válidas mesmo em PC:
    ///  - CPU: assume que trabalho de main thread escala com o índice single-thread.
    ///    Lógica, física e submissão escalam de formas parecidas, não idênticas.
    ///  - GPU: assume tempo linear em pixels e inverso ao G3D. G3D é benchmark composto,
    ///    não medida por carga; cena dominada por preenchimento e cena dominada por
    ///    geometria escalam diferente entre placas.
    ///
    /// Servem para ORIENTAR, não para afirmar que o jogo roda.
    /// </remarks>
    public static class PerformanceProjection
    {
        /// <summary>Why a projection could not be produced. Empty means it could.</summary>
        public enum Status
        {
            Ok,
            SemIndiceDev,        // não sabemos o índice da máquina do desenvolvedor
            SemIndiceAlvo,       // a peça alvo não tem índice publicado
            PlataformaSemEscala  // mobile/VR: não existe índice comparável ao de desktop
        }

        /// <summary>
        /// Projects a CPU time. Returns the projected ms, or 0 with a non-Ok status.
        /// </summary>
        public static float ProjectCpuMs(float measuredMs, string targetCpuId, out Status status)
        {
            int dev = CpuPerformanceIndex.DevRating;
            if (dev <= 0) { status = Status.SemIndiceDev; return 0f; }

            if (!HardwareCatalog.TryGetCpu(targetCpuId, out var target) || target.SingleThreadRating <= 0)
            { status = Status.SemIndiceAlvo; return 0f; }

            status = Status.Ok;
            return measuredMs * ((float)dev / target.SingleThreadRating);
        }

        /// <summary>
        /// Projects a GPU time, scaling by both pixel count and raw GPU power.
        /// </summary>
        public static float ProjectGpuMs(float measuredMs, string targetGpuId,
                                         long devPixels, long targetPixels, out Status status)
        {
            int dev = GpuPerformanceIndex.DevRating;
            if (dev <= 0) { status = Status.SemIndiceDev; return 0f; }

            if (!HardwareCatalog.TryGetGpu(targetGpuId, out var target) || target.G3dRating <= 0)
            { status = Status.SemIndiceAlvo; return 0f; }

            if (devPixels <= 0 || targetPixels <= 0) { status = Status.SemIndiceAlvo; return 0f; }

            status = Status.Ok;
            float pixelFactor = (float)targetPixels / devPixels;
            float powerFactor = (float)dev / target.G3dRating;
            return measuredMs * pixelFactor * powerFactor;
        }

        /// <summary>Human-readable explanation of a non-Ok status, for UI and report.</summary>
        public static string Explain(Status status, TargetPlatform platform)
        {
            switch (status)
            {
                case Status.Ok:
                    return "";
                case Status.PlataformaSemEscala:
                    // MOTIVO: a redação anterior dizia que "não existe índice que compare
                    // SoC móvel com desktop". É falso — o 3DMark Wild Life compara. O que
                    // falta é levantamento por aparelho na MESMA régua usada no PC, e a
                    // mensagem precisa dizer o que de fato falta, não uma impossibilidade
                    // inventada que o avaliador derruba com uma busca.
                    return Loc.T(
                        $"sem veredito para {platform}: os índices deste catálogo são PassMark, " +
                        "que não cobre SoC móvel. Existe régua cross-platform (3DMark Wild Life), " +
                        "mas os valores por aparelho ainda não foram levantados. Medir no aparelho resolve.",
                        $"no verdict for {platform}: this catalog's indices are PassMark, " +
                        "which does not cover mobile SoCs. A cross-platform benchmark exists (3DMark Wild Life), " +
                        "but per-device values have not been collected yet. Measuring on the device solves it.",
                        $"sin veredicto para {platform}: los índices de este catálogo son PassMark, " +
                        "que no cubre SoC móviles. Existe una referencia multiplataforma (3DMark Wild Life), " +
                        "pero aún no se recopilaron los valores por dispositivo. Medir en el dispositivo lo resuelve.");
                case Status.SemIndiceDev:
                    return Loc.T("sem veredito: não foi possível identificar o desempenho desta máquina.",
                                 "no verdict: could not identify this machine's performance.",
                                 "sin veredicto: no se pudo identificar el rendimiento de esta máquina.");
                case Status.SemIndiceAlvo:
                    return Loc.T("sem veredito: a peça alvo não tem índice publicado.",
                                 "no verdict: the target part has no published index.",
                                 "sin veredicto: la pieza objetivo no tiene índice publicado.");
                default:
                    return Loc.T("sem veredito.", "no verdict.", "sin veredicto.");
            }
        }

        /// <summary>Pixels rendered per frame by whatever is being measured.</summary>
        /// <remarks>
        /// MOTIVO: é a resolução em que a medição de GPU aconteceu. Sem ela o fator de
        /// pixels não tem denominador.
        ///
        /// E ela depende de ONDE a medição acontece, o que a primeira versão ignorava:
        /// <c>Screen.width/height</c> no Editor é o tamanho da Game view, que não tem
        /// relação nenhuma com a janela de uma build standalone. Medindo na build, o
        /// tempo de GPU vem de um processo renderizando em OUTRA resolução, e dividir
        /// pelos pixels da Game view produzia um fator silenciosamente errado — o
        /// veredito de GPU ficava escalado por um número arbitrário.
        ///
        /// Na build, quem manda é a resolução que a própria build usa, e isso é
        /// especificação do projeto: <c>PlayerSettings.defaultScreenWidth/Height</c>. É o
        /// mesmo valor que o Unity aplica ao abrir o player, então os dois lados da conta
        /// passam a falar da mesma tela.
        /// </remarks>
        public static long DevPixels()
        {
            Vector2Int px = DevResolution(out _);
            if (px.x <= 0 || px.y <= 0) return 0;
            return (long)px.x * px.y;
        }

        /// <summary>Resolution the measured process renders at, and where that number came from.</summary>
        /// <remarks>
        /// MOTIVO (Editor): a primeira versão lia <c>Screen.width/height</c>. Em código de
        /// Editor isso NÃO é a Game view: é o tamanho da view que está sendo desenhada no
        /// momento — e o fator era calculado ao recarregar o domínio ou quando o dev mexia
        /// na aba Build, ou seja, com a JANELA DO PLUGIN como denominador. O rótulo dizia
        /// "Game view 520×780" e o fator de GPU saía multiplicado por um número arbitrário.
        /// <c>Handles.GetMainGameViewSize</c> lê a Game view de fato.
        ///
        /// MOTIVO (build): <c>defaultScreenWidth/Height</c> só valem com o player em
        /// JANELA. Em tela cheia — o padrão de uma build standalone — o player abre na
        /// resolução do monitor e ignora esses campos. Nesse caso o denominador certo é a
        /// resolução do monitor desta máquina, que é onde a build roda.
        /// </remarks>
        public static Vector2Int DevResolution(out string source)
        {
            if (HardwareBuildSettings.ActiveTestMode == TestMode.NaBuild)
            {
                if (PlayerSettings.fullScreenMode == FullScreenMode.Windowed)
                {
                    int bw = PlayerSettings.defaultScreenWidth;
                    int bh = PlayerSettings.defaultScreenHeight;
                    if (bw > 0 && bh > 0)
                    {
                        source = Loc.T($"build em janela {bw}×{bh}", $"windowed build {bw}×{bh}",
                                       $"build en ventana {bw}×{bh}");
                        return new Vector2Int(bw, bh);
                    }
                }
                else
                {
                    Resolution r = Screen.currentResolution;
                    if (r.width > 0 && r.height > 0)
                    {
                        source = Loc.T($"build em tela cheia {r.width}×{r.height} (monitor)",
                                       $"fullscreen build {r.width}×{r.height} (monitor)",
                                       $"build a pantalla completa {r.width}×{r.height} (monitor)");
                        return new Vector2Int(r.width, r.height);
                    }
                }
                // Sem resolução utilizável da build, cai na Game view — declarado no
                // rótulo, não silencioso.
            }

            Vector2 gv = Handles.GetMainGameViewSize();
            int w = Mathf.RoundToInt(gv.x);
            int h = Mathf.RoundToInt(gv.y);
            source = $"Game view {w}×{h}";
            return new Vector2Int(w, h);
        }

        /// <summary>Human-readable description of where <see cref="DevPixels"/> came from.</summary>
        /// <remarks>
        /// MOTIVO: o fator de GPU aparece na UI como "×5,03 (1920×1080 …)". Se o
        /// denominador mudou de fonte, o rótulo tem que dizer — senão o dev confere a
        /// conta contra a Game view e não fecha.
        /// </remarks>
        public static string DevPixelsSource()
        {
            DevResolution(out string source);
            return source;
        }
    }
}
