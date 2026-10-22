using UnityEditor;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Resolves the G3D index of the GPU this Editor renders on, mirroring
    /// <see cref="CpuPerformanceIndex"/>.
    /// </summary>
    /// <remarks>
    /// MOTIVO: o mesmo raciocínio da CPU — o catálogo lista hardware ALVO, e a placa da
    /// máquina de desenvolvimento não está lá.
    ///
    /// **Este caso é menos confiável que o da CPU, e isso é estrutural.** Máquinas com
    /// processador que tem gráficos integrados expõem DUAS placas, e a integrada costuma
    /// reportar nome genérico. Nesta máquina de desenvolvimento, por exemplo, o sistema
    /// lista "AMD Radeon(TM) Graphics" (a iGPU do Ryzen 9 7900X, nome que não identifica
    /// modelo algum) antes da "NVIDIA GeForce RTX 5070 Ti".
    ///
    /// Usamos <c>SystemInfo.graphicsDeviceName</c>, que reporta a placa em que a Unity
    /// está efetivamente renderizando — e não a enumeração do sistema. Ainda assim, nomes
    /// genéricos de integradas são esperados, então o caminho manual **não é exceção
    /// rara**: a UI precisa apresentá-lo como alternativa normal, não como erro.
    ///
    /// Fonte dos valores: PassMark G3D Mark, videocardbenchmark.net, 2026-09-19.
    /// </remarks>
    public static class GpuPerformanceIndex
    {
        private const string MANUAL_PREF = "PerformanceAdvisor_DevGpuRating";

        private struct Entry
        {
            public readonly string Key;
            public readonly int Rating;
            public Entry(string key, int rating) { Key = key; Rating = rating; }
        }

        // Chaves testadas da MAIS LONGA para a mais curta, senão "RTX 5070" casaria antes
        // de "RTX 5070 TI" e devolveria o índice errado.
        private static readonly Entry[] TABLE =
        {
            // NVIDIA — desenvolvimento
            new Entry("RTX 5070 TI", 32324), new Entry("RTX 4060", 19486),
            new Entry("RTX 3060",    16705), new Entry("RTX 2060", 14074),
            new Entry("GTX 1650",     7866), new Entry("GTX 1060", 10022),
            new Entry("GTX 1050 TI",  6366), new Entry("GTX 970",   9634),
            new Entry("GTX 960",      6150), new Entry("GTX 750 TI", 3900),
            new Entry("GTX 560 TI",   3083), new Entry("GTX 460",    2266),
            new Entry("GTX 650",      1757), new Entry("GTX 260",    1197),
            new Entry("8800 GT",       452),

            // AMD
            new Entry("RX 6600", 15049), new Entry("RX 580", 8782),
            new Entry("HD 5770",  1346),

            // Integradas
            new Entry("UHD GRAPHICS 770", 1908), new Entry("UHD GRAPHICS 730", 1544),
            new Entry("UHD GRAPHICS 630", 1225), new Entry("HD GRAPHICS 3000",  261),
            new Entry("HD GRAPHICS 2000",  213),
        };

        /// <summary>G3D the user typed by hand. Zero when never set.</summary>
        public static int ManualRating
        {
            get => EditorPrefs.GetInt(MANUAL_PREF, 0);
            set => EditorPrefs.SetInt(MANUAL_PREF, value);
        }

        /// <summary>GPU name as Unity reports it — the one actually rendering.</summary>
        public static string RawDeviceName => SystemInfo.graphicsDeviceName ?? "";

        /// <summary>Table match, or the manual value. Zero means unknown.</summary>
        public static int DevRating
        {
            get
            {
                int detected = Detect();
                return detected > 0 ? detected : ManualRating;
            }
        }

        /// <summary>True when the index came from the table, with no user input.</summary>
        public static bool WasAutoDetected => Detect() > 0;

        /// <summary>Table lookup against <see cref="RawDeviceName"/>. Zero when no match.</summary>
        public static int Detect()
        {
            string name = Normalize(RawDeviceName);
            if (string.IsNullOrEmpty(name)) return 0;

            int best = 0, bestLen = 0;
            for (int i = 0; i < TABLE.Length; i++)
            {
                string key = TABLE[i].Key;
                if (key.Length <= bestLen) continue;
                if (name.IndexOf(key, System.StringComparison.Ordinal) >= 0)
                {
                    best = TABLE[i].Rating;
                    bestLen = key.Length;
                }
            }
            return best;
        }

        /// <summary>PassMark page for this GPU, for the manual fallback.</summary>
        public static string LookupUrl =>
            "https://www.videocardbenchmark.net/gpu_list.php";

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
    }
}
