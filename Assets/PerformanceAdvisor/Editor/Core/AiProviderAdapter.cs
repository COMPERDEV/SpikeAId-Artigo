using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Translates the plugin's provider-independent request into each API's wire format,
    /// and reads the answer back out.
    /// </summary>
    /// <remarks>
    /// MOTIVO: as quatro APIs pedem coisas diferentes para fazer a MESMA coisa — a chave
    /// vai na URL (Gemini), num Bearer (OpenAI) ou num header próprio (Anthropic); o
    /// prompt de sistema é um campo separado em três delas e uma mensagem de papel
    /// "system" na outra; a resposta sai em `candidates`, `choices` ou `content`.
    ///
    /// Tudo isso é tradução de borda. Concentrado aqui, o resto do plugin continua
    /// mandando "prompt de sistema + turnos" e recebendo texto, sem saber com quem está
    /// falando. Espalhar esses ifs pela Ponte LLM seria pagar o custo em cada ponto de
    /// uso e garantir que o quinto provedor exigisse mexer em tudo de novo.
    ///
    /// Ollama reusa o caminho da OpenAI de propósito: a API dele é compatível, então a
    /// diferença é só o endereço base e a ausência de chave.
    /// </remarks>
    public static class AiProviderAdapter
    {
        // MOTIVO: a Anthropic exige max_tokens e rejeita a requisição sem ele. O valor
        // precisa caber o laudo inteiro (JSON multi-finding com código) — 8192 é o teto
        // confortável dos modelos atuais dela.
        private const int ANTHROPIC_MAX_TOKENS = 8192;

        // MOTIVO: versão da API exigida em header pela Anthropic. Fixa, não é a versão
        // do modelo — mudar isso sem ler o changelog deles quebra o contrato do corpo.
        private const string ANTHROPIC_VERSION = "2023-06-01";

        // ── Endpoints ───────────────────────────────────────────────────────

        public static string ChatEndpoint(AiProviderKind p, string model)
        {
            switch (p)
            {
                case AiProviderKind.Gemini:
                    return "https://generativelanguage.googleapis.com/v1beta/models/"
                           + model + ":generateContent?key=" + AiSettings.GetApiKey(p);
                case AiProviderKind.OpenAi:
                    return "https://api.openai.com/v1/chat/completions";
                case AiProviderKind.Anthropic:
                    return "https://api.anthropic.com/v1/messages";
                case AiProviderKind.Ollama:
                    return AiSettings.OllamaUrl + "/v1/chat/completions";
                default:
                    return "";
            }
        }

        public static string ModelsEndpoint(AiProviderKind p)
        {
            switch (p)
            {
                case AiProviderKind.Gemini:
                    return "https://generativelanguage.googleapis.com/v1beta/models?key="
                           + AiSettings.GetApiKey(p);
                case AiProviderKind.OpenAi:
                    return "https://api.openai.com/v1/models";
                case AiProviderKind.Anthropic:
                    // MOTIVO: a listagem da Anthropic é paginada e devolve 20 por padrão.
                    // Sem o limite, modelos além da 1ª página sumiam da lista sem aviso.
                    // 1000 é o máximo que a API aceita.
                    return "https://api.anthropic.com/v1/models?limit=1000";
                case AiProviderKind.Ollama:
                    return AiSettings.OllamaUrl + "/api/tags";
                default:
                    return "";
            }
        }

        /// <summary>Applies auth and content headers for this provider.</summary>
        public static void ApplyHeaders(AiProviderKind p, UnityWebRequest req)
        {
            req.SetRequestHeader("Content-Type", "application/json");

            switch (p)
            {
                case AiProviderKind.OpenAi:
                    req.SetRequestHeader("Authorization", "Bearer " + AiSettings.GetApiKey(p));
                    break;
                case AiProviderKind.Anthropic:
                    req.SetRequestHeader("x-api-key", AiSettings.GetApiKey(p));
                    req.SetRequestHeader("anthropic-version", ANTHROPIC_VERSION);
                    break;
                // Gemini leva a chave na URL; Ollama local não usa chave.
            }
        }

        // ── Corpo da requisição ─────────────────────────────────────────────

        /// <summary>Builds the request body a provider expects, from the neutral input.</summary>
        public static string BuildChatBody(AiProviderKind p, string systemPrompt,
                                           IReadOnlyList<AiTurn> turns, string model,
                                           string geminiGenerationConfig = null)
        {
            switch (p)
            {
                case AiProviderKind.Gemini:
                    return BuildGeminiBody(systemPrompt, turns, geminiGenerationConfig);
                case AiProviderKind.Anthropic:
                    return BuildAnthropicBody(systemPrompt, turns, model);
                default: // OpenAI e Ollama
                    return BuildOpenAiBody(systemPrompt, turns, model);
            }
        }

        /// <summary>Gemini body; with a generationConfig (response schema, temperature) when given.</summary>
        /// <remarks>
        /// MOTIVO: o generationConfig do artigo (Artigo/response_schema.json) tem esquema
        /// aninhado, enum e nullable — o JsonUtility não serializa isso. Ele entra como texto,
        /// verbatim, no fim do corpo: o que está no arquivo é exatamente o que vai à API.
        /// </remarks>
        public static string BuildGeminiBody(string systemPrompt, IReadOnlyList<AiTurn> turns,
                                             string generationConfigJson = null)
        {
            var contents = new GeminiContent[turns.Count];
            for (int i = 0; i < turns.Count; i++)
            {
                contents[i] = new GeminiContent
                {
                    role  = turns[i].Role,   // Gemini usa "user"/"model" — já é o nosso
                    parts = new[] { new GeminiPart { text = turns[i].Text } }
                };
            }

            var req = new GeminiRequest
            {
                system_instruction = new GeminiContent
                {
                    role  = AiTurn.ROLE_USER,
                    parts = new[] { new GeminiPart { text = systemPrompt } }
                },
                contents = contents
            };
            string json = JsonUtility.ToJson(req);
            if (string.IsNullOrEmpty(generationConfigJson)) return json;
            return json.Substring(0, json.LastIndexOf('}')) + ",\"generationConfig\":" + generationConfigJson.Trim() + "}";
        }

        private static string BuildOpenAiBody(string systemPrompt, IReadOnlyList<AiTurn> turns, string model)
        {
            // MOTIVO: na OpenAI o prompt de sistema é a PRIMEIRA MENSAGEM, com papel
            // "system" — não existe campo separado como nas outras duas.
            var msgs = new OpenAiMessage[turns.Count + 1];
            msgs[0] = new OpenAiMessage { role = "system", content = systemPrompt };
            for (int i = 0; i < turns.Count; i++)
                msgs[i + 1] = new OpenAiMessage { role = ToOpenAiRole(turns[i].Role), content = turns[i].Text };

            return JsonUtility.ToJson(new OpenAiRequest { model = model, messages = msgs });
        }

        private static string BuildAnthropicBody(string systemPrompt, IReadOnlyList<AiTurn> turns, string model)
        {
            var msgs = new OpenAiMessage[turns.Count];
            for (int i = 0; i < turns.Count; i++)
                msgs[i] = new OpenAiMessage { role = ToOpenAiRole(turns[i].Role), content = turns[i].Text };

            return JsonUtility.ToJson(new AnthropicRequest
            {
                model      = model,
                max_tokens = ANTHROPIC_MAX_TOKENS,
                system     = systemPrompt,
                messages   = msgs
            });
        }

        // MOTIVO: o Gemini chama a resposta de "model"; OpenAI e Anthropic, de
        // "assistant". Mandar "model" para elas faz a API rejeitar o papel.
        private static string ToOpenAiRole(string role)
            => role == AiTurn.ROLE_AI ? "assistant" : "user";

        // ── Leitura da resposta ─────────────────────────────────────────────

        /// <summary>Extracts the answer text, or null when the shape is unexpected.</summary>
        public static string ParseAnswer(AiProviderKind p, string rawJson)
        {
            try
            {
                switch (p)
                {
                    case AiProviderKind.Gemini:
                    {
                        var r = JsonUtility.FromJson<GeminiResponse>(rawJson);
                        if (r?.candidates == null || r.candidates.Length == 0) return null;
                        var parts = r.candidates[0].content?.parts;
                        return (parts != null && parts.Length > 0) ? parts[0].text : null;
                    }
                    case AiProviderKind.Anthropic:
                    {
                        var r = JsonUtility.FromJson<AnthropicResponse>(rawJson);
                        if (r?.content == null || r.content.Length == 0) return null;
                        // MOTIVO: a resposta vem como LISTA de blocos e nem todo bloco é
                        // texto. Pegar content[0] cegamente devolveria vazio quando o
                        // primeiro bloco for de outro tipo.
                        for (int i = 0; i < r.content.Length; i++)
                            if (r.content[i] != null && !string.IsNullOrEmpty(r.content[i].text))
                                return r.content[i].text;
                        return null;
                    }
                    default: // OpenAI e Ollama
                    {
                        var r = JsonUtility.FromJson<OpenAiResponse>(rawJson);
                        if (r?.choices == null || r.choices.Length == 0) return null;
                        return r.choices[0].message?.content;
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Extracts the list of model ids the key can actually use.</summary>
        /// <remarks>
        /// MOTIVO: o Gemini devolve TODOS os modelos, inclusive os de embedding, que não
        /// respondem a generateContent. Filtrar por supportedGenerationMethods evita
        /// oferecer ao dev uma opção que falha na primeira chamada.
        /// </remarks>
        public static string[] ParseModels(AiProviderKind p, string rawJson)
        {
            try
            {
                var ids = new List<string>();

                switch (p)
                {
                    case AiProviderKind.Gemini:
                    {
                        var r = JsonUtility.FromJson<GeminiModelList>(rawJson);
                        if (r?.models == null) break;
                        foreach (var m in r.models)
                        {
                            if (m == null || string.IsNullOrEmpty(m.name)) continue;
                            if (!SupportsGenerate(m)) continue;
                            // "models/gemini-2.5-flash" → "gemini-2.5-flash"
                            int slash = m.name.LastIndexOf('/');
                            ids.Add(slash >= 0 ? m.name.Substring(slash + 1) : m.name);
                        }
                        break;
                    }
                    case AiProviderKind.Anthropic:
                    {
                        var r = JsonUtility.FromJson<AnthropicModelList>(rawJson);
                        if (r?.data == null) break;
                        foreach (var m in r.data)
                            if (m != null && !string.IsNullOrEmpty(m.id)) ids.Add(m.id);
                        break;
                    }
                    case AiProviderKind.Ollama:
                    {
                        var r = JsonUtility.FromJson<OllamaModelList>(rawJson);
                        if (r?.models == null) break;
                        foreach (var m in r.models)
                            if (m != null && !string.IsNullOrEmpty(m.name)) ids.Add(m.name);
                        break;
                    }
                    default: // OpenAI
                    {
                        var r = JsonUtility.FromJson<OpenAiModelList>(rawJson);
                        if (r?.data == null) break;
                        foreach (var m in r.data)
                            if (m != null && !string.IsNullOrEmpty(m.id)) ids.Add(m.id);
                        break;
                    }
                }

                ids.RemoveAll(id => !IsTextChatModel(p, id));
                ids.Sort(StringComparer.OrdinalIgnoreCase);
                return ids.ToArray();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        // MOTIVO: as listagens devolvem o catálogo inteiro do provedor, não só o que serve
        // ao laudo. A OpenAI devolve embedding, Whisper, TTS, DALL-E e moderação; o Gemini,
        // geração de imagem, voz e áudio ao vivo. Escolher um desses faz o laudo falhar na
        // primeira chamada — e o dev não tem como saber, pelo nome, que não era de texto.
        // Os experimentais (-exp) saem porque somem sem aviso e costumam não ter cota.
        //
        // Isto filtra pelo TIPO do modelo. Não diz se a conta do dev tem cota para ele:
        // nenhum dos provedores informa isso na listagem. Quem confirma é ProbeModel.
        private static readonly string[] NON_TEXT_MARKERS =
        {
            "embed", "aqa", "tts", "transcribe", "whisper", "audio", "realtime", "live",
            "image", "imagen", "dall-e", "veo", "moderation", "search",
            "robotics", "computer-use", "-exp",
        };

        // MOTIVO: a OpenAI lista também modelos legados de completion (davinci, babbage e
        // os "-instruct"), que não respondem em /chat/completions. Só os prefixos de chat
        // entram. "instruct" NÃO vai para a lista geral: no Ollama, "llama3.1:8b-instruct"
        // é justamente o modelo de chat.
        private static readonly string[] OPENAI_CHAT_PREFIXES = { "gpt-", "chatgpt-", "o1", "o3", "o4" };

        private static bool IsTextChatModel(AiProviderKind p, string id)
        {
            string lower = id.ToLowerInvariant();

            if (p == AiProviderKind.OpenAi)
            {
                bool chatFamily = false;
                foreach (string prefix in OPENAI_CHAT_PREFIXES)
                    if (lower.StartsWith(prefix, StringComparison.Ordinal)) { chatFamily = true; break; }
                if (!chatFamily || lower.Contains("instruct")) return false;
            }

            foreach (string marker in NON_TEXT_MARKERS)
                if (lower.Contains(marker)) return false;
            return true;
        }

        private static bool SupportsGenerate(GeminiModelEntry m)
        {
            if (m.supportedGenerationMethods == null) return true; // sem info: não descarta
            foreach (string s in m.supportedGenerationMethods)
                if (s == "generateContent") return true;
            return false;
        }

        /// <summary>Bytes of a UTF-8 body, ready for an upload handler.</summary>
        public static byte[] Utf8(string body) => Encoding.UTF8.GetBytes(body);

        // MOTIVO: os quatro provedores põem o motivo do erro em "message" (Gemini, OpenAI,
        // Anthropic: {"error":{"message":…}}) ou direto em "error" (Ollama: {"error":"…"}).
        // Regex e não JsonUtility porque o formato muda entre eles e o JsonUtility não lê
        // um campo que às vezes é objeto e às vezes é string.
        private static readonly System.Text.RegularExpressions.Regex ERROR_MESSAGE =
            new System.Text.RegularExpressions.Regex("\"message\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
        private static readonly System.Text.RegularExpressions.Regex ERROR_STRING =
            new System.Text.RegularExpressions.Regex("\"error\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");

        /// <summary>The provider's own explanation inside an error body, or null.</summary>
        /// <remarks>
        /// MOTIVO: no teste com a Anthropic a tela dizia só "HTTP 400 Bad Request" — a
        /// linha de status, que vale para dezenas de causas. O motivo real (sem crédito na
        /// conta, parâmetro recusado, modelo aposentado) vem escrito no corpo da resposta,
        /// e sem ele o dev não tem o que corrigir.
        /// </remarks>
        public static string ProviderErrorMessage(string rawBody)
        {
            if (string.IsNullOrEmpty(rawBody)) return null;
            var m = ERROR_MESSAGE.Match(rawBody);
            if (!m.Success) m = ERROR_STRING.Match(rawBody);
            if (!m.Success) return null;
            try { return System.Text.RegularExpressions.Regex.Unescape(m.Groups[1].Value).Trim(); }
            catch { return m.Groups[1].Value.Trim(); }
        }
    }
}
