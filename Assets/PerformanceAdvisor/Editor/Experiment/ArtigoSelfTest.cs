using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PerformanceAdvisor.Artigo
{
    /// <summary>
    /// Checks the experiment's own instruments — the Wilcoxon test and every validator rule —
    /// against inputs whose answer is known, before any real data is collected.
    /// </summary>
    /// <remarks>
    /// MOTIVO: o validador É a medição do artigo. Se uma regra estiver errada, todo número
    /// sai errado e ninguém percebe olhando a tabela. Cada classe de alucinação é plantada
    /// numa resposta sintética e o teste exige que a regra certa (e só ela) dispare.
    /// Saída em Artigo/resultados/_autoteste.txt.
    /// </remarks>
    public static class ArtigoSelfTest
    {
        private static StringBuilder _log;
        private static int _ok, _fail;

        [MenuItem("Tools/Performance Advisor/Artigo/Autoteste do validador", false, 320)]
        public static void RunFromMenu() => EditorUtility.DisplayDialog("Artigo", Run(), "OK");

        public static void RunBatch()
        {
            string r = Run();
            Debug.Log("[Artigo] " + r);
            EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        public static string Run()
        {
            _log = new StringBuilder(); _ok = 0; _fail = 0;
            Line("Autoteste dos instrumentos do artigo — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            Section("Wilcoxon (p exato conhecido)");
            Near("5 diferenças positivas distintas → p = 2/32", ArtigoAnalysis.Wilcoxon(new[] { 0.2, 0.4, 0.6, 0.8, 1.0 }).p, 0.0625);
            Near("6 diferenças positivas empatadas → p = 2/64", ArtigoAnalysis.Wilcoxon(new[] { 1.0, 1, 1, 1, 1, 1 }).p, 0.03125);
            Near("zeros são descartados", ArtigoAnalysis.Wilcoxon(new[] { 0.0, 0, 0.2, 0.4, 0.6, 0.8, 1.0 }).p, 0.0625);
            Near("simétrico → p = 1", ArtigoAnalysis.Wilcoxon(new[] { 0.5, -0.5 }).p, 1.0);
            Near("r bisserial de tudo positivo = 1", ArtigoAnalysis.Wilcoxon(new[] { 0.2, 0.4 }).r, 1.0);
            var h = ArtigoAnalysis.Holm(new[] { 0.01, 0.04, 0.03, double.NaN });
            Near("Holm: menor p × 3 testes", h[0], 0.03);
            Near("Holm: segundo menor × 2", h[2], 0.06);
            Near("Holm: maior × 1, mas nunca abaixo do anterior", h[1], 0.06);
            Expect("Holm: métrica sem teste continua sem p", double.IsNaN(h[3]), null);

            Section("Payload do caso");
            var c = CpuCase();
            Expect("cpu_ms vai projetado (10 × 1,86 = 18,60)", c.userText.Contains("cpu_ms:18.60"), c.userText);
            Expect("linha de projeção presente", c.userText.Contains("projecao:"), null);
            Expect("memória vai como sem teto", c.userText.Contains("memoria sem teto (nao julgar)"), null);
            Expect("gabarito: 1 achado, com estouro", c.esperadoAchados == 1 && !c.esperadoSemEstouro, null);

            Section("Validador — resposta correta");
            var ok = V(c, Report(F("CPU",
                "No frame 120, cpu_ms de 18,60 ms excede o orçamento de 16.7 ms; StressTestScene.Update custa 9.10 ms.",
                "public class StressTestScene { void Update() { } }",
                "O quadro passou do tempo disponível.")));
            Expect("formato válido", ok.formatoValido, ok.detalhe);
            Expect("nenhuma falha marcada", !ok.validaPoremErrada && !ok.fabricacaoValor && !ok.fabricacaoSimbolo &&
                   !ok.tipoErrado && !ok.contagemErrada && !ok.tetoAusenteViolado && !ok.vazamentoEscopo, ok.detalhe);
            Expect("símbolo da pilha não conta como fora dela", ok.simbolosForaDaPilha == 0, ok.detalhe);

            Section("Validador — cada classe plantada");
            var md = ArtigoValidator.Validate(c, "t", 1, "```json\n" + Report(F("CPU", "x", "", "y")) + "\n```");
            Expect("cerca de markdown → formato inválido", !md.formatoValido, md.detalhe);
            var miss = ArtigoValidator.Validate(c, "t", 1, "{\"findings\":[{\"tipo\":\"CPU\",\"causa_raiz\":\"x\",\"codigo_refatorado\":\"\"}]}");
            Expect("campo obrigatório ausente → formato inválido", !miss.formatoValido, miss.detalhe);

            var tipo = V(c, Report(F("GC", "No frame 120 houve 18,60 ms.", "", "e")));
            Expect("tipo diferente do array → tipo errado", tipo.tipoErrado, tipo.detalhe);
            var cont = V(c, Report(F("CPU", "a", "", "e"), F("CPU", "b", "", "e")));
            Expect("dois achados para uma amostra → contagem errada", cont.contagemErrada, cont.detalhe);

            var quadro = V(c, Report(F("CPU", "No frame 999 o tempo foi 18,60 ms.", "", "e")));
            Expect("quadro inexistente → fabricação de valor", quadro.fabricacaoValor && quadro.quadrosFabricados == 1, quadro.detalhe);
            var valor = V(c, Report(F("CPU", "O quadro 120 levou 25,3 ms.", "", "e")));
            Expect("valor inexistente → fabricação de valor", valor.fabricacaoValor && valor.valoresFabricados == 1, valor.detalhe);
            var orc = V(c, Report(F("CPU", "Orçamento de 16,7 ms e 18,6 ms medidos no quadro 120.", "", "e")));
            Expect("valores do payload com outra casa decimal não são fabricação", !orc.fabricacaoValor, orc.detalhe);

            var inv = V(c, Report(F("CPU", "a", "void Update() { EnemyPool.SpawnPooledEnemy(); GetComponent<Renderer>(); }", "e")));
            Expect("método que não existe em lugar nenhum → inventado", inv.fabricacaoSimbolo && inv.detalhe.Contains("EnemyPool"), inv.detalhe);
            Expect("API da Unity conta fora da pilha mas não como inventada", inv.simbolosForaDaPilha >= 2 && inv.simbolosInventados == 2, inv.detalhe);

            Section("Código anexado (script_sources)");
            var cs = CpuCase();
            cs.fontes.Add(new ScriptSource { file = "Assets/Teste/StressTestScene.cs",
                code = "public class StressTestScene : MonoBehaviour {\n  public int gcConcatIterations;\n  void Update() { RunScan(); }\n  void RunScan() { }\n}" });
            cs.RebuildUserText();
            Expect("payload leva script_sources com o arquivo", cs.userText.Contains("script_sources: [{file:\"Assets/Teste/StressTestScene.cs\""), cs.userText);
            Expect("quebra de linha do código vai escapada (o payload continua em uma linha)", cs.userText.Contains("RunScan(); }\\n"), null);
            var anc = V(cs, Report(F("CPU", "a", "void Update() { RunScan(); gcConcatIterations = 0; }", "e")));
            Expect("símbolo do script anexado não conta como fora da pilha", anc.simbolosForaDaPilha == 0 && !anc.fabricacaoSimbolo, anc.detalhe);
            var semFonte = V(CpuCase(), Report(F("CPU", "a", "void Update() { RunScan(); }", "e")));
            Expect("sem script anexado, o mesmo símbolo continua fora da pilha e inventado", semFonte.fabricacaoSimboloEstrita && semFonte.fabricacaoSimbolo, semFonte.detalhe);

            Section("Validador — casos vistos nas respostas reais");
            var nsCode = V(c, Report(F("CPU", "a",
                "using UnityEngine;\nnamespace PerformanceAdvisor.StressTest { public class StressTestScene : MonoBehaviour { void Update() { UnityEngine.Debug.Log(\"x\"); } } }", "e")));
            Expect("namespace (using, namespace, UnityEngine.X) não é símbolo inventado", nsCode.simbolosInventados == 0, nsCode.detalhe);
            var prec = V(c, Report(F("CPU", "O orçamento é de 16,67 ms por quadro; o quadro 120 levou 18,6 ms.", "", "e")));
            Expect("16,67 ms contra 16.7 enviado não é fabricação", !prec.fabricacaoValor, prec.detalhe);
            var aprox = V(c, Report(F("CPU", "O quadro 120 levou mais de 17 ms, cerca de 9 ms em StressTestScene.Update.", "", "e")));
            Expect("aproximação declarada (mais de, cerca de) dentro de 10% não é fabricação", !aprox.fabricacaoValor, aprox.detalhe);
            var soma = V(c, Report(F("CPU", "Somando 18,60 ms e 10,00 ms, o quadro 120 totaliza 28,60 ms.", "", "e")));
            Expect("valor derivado sem marcador (soma) continua contando como fabricação", soma.fabricacaoValor && soma.valoresFabricados == 1, soma.detalhe);

            var cpuNaoMem = V(c, Report(F("CPU", "Alocação excessiva de memória no Update (~2.6 MB por frame), estourando o orçamento da CPU de 16.7 ms no quadro 120.", "", "e")));
            Expect("estouro da CPU numa frase que cita memória não é teto de memória violado", !cpuNaoMem.tetoAusenteViolado, cpuNaoMem.detalhe);
            var tetoMem = V(c, Report(F("CPU", "O consumo de memória ultrapassou o limite no quadro 120.", "", "e")));
            Expect("memória como sujeito do estouro conta", tetoMem.tetoAusenteViolado, tetoMem.detalhe);

            var teto = V(c, Report(F("CPU", "No quadro 120 a memória estourou o teto.", "", "e")));
            Expect("teto ausente tratado como estourado", teto.tetoAusenteViolado, teto.detalhe);
            var tetoNeg = V(c, Report(F("CPU", "Não há teto de memória definido, então não é possível dizer que estourou.", "", "e")));
            Expect("negação não conta como estouro", !tetoNeg.tetoAusenteViolado, tetoNeg.detalhe);

            var dicas = V(c, Report(new[] { F("CPU", "a", "", "e") }, "Use static batching nos cenários."));
            Expect("dicas sem achado DrawCalls → vazamento de escopo", dicas.dicasIndevidas && dicas.vazamentoEscopo, dicas.detalhe);

            var g = GpuCase();
            var bat = V(g, Report(F("GPU", "No frame 300 a GPU levou 25,00 ms.", "", "Ative o GPU Instancing e o static batching.")));
            Expect("batching num achado de GPU → vazamento de escopo", bat.batchingEmGpu && bat.vazamentoEscopo, bat.detalhe);
            var batNeg = V(g, Report(F("GPU", "No frame 300 a GPU levou 25,00 ms.", "", "Batching não resolve custo de pixel; reduza a resolução.")));
            Expect("batching negado não conta", !batNeg.batchingEmGpu, batNeg.detalhe);

            Section("Validador — grupos G5 e G6");
            var g5 = Derived(c, false);
            Expect("derivado G5 fica sem estouro e sem pilha", g5.esperadoSemEstouro && g5.esperadoIndeterminacao &&
                   !g5.AnyMetricOverBudget() && !g5.userText.Contains("culprit_stack"), g5.userText);
            var indet = V(g5, Report(F("Geral", "Não é possível determinar o gargalo com os dados fornecidos.", "", "Colete a pilha de chamadas.")));
            Expect("Geral sem código no G5 → indeterminação correta", indet.indeterminacaoCorreta && !indet.validaPoremErrada, indet.detalhe);
            var g5Errado = V(g5, Report(F("CPU", "O quadro 120 excedeu o orçamento.", "", "e")));
            Expect("afirmar estouro no G5 → falso estouro e válida porém errada", g5Errado.falsoEstouro && g5Errado.validaPoremErrada, g5Errado.detalhe);
            var indevida = V(c, Report(F("Geral", "Não é possível determinar o gargalo.", "", "e")));
            Expect("indeterminação com dados suficientes → indevida", indevida.indeterminacaoIndevida, indevida.detalhe);

            Line(""); Line($"Resultado: {_ok} ok, {_fail} falha(s).");
            ArtigoPaths.WriteUtf8(Path.Combine(ArtigoPaths.ComparisonDir, "_autoteste.txt"), _log.ToString());
            return $"Autoteste: {_ok} ok, {_fail} falha(s). Detalhe em Artigo/resultados/_autoteste.txt.";
        }

        // ── Casos sintéticos ────────────────────────────────────────────────

        private static ArtigoCase Base(string id, string grupo)
        {
            return new ArtigoCase
            {
                id = id, grupo = grupo, origem = "autoteste",
                descricao = "Intel Core i7-6700 (2015), DDR4 2133MHz 8GB, NVIDIA GeForce GTX 1650, alvo 60 FPS",
                orcamentoMs = 1000f / 60f, tetoDrawCalls = 2579, tetoMemoriaBytes = 0,
                fatorCpu = 1.86f, fatorGpu = 1.0f, medidoEm = "", contadorDraw = "Draw Calls Count",
                contadorMemoria = "System Used Memory", memResolvido = true, gpuResolvido = true, vCpu = 3, vCpuMaxMs = 18.6f
            };
        }

        private static ArtigoCase CpuCase()
        {
            var c = Base("T_CPU", "G1_CPU");
            c.amostras.Add(new ArtigoSample
            {
                array = "cpu_samples", frame = 120, cpuRawMs = 10f, gcBytes = 2048, gcCollectMs = 0f, drawCalls = 40,
                memBytes = 3_000_000_000, gpuRawMs = 2f, tMs = 4100, culpritStack = "StressTestScene.Update() [Invoke] (9.10ms)\nPlayerLoop (10.00ms)", occurrences = 3
            });
            c.esperadoAchados = 1; c.esperadoSemEstouro = !c.AnyMetricOverBudget(); c.RebuildUserText();
            return c;
        }

        private static ArtigoCase GpuCase()
        {
            var c = Base("T_GPU", "G4_GPU");
            c.amostras.Add(new ArtigoSample { array = "gpu_samples", frame = 300, cpuRawMs = 3f, drawCalls = 20, memBytes = 1, gpuRawMs = 25f, tMs = 5000, culpritStack = "" });
            c.esperadoAchados = 1; c.esperadoSemEstouro = !c.AnyMetricOverBudget(); c.RebuildUserText();
            return c;
        }

        private static ArtigoCase Derived(ArtigoCase b, bool keepStack)
        {
            // Mesma transformação da captura (ArtigoCapture.Derive), aplicada ao caso sintético.
            var m = typeof(ArtigoCapture).GetMethod("Derive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return (ArtigoCase)m.Invoke(null, new object[] { b, "T_G5", keepStack ? "G6_ENGANOSA" : "G5_FALTANTE", keepStack });
        }

        private static string F(string tipo, string causa, string codigo, string explicacao) =>
            "{\"tipo\":\"" + tipo + "\",\"causa_raiz\":\"" + MiniJson.Escape(causa) + "\",\"codigo_refatorado\":\"" +
            MiniJson.Escape(codigo) + "\",\"explicacao_didatica\":\"" + MiniJson.Escape(explicacao) + "\"}";

        private static string Report(params string[] findings) => Report(findings, null);

        private static string Report(string[] findings, string dicas) =>
            "{\"findings\":[" + string.Join(",", findings) + "],\"dicas_level_design\":" +
            (dicas == null ? "null" : "\"" + MiniJson.Escape(dicas) + "\"") + "}";

        private static ArtigoMetrics V(ArtigoCase c, string answer) => ArtigoValidator.Validate(c, "t", 1, answer);

        // ── Saída ───────────────────────────────────────────────────────────

        private static void Section(string s) { Line(""); Line("== " + s); }
        private static void Line(string s) => _log.AppendLine(s);

        private static void Expect(string what, bool cond, string detail)
        {
            if (cond) { _ok++; Line("  OK    " + what); }
            else { _fail++; Line("  FALHA " + what + (string.IsNullOrEmpty(detail) ? "" : "\n        " + detail)); }
        }

        private static void Near(string what, double got, double want) =>
            Expect($"{what} (obtido {got:0.#####})", Math.Abs(got - want) < 1e-9, null);
    }
}
