using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace PerformanceAdvisor.Artigo
{
    /// <summary>One telemetry sample of a test case, with the raw values the plugin measured.</summary>
    [Serializable]
    public class ArtigoSample
    {
        public string array;            // cpu_samples, gc_samples, drawcalls_samples, memory_samples, gpu_samples
        public int    frame;
        public float  cpuRawMs;         // medido nesta máquina; o payload leva × fator
        public long   gcBytes;
        public float  gcCollectMs;
        public int    drawCalls;
        public long   memBytes;
        public float  gpuRawMs;
        public long   tMs;
        public string culpritStack;     // vazio = ausente
        public int    occurrences;
    }

    /// <summary>
    /// A frozen test case: the payload the plugin sent for one selection of snapshots,
    /// plus the answer key computed from it.
    /// </summary>
    /// <remarks>
    /// MOTIVO: o caso guarda os VALORES, não só o texto. O texto enviado (userText) é
    /// regenerado pelo mesmo formatador do plugin (GeminiRestBridge.FormatUserText) a partir
    /// destes campos, e o validador confere o laudo contra os mesmos campos — texto e
    /// gabarito não podem divergir porque saem da mesma fonte.
    /// </remarks>
    [Serializable]
    public class ArtigoCase
    {
        public string id;               // ex.: G1_CPU_01
        public string grupo;            // G1_CPU, G2_GC, G3_DRAWCALLS, G4_GPU, G5_FALTANTE, G6_ENGANOSA
        public string origem;           // "captura" ou "derivado de <id>"
        public string cena;
        public string parametros;
        public string criadoEm;

        public string descricao;        // hardware_budget
        public float  orcamentoMs;
        public int    tetoDrawCalls;    // 0 = sem teto
        public long   tetoMemoriaBytes; // 0 = sem teto
        public float  fatorCpu;
        public float  fatorGpu;
        public string medidoEm;
        public string contadorDraw;
        public string contadorMemoria;
        public bool   memResolvido = true;
        public bool   gpuResolvido = true;

        public int    vCpu, vGc, vDraw, vGpu, vMem;
        public float  vCpuMaxMs, vGpuMaxMs;
        public long   vGcMaxBytes, vMemMaxBytes;
        public int    vDrawMax;

        public List<ArtigoSample> amostras = new List<ArtigoSample>();

        /// <summary>Source of the project scripts named in the stacks, as the plugin attaches it.</summary>
        public List<ScriptSource> fontes = new List<ScriptSource>();

        public string userText;         // exatamente o que vai no turno do usuário

        // ── Gabarito ────────────────────────────────────────────────────────
        public int  esperadoAchados;
        public bool esperadoIndeterminacao;   // G5: a resposta certa é "não é possível determinar"
        public bool esperadoSemEstouro;       // G5 e G6: nenhuma métrica acima de um teto existente

        // ── Conversões ──────────────────────────────────────────────────────

        public static FrameSnapshot ToSnapshot(ArtigoSample s) => new FrameSnapshot
        {
            FrameIndex       = s.frame,
            CpuLatencyMs     = s.cpuRawMs,
            GcAllocatedBytes = s.gcBytes,
            GcCollectMs      = s.gcCollectMs,
            DrawCallsCount   = s.drawCalls,
            MemoryUsedBytes  = s.memBytes,
            GpuMs            = s.gpuRawMs,
            TimestampMs      = s.tMs,
            CulpritCallStack = string.IsNullOrEmpty(s.culpritStack) ? null : s.culpritStack,
            Occurrences      = s.occurrences
        };

        public static ArtigoSample FromSnapshot(string array, FrameSnapshot s) => new ArtigoSample
        {
            array        = array,
            frame        = s.FrameIndex,
            cpuRawMs     = s.CpuLatencyMs,
            gcBytes      = s.GcAllocatedBytes,
            gcCollectMs  = s.GcCollectMs,
            drawCalls    = s.DrawCallsCount,
            memBytes     = s.MemoryUsedBytes,
            gpuRawMs     = s.GpuMs,
            tMs          = s.TimestampMs,
            culpritStack = s.CulpritCallStack ?? "",
            occurrences  = s.Occurrences
        };

        private FrameSnapshot[] Array(string name)
        {
            var l = new List<FrameSnapshot>();
            foreach (var s in amostras) if (s.array == name) l.Add(ToSnapshot(s));
            return l.ToArray();
        }

        /// <summary>The payload as the plugin would build it, from this case's values.</summary>
        public GeminiRestBridge.ReportPayload ToPayload() => new GeminiRestBridge.ReportPayload
        {
            Description       = descricao,
            CpuBudgetMs       = orcamentoMs,
            DrawCallBudget    = tetoDrawCalls,
            MemoryBudgetBytes = tetoMemoriaBytes,
            CpuFactor         = fatorCpu,
            GpuFactor         = fatorGpu,
            MeasuredOn        = medidoEm,
            DrawCounter       = contadorDraw,
            MemoryCounter     = contadorMemoria,
            MemResolved       = memResolvido,
            GpuResolved       = gpuResolvido,
            Summary           = (vCpu, vGc, vDraw, vGpu, vMem, vCpuMaxMs, vGcMaxBytes, vDrawMax, vGpuMaxMs, vMemMaxBytes),
            Cpu  = Array("cpu_samples"),
            Gc   = Array("gc_samples"),
            Draw = Array("drawcalls_samples"),
            Mem  = Array("memory_samples"),
            Gpu  = Array("gpu_samples"),
            Sources = fontes != null ? fontes.ToArray() : System.Array.Empty<ScriptSource>()
        };

        /// <summary>
        /// Attaches the scripts named in this case's stacks, with the plugin's own resolver.
        /// </summary>
        /// <remarks>
        /// MOTIVO: a telemetria capturada não muda — só se acrescenta o que o plugin anexaria
        /// hoje ao montar o laudo. Caso sem pilha (G3, G4, G5) fica sem código, como no plugin.
        /// </remarks>
        public void AttachSources()
        {
            fontes = new List<ScriptSource>(ScriptSourceResolver.Resolve(amostras.ConvertAll(a => a.culpritStack)));
            foreach (var f in fontes) f.code = StripHints(f.code);
            RebuildUserText();
        }

        /// <summary>Removes comments and Tooltip/Header attributes from a test script.</summary>
        /// <remarks>
        /// MOTIVO: o script da cena de estresse tem os defeitos plantados DESCRITOS nos
        /// comentários e tooltips ("Anti-pattern on purpose: forces a heap allocation chain").
        /// Enviados ao modelo, eles entregariam a resposta — as duas condições recebem o mesmo
        /// texto, então a comparação seguiria justa, mas os números absolutos ficariam inflados.
        /// Só o experimento tira; o plugin envia o código do projeto como ele está.
        /// </remarks>
        public static string StripHints(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;
            code = System.Text.RegularExpressions.Regex.Replace(code, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);
            code = System.Text.RegularExpressions.Regex.Replace(code, @"//[^\n]*", "");
            code = System.Text.RegularExpressions.Regex.Replace(code, @"\[(?:Tooltip|Header)\(""(?:[^""\\]|\\.)*""(?:\s*\+\s*""(?:[^""\\]|\\.)*"")*\)\]", "");
            code = System.Text.RegularExpressions.Regex.Replace(code, @"[ \t]+\n", "\n");
            code = System.Text.RegularExpressions.Regex.Replace(code, @"\n{3,}", "\n\n");
            return code.Trim() + "\n";
        }

        /// <summary>Regenerates userText with the plugin's own formatter.</summary>
        public void RebuildUserText() => userText = GeminiRestBridge.FormatUserText(ToPayload());

        // ── Valores como o modelo os recebe ─────────────────────────────────

        /// <summary>cpu_ms as sent (projected and rounded like the payload).</summary>
        public float SentCpuMs(ArtigoSample s) => Round2(GeminiRestBridge.OnTarget(s.cpuRawMs, fatorCpu));
        /// <summary>gpu_ms as sent, or NaN when it went as null.</summary>
        public float SentGpuMs(ArtigoSample s) => gpuResolvido ? Round2(GeminiRestBridge.OnTarget(s.gpuRawMs, fatorGpu)) : float.NaN;

        private static float Round2(float v) =>
            float.Parse(v.ToString("F2", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        /// <summary>True when at least one sample is above a ceiling that exists.</summary>
        public bool AnyMetricOverBudget()
        {
            foreach (var s in amostras)
            {
                if (SentCpuMs(s) > orcamentoMs) return true;
                float g = SentGpuMs(s);
                if (!float.IsNaN(g) && g > orcamentoMs) return true;
                if (tetoDrawCalls > 0 && s.drawCalls > tetoDrawCalls) return true;
                if (tetoMemoriaBytes > 0 && memResolvido && s.memBytes > tetoMemoriaBytes) return true;
            }
            return false;
        }

        // ── Arquivo ─────────────────────────────────────────────────────────

        public static string PathOf(string id) => Path.Combine(ArtigoPaths.CasesDir, id + ".json");

        public void Save() => ArtigoPaths.WriteUtf8(PathOf(id), JsonUtility.ToJson(this, true));

        public static ArtigoCase Load(string path) =>
            JsonUtility.FromJson<ArtigoCase>(File.ReadAllText(path, Encoding.UTF8));

        public static List<ArtigoCase> LoadAll()
        {
            var l = new List<ArtigoCase>();
            if (!Directory.Exists(ArtigoPaths.CasesDir)) return l;
            var files = Directory.GetFiles(ArtigoPaths.CasesDir, "*.json");
            System.Array.Sort(files, StringComparer.Ordinal);
            foreach (var f in files) l.Add(Load(f));
            return l;
        }
    }
}
