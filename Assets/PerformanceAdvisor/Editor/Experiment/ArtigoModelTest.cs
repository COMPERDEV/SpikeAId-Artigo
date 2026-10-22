using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PerformanceAdvisor.Artigo
{
    /// <summary>
    /// Probes a candidate model with a few cases under both prompts and estimates the cost of
    /// the full experiment — before committing the budget to it.
    /// </summary>
    /// <remarks>
    /// MOTIVO: o custo do experimento depende menos do preço por token (o 3.6 e o 3.8 têm o
    /// mesmo) do que de QUANTO o modelo raciocina por chamada, e isso só se sabe chamando. O
    /// teste usa os dois prompts porque o profissional é maior e pode induzir mais raciocínio.
    /// Grava em Artigo/teste-modelo/, separado das respostas do experimento.
    ///
    /// Linha de comando: -executeMethod PerformanceAdvisor.Artigo.ArtigoModelTest.RunBatch -artigoModelo gemini-3.8-flash
    /// </remarks>
    public static class ArtigoModelTest
    {
        private static readonly string[] CASES = { "G1_CPU_01", "G3_DRAWCALLS_01", "G5_FALTANTE_01" };
        private static readonly string[] CONDS = { "amador", "profissional" };

        private class Item { public ArtigoCase c; public string cond; }
        private static Queue<Item> _q;
        private static string _model, _genCfg;
        private static ArtigoConfig _cfg;
        private static bool _busy;
        private static double _notBefore;
        private static readonly StringBuilder _csv = new StringBuilder();
        private static readonly List<(string cond, int inTok, int outTok, int thinkTok, double usd, double sec, bool ok)> _rows =
            new List<(string, int, int, int, double, double, bool)>();

        public static void RunBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            _model = "gemini-3.8-flash";
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-artigoModelo") _model = args[i + 1];
            _cfg = ArtigoPaths.Config;
            _genCfg = ArtigoPaths.GenerationConfigJson;
            var all = ArtigoCase.LoadAll().ToDictionary(c => c.id);
            _q = new Queue<Item>();
            foreach (string id in CASES)
                foreach (string cond in CONDS)
                    if (all.TryGetValue(id, out var c)) _q.Enqueue(new Item { c = c, cond = cond });
            _csv.Append("modelo;caso;condicao;http;versao;fim;tokens_entrada;tokens_saida;tokens_raciocinio;custo_usd;segundos;formato_valido;valida_porem_errada;detalhe\n");
            Debug.Log($"[Artigo] Teste do modelo {_model}: {_q.Count} chamadas.");
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (_busy || EditorApplication.timeSinceStartup < _notBefore) return;
            if (_q.Count == 0) { Finish(); return; }
            var it = _q.Dequeue();
            string body = AiProviderAdapter.BuildGeminiBody(ArtigoPaths.Prompt(it.cond),
                new List<AiTurn> { new AiTurn(AiTurn.ROLE_USER, it.c.userText) }, _genCfg);
            _busy = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            AiHttpClient.SendRaw(AiProviderKind.Gemini, _model, body,
                raw => { sw.Stop(); Record(it, raw, 200, null, sw.Elapsed.TotalSeconds); },
                err => { sw.Stop(); Record(it, AiHttpClient.LastErrorBody ?? "", AiHttpClient.LastErrorStatus, err, sw.Elapsed.TotalSeconds); });
        }

        private static void Record(Item it, string raw, long status, string err, double sec)
        {
            var r = new ArtigoResponse();
            string version = "", finish = "";
            bool ok = false, wrong = false; string detail = err ?? "";
            if (err == null)
            {
                string text = AiProviderAdapter.ParseAnswer(AiProviderKind.Gemini, raw) ?? "";
                try
                {
                    var root = MiniJson.Parse(raw) as Dictionary<string, object>;
                    if (root.TryGetValue("modelVersion", out object mv)) version = mv as string;
                    if (root.TryGetValue("usageMetadata", out object uo) && uo is Dictionary<string, object> u)
                    {
                        r.tokensEntrada = u.TryGetValue("promptTokenCount", out object a) && a is double da ? (int)da : 0;
                        r.tokensSaida = u.TryGetValue("candidatesTokenCount", out object b) && b is double db ? (int)db : 0;
                        r.tokensRaciocinio = u.TryGetValue("thoughtsTokenCount", out object t) && t is double dt ? (int)dt : 0;
                    }
                    if (root.TryGetValue("candidates", out object co) && co is List<object> cl && cl.Count > 0 &&
                        cl[0] is Dictionary<string, object> c0 && c0.TryGetValue("finishReason", out object fr)) finish = fr as string;
                }
                catch (FormatException) { }
                var m = ArtigoValidator.Validate(it.c, it.cond, 0, text);
                ok = m.formatoValido; wrong = m.validaPoremErrada; detail = m.detalhe;
            }
            double usd = (r.tokensEntrada * _cfg.precoEntradaUsdMilhao + (r.tokensSaida + r.tokensRaciocinio) * _cfg.precoSaidaUsdMilhao) / 1e6;
            _rows.Add((it.cond, r.tokensEntrada, r.tokensSaida, r.tokensRaciocinio, usd, sec, err == null));
            string I(double x) => x.ToString("0.######", CultureInfo.InvariantCulture);
            _csv.Append($"{_model};{it.c.id};{it.cond};{status};{version};{finish};{r.tokensEntrada};{r.tokensSaida};{r.tokensRaciocinio};{I(usd)};{I(sec)};{(ok ? 1 : 0)};{(wrong ? 1 : 0)};\"{detail.Replace("\"", "'")}\"\n");
            Debug.Log($"[Artigo] {it.c.id}/{it.cond}: http {status}, versão {version}, tokens {r.tokensEntrada}/{r.tokensSaida}/{r.tokensRaciocinio}, US$ {usd:0.0000}, {sec:0.0} s");
            _busy = false;
            _notBefore = EditorApplication.timeSinceStartup + _cfg.intervaloSegundos;
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;
            string dir = Path.Combine(ArtigoPaths.Dir, "teste-modelo");
            ArtigoPaths.WriteUtf8(Path.Combine(dir, _model + ".csv"), _csv.ToString());
            foreach (string cond in CONDS)
            {
                var r = _rows.Where(x => x.cond == cond && x.ok).ToList();
                if (r.Count == 0) continue;
                Debug.Log($"[Artigo] {_model} {cond}: média por chamada US$ {r.Average(x => x.usd):0.0000}, " +
                          $"{r.Average(x => x.thinkTok):0} tokens de raciocínio, {r.Average(x => x.sec):0.0} s → 150 chamadas ≈ US$ {r.Average(x => x.usd) * 150:0.00}");
            }
            EditorApplication.Exit(0);
        }
    }
}
