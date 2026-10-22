using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Self-test of the AI provider layer: request bodies, headers, endpoints, response
    /// parsing for the four providers, and a real HTTP round trip through the plugin's
    /// own client against a local mock server.
    /// </summary>
    /// <remarks>
    /// MOTIVO: só o Gemini tinha sido validado de verdade — OpenAI, Anthropic e Ollama
    /// nunca foram chamados. Testar contra as APIs reais exige a chave do dev, e usar a
    /// chave dele por conta própria não é aceitável. O que dá para provar sem chave:
    ///
    ///  - o corpo que o plugin MONTA é exatamente o que cada API documenta;
    ///  - o plugin LÊ as respostas no formato documentado (amostras da documentação);
    ///  - o caminho HTTP inteiro funciona — transporte, erros, teste de modelo, laudo e
    ///    chat — contra um servidor simulado no formato OpenAI, que é o do Ollama.
    ///
    /// O que sobra para a chave do dev é só a autenticação. Nenhuma chave é lida para
    /// fora, gravada ou impressa aqui.
    ///
    /// Uso em lote: Unity -batchmode -projectPath ... -executeMethod
    /// PerformanceAdvisor.AiProviderSelfTest.RunBatch (sem -quit: o teste é assíncrono e
    /// encerra o Editor sozinho). Saída em Diagnostics/ai-selftest.txt.
    /// </remarks>
    public static class AiProviderSelfTest
    {
        private static readonly StringBuilder _log = new StringBuilder();
        private static int _pass, _fail;
        private static bool _exitWhenDone;

        [MenuItem("Tools/Performance Advisor/Diagnostics/AI provider self-test", false, 220)]
        public static void RunFromMenu() => Run(false);

        /// <summary>Entry point for -executeMethod; exits the Editor with 0 (all passed) or 1.</summary>
        public static void RunBatch() => Run(true);

        private static void Run(bool exitWhenDone)
        {
            _exitWhenDone = exitWhenDone;
            _log.Clear(); _pass = 0; _fail = 0;
            Line("Autoteste dos provedores de IA — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            try
            {
                Section("1. Corpo das requisições");     TestBodies();
                Section("2. Cabeçalhos e endereços");    TestHeadersAndEndpoints();
                Section("3. Leitura das respostas");     TestParseAnswer();
                Section("4. Listas de modelos");          TestParseModels();
                Section("5. JSON do laudo dentro do texto"); TestReportExtraction();
                Section("5b. Motivo do erro no corpo da resposta"); TestErrorMessages();
            }
            catch (Exception ex)
            {
                Fail("exceção na parte offline: " + ex);
            }

            Section("6. Ida e volta HTTP real (servidor simulado, formato OpenAI/Ollama)");
            LiveRoundTrip.Start(Finish);
        }

        // ── 1. Corpos ───────────────────────────────────────────────────────

        private static readonly List<AiTurn> TURNS = new List<AiTurn>
        {
            new AiTurn(AiTurn.ROLE_USER, "ola"),
            new AiTurn(AiTurn.ROLE_AI,   "resp"),
            new AiTurn(AiTurn.ROLE_USER, "de novo"),
        };

        private static void TestBodies()
        {
            // Referências: OpenAI Chat Completions; Anthropic Messages; Gemini
            // generateContent; Ollama OpenAI compatibility. Os corpos esperados abaixo são
            // o mínimo que cada documentação exige, na ordem em que o JsonUtility escreve.
            string openAi = AiProviderAdapter.BuildChatBody(AiProviderKind.OpenAi, "SYS", TURNS, "m");
            Expect("OpenAI: prompt de sistema como 1ª mensagem, papel 'assistant'", openAi ==
                "{\"model\":\"m\",\"messages\":[{\"role\":\"system\",\"content\":\"SYS\"}," +
                "{\"role\":\"user\",\"content\":\"ola\"},{\"role\":\"assistant\",\"content\":\"resp\"}," +
                "{\"role\":\"user\",\"content\":\"de novo\"}]}", openAi);

            string ollama = AiProviderAdapter.BuildChatBody(AiProviderKind.Ollama, "SYS", TURNS, "m");
            Expect("Ollama: mesmo corpo da OpenAI", ollama == openAi, ollama);

            string anthropic = AiProviderAdapter.BuildChatBody(AiProviderKind.Anthropic, "SYS", TURNS, "m");
            Expect("Anthropic: max_tokens obrigatório, 'system' em campo próprio, papel 'assistant'", anthropic ==
                "{\"model\":\"m\",\"max_tokens\":8192,\"system\":\"SYS\",\"messages\":[" +
                "{\"role\":\"user\",\"content\":\"ola\"},{\"role\":\"assistant\",\"content\":\"resp\"}," +
                "{\"role\":\"user\",\"content\":\"de novo\"}]}", anthropic);

            string gemini = AiProviderAdapter.BuildChatBody(AiProviderKind.Gemini, "SYS", TURNS, "m");
            Expect("Gemini: system_instruction separado, papel 'model'",
                gemini.Contains("\"system_instruction\":{\"role\":\"user\",\"parts\":[{\"text\":\"SYS\"}]}") &&
                gemini.Contains("{\"role\":\"model\",\"parts\":[{\"text\":\"resp\"}]}") &&
                !gemini.Contains("assistant"), gemini);
        }

        // ── 2. Cabeçalhos e endereços ───────────────────────────────────────

        private static void TestHeadersAndEndpoints()
        {
            // Só o FORMATO do cabeçalho é verificado — o valor é a chave do dev e não sai daqui.
            using (var r = new UnityWebRequest("https://example.invalid", "POST"))
            {
                AiProviderAdapter.ApplyHeaders(AiProviderKind.OpenAi, r);
                Expect("OpenAI: Authorization: Bearer <chave>", (r.GetRequestHeader("Authorization") ?? "").StartsWith("Bearer "), null);
                Expect("OpenAI: sem cabeçalho da Anthropic", string.IsNullOrEmpty(r.GetRequestHeader("x-api-key")), null);
            }
            using (var r = new UnityWebRequest("https://example.invalid", "POST"))
            {
                AiProviderAdapter.ApplyHeaders(AiProviderKind.Anthropic, r);
                Expect("Anthropic: x-api-key presente", r.GetRequestHeader("x-api-key") != null, null);
                Expect("Anthropic: anthropic-version: 2023-06-01", r.GetRequestHeader("anthropic-version") == "2023-06-01", null);
                Expect("Anthropic: sem Authorization Bearer", string.IsNullOrEmpty(r.GetRequestHeader("Authorization")), null);
            }
            using (var r = new UnityWebRequest("https://example.invalid", "POST"))
            {
                AiProviderAdapter.ApplyHeaders(AiProviderKind.Ollama, r);
                Expect("Ollama: nenhuma credencial enviada", string.IsNullOrEmpty(r.GetRequestHeader("Authorization"))
                                                            && string.IsNullOrEmpty(r.GetRequestHeader("x-api-key")), null);
                Expect("Todos: Content-Type: application/json", r.GetRequestHeader("Content-Type") == "application/json", null);
            }

            Expect("OpenAI: POST /v1/chat/completions",
                AiProviderAdapter.ChatEndpoint(AiProviderKind.OpenAi, "m") == "https://api.openai.com/v1/chat/completions", null);
            Expect("Anthropic: POST /v1/messages",
                AiProviderAdapter.ChatEndpoint(AiProviderKind.Anthropic, "m") == "https://api.anthropic.com/v1/messages", null);
            Expect("Gemini: /v1beta/models/{modelo}:generateContent",
                AiProviderAdapter.ChatEndpoint(AiProviderKind.Gemini, "m")
                    .StartsWith("https://generativelanguage.googleapis.com/v1beta/models/m:generateContent?key="), null);
            Expect("Ollama: {servidor}/v1/chat/completions",
                AiProviderAdapter.ChatEndpoint(AiProviderKind.Ollama, "m") == AiSettings.OllamaUrl + "/v1/chat/completions", null);
            Expect("Listagem OpenAI: /v1/models",
                AiProviderAdapter.ModelsEndpoint(AiProviderKind.OpenAi) == "https://api.openai.com/v1/models", null);
            Expect("Listagem Anthropic: /v1/models?limit=1000",
                AiProviderAdapter.ModelsEndpoint(AiProviderKind.Anthropic) == "https://api.anthropic.com/v1/models?limit=1000", null);
            Expect("Listagem Ollama: /api/tags",
                AiProviderAdapter.ModelsEndpoint(AiProviderKind.Ollama) == AiSettings.OllamaUrl + "/api/tags", null);
        }

        // ── 3. Respostas (amostras no formato das documentações) ────────────

        private static void TestParseAnswer()
        {
            const string openAi =
                "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"gpt-4o-mini\"," +
                "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"OK-openai\",\"refusal\":null}," +
                "\"logprobs\":null,\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":9,\"completion_tokens\":2,\"total_tokens\":11}}";
            Expect("OpenAI: lê choices[0].message.content", AiProviderAdapter.ParseAnswer(AiProviderKind.OpenAi, openAi) == "OK-openai", null);
            Expect("Ollama: mesma leitura da OpenAI", AiProviderAdapter.ParseAnswer(AiProviderKind.Ollama, openAi) == "OK-openai", null);

            const string anthropic =
                "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-x\"," +
                "\"content\":[{\"type\":\"text\",\"text\":\"OK-anthropic\"}],\"stop_reason\":\"end_turn\"," +
                "\"stop_sequence\":null,\"usage\":{\"input_tokens\":9,\"output_tokens\":2}}";
            Expect("Anthropic: lê content[].text", AiProviderAdapter.ParseAnswer(AiProviderKind.Anthropic, anthropic) == "OK-anthropic", null);

            const string anthropicThinking =
                "{\"type\":\"message\",\"role\":\"assistant\",\"content\":[" +
                "{\"type\":\"thinking\",\"thinking\":\"...\",\"signature\":\"s\"},{\"type\":\"text\",\"text\":\"OK-depois\"}]}";
            Expect("Anthropic: ignora bloco que não é texto", AiProviderAdapter.ParseAnswer(AiProviderKind.Anthropic, anthropicThinking) == "OK-depois", null);

            const string gemini =
                "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"OK-gemini\"}],\"role\":\"model\"}," +
                "\"finishReason\":\"STOP\",\"index\":0}],\"usageMetadata\":{\"promptTokenCount\":9}}";
            Expect("Gemini: lê candidates[0].content.parts[0].text", AiProviderAdapter.ParseAnswer(AiProviderKind.Gemini, gemini) == "OK-gemini", null);

            const string openAiError = "{\"error\":{\"message\":\"x\",\"type\":\"invalid_request_error\"}}";
            Expect("Resposta de erro vira null (não texto vazio fingindo resposta)",
                AiProviderAdapter.ParseAnswer(AiProviderKind.OpenAi, openAiError) == null, null);
        }

        // ── 4. Listas de modelos ────────────────────────────────────────────

        private static void TestParseModels()
        {
            const string openAi = "{\"object\":\"list\",\"data\":[" +
                "{\"id\":\"gpt-4o\",\"object\":\"model\"},{\"id\":\"text-embedding-3-small\"},{\"id\":\"whisper-1\"}," +
                "{\"id\":\"dall-e-3\"},{\"id\":\"gpt-4o-mini-tts\"},{\"id\":\"gpt-4o-realtime-preview\"},{\"id\":\"o3-mini\"}," +
                "{\"id\":\"gpt-3.5-turbo-instruct\"},{\"id\":\"omni-moderation-latest\"},{\"id\":\"gpt-image-1\"}," +
                "{\"id\":\"babbage-002\"},{\"id\":\"gpt-4o-transcribe\"},{\"id\":\"gpt-4o-search-preview\"}]}";
            ExpectList("OpenAI: só modelos de chat", AiProviderAdapter.ParseModels(AiProviderKind.OpenAi, openAi), "gpt-4o", "o3-mini");

            const string anthropic = "{\"data\":[{\"type\":\"model\",\"id\":\"claude-sonnet-x\",\"display_name\":\"S\"}," +
                "{\"type\":\"model\",\"id\":\"claude-haiku-y\"}],\"has_more\":false,\"first_id\":\"a\",\"last_id\":\"b\"}";
            ExpectList("Anthropic: todos os modelos", AiProviderAdapter.ParseModels(AiProviderKind.Anthropic, anthropic),
                "claude-haiku-y", "claude-sonnet-x");

            const string gemini = "{\"models\":[" +
                "{\"name\":\"models/gemini-2.5-flash\",\"supportedGenerationMethods\":[\"generateContent\",\"countTokens\"]}," +
                "{\"name\":\"models/text-embedding-004\",\"supportedGenerationMethods\":[\"embedContent\"]}," +
                "{\"name\":\"models/gemini-2.5-flash-preview-tts\",\"supportedGenerationMethods\":[\"generateContent\"]}," +
                "{\"name\":\"models/gemini-2.5-flash-image\",\"supportedGenerationMethods\":[\"generateContent\"]}," +
                "{\"name\":\"models/gemini-2.0-flash-exp\",\"supportedGenerationMethods\":[\"generateContent\"]}," +
                "{\"name\":\"models/gemma-3-27b-it\",\"supportedGenerationMethods\":[\"generateContent\"]}]}";
            ExpectList("Gemini: só modelos de texto que geram conteúdo", AiProviderAdapter.ParseModels(AiProviderKind.Gemini, gemini),
                "gemini-2.5-flash", "gemma-3-27b-it");

            const string ollama = "{\"models\":[{\"name\":\"llama3.1:8b-instruct-q4_K_M\",\"model\":\"llama3.1:8b-instruct-q4_K_M\",\"size\":1}," +
                "{\"name\":\"nomic-embed-text:latest\",\"size\":1},{\"name\":\"qwen2.5-coder:7b\",\"size\":1}]}";
            ExpectList("Ollama: mantém 'instruct', tira embedding", AiProviderAdapter.ParseModels(AiProviderKind.Ollama, ollama),
                "llama3.1:8b-instruct-q4_K_M", "qwen2.5-coder:7b");
        }

        // ── 5. Laudo embrulhado em texto ────────────────────────────────────

        internal const string REPORT_JSON =
            "{\"findings\":[{\"tipo\":\"CPU\",\"causa_raiz\":\"EnemyAI.Update\",\"codigo_refatorado\":\"// c\"," +
            "\"explicacao_didatica\":\"e\"}],\"dicas_level_design\":null}";

        private static void TestErrorMessages()
        {
            // Formatos de erro documentados por cada provedor.
            Expect("Anthropic: lê error.message",
                AiProviderAdapter.ProviderErrorMessage(
                    "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":" +
                    "\"Your credit balance is too low to access the Anthropic API.\"}}")
                == "Your credit balance is too low to access the Anthropic API.", null);
            Expect("OpenAI: lê error.message",
                AiProviderAdapter.ProviderErrorMessage(
                    "{\"error\":{\"message\":\"Unsupported parameter: 'max_tokens'\",\"type\":\"invalid_request_error\"}}")
                == "Unsupported parameter: 'max_tokens'", null);
            Expect("Gemini: lê error.message",
                AiProviderAdapter.ProviderErrorMessage(
                    "{\"error\":{\"code\":400,\"message\":\"API key not valid.\",\"status\":\"INVALID_ARGUMENT\"}}")
                == "API key not valid.", null);
            Expect("Ollama: lê error como texto",
                AiProviderAdapter.ProviderErrorMessage("{\"error\":\"model \\\"x\\\" not found\"}") == "model \"x\" not found", null);
            Expect("corpo sem motivo devolve null", AiProviderAdapter.ProviderErrorMessage("<html>Bad Request</html>") == null, null);
        }

        private static void TestReportExtraction()
        {
            MethodInfo extract = typeof(GeminiRestBridge).GetMethod("ExtractReportJson",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (extract == null)
            {
                Fail("GeminiRestBridge.ExtractReportJson não existe");
                return;
            }

            var cases = new (string name, string text)[]
            {
                ("JSON puro",                              REPORT_JSON),
                ("cercado por ```json",                    "```json\n" + REPORT_JSON + "\n```"),
                ("frase antes + cerca (comum em Claude/GPT)", "Aqui está o laudo:\n```json\n" + REPORT_JSON + "\n```"),
                ("frase antes, sem cerca (comum no Ollama)",  "Segue o laudo solicitado: " + REPORT_JSON),
                ("texto depois do JSON",                    REPORT_JSON + "\n\nEspero ter ajudado!"),
            };
            foreach (var c in cases)
            {
                string json = (string)extract.Invoke(null, new object[] { c.text });
                GeminiAdvisoryReport r = null;
                try { r = JsonUtility.FromJson<GeminiAdvisoryReport>(json); } catch { }
                Expect("laudo lido — " + c.name, r != null && r.findings != null && r.findings.Length == 1
                                                 && r.findings[0].tipo == "CPU", json);
            }
        }

        // ── 6. Ida e volta HTTP real ────────────────────────────────────────

        /// <summary>Real requests through AiHttpClient/UnityWebRequest against a local mock.</summary>
        private static class LiveRoundTrip
        {
            private static HttpListener _listener;
            private static string _baseUrl;
            private static Action _done;
            private static double _deadline;

            // Estado do usuário, restaurado no fim — o teste não pode deixar rastro.
            private static AiProviderKind _savedProvider;
            private static string _savedOllamaUrl, _savedOllamaModel;

            // O que o servidor recebeu, para conferir do lado do plugin.
            private static readonly List<string> _bodies = new List<string>();
            private static readonly List<string> _authHeaders = new List<string>();

            public static void Start(Action done)
            {
                _done = done;
                try
                {
                    int port = FreePort();
                    _baseUrl = "http://127.0.0.1:" + port;
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(_baseUrl + "/");
                    _listener.Start();
                    _listener.BeginGetContext(OnRequest, null);
                }
                catch (Exception ex)
                {
                    Fail("não foi possível subir o servidor simulado: " + ex.Message);
                    _done();
                    return;
                }

                _savedProvider    = AiSettings.Provider;
                AiSettings.Provider = AiProviderKind.Ollama;
                _savedOllamaUrl   = AiSettings.OllamaUrl;
                _savedOllamaModel = AiSettings.SelectedModel;
                AiSettings.OllamaUrl = _baseUrl;
                AiSettings.SelectedModel = "mock-model";

                _deadline = EditorApplication.timeSinceStartup + 60;
                EditorApplication.update += Watchdog;
                Step1_ListModels();
            }

            private static void Watchdog()
            {
                if (EditorApplication.timeSinceStartup < _deadline) return;
                Fail("tempo esgotado (60 s) esperando resposta do cliente HTTP");
                End();
            }

            private static void Step1_ListModels()
            {
                AiHttpClient.ListModels(
                    ids =>
                    {
                        ExpectList("listagem pelo cliente real (embedding filtrado)", ids, "llama3.1:8b-instruct");
                        Step2_ProbeOk();
                    },
                    err => { Fail("listagem: " + err); Step2_ProbeOk(); });
            }

            private static void Step2_ProbeOk()
            {
                AiHttpClient.ProbeModel("mock-model",
                    () => { Pass("teste de modelo: aceito"); Step3_Probe429(); },
                    err => { Fail("teste de modelo deveria passar: " + err); Step3_Probe429(); });
            }

            private static void Step3_Probe429()
            {
                AiHttpClient.ProbeModel("mock-429",
                    () => { Fail("teste de modelo com 429 deveria falhar"); Step4_Report(); },
                    err =>
                    {
                        Expect("teste de modelo com 429 vira 'sem cota'",
                               err.Contains(Loc.T("Sem cota", "No quota", "Sin cuota")), err);
                        Step4_Report();
                    });
            }

            private static void Step4_Report()
            {
                Action<GeminiAdvisoryReport> ok = null;
                Action<string> fail = null;
                ok = r =>
                {
                    GeminiRestBridge.OnReportReceived -= ok; GeminiRestBridge.OnError -= fail;
                    Expect("laudo completo pelo caminho real (resposta com frase + cerca)",
                        r != null && r.findings != null && r.findings.Length == 1 && r.findings[0].tipo == "CPU", null);
                    string body = _bodies.Count > 0 ? _bodies[_bodies.Count - 1] : "";
                    Expect("laudo: prompt de sistema enviado como 1ª mensagem 'system'",
                        body.Contains("\"messages\":[{\"role\":\"system\",\"content\":\"Assuma o papel"), null);
                    Expect("laudo: payload com cpu_samples e hardware_budget",
                        body.Contains("cpu_samples") && body.Contains("hardware_budget"), null);
                    // MOTIVO: o idioma da resposta segue a aba Configuração. Sem a instrução
                    // no prompt, o laudo voltaria sempre em português.
                    Expect("laudo: prompt pede a resposta no idioma escolhido",
                        body.Contains("IDIOMA DA RESPOSTA"), null);
                    Step5_Chat();
                };
                fail = msg =>
                {
                    GeminiRestBridge.OnReportReceived -= ok; GeminiRestBridge.OnError -= fail;
                    Fail("laudo pelo caminho real: " + msg);
                    Step5_Chat();
                };
                GeminiRestBridge.OnReportReceived += ok;
                GeminiRestBridge.OnError += fail;

                var s = new FrameSnapshot
                {
                    Id = 1, Kind = SnapshotKind.Cpu, FrameIndex = 10, CpuLatencyMs = 41.8f,
                    MemoryUsedBytes = 1, CulpritCallStack = "EnemyAI.Update", Occurrences = 1
                };
                var empty = Array.Empty<FrameSnapshot>();
                GeminiRestBridge.SendSnapshots(new[] { s }, empty, empty, empty, empty);
            }

            private static void Step5_Chat()
            {
                var history = new List<ChatMessage>
                {
                    new ChatMessage { role = ChatMessage.ROLE_USER, text = "Por que o EnemyAI pesa?" },
                };
                GeminiChatService.Ask("CONTEXTO DE TESTE", history,
                    answer =>
                    {
                        Expect("chat pelo caminho real", answer == "resposta do chat", answer);
                        Expect("nenhuma credencial enviada ao servidor local",
                            _authHeaders.TrueForAll(string.IsNullOrEmpty), null);
                        End();
                    },
                    err => { Fail("chat: " + err); End(); },
                    null);
            }

            private static void End()
            {
                EditorApplication.update -= Watchdog;
                try { _listener?.Stop(); _listener?.Close(); } catch { }
                _listener = null;

                AiSettings.Provider = AiProviderKind.Ollama;
                AiSettings.OllamaUrl = _savedOllamaUrl;
                AiSettings.SelectedModel = _savedOllamaModel;
                AiSettings.Provider = _savedProvider;

                Action d = _done; _done = null;
                d?.Invoke();
            }

            // ── servidor simulado (thread do HttpListener) ───────────────────

            private static void OnRequest(IAsyncResult ar)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.EndGetContext(ar); }
                catch { return; } // listener encerrado
                try { _listener.BeginGetContext(OnRequest, null); } catch { }

                string body;
                using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = sr.ReadToEnd();
                lock (_bodies)
                {
                    _bodies.Add(body);
                    _authHeaders.Add(ctx.Request.Headers["Authorization"] ?? ctx.Request.Headers["x-api-key"]);
                }

                string path = ctx.Request.Url.AbsolutePath;
                int status = 200;
                string reply;

                if (path == "/api/tags")
                {
                    reply = "{\"models\":[{\"name\":\"llama3.1:8b-instruct\"},{\"name\":\"nomic-embed-text:latest\"}]}";
                }
                else if (path == "/v1/chat/completions")
                {
                    if (body.Contains("\"model\":\"mock-429\""))
                    {
                        status = 429;
                        reply = "{\"error\":{\"message\":\"rate limit\"}}";
                    }
                    else if (body.Contains("Responda apenas: ok"))
                    {
                        reply = OpenAiAnswer("ok");
                    }
                    else if (body.Contains("REGRA CR"))
                    {
                        // MOTIVO: o caso realista — o modelo ignora "sem markdown" e põe uma
                        // frase antes. É assim que GPT, Claude e modelos locais costumam errar.
                        reply = OpenAiAnswer("Aqui está o laudo:\n```json\n" + REPORT_JSON + "\n```");
                    }
                    else
                    {
                        reply = OpenAiAnswer("resposta do chat");
                    }
                }
                else
                {
                    status = 404;
                    reply = "{}";
                }

                byte[] bytes = Encoding.UTF8.GetBytes(reply);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Close();
            }

            private static string OpenAiAnswer(string text)
            {
                var msg = new OpenAiMessage { role = "assistant", content = text };
                return "{\"choices\":[{\"index\":0,\"message\":" + JsonUtility.ToJson(msg) + ",\"finish_reason\":\"stop\"}]}";
            }

            private static int FreePort()
            {
                var l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                int p = ((IPEndPoint)l.LocalEndpoint).Port;
                l.Stop();
                return p;
            }
        }

        // ── saída ───────────────────────────────────────────────────────────

        private static void Finish()
        {
            Line("");
            Line($"RESULTADO: {_pass} passaram, {_fail} falharam.");
            string text = _log.ToString();

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Diagnostics");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "ai-selftest.txt"), text, new UTF8Encoding(false));

            if (_fail == 0) Debug.Log("[Performance Advisor] " + text);
            else            Debug.LogWarning("[Performance Advisor] " + text);

            if (_exitWhenDone) EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        private static void Section(string s) { Line(""); Line("== " + s); }
        private static void Line(string s) => _log.AppendLine(s);
        private static void Pass(string what) { _pass++; Line("  OK    " + what); }
        private static void Fail(string what) { _fail++; Line("  FALHA " + what); }

        private static void Expect(string what, bool ok, string detail)
        {
            if (ok) Pass(what);
            else Fail(what + (detail != null ? "\n        obtido: " + detail : ""));
        }

        private static void ExpectList(string what, string[] got, params string[] expected)
        {
            bool ok = got != null && got.Length == expected.Length;
            if (ok) for (int i = 0; i < expected.Length; i++) if (got[i] != expected[i]) { ok = false; break; }
            Expect(what, ok, got == null ? "null" : string.Join(", ", got));
        }
    }
}
