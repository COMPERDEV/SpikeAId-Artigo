using System;

namespace PerformanceAdvisor
{
    // MOTIVO: snake_case e nomes "errados" para C# são intencionais — JsonUtility
    // serializa pelo nome EXATO do campo, e cada API exige o seu. Mesmo motivo do
    // GeminiPayload.cs, que continua servindo ao formato do Gemini.
#pragma warning disable IDE1006 // Naming Styles

    /// <summary>One turn of conversation, provider-independent.</summary>
    /// <remarks>
    /// MOTIVO: cada API nomeia os papéis de um jeito — Gemini usa "model" para a
    /// resposta, OpenAI e Anthropic usam "assistant". Traduzir na borda, e não espalhar
    /// condicional por todo o código, mantém o resto do plugin ignorante de provedor.
    /// </remarks>
    public sealed class AiTurn
    {
        public const string ROLE_USER = "user";
        public const string ROLE_AI   = "model";

        public string Role;
        public string Text;

        public AiTurn(string role, string text) { Role = role; Text = text; }
    }

    // ── Formato OpenAI (usado também pelo Ollama, que expõe API compatível) ──

    [Serializable]
    public class OpenAiMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    public class OpenAiRequest
    {
        public string model;
        public OpenAiMessage[] messages;
    }

    [Serializable]
    public class OpenAiChoice
    {
        public OpenAiMessage message;
    }

    [Serializable]
    public class OpenAiResponse
    {
        public OpenAiChoice[] choices;
    }

    [Serializable]
    public class OpenAiModel
    {
        public string id;
    }

    [Serializable]
    public class OpenAiModelList
    {
        public OpenAiModel[] data;
    }

    // ── Formato Anthropic ───────────────────────────────────────────────────

    [Serializable]
    public class AnthropicRequest
    {
        public string model;
        // MOTIVO: a Anthropic exige max_tokens no corpo, diferente das outras duas.
        // Sem ele a requisição é rejeitada com 400.
        public int max_tokens;
        public string system;
        public OpenAiMessage[] messages; // mesmo formato {role, content}
    }

    [Serializable]
    public class AnthropicContentBlock
    {
        public string type;
        public string text;
    }

    [Serializable]
    public class AnthropicResponse
    {
        public AnthropicContentBlock[] content;
    }

    [Serializable]
    public class AnthropicModel
    {
        public string id;
    }

    [Serializable]
    public class AnthropicModelList
    {
        public AnthropicModel[] data;
    }

    // ── Listagem de modelos do Gemini ───────────────────────────────────────

    [Serializable]
    public class GeminiModelEntry
    {
        // Vem como "models/gemini-2.5-flash" — o prefixo é removido na leitura.
        public string name;
        public string[] supportedGenerationMethods;
    }

    [Serializable]
    public class GeminiModelList
    {
        public GeminiModelEntry[] models;
    }

    // ── Listagem de modelos do Ollama ───────────────────────────────────────

    [Serializable]
    public class OllamaModelEntry
    {
        public string name;
    }

    [Serializable]
    public class OllamaModelList
    {
        public OllamaModelEntry[] models;
    }

#pragma warning restore IDE1006
}
