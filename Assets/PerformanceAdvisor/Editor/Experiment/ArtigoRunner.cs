using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PerformanceAdvisor.Artigo
{
    /// <summary>One raw model answer, as stored in Artigo/respostas/&lt;condição&gt;/.</summary>
    [Serializable]
    public class ArtigoResponse
    {
        public string caso, condicao, modelo, enviadoEm;
        public int    repeticao;
        public long   duracaoMs;
        public long   httpStatus;
        public double custoUsd;         // tokens informados pela API × preço do config (0 = camada gratuita)
        public string erro;
        public string texto;            // texto do laudo extraído da resposta
        public string finishReason;
        public string modelVersion;     // versão que a API diz ter usado
        public int    tokensEntrada, tokensSaida, tokensRaciocinio;
        public string promptSha256, userTextSha256, generationConfigSha256;
        public string raw;              // corpo HTTP inteiro, sem alteração

        public static string PathOf(string condicao, string caso, int rep) =>
            Path.Combine(ArtigoPaths.ResponsesDir(condicao), $"{caso}_r{rep}.json");
    }

    /// <summary>
    /// Sends every test case to the model, N times, with this branch's prompt — no Play Mode,
    /// no window. Resumable: an answer already on disk is never requested again.
    /// </summary>
    /// <remarks>
    /// Linha de comando (Unity fechado):
    /// Unity.exe -batchmode -projectPath D:\FlightLog -executeMethod PerformanceAdvisor.Artigo.ArtigoRunner.RunBatch -logFile Artigo\coleta.log
    ///
    /// MOTIVO da ordem repetição-primeiro (todos os casos na r1, depois todos na r2...): se a
    /// cota acabar no meio, o que foi coletado cobre todos os casos com menos repetições, em
    /// vez de metade dos casos com todas. O desenho pareado por caso continua de pé.
    ///
    /// MOTIVO de não gravar 429: limite de requisição não é resposta do modelo. O item volta
    /// para a fila e espera; cota diária esgotada encerra a coleta para retomar depois.
    /// </remarks>
    public static class ArtigoRunner
    {
        private class Item { public ArtigoCase c; public int rep; }

        private static Queue<Item> _queue;
        private static string _condicao, _prompt, _genCfg, _promptSha, _cfgSha;
        private static ArtigoConfig _cfg;
        private static double _notBefore;
        private static bool   _busy, _batch;
        private static int    _sent, _errors, _retries429;
        private static double _started;
        private static double _spentUsd, _spentThisRun;

        /// <summary>Cost already recorded in every stored answer, both conditions.</summary>
        private static double SpentSoFar()
        {
            double s = 0;
            foreach (string cond in new[] { "amador", "profissional" })
            {
                string dir = ArtigoPaths.ResponsesDir(cond);
                if (!Directory.Exists(dir)) continue;
                foreach (string f in Directory.GetFiles(dir, "*.json"))
                    s += JsonUtility.FromJson<ArtigoResponse>(File.ReadAllText(f, Encoding.UTF8)).custoUsd;
            }
            return s;
        }

        [MenuItem("Tools/Performance Advisor/Artigo/Ver o que falta coletar", false, 310)]
        public static void ShowPlan()
        {
            var q = BuildQueue(out string problem);
            string msg = problem ?? $"Condição: {_condicao}\nModelo: {_cfg.modelo}\nCasos: {ArtigoCase.LoadAll().Count}\n" +
                                    $"Repetições: {_cfg.repeticoes}\nChamadas que faltam: {q.Count}";
            EditorUtility.DisplayDialog("Artigo", msg, "OK");
        }

        [MenuItem("Tools/Performance Advisor/Artigo/Coletar respostas (envia à API)", false, 311)]
        public static void RunFromMenu()
        {
            var q = BuildQueue(out string problem);
            if (problem != null) { EditorUtility.DisplayDialog("Artigo", problem, "OK"); return; }
            if (!EditorUtility.DisplayDialog("Artigo",
                    $"Vai enviar {q.Count} requisições ao {_cfg.modelo} com o prompt \"{_condicao}\", " +
                    $"uma a cada {_cfg.intervaloSegundos:0} s. Isso usa a cota da sua chave do Gemini.\n\n" +
                    "Pode fechar esta janela: a coleta segue no Editor e retoma de onde parou se for interrompida.",
                    "Enviar", "Cancelar"))
                return;
            Start(q, false);
        }

        /// <summary>Command-line entry (batch mode). Exits the Editor when done.</summary>
        public static void RunBatch()
        {
            var q = BuildQueue(out string problem);
            if (problem != null) { Debug.LogError("[Artigo] " + problem); EditorApplication.Exit(2); return; }
            Start(q, true);
        }

        [MenuItem("Tools/Performance Advisor/Artigo/Calcular métricas", false, 312)]
        public static void AnalyzeFromMenu()
        {
            string report = ArtigoAnalysis.Run();
            EditorUtility.DisplayDialog("Artigo", report, "OK");
        }

        /// <summary>Command-line entry: recompute every metric from the stored answers.</summary>
        public static void AnalyzeBatch()
        {
            Debug.Log("[Artigo] " + ArtigoAnalysis.Run());
            EditorApplication.Exit(0);
        }

        // ── Fila ────────────────────────────────────────────────────────────

        private static Queue<Item> BuildQueue(out string problem)
        {
            problem = null;
            _cfg = ArtigoPaths.Config;
            _condicao = ArtigoPaths.Condition;
            var q = new Queue<Item>();
            if (string.IsNullOrEmpty(_condicao)) { problem = "Esta branch não tem Artigo/condicao.txt."; return q; }
            _prompt = ArtigoPaths.Prompt(_condicao);
            _genCfg = ArtigoPaths.GenerationConfigJson;
            if (string.IsNullOrEmpty(_genCfg)) { problem = "Falta Artigo/response_schema.json."; return q; }
            _promptSha = Sha(_prompt); _cfgSha = Sha(_genCfg);
            if (string.IsNullOrEmpty(AiSettings.GetApiKey(AiProviderKind.Gemini)))
            { problem = "Não há chave do Gemini salva (aba Configuração, provedor Google Gemini)."; return q; }

            var cases = ArtigoCase.LoadAll();
            if (cases.Count == 0) { problem = "Nenhum caso em Artigo/casos/. Rode a captura antes."; return q; }
            for (int rep = 1; rep <= _cfg.repeticoes; rep++)
                foreach (var c in cases)
                    if (!File.Exists(ArtigoResponse.PathOf(_condicao, c.id, rep)))
                        q.Enqueue(new Item { c = c, rep = rep });
            return q;
        }

        private static void Start(Queue<Item> q, bool batch)
        {
            _queue = q; _batch = batch; _busy = false; _sent = 0; _errors = 0; _retries429 = 0;
            _notBefore = 0; _started = EditorApplication.timeSinceStartup;
            _spentUsd = SpentSoFar(); _spentThisRun = 0;
            Debug.Log($"[Artigo] Coleta iniciada: condição {_condicao}, modelo {_cfg.modelo}, {q.Count} chamadas pendentes. " +
                      $"Gasto registrado até agora: US$ {_spentUsd:0.000} de US$ {_cfg.limiteGastoUsd:0.00}.");
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (_busy || EditorApplication.timeSinceStartup < _notBefore) return;
            if (_queue == null || _queue.Count == 0) { Finish("todas as chamadas feitas"); return; }

            // MOTIVO: teto de gasto definido pelo autor (75% do crédito). A próxima chamada é
            // estimada pela média das que já foram cobradas nesta rodada — ou, antes da primeira,
            // por um valor alto de propósito (3.000 tokens de saída) — e a coleta para ANTES de
            // uma chamada que possa passar do teto, não depois.
            double estimate = _sent > 0 ? _spentThisRun / _sent
                            : (1000 * _cfg.precoEntradaUsdMilhao + 3000 * _cfg.precoSaidaUsdMilhao) / 1e6;
            if (_spentUsd + estimate > _cfg.limiteGastoUsd)
            { Finish($"teto de gasto atingido (US$ {_spentUsd:0.000} de US$ {_cfg.limiteGastoUsd:0.00})"); return; }

            Item it = _queue.Peek();
            var turns = new List<AiTurn> { new AiTurn(AiTurn.ROLE_USER, it.c.userText) };
            string body = AiProviderAdapter.BuildGeminiBody(_prompt, turns, _genCfg);
            _busy = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            AiHttpClient.SendRaw(AiProviderKind.Gemini, _cfg.modelo, body,
                raw =>
                {
                    sw.Stop();
                    Store(it, raw, 200, null, sw.ElapsedMilliseconds);
                    _queue.Dequeue(); _sent++; _retries429 = 0;
                    _notBefore = EditorApplication.timeSinceStartup + _cfg.intervaloSegundos;
                    _busy = false;
                    if (_sent % 10 == 0) Debug.Log($"[Artigo] {_sent} respostas gravadas, {_queue.Count} na fila, gasto total US$ {_spentUsd:0.000}.");
                },
                err =>
                {
                    sw.Stop();
                    long status = AiHttpClient.LastErrorStatus;
                    string errBody = AiHttpClient.LastErrorBody ?? "";
                    _busy = false;

                    // MOTIVO: 402 = créditos pré-pagos do projeto acabaram. Não é resposta do modelo e
                    // não se resolve esperando: para a coleta sem gravar, para retomar depois da recarga.
                    if (status == 402)
                    { Finish("créditos pré-pagos esgotados (HTTP 402) — recarregue no AI Studio e rode de novo"); return; }
                    // MOTIVO: 401/403 = chave recusada. Vale para todas as chamadas seguintes; gravar
                    // cada uma como erro só suja a coleta (aconteceu: 29 registros de 401 apagados à mão).
                    if (status == 401 || status == 403)
                    { Finish($"chave de API recusada (HTTP {status}) — confira a chave na aba Configuração e rode de novo"); return; }

                    if (status == 429)
                    {
                        // MOTIVO: o corpo diz QUAL cota acabou e o valor dela — sem isso o log só
                        // dizia "esgotada", e o autor não tinha como decidir entre esperar e pagar.
                        Debug.LogWarning("[Artigo] 429 — " + (AiProviderAdapter.ProviderErrorMessage(errBody) ?? "(sem corpo)"));
                        // MOTIVO: o Gemini diz no corpo QUAL cota acabou. Por minuto → espera;
                        // por dia → não adianta esperar minutos, encerra e retoma amanhã.
                        if (errBody.IndexOf("PerDay", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            errBody.IndexOf("per day", StringComparison.OrdinalIgnoreCase) >= 0)
                        { Finish("cota DIÁRIA da chave esgotada — rode de novo amanhã, a coleta retoma de onde parou"); return; }
                        if (++_retries429 > _cfg.maxTentativasPor429)
                        { Finish($"429 repetido {_retries429 - 1} vezes seguidas — interrompido"); return; }
                        Debug.LogWarning($"[Artigo] 429 (limite por minuto). Esperando {_cfg.esperaApos429Segundos:0} s.");
                        _notBefore = EditorApplication.timeSinceStartup + _cfg.esperaApos429Segundos;
                        return;
                    }

                    // Outros erros ficam registrados como resposta com erro (a análise os conta
                    // à parte, como "sem resposta") e a coleta segue.
                    Store(it, errBody, status, err, sw.ElapsedMilliseconds);
                    _queue.Dequeue(); _errors++;
                    _notBefore = EditorApplication.timeSinceStartup + _cfg.intervaloSegundos;
                    Debug.LogWarning($"[Artigo] {it.c.id} r{it.rep}: {err}");
                });
        }

        private static void Store(Item it, string raw, long status, string error, long ms)
        {
            var r = new ArtigoResponse
            {
                caso = it.c.id, condicao = _condicao, modelo = _cfg.modelo, repeticao = it.rep,
                enviadoEm = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), duracaoMs = ms,
                httpStatus = status, erro = error ?? "", raw = raw ?? "",
                promptSha256 = _promptSha, userTextSha256 = Sha(it.c.userText), generationConfigSha256 = _cfgSha
            };
            if (error == null)
            {
                r.texto = AiProviderAdapter.ParseAnswer(AiProviderKind.Gemini, raw) ?? "";
                ReadMetadata(raw, r);
                r.custoUsd = (r.tokensEntrada * _cfg.precoEntradaUsdMilhao +
                              (r.tokensSaida + r.tokensRaciocinio) * _cfg.precoSaidaUsdMilhao) / 1e6;
                _spentUsd += r.custoUsd; _spentThisRun += r.custoUsd;
            }
            ArtigoPaths.WriteUtf8(ArtigoResponse.PathOf(_condicao, it.c.id, it.rep), JsonUtility.ToJson(r, true));
        }

        private static void ReadMetadata(string raw, ArtigoResponse r)
        {
            try
            {
                var root = MiniJson.Parse(raw) as Dictionary<string, object>;
                if (root == null) return;
                if (root.TryGetValue("modelVersion", out object mv)) r.modelVersion = mv as string;
                if (root.TryGetValue("candidates", out object co) && co is List<object> cl && cl.Count > 0 &&
                    cl[0] is Dictionary<string, object> c0 && c0.TryGetValue("finishReason", out object fr))
                    r.finishReason = fr as string;
                if (root.TryGetValue("usageMetadata", out object uo) && uo is Dictionary<string, object> u)
                {
                    r.tokensEntrada    = Int(u, "promptTokenCount");
                    r.tokensSaida      = Int(u, "candidatesTokenCount");
                    r.tokensRaciocinio = Int(u, "thoughtsTokenCount");
                }
            }
            catch (FormatException) { /* metadado é extra; o texto já foi gravado */ }
        }

        private static int Int(Dictionary<string, object> d, string k) =>
            d.TryGetValue(k, out object v) && v is double x ? (int)x : 0;

        private static void Finish(string why)
        {
            EditorApplication.update -= Tick;
            double min = (EditorApplication.timeSinceStartup - _started) / 60.0;
            Debug.Log($"[Artigo] Coleta encerrada ({why}). Gravadas: {_sent}, com erro: {_errors}, " +
                      $"pendentes: {_queue?.Count ?? 0}, duração: {min:0.0} min, gasto nesta rodada US$ {_spentThisRun:0.000}, " +
                      $"total US$ {_spentUsd:0.000}.");
            try { Debug.Log("[Artigo] " + ArtigoAnalysis.Run()); }
            catch (Exception ex) { Debug.LogError("[Artigo] Falha ao calcular as métricas: " + ex); }
            if (_batch) EditorApplication.Exit(_queue != null && _queue.Count > 0 ? 3 : 0);
        }

        public static string Sha(string s)
        {
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""))).Replace("-", "").ToLowerInvariant();
        }
    }
}
