using UnityEditor;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Resolves the single-thread performance index of the machine the Editor runs on,
    /// so a measured draw-call cost can be projected onto target hardware.
    /// </summary>
    /// <remarks>
    /// MOTIVO: o <see cref="HardwareCatalog"/> lista hardware ALVO — peças legadas que o
    /// jogo precisa atender. A máquina de desenvolvimento nunca está lá, por construção.
    /// Reaproveitar aquele dropdown para perguntar "qual é a sua CPU" foi erro de desenho.
    ///
    /// **Por que uma tabela embutida e não consulta automática ao site da PassMark:**
    /// tecnicamente a página por CPU devolve HTML com o rating e daria pra raspar. Mas a
    /// PassMark não oferece API pública (vendem dump CSV licenciado) e trata os dados como
    /// uso pessoal. O plugin será publicado sob MIT — distribuir uma ferramenta que raspa
    /// o site deles é passivo jurídico, além de frágil, já que hoje o site renderiza as
    /// tabelas por jQuery. Números publicados PODEM ser embutidos com atribuição, que é
    /// prática normal e mantém o plugin offline.
    ///
    /// Fonte de todos os valores: PassMark Single Thread Rating, cpubenchmark.net,
    /// consultados em 2026-09-18.
    /// </remarks>
    public static class CpuPerformanceIndex
    {
        private const string MANUAL_PREF = "PerformanceAdvisor_DevCpuRating";

        private struct Entry
        {
            public readonly string Key;    // trecho distintivo do nome, em maiúsculas
            public readonly int Rating;
            public Entry(string key, int rating) { Key = key; Rating = rating; }
        }

        // MOTIVO: chaves são o trecho que identifica o modelo, não o nome completo — o
        // SystemInfo.processorType varia ("12-Core Processor", "@ 3.00GHz", "CPU"…).
        // A busca testa as chaves MAIS LONGAS primeiro, senão "RYZEN 9 7900X" casaria
        // antes de "RYZEN 9 7900X3D" e devolveria o índice errado.
        private static readonly Entry[] TABLE =
        {
            // ── AMD ──────────────────────────────────────────────────────
            new Entry("RYZEN 9 9950X3D", 4737), new Entry("RYZEN 9 9900X3D", 4629),
            new Entry("RYZEN 7 9850X3D", 4705), new Entry("RYZEN 9 9950X",   4727),
            new Entry("RYZEN 9 9900X",   4672), new Entry("RYZEN 7 9700X",   4643),
            new Entry("RYZEN 9 7950X",   4252), new Entry("RYZEN 9 7900X",   4226),
            new Entry("RYZEN 7 7800X3D", 3759), new Entry("RYZEN 7 5800X",   3448),
            new Entry("RYZEN 5 5600G",   3178), new Entry("RYZEN 5 5600",    3253),
            new Entry("RYZEN 5 3600",    2557), new Entry("RYZEN 3 3100",    2410),
            new Entry("RYZEN 5 2600",    2234),

            // ── Intel Core Ultra ─────────────────────────────────────────
            new Entry("CORE ULTRA 9 285K", 5086), new Entry("CORE ULTRA 7 265K", 4927),
            new Entry("CORE ULTRA 5 245K", 4715),

            // ── Intel Core ───────────────────────────────────────────────
            new Entry("I9-14900KS", 4809), new Entry("I9-14900KF", 4688),
            new Entry("I9-14900K",  4690), new Entry("I9-13900KS", 4715),
            new Entry("I9-13900KF", 4580), new Entry("I9-13900K",  4595),
            new Entry("I5-12400",   3465), new Entry("I7-8700K",   2714),
            new Entry("I5-8400",    2428), new Entry("I7-4790K",   2464),
            new Entry("I5-4460",    1902), new Entry("I7-2600K",   1740),
            new Entry("I5-2500K",   1698), new Entry("I3-2100",    1410),
            new Entry("E8400",       1235), new Entry("Q6600",      953),
            new Entry("E2180",        744),

            // ── Apple ────────────────────────────────────────────────────
            // MOTIVO: prefixo "APPLE " obrigatório. Chaves como "M3" sozinhas têm 2
            // caracteres e casariam por acidente dentro de outros nomes de CPU.
            new Entry("APPLE M5",     5755), new Entry("APPLE M3 MAX", 4780),
            new Entry("APPLE M3 PRO", 4742), new Entry("APPLE M4 MAX", 4590),
            new Entry("APPLE M3",     4707),
        };

        /// <summary>Rating the user typed by hand. Zero when never set.</summary>
        public static int ManualRating
        {
            get => EditorPrefs.GetInt(MANUAL_PREF, 0);
            set => EditorPrefs.SetInt(MANUAL_PREF, value);
        }

        /// <summary>Raw CPU name as the OS reports it.</summary>
        public static string RawProcessorName => SystemInfo.processorType ?? "";

        /// <summary>
        /// Single-thread index of this machine: the table match, or the manual value
        /// when nothing matched. Zero means "unknown" — callers must not project.
        /// </summary>
        public static int DevRating
        {
            get
            {
                int detected = Detect();
                if (detected > 0) return detected;
                return ManualRating;
            }
        }

        /// <summary>True when the index came from the built-in table, with no user input.</summary>
        public static bool WasAutoDetected => Detect() > 0;

        /// <summary>Table lookup against <see cref="RawProcessorName"/>. Zero when no match.</summary>
        public static int Detect()
        {
            string name = Normalize(RawProcessorName);
            if (string.IsNullOrEmpty(name)) return 0;

            int best = 0, bestLen = 0;
            for (int i = 0; i < TABLE.Length; i++)
            {
                string key = TABLE[i].Key;
                if (key.Length <= bestLen) continue;       // já temos casamento mais específico
                if (name.IndexOf(key, System.StringComparison.Ordinal) >= 0)
                {
                    best = TABLE[i].Rating;
                    bestLen = key.Length;
                }
            }
            return best;
        }

        /// <summary>PassMark page for this exact CPU, for the manual fallback.</summary>
        public static string LookupUrl =>
            "https://www.cpubenchmark.net/cpu.php?cpu=" + UnityWebRequestEscape(RawProcessorName);

        // Uppercase + colapsa espaços. Mantém dígitos e hífens, que são o que distingue
        // os modelos ("I9-14900K" vs "I9-13900K").
        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            bool lastSpace = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = char.ToUpperInvariant(s[i]);
                if (c == ' ')
                {
                    if (!lastSpace && sb.Length > 0) sb.Append(' ');
                    lastSpace = true;
                }
                else { sb.Append(c); lastSpace = false; }
            }
            return sb.ToString().Trim();
        }

        private static string UnityWebRequestEscape(string s) =>
            UnityEngine.Networking.UnityWebRequest.EscapeURL(s ?? "");
    }
}
