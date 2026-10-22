using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PerformanceAdvisor.Artigo
{
    /// <summary>
    /// Turns stored answers into the article's numbers: per-answer metrics, per-case rates,
    /// the paired Wilcoxon test between conditions, stability across repetitions, and the
    /// sample for the manual rubric.
    /// </summary>
    /// <remarks>
    /// MOTIVO de recalcular tudo a partir das respostas brutas (e não guardar só a tabela):
    /// se uma regra de validação mudar, os números mudam junto, sem chamar a API de novo.
    /// As respostas são o dado; as métricas são função delas.
    ///
    /// Desenho (seção 5.4 do artigo, fixado antes da coleta, decisão do autor em 09/10/2026):
    /// unidade de análise = caso; taxa de cada métrica = média nas repetições com formato
    /// válido; comparação caso a caso por Wilcoxon de postos sinalizados, bilateral, α = 0,05,
    /// p exato (distribuição de permutação com postos médios para empates); tamanho de
    /// efeito = correlação bisserial de postos pareada.
    /// </remarks>
    public static class ArtigoAnalysis
    {
        public const double ALPHA = 0.05;
        private static readonly string[] CONDITIONS = { "amador", "profissional" };

        private class Metric
        {
            public string Name, Label; public Func<ArtigoMetrics, bool> Fail;
            public Func<ArtigoCase, bool> Applies; public bool HigherIsBetter; public bool NeedsValidFormat = true;
            // Pós-hoc: definida depois de ver os dados. Reportada e testada, mas fora da família
            // do ajuste de Holm — o ajuste vale para as métricas fixadas antes da coleta.
            public bool PostHoc;
        }

        private static readonly Func<ArtigoCase, bool> ALL = c => true;
        private static bool G1to4(ArtigoCase c) => !c.esperadoSemEstouro;
        private static bool G5(ArtigoCase c) => c.esperadoIndeterminacao;
        private static bool G5or6(ArtigoCase c) => c.esperadoSemEstouro;
        // G1 e G2: há pilha e há estouro, então um código ancorado na pilha era possível.
        private static bool G1or2(ArtigoCase c) => c.grupo == "G1_CPU" || c.grupo == "G2_GC";

        private static List<Metric> Metrics() => new List<Metric>
        {
            new Metric { Name = "formato_invalido", Label = "Violação de formato", Fail = m => !m.formatoValido, Applies = ALL, NeedsValidFormat = false },
            new Metric { Name = "contagem_errada", Label = "Quantidade de achados errada", Fail = m => m.contagemErrada, Applies = ALL },
            new Metric { Name = "tipo_errado", Label = "Atribuição de tipo errada", Fail = m => m.tipoErrado, Applies = ALL },
            new Metric { Name = "fabricacao_simbolo", Label = "Fabricação de símbolo (inexistente)", Fail = m => m.fabricacaoSimbolo, Applies = ALL },
            new Metric { Name = "fabricacao_simbolo_estrita", Label = "Símbolo fora da pilha (R4 estrita)", Fail = m => m.fabricacaoSimboloEstrita, Applies = ALL },
            new Metric { Name = "fabricacao_valor", Label = "Fabricação de valor ou quadro", Fail = m => m.fabricacaoValor, Applies = ALL },
            new Metric { Name = "teto_ausente_violado", Label = "Teto ausente tratado como estourado", Fail = m => m.tetoAusenteViolado, Applies = ALL },
            new Metric { Name = "vazamento_escopo", Label = "Vazamento de escopo", Fail = m => m.vazamentoEscopo, Applies = ALL },
            new Metric { Name = "dicas_ausentes", Label = "Dicas de level design ausentes com achado DrawCalls", Fail = m => m.dicasAusentes, Applies = ALL },
            new Metric { Name = "indeterminacao_correta", Label = "Indeterminação correta (G5)", Fail = m => m.indeterminacaoCorreta, Applies = G5, HigherIsBetter = true },
            new Metric { Name = "indeterminacao_indevida", Label = "Indeterminação indevida (G1–G4)", Fail = m => m.indeterminacaoIndevida, Applies = G1to4 },
            new Metric { Name = "falso_estouro", Label = "Estouro afirmado sem estouro (G5–G6)", Fail = m => m.falsoEstouro, Applies = G5or6 },
            new Metric { Name = "valida_porem_errada", Label = "Válida porém errada", Fail = m => m.validaPoremErrada, Applies = ALL },
            // MOTIVO: o prompt profissional zerou a fabricação de símbolos deixando o código
            // vazio em todos os achados. Sem esta medida, a tabela venderia "zero fabricação"
            // sem dizer que veio de "zero código" — é o custo da estrutura (seção 7 do artigo).
            new Metric { Name = "sem_codigo", Label = "Abstenção de código (G1–G2, pós-hoc)", Fail = m => m.semCodigo, Applies = G1or2, PostHoc = true },
        };

        public static string Run()
        {
            var cases = ArtigoCase.LoadAll().ToDictionary(c => c.id);
            if (cases.Count == 0) return "Nenhum caso em Artigo/casos/.";

            // ── 1. Métricas por resposta, por condição ─────────────────────────
            var byCond = new Dictionary<string, List<ArtigoMetrics>>();
            int semResposta = 0;
            foreach (string cond in CONDITIONS)
            {
                string dir = ArtigoPaths.ResponsesDir(cond);
                if (!Directory.Exists(dir)) continue;
                var list = new List<ArtigoMetrics>();
                foreach (string f in Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.Ordinal))
                {
                    var r = JsonUtility.FromJson<ArtigoResponse>(File.ReadAllText(f, Encoding.UTF8));
                    if (!cases.TryGetValue(r.caso, out ArtigoCase c)) continue;
                    var m = ArtigoValidator.Validate(c, cond, r.repeticao, string.IsNullOrEmpty(r.erro) ? r.texto : null);
                    if (!m.respondeu) semResposta++;
                    list.Add(m);
                }
                byCond[cond] = list;
                var sb = new StringBuilder(string.Join(";", ArtigoMetrics.Header)).Append('\n');
                foreach (var m in list) sb.Append(m.ToCsv()).Append('\n');
                ArtigoPaths.WriteUtf8(Path.Combine(ArtigoPaths.ResultsDir(cond), "metricas_por_resposta.csv"), sb.ToString());
            }
            if (byCond.Count == 0) return "Nenhuma resposta coletada ainda.";

            // ── 2. Taxa por caso e por condição ────────────────────────────────
            var metrics = Metrics();
            var rates = new Dictionary<(string cond, string metric, string caso), double>();
            var stability = new Dictionary<(string cond, string metric), List<double>>();
            foreach (var kv in byCond)
                foreach (var g in kv.Value.Where(m => m.respondeu).GroupBy(m => m.caso))
                {
                    ArtigoCase c = cases[g.Key];
                    foreach (var mt in metrics)
                    {
                        if (!mt.Applies(c)) continue;
                        var rows = g.Where(m => !mt.NeedsValidFormat || m.formatoValido).ToList();
                        if (rows.Count == 0) continue;
                        int k = rows.Count(mt.Fail);
                        rates[(kv.Key, mt.Name, g.Key)] = (double)k / rows.Count;
                        if (!stability.TryGetValue((kv.Key, mt.Name), out var st)) stability[(kv.Key, mt.Name)] = st = new List<double>();
                        st.Add((double)Math.Max(k, rows.Count - k) / rows.Count);
                    }
                }

            // ── 3. Comparação pareada ──────────────────────────────────────────
            var cmp = new StringBuilder("metrica;descricao;casos_pareados;media_amador;media_profissional;W_mais;W_menos;p_exato;p_holm;r_bisserial;significativo_holm_5pct;melhor_e_maior\n");
            var resumo = new StringBuilder();
            resumo.Append("# Resultados do experimento\n\n");
            resumo.Append($"Gerado em {DateTime.Now:yyyy-MM-dd HH:mm}. Respostas por condição: ");
            resumo.Append(string.Join(", ", byCond.Select(kv => $"{kv.Key} = {kv.Value.Count}"))).Append($"; sem resposta (erro de API): {semResposta}.\n\n");
            resumo.Append("| Métrica | Casos | Amador | Profissional | p exato | p Holm | r |\n|---|---|---|---|---|---|---|\n");

            // MOTIVO do ajuste de Holm: são várias métricas testadas a 5%. Sem ajuste, a chance
            // de ao menos um "significativo" por acaso cresce com o número de testes. Holm controla
            // essa taxa sem a perda de poder do Bonferroni simples. O p exato de cada métrica
            // continua reportado ao lado.
            bool both = byCond.ContainsKey("amador") && byCond.ContainsKey("profissional");
            var linhas = new List<(Metric mt, int n, double mA, double mP, double wPlus, double wMinus, double p, double r)>();
            foreach (var mt in metrics)
            {
                var ids = cases.Keys.Where(id => rates.ContainsKey(("amador", mt.Name, id)) && rates.ContainsKey(("profissional", mt.Name, id))).ToList();
                double mA = Mean(cases.Keys.Select(id => Get(rates, "amador", mt.Name, id)));
                double mP = Mean(cases.Keys.Select(id => Get(rates, "profissional", mt.Name, id)));
                double p = double.NaN, r = double.NaN, wPl = double.NaN, wMi = double.NaN;
                if (both && ids.Count > 0)
                {
                    var w = Wilcoxon(ids.Select(id => rates[("amador", mt.Name, id)] - rates[("profissional", mt.Name, id)]).ToList());
                    p = w.p; r = w.r; wPl = w.wPlus; wMi = w.wMinus;
                }
                linhas.Add((mt, ids.Count, mA, mP, wPl, wMi, p, r));
            }
            var holm = Holm(linhas.Select(x => x.mt.PostHoc ? double.NaN : x.p).ToList());
            for (int i = 0; i < linhas.Count; i++)
            {
                var x = linhas[i];
                string P(double v, string fmt) => double.IsNaN(v) ? "—" : v.ToString(fmt, CultureInfo.InvariantCulture);
                string sig = double.IsNaN(holm[i]) ? "" : holm[i] < ALPHA ? "sim" : "nao";
                cmp.Append($"{x.mt.Name};{x.mt.Label};{x.n};{F(x.mA)};{F(x.mP)};{P(x.wPlus, "0.#")};{P(x.wMinus, "0.#")};" +
                           $"{P(x.p, "0.0000")};{P(holm[i], "0.0000")};{P(x.r, "0.00")};{sig};{(x.mt.HigherIsBetter ? "sim" : "nao")}\n");
                resumo.Append($"| {x.mt.Label} | {x.n} | {Pct(x.mA)} | {Pct(x.mP)} | {P(x.p, "0.0000")} | {P(holm[i], "0.0000")} | {P(x.r, "0.00")} |\n");
            }
            ArtigoPaths.WriteUtf8(Path.Combine(ArtigoPaths.ComparisonDir, "comparacao_metricas.csv"), cmp.ToString());

            // ── 4. Descritivo por grupo ────────────────────────────────────────
            var groups = cases.Values.Select(c => c.grupo).Distinct().OrderBy(x => x).ToList();
            var tg = new StringBuilder("metrica;grupo;casos;media_amador;media_profissional\n");
            foreach (var mt in metrics)
                foreach (string g in groups)
                {
                    var gid = cases.Values.Where(c => c.grupo == g && mt.Applies(c)).Select(c => c.id).ToList();
                    if (gid.Count == 0) continue;
                    tg.Append($"{mt.Name};{g};{gid.Count};{F(Mean(gid.Select(id => Get(rates, "amador", mt.Name, id))))};{F(Mean(gid.Select(id => Get(rates, "profissional", mt.Name, id))))}\n");
                }
            ArtigoPaths.WriteUtf8(Path.Combine(ArtigoPaths.ComparisonDir, "taxas_por_grupo.csv"), tg.ToString());

            // ── 5. Estabilidade entre repetições ──────────────────────────────
            var est = new StringBuilder("metrica;condicao;concordancia_media\n");
            foreach (var kv in stability.OrderBy(k => k.Key.metric).ThenBy(k => k.Key.cond))
                est.Append($"{kv.Key.metric};{kv.Key.cond};{F(kv.Value.Average())}\n");
            ArtigoPaths.WriteUtf8(Path.Combine(ArtigoPaths.ComparisonDir, "estabilidade.csv"), est.ToString());

            // ── 6. Amostra para a rubrica manual ──────────────────────────────
            WriteRubricSample(cases);

            resumo.Append("\nTaxas: média, entre os casos, da fração de repetições em que a falha ocorreu (para \"indeterminação correta\", a fração em que ocorreu o acerto). ");
            resumo.Append($"Teste: Wilcoxon pareado bilateral, p exato, α = {ALPHA.ToString(CultureInfo.InvariantCulture)}, com ajuste de Holm entre as métricas testadas. r = correlação bisserial de postos (positivo = amador maior).\n");
            ArtigoPaths.WriteUtf8(Path.Combine(ArtigoPaths.ComparisonDir, "resumo.md"), resumo.ToString());

            return $"Métricas calculadas para {string.Join(" e ", byCond.Keys)}. Arquivos em Artigo/resultados/." +
                   (both ? "" : " A comparação pareada só aparece quando as duas condições estiverem em Artigo/respostas/.");
        }

        private static void WriteRubricSample(Dictionary<string, ArtigoCase> cases)
        {
            // MOTIVO: semente fixa — a amostra avaliada à mão é reprodutível e não é escolhida
            // depois de ver as respostas.
            var rnd = new System.Random(20261009);
            var ids = cases.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var chosen = ids.OrderBy(_ => rnd.Next()).Take(Math.Max(1, ids.Count / 5)).OrderBy(x => x).ToList();
            var sb = new StringBuilder("caso;condicao;repeticao;tipo;causa_raiz;codigo_refatorado;explicacao_didatica;nota_didatica_1a5;nota_pertinencia_codigo_1a5;avaliador\n");
            foreach (string id in chosen)
                foreach (string cond in CONDITIONS)
                {
                    string f = ArtigoResponse.PathOf(cond, id, 1);
                    if (!File.Exists(f)) continue;
                    var r = JsonUtility.FromJson<ArtigoResponse>(File.ReadAllText(f, Encoding.UTF8));
                    try
                    {
                        var root = MiniJson.Parse(r.texto ?? "") as Dictionary<string, object>;
                        foreach (var o in (List<object>)root["findings"])
                        {
                            var fd = (Dictionary<string, object>)o;
                            sb.Append(string.Join(";", new[] { id, cond, "1", S(fd, "tipo"), S(fd, "causa_raiz"), S(fd, "codigo_refatorado"), S(fd, "explicacao_didatica"), "", "", "" }.Select(Q))).Append('\n');
                        }
                    }
                    catch { sb.Append($"{id};{cond};1;(formato inválido);;;;;;\n"); }
                }
            ArtigoPaths.WriteUtf8(Path.Combine(ArtigoPaths.ComparisonDir, "rubrica_amostra.csv"), sb.ToString());
        }

        // ── Wilcoxon de postos sinalizados, p exato ────────────────────────

        /// <summary>Exact two-sided Wilcoxon signed-rank test, with mid-ranks for ties and zeros dropped.</summary>
        public static (double wPlus, double wMinus, double p, double r) Wilcoxon(IList<double> diffs)
        {
            var d = diffs.Where(x => Math.Abs(x) > 1e-12).ToList();
            int n = d.Count;
            if (n == 0) return (0, 0, 1.0, 0.0);

            // Postos médios de |d|, dobrados para virarem inteiros.
            var order = Enumerable.Range(0, n).OrderBy(i => Math.Abs(d[i])).ToList();
            var rank2 = new int[n];
            for (int i = 0; i < n;)
            {
                int j = i;
                while (j + 1 < n && Math.Abs(Math.Abs(d[order[j + 1]]) - Math.Abs(d[order[i]])) < 1e-12) j++;
                int r2 = (i + 1) + (j + 1);   // 2 × posto médio
                for (int k = i; k <= j; k++) rank2[order[k]] = r2;
                i = j + 1;
            }
            int total2 = rank2.Sum();
            int w2 = 0; for (int i = 0; i < n; i++) if (d[i] > 0) w2 += rank2[i];

            // Distribuição de W+ (dobrado) sob H0: cada posto entra com sinal + ou −, igualmente provável.
            var counts = new double[total2 + 1]; counts[0] = 1;
            foreach (int r2 in rank2)
                for (int s = total2; s >= r2; s--) counts[s] += counts[s - r2];
            double all = Math.Pow(2, n), le = 0, ge = 0;
            for (int s = 0; s <= total2; s++) { if (s <= w2) le += counts[s]; if (s >= w2) ge += counts[s]; }
            double p = Math.Min(1.0, 2.0 * Math.Min(le, ge) / all);

            double wPlus = w2 / 2.0, wMinus = total2 / 2.0 - wPlus;
            double r = (wPlus - wMinus) / (wPlus + wMinus);
            return (wPlus, wMinus, p, r);
        }

        /// <summary>Holm step-down adjustment; NaN entries (untested metrics) are left out and stay NaN.</summary>
        public static double[] Holm(IList<double> p)
        {
            var adj = Enumerable.Repeat(double.NaN, p.Count).ToArray();
            var idx = Enumerable.Range(0, p.Count).Where(i => !double.IsNaN(p[i])).OrderBy(i => p[i]).ToList();
            int m = idx.Count; double running = 0;
            for (int k = 0; k < m; k++)
            {
                running = Math.Max(running, Math.Min(1.0, (m - k) * p[idx[k]]));
                adj[idx[k]] = running;
            }
            return adj;
        }

        // ── Utilidades ──────────────────────────────────────────────────────

        private static double Get(Dictionary<(string, string, string), double> rates, string cond, string metric, string id) =>
            rates.TryGetValue((cond, metric, id), out double v) ? v : double.NaN;

        private static double Mean(IEnumerable<double> xs)
        {
            var l = xs.Where(x => !double.IsNaN(x)).ToList();
            return l.Count == 0 ? double.NaN : l.Average();
        }

        private static string F(double x) => double.IsNaN(x) ? "" : x.ToString("0.000", CultureInfo.InvariantCulture);
        private static string Pct(double x) => double.IsNaN(x) ? "—" : (x * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        private static string S(Dictionary<string, object> d, string k) => d.TryGetValue(k, out object v) ? v as string ?? "" : "";
        private static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
