using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// "Ponte LLM" — domínio do laudo: monta o payload post-mortem, envia via
    /// <see cref="AiHttpClient"/> e emite eventos fortemente tipados com o
    /// relatório parseado. O transporte (fallback/retry) vive no client.
    /// </summary>
    public static class GeminiRestBridge
    {
        // System prompt — agora exige múltiplos "findings" para que cada gargalo
        // distinto receba sua própria análise dedicada.
        private const string SYSTEM_PROMPT =
@"Assuma o papel de Engenheiro de Software Unity Sênior especializado em otimização
de desempenho para o hardware alvo informado.

REGRA CRÍTICA: o payload tem 5 ARRAYS SEPARADOS de amostras, um por tipo
de gargalo (cpu_samples, gc_samples, drawcalls_samples,
memory_samples, gpu_samples).

Cada amostra de um array é uma CAUSA RAIZ DISTINTA, já deduplicada pelo plugin:
frames que compartilhavam o mesmo culpado foram colapsados num representante.
Cada amostra pode incluir um campo ""culprit_stack"" com os Scripts→Métodos
lidos do Profiler do Unity — quando presente, CITE ESSES MÉTODOS LITERALMENTE
na causa_raiz do finding correspondente.

O ""hardware_budget"" descreve a BUILD REAL do alvo (CPU, RAM, GPU ou
aparelho mobile/VR) — cite o hardware pelo nome nas recomendações quando
fizer sentido (ex.: limites da GPU citada, RAM disponível).

O ""medido_em"", quando presente, é o aparelho REAL em que os números foram
coletados (a build rodando nele, sem Editor no meio). Quando ele existe, os tempos
são do próprio aparelho — cite-o pelo nome e não trate os valores como estimativa.

Um campo com valor null NÃO foi medido (o aparelho não expõe aquele contador).
Nunca o interprete como zero nem tire conclusão dele.

REGRA DE QUANTIDADE: gere UM finding POR AMOSTRA enviada, não um por array.
Se cpu_samples tem 3 amostras, são 3 problemas diferentes de CPU e você deve
retornar 3 findings de tipo ""CPU"", cada um com seu próprio codigo_refatorado
atacando aquele culprit_stack específico. NÃO consolide amostras distintas do
mesmo array numa resposta só — elas já foram deduplicadas e o que sobrou é
genuinamente diferente.

O tipo de cada finding vem do array de origem da amostra:
- amostra de cpu_samples → tipo ""CPU"". Analise os
  cpu_ms das amostras e cite frames específicos no diagnóstico.
- amostra de gc_samples → tipo ""GC"". Analise gc_collect_ms, que é o tempo
  que a COLETA de lixo custou naquele frame, medido — compare com cpu_ms para dizer
  quanto do pico de CPU o GC explica. gc_bytes é contexto (quanto foi alocado), e o
  responsável indica quem alocou. Se gc_collect_ms for pequeno perto de cpu_ms, diga
  que o GC NÃO explica o pico, em vez de prescrever redução de alocação por reflexo.
- amostra de drawcalls_samples → tipo ""DrawCalls"".
  Analise draw_calls.
- amostra de memory_samples → tipo ""Memoria"". Analise
  mem_bytes (pegada do processo vista pelo SO) contra o teto do hardware_budget.
- amostra de gpu_samples → tipo ""GPU"". Analise gpu_ms,
  que é tempo de placa de vídeo — custo de PIXEL, não de CPU. Prescreva
  resolução, overdraw, complexidade de shader, transparência sobreposta,
  pós-processamento e sombras. NUNCA prescreva batching ou redução de draw
  calls num finding de GPU: batching alivia CPU, não a placa.
- Se os 5 arrays estão vazios mas violations_summary tem contagens > 0
  → use o summary e gere 1 finding com tipo ""Geral"".


O ""violations_summary"" mostra a contagem TOTAL de cada violação na sessão
inteira — útil pra dimensionar a gravidade.


Para cada finding:
(1) identifique a causa raiz daquele gargalo específico;
(2) forneça o código C# refatorado completo e funcional para corrigir aquele gargalo;
(3) explique a otimização em linguagem acessível para desenvolvedores iniciantes.

Adicionalmente, se houver problema de Draw Calls, preencha ""dicas_level_design""
com orientações de Level Design e gestão de assets (Static Batching, GPU Instancing,
atlas de textura, occlusion culling, LOD). Caso contrário, deixe ""dicas_level_design"" como null.

Retorne EXCLUSIVAMENTE um objeto JSON válido, sem markdown, sem blocos de código,
sem texto antes ou depois. O JSON deve ter exatamente este formato:
{
  ""findings"": [
    {
      ""tipo"": ""CPU"" | ""GC"" | ""DrawCalls"" | ""GPU"" | ""Memoria"" | ""Geral"",
      ""causa_raiz"": ""string"",
      ""codigo_refatorado"": ""string com o código C# completo"",
      ""explicacao_didatica"": ""string""
    }
  ],
  ""dicas_level_design"": ""string ou null se não aplicável""
}";

        /// <summary>Fired when the request leaves the Editor.</summary>
        public static event Action OnRequestStarted;

        /// <summary>
        /// Fired quando o modelo preview devolve 5xx e a ponte re-tenta sozinha com
        /// o modelo estável. Carrega uma mensagem amigável pra UI exibir.
        /// </summary>
        public static event Action<string> OnFallbackRetry;

        /// <summary>
        /// True se o último laudo recebido foi gerado pelo modelo estável via
        /// fallback automático (e não pelo modelo selecionado na Configuração).
        /// </summary>
        public static bool LastReportUsedFallback { get; private set; }

        /// <summary>Fired with a parsed report on success.</summary>
        public static event Action<GeminiAdvisoryReport> OnReportReceived;

        /// <summary>Fired with a user-facing message on any failure.</summary>
        public static event Action<string> OnError;

        /// <summary>
        /// Envia uma seleção de snapshots para o Gemini. Cada array é independente —
        /// passar array vazio para tipos que o dev decidiu não enviar. A call retorna
        /// imediatamente; observe <see cref="OnReportReceived"/>/<see cref="OnError"/>.
        /// </summary>
        public static void SendSnapshots(FrameSnapshot[] cpuSamples, FrameSnapshot[] gcSamples,
                                         FrameSnapshot[] drawSamples,
                                         FrameSnapshot[] memSamples, FrameSnapshot[] gpuSamples)
        {
            if (!AiSettings.IsReady)
            {
                OnError?.Invoke(GeminiChatService.NotReadyMessage());
                return;
            }
            int total = (cpuSamples?.Length ?? 0) + (gcSamples?.Length ?? 0)
                      + (drawSamples?.Length ?? 0)
                      + (memSamples?.Length ?? 0) + (gpuSamples?.Length ?? 0);
            if (total == 0)
            {
                OnError?.Invoke(Loc.T("Nenhum snapshot selecionado para analisar.",
                                      "No snapshot selected for analysis.",
                                      "Ningún snapshot seleccionado para analizar."));
                return;
            }

            string userText = BuildUserText(cpuSamples ?? System.Array.Empty<FrameSnapshot>(),
                                            gcSamples  ?? System.Array.Empty<FrameSnapshot>(),
                                            drawSamples ?? System.Array.Empty<FrameSnapshot>(),
                                            memSamples ?? System.Array.Empty<FrameSnapshot>(),
                                            gpuSamples ?? System.Array.Empty<FrameSnapshot>());

            // MOTIVO: o corpo deixou de ser montado aqui. Cada provedor quer um formato
            // diferente — prompt de sistema em campo separado (Gemini, Anthropic) ou
            // como primeira mensagem (OpenAI) — e essa tradução vive no adaptador. Aqui
            // só se diz O QUE mandar: prompt de sistema e um turno de usuário.
            var turns = new System.Collections.Generic.List<AiTurn>(1)
            {
                new AiTurn(AiTurn.ROLE_USER, userText)
            };

            OnRequestStarted?.Invoke();

            // MOTIVO: LastReportUsedFallback fica false porque a troca automática de
            // modelo foi removida junto com o cliente antigo. Ela fazia sentido com dois
            // modelos fixos no código; agora o dev escolhe o modelo de uma lista vinda
            // da API dele, e substituir por baixo devolveria o laudo de um modelo que
            // ele não pediu, sem ele saber.
            LastReportUsedFallback = false;

            // MOTIVO: o prompt documentado no TIC (A.4.3) segue intacto; o idioma da
            // resposta entra como uma instrução a mais no fim, conforme a aba Configuração.
            //
            // ARTIGO: nas branches do experimento (Artigo/condicao.txt existe), o laudo usa o
            // prompt da condição, SOZINHO — sem a instrução de idioma, que seria uma segunda
            // variável — e, no Gemini, o mesmo generationConfig do experimento (esquema de
            // resposta e temperatura 0). Assim o plugin usado à mão se comporta como a coleta.
            string artigoPrompt = Artigo.ArtigoPaths.ActivePrompt;
            string system = artigoPrompt ?? SYSTEM_PROMPT + "\n\n" + Loc.AiLanguageInstruction();
            string genCfg = artigoPrompt != null && AiSettings.Provider == AiProviderKind.Gemini
                ? Artigo.ArtigoPaths.GenerationConfigJson : null;
            AiHttpClient.Chat(system, turns,
                onAnswer:      ParseAndDispatch,
                onError:       msg => OnError?.Invoke(msg),
                onRetryNotice: msg => OnFallbackRetry?.Invoke(msg),
                geminiGenerationConfig: genCfg);
        }

        /// <summary>Reads the advisory JSON out of the model's answer.</summary>
        /// <remarks>
        /// MOTIVO: recebe TEXTO, não mais o JSON cru da resposta HTTP. Extrair o texto de
        /// dentro do envelope (`candidates`, `choices` ou `content`, conforme o provedor)
        /// virou trabalho do adaptador — aqui só resta o problema real, que é o modelo
        /// devolver o JSON do laudo embrulhado em cercas de markdown.
        /// </remarks>
        private static void ParseAndDispatch(string answerText)
        {
            try
            {
                string innerJson = ExtractReportJson(answerText);

                var report = JsonUtility.FromJson<GeminiAdvisoryReport>(innerJson);
                if (report == null || report.findings == null || report.findings.Length == 0)
                {
                    OnError?.Invoke(Loc.T("Não foi possível interpretar o laudo da IA. Tente novamente.",
                                          "Could not parse the AI's report. Try again.",
                                          "No se pudo interpretar el informe de la IA. Inténtalo de nuevo."));
                    return;
                }

                OnReportReceived?.Invoke(report);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PerformanceAdvisor] JSON parse error: {ex}");
                OnError?.Invoke($"Erro ao processar resposta da IA: {ex.Message}");
            }
        }

        /// <summary>Pulls the report's JSON object out of the model's answer text.</summary>
        /// <remarks>
        /// MOTIVO: a versão anterior só tirava as cercas de markdown quando a resposta
        /// COMEÇAVA com elas. Com o Gemini isso bastava; com GPT, Claude e modelos locais é
        /// comum vir uma frase antes ("Aqui está o laudo:") ou depois, mesmo com o prompt
        /// pedindo JSON puro — e o laudo inteiro falhava por causa de uma frase de cortesia.
        /// O laudo é sempre UM objeto: vai do primeiro '{' ao último '}'. Se não houver
        /// objeto, devolve o texto como veio e o erro de leitura aparece como antes.
        /// </remarks>
        private static string ExtractReportJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int start = s.IndexOf('{');
            int end = s.LastIndexOf('}');
            if (start < 0 || end <= start) return s.Trim();
            return s.Substring(start, end - start + 1);
        }

        /// <summary>Everything the report request carries, gathered before it is formatted.</summary>
        /// <remarks>
        /// MOTIVO: separar COLETAR de FORMATAR. O texto enviado à IA saía direto do estado
        /// vivo da sessão, e nada fora de uma sessão conseguia reproduzi-lo. Com os dados
        /// num struct, o mesmo formatador serve à sessão real e a quem precisa remontar o
        /// payload depois (testes, experimento) — o formato fica num lugar só.
        /// </remarks>
        public struct ReportPayload
        {
            public string Description;
            public float  CpuBudgetMs;
            public int    DrawCallBudget;     // 0 = sem teto
            public long   MemoryBudgetBytes;  // 0 = sem teto
            public float  CpuFactor;          // tempo medido → tempo no alvo
            public float  GpuFactor;
            public string MeasuredOn;         // vazio = Editor
            public string DrawCounter;
            public string MemoryCounter;
            public bool   MemResolved;
            public bool   GpuResolved;
            public (int cpu, int gc, int draw, int gpu, int mem,
                    float cpuMaxMs, long gcMaxBytes, int drawMax, float gpuMaxMs, long memMaxBytes) Summary;
            public FrameSnapshot[] Cpu, Gc, Draw, Mem, Gpu;
            public ScriptSource[] Sources;    // vazio = não enviar código
        }

        /// <summary>Reads the live session state into a payload for these samples.</summary>
        public static ReportPayload CapturePayload(FrameSnapshot[] cpuSamples, FrameSnapshot[] gcSamples,
                                                   FrameSnapshot[] drawSamples,
                                                   FrameSnapshot[] memSamples, FrameSnapshot[] gpuSamples)
        {
            return new ReportPayload
            {
                Description       = HardwareBuildSettings.Description,
                CpuBudgetMs       = HardwareBuildSettings.CpuBudgetMs,
                DrawCallBudget    = HardwareBuildSettings.DrawCallBudget,
                MemoryBudgetBytes = HardwareBuildSettings.MemoryBudgetBytes,
                CpuFactor         = HardwareBuildSettings.CpuProjectionFactor,
                GpuFactor         = HardwareBuildSettings.GpuProjectionFactor,
                MeasuredOn        = TelemetryMonitorCore.IsRemote ? TelemetryMonitorCore.SessionTargetName : "",
                DrawCounter       = TelemetryMonitorCore.DrawCallsCounterName,
                MemoryCounter     = TelemetryMonitorCore.MemoryCounterName,
                MemResolved       = TelemetryMonitorCore.RemoteMemResolved,
                GpuResolved       = TelemetryMonitorCore.RemoteGpuResolved,
                Summary           = SnapshotHeuristicsController.GetViolationSummary(),
                Cpu = cpuSamples, Gc = gcSamples, Draw = drawSamples, Mem = memSamples, Gpu = gpuSamples,
                Sources = AiSettings.SendScriptSources
                    ? ScriptSourceResolver.Resolve(StacksOf(cpuSamples, gcSamples, drawSamples, memSamples, gpuSamples))
                    : System.Array.Empty<ScriptSource>()
            };
        }

        /// <summary>Every culprit stack in the given samples.</summary>
        public static IEnumerable<string> StacksOf(params FrameSnapshot[][] arrays)
        {
            foreach (var arr in arrays)
            {
                if (arr == null) continue;
                foreach (var s in arr) if (!string.IsNullOrEmpty(s.CulpritCallStack)) yield return s.CulpritCallStack;
            }
        }

        private static string BuildUserText(FrameSnapshot[] cpuSamples, FrameSnapshot[] gcSamples,
                                            FrameSnapshot[] drawSamples,
                                            FrameSnapshot[] memSamples, FrameSnapshot[] gpuSamples)
            => FormatUserText(CapturePayload(cpuSamples, gcSamples, drawSamples, memSamples, gpuSamples));

        /// <summary>The user turn of the report request, built only from the payload.</summary>
        public static string FormatUserText(ReportPayload p)
        {
            // Hand-rolled string assembly keeps the payload compact and avoids
            // the need for a Dictionary<,> serializer (JsonUtility doesn't support it).
            var sb = new StringBuilder(4096);
            // MOTIVO: a IA recebe a build REAL — cita o hardware nas recomendações.
            sb.Append("hardware_budget: ").Append(p.Description)
              .Append(" — budgets: ")
              .Append(p.CpuBudgetMs.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append("ms/frame, ")
              // MOTIVO: teto 0 significa SEM TETO, e mandar o literal "0 draw calls" /
              // "0 bytes" faria o modelo concluir que tudo estourou — o oposto do que o
              // número quer dizer. O laudo precisa receber a ausência como ausência.
              .Append(p.DrawCallBudget > 0
                  ? p.DrawCallBudget + " draw calls"
                  : "draw calls sem teto (nao julgar)").Append(", ")
              .Append(p.MemoryBudgetBytes > 0
                  ? p.MemoryBudgetBytes + " bytes de memoria"
                  : "memoria sem teto (nao julgar)").Append(". ");

            // MOTIVO: o orçamento é o do ALVO, mas no PC o tempo é medido NESTA máquina. A
            // amostra era capturada por estourar o alvo (tempo × fator) e chegava à IA com o
            // tempo cru: 9 ms "de 16,7 ms", dentro do orçamento — o laudo lia o contrário do
            // que disparou a captura. Agora cpu_ms e gpu_ms vão já projetados, e a linha diz
            // isso; com fator 1 (aparelho medido direto) nada muda e nada é dito.
            bool projeta = (p.CpuFactor > 0f && Mathf.Abs(p.CpuFactor - 1f) > 0.005f)
                        || (p.GpuFactor > 0f && Mathf.Abs(p.GpuFactor - 1f) > 0.005f);
            if (projeta)
                sb.Append("projecao: cpu_ms, gc_collect_ms e gpu_ms ja projetados para o alvo (fator cpu ")
                  .Append(p.CpuFactor.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(", fator gpu ")
                  .Append(p.GpuFactor.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                  .Append("). ");

            // MOTIVO: o laudo sabia que o alvo era "Android, 60 FPS", mas não EM QUAL
            // aparelho os números foram medidos. Com a leitura remota, esse aparelho é
            // conhecido — e a IA só pode citar o hardware real se receber o nome dele.
            // Ausente quando a sessão leu o Editor: aí não há aparelho a nomear.
            if (!string.IsNullOrEmpty(p.MeasuredOn))
                sb.Append("medido_em: ").Append(p.MeasuredOn).Append(". ");

            // MOTIVO: o contador de draw calls varia por pipeline; declarar qual foi lido
            // evita que o modelo (e o leitor do laudo) assuma a grandeza errada.
            sb.Append("counters: {draw_calls_from:\"").Append(p.DrawCounter)
              .Append("\", memory_from:\"").Append(p.MemoryCounter)
              .Append("\"}. ");

            // Resumo agregado — conta TODA violação da sessão, independente do buffer.
            var v = p.Summary;
            sb.Append("violations_summary: {");
            sb.Append("cpu_violations:").Append(v.cpu)
              .Append(", cpu_max_ms:").Append(v.cpuMaxMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(", gc_violations:").Append(v.gc)
              .Append(", gc_max_bytes:").Append(v.gcMaxBytes);
            sb.Append(", drawcalls_violations:").Append(v.draw)
              .Append(", drawcalls_max:").Append(v.drawMax);
            sb.Append(", gpu_violations:").Append(v.gpu)
              .Append(", gpu_max_ms:").Append(v.gpuMaxMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(", memory_violations:").Append(v.mem)
              .Append(", memory_max_bytes:").Append(v.memMaxBytes);
            sb.Append("}. ");

            // 6 arrays separados — vêm da seleção do dev (aba Snapshots).
            AppendSamples(sb, "cpu_samples",        p.Cpu,  p);
            AppendSamples(sb, "gc_samples",         p.Gc,   p);
            AppendSamples(sb, "drawcalls_samples",  p.Draw, p);
            AppendSamples(sb, "memory_samples",     p.Mem,  p);
            AppendSamples(sb, "gpu_samples",        p.Gpu,  p);

            // MOTIVO: o código dos scripts da pilha vai no MESMO turno, como dado. Sem ele, um
            // prompt que proíbe inventar não tem como corrigir nada; com ele, a correção pode
            // partir do código real. Ausente quando não há pilha ou o usuário desligou o envio.
            if (p.Sources != null && p.Sources.Length > 0)
            {
                sb.Append(" script_sources: [");
                for (int i = 0; i < p.Sources.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append("{file:\"").Append(Escape(p.Sources[i].file))
                      .Append("\", code:\"").Append(Escape(p.Sources[i].code)).Append("\"}");
                }
                sb.Append(']');
            }

            return sb.ToString();
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        /// <summary>A measured time as the target would see it; unchanged when there is no factor.</summary>
        public static float OnTarget(float measuredMs, float factor) => factor > 0f ? measuredMs * factor : measuredMs;

        private static void AppendSamples(StringBuilder sb, string label, FrameSnapshot[] samples, ReportPayload p)
        {
            if (samples == null) samples = System.Array.Empty<FrameSnapshot>();
            sb.Append(' ').Append(label).Append(": [");
            for (int i = 0; i < samples.Length; i++)
            {
                FrameSnapshot s = samples[i];
                if (i > 0) sb.Append(", ");
                // MOTIVO: contador ausente no aparelho virava "gpu_ms:0.00" / "mem_bytes:0"
                // no payload. A tela já dizia "contador ausente", mas a IA recebia zero — e o
                // prompt manda analisar gpu_ms: zero de GPU se lê como placa ociosa. Ausência
                // vai como null, que o prompt declara como "não medido".
                sb.Append("{frame:").Append(s.FrameIndex)
                  .Append(", cpu_ms:").Append(OnTarget(s.CpuLatencyMs, p.CpuFactor).ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(", gc_bytes:").Append(s.GcAllocatedBytes)
                  // MOTIVO: a coleta roda na thread principal — é tempo de CPU, e vai pelo mesmo
                  // fator. Com cpu_ms projetado e gc_collect_ms cru, a comparação que o prompt
                  // pede ("quanto do pico a coleta explica") ficava distorcida pelo fator.
                  .Append(", gc_collect_ms:").Append(OnTarget(s.GcCollectMs, p.CpuFactor).ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(", draw_calls:").Append(s.DrawCallsCount)
                  .Append(", mem_bytes:").Append(p.MemResolved
                      ? s.MemoryUsedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")
                  .Append(", gpu_ms:").Append(p.GpuResolved
                      ? OnTarget(s.GpuMs, p.GpuFactor).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "null")
                  .Append(", t_ms:").Append(s.TimestampMs);
                if (!string.IsNullOrEmpty(s.CulpritCallStack))
                {
                    // Escapa quebras de linha e aspas pra não quebrar o pseudo-JSON.
                    string esc = s.CulpritCallStack.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " | ");
                    sb.Append(", culprit_stack:\"").Append(esc).Append('"');
                }
                sb.Append('}');
            }
            sb.Append(']');
        }

        private static string BuildRequestBody(string userText)
        {
            var req = new GeminiRequest
            {
                system_instruction = new GeminiContent
                {
                    role  = "user",
                    parts = new[] { new GeminiPart { text = SYSTEM_PROMPT } }
                },
                contents = new[]
                {
                    new GeminiContent
                    {
                        role  = "user",
                        parts = new[] { new GeminiPart { text = userText } }
                    }
                }
            };
            return JsonUtility.ToJson(req);
        }
    }
}
