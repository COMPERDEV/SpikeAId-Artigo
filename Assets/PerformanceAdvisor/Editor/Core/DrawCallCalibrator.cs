using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Measures the CPU cost of a single draw call on this machine with a self-contained
    /// benchmark — no scene, no Play Mode.
    /// </summary>
    /// <remarks>
    /// MOTIVO: a versão anterior media durante o monitoramento, dividindo o tempo do render
    /// thread pelo número de draws do frame. Isso tinha dois defeitos:
    ///
    ///  1. **Exigia abrir uma cena e rodar**, que é trabalho manual para algo que o plugin
    ///     pode fazer sozinho.
    ///  2. **O número dependia da cena.** Medido numa cena deu 0,42 µs; noutra, 0,81 µs —
    ///     quase o dobro, na mesma máquina. A divisão incluía
    ///     o custo FIXO do frame (culling, setup, post) rateado entre os draws, então cenas
    ///     com poucos draws inflavam o custo unitário e cenas pesadas o diluíam.
    ///
    /// A correção é medir a INCLINAÇÃO, não a média: rendemos N1 e N2 draw calls e tomamos
    /// (t2 − t1) ÷ (N2 − N1). Tudo que é custo fixo aparece igual nas duas medições e se
    /// cancela na subtração — sobra só o custo marginal de UM draw call, que é exatamente
    /// a grandeza que o DrawCallBudgetModel precisa.
    ///
    /// Usa <c>Graphics.DrawMeshNow</c> em modo imediato: a submissão acontece na hora, na
    /// thread que estamos cronometrando, incluindo a validação do driver. O estado é ligado
    /// uma vez fora do laço — senão estaríamos medindo troca de estado, que é outra grandeza.
    ///
    /// Limite honesto: mede custo de submissão de um draw trivial. Draws com troca de
    /// material ou shader custam mais, e é por isso que o modelo reserva apenas uma fatia
    /// do frame para submissão, em vez de assumir que o orçamento inteiro é gasto assim.
    /// </remarks>
    public static class DrawCallCalibrator
    {
        // MOTIVO: dois volumes bem separados para a inclinação ter sinal acima do ruído,
        // e ainda assim rápidos o bastante para não travar o Editor.
        private const int DRAWS_LOW  = 200;
        private const int DRAWS_HIGH = 2000;

        private const int WARMUP_REPS = 2;
        private const int MEASURE_REPS = 5;
        private const int RT_SIZE = 128;

        // Evita repetir a calibração automática a cada repaint dentro do mesmo domínio.
        private static bool _autoTried;

        /// <summary>Runs the benchmark once per domain reload if nothing was measured yet.</summary>
        public static void EnsureCalibrated()
        {
            if (_autoTried) return;
            _autoTried = true;
            if (DrawCallBudgetModel.MeasuredCostUs > 0f) return;
            Calibrate();
        }

        /// <summary>
        /// Runs the benchmark and stores the result. Returns the cost in microseconds,
        /// or 0 when the machine could not be measured.
        /// </summary>
        public static float Calibrate()
        {
            Mesh mesh = null;
            Material mat = null;
            RenderTexture rt = null;
            RenderTexture previous = RenderTexture.active;

            try
            {
                Shader shader = FindUsableShader();
                if (shader == null)
                {
                    UnityEngine.Debug.LogWarning(
                        "[Performance Advisor] " +
                        Loc.T("Calibração abortada: nenhum shader utilizável encontrado.",
                              "Calibration aborted: no usable shader found.",
                              "Calibración abortada: no se encontró ningún shader utilizable."));
                    return 0f;
                }

                mesh = BuildQuad();
                mat  = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                rt   = new RenderTexture(RT_SIZE, RT_SIZE, 0) { hideFlags = HideFlags.HideAndDontSave };

                // Aquecimento: primeira submissão paga compilação de shader e alocação de
                // buffers do driver, que não é custo de draw call.
                for (int i = 0; i < WARMUP_REPS; i++)
                {
                    RenderPass(rt, mesh, mat, DRAWS_LOW);
                    RenderPass(rt, mesh, mat, DRAWS_HIGH);
                }

                double lowMs  = MedianOf(rt, mesh, mat, DRAWS_LOW);
                double highMs = MedianOf(rt, mesh, mat, DRAWS_HIGH);

                // A inclinação cancela todo custo fixo presente nas duas medições.
                double deltaMs = highMs - lowMs;
                if (deltaMs <= 0.0)
                {
                    UnityEngine.Debug.LogWarning(
                        "[Performance Advisor] " +
                        Loc.T("Calibração inconclusiva: o tempo não cresceu com o número " +
                              "de draw calls. Pode ser driver agrupando as chamadas. Nada foi gravado.",
                              "Inconclusive calibration: the time did not grow with the number " +
                              "of draw calls. The driver may be batching the calls. Nothing was saved.",
                              "Calibración no concluyente: el tiempo no creció con el número " +
                              "de draw calls. Puede ser el driver agrupando las llamadas. No se guardó nada."));
                    return 0f;
                }

                float costUs = (float)(deltaMs * 1000.0 / (DRAWS_HIGH - DRAWS_LOW));
                DrawCallBudgetModel.MeasuredCostUs = costUs;

                UnityEngine.Debug.Log(
                    "[Performance Advisor] " +
                    Loc.T($"Calibrado: {costUs:F3} µs por draw call" +
                          $" (inclinação entre {DRAWS_LOW} e {DRAWS_HIGH}, mediana de {MEASURE_REPS} medições).",
                          $"Calibrated: {costUs:F3} µs per draw call" +
                          $" (slope between {DRAWS_LOW} and {DRAWS_HIGH}, median of {MEASURE_REPS} measurements).",
                          $"Calibrado: {costUs:F3} µs por draw call" +
                          $" (pendiente entre {DRAWS_LOW} y {DRAWS_HIGH}, mediana de {MEASURE_REPS} mediciones)."));

                return costUs;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null)   { rt.Release(); Object.DestroyImmediate(rt); }
                if (mat != null)  Object.DestroyImmediate(mat);
                if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }

        private static double MedianOf(RenderTexture rt, Mesh mesh, Material mat, int draws)
        {
            var samples = new double[MEASURE_REPS];
            for (int i = 0; i < MEASURE_REPS; i++)
                samples[i] = RenderPass(rt, mesh, mat, draws);

            System.Array.Sort(samples);
            // MOTIVO: mediana e não média — uma interrupção do SO no meio de uma repetição
            // contamina a média inteira, mas desloca a mediana quase nada.
            return samples[MEASURE_REPS / 2];
        }

        /// <summary>Renders <paramref name="draws"/> trivial meshes and returns elapsed ms.</summary>
        private static double RenderPass(RenderTexture rt, Mesh mesh, Material mat, int draws)
        {
            var matrix = Matrix4x4.identity;

            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);

            // Estado ligado UMA vez, fora do laço: queremos o custo de SUBMETER um draw,
            // não o de trocar de shader.
            mat.SetPass(0);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < draws; i++)
            {
                Graphics.DrawMeshNow(mesh, matrix);
            }
            GL.Flush();
            sw.Stop();

            RenderTexture.active = null;
            return sw.Elapsed.TotalMilliseconds;
        }

        // Quad mínimo — 4 vértices, 2 triângulos. Geometria trivial de propósito: queremos
        // o custo de SUBMETER o draw, não o de processar vértices.
        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f)
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Shader FindUsableShader()
        {
            // Ordem de preferência: o unlit da URP, depois fallbacks que existem em
            // qualquer projeto. Não assumimos pipeline nenhum.
            string[] candidates =
            {
                "Universal Render Pipeline/Unlit",
                "Unlit/Color",
                "Hidden/Internal-Colored",
                "Sprites/Default"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                Shader s = Shader.Find(candidates[i]);
                if (s != null) return s;
            }
            return null;
        }
    }
}
