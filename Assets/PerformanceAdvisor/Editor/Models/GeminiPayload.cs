using System;

namespace PerformanceAdvisor
{
    // MOTIVO: snake_case nos campos é intencional — JsonUtility serializa pelo nome
    // exato do campo, e a API do Gemini exige "system_instruction", "contents", etc.
    // Mantemos `#pragma warning disable` para silenciar avisos de convenção C#.
#pragma warning disable IDE1006 // Naming Styles

    /// <summary>
    /// Top-level request body sent to <c>generateContent</c>.
    /// Mirrors the Gemini REST schema 1:1 so that <see cref="UnityEngine.JsonUtility"/>
    /// produces JSON acceptable by the endpoint without any post-processing.
    /// </summary>
    [Serializable]
    public class GeminiRequest
    {
        public GeminiContent system_instruction;
        public GeminiContent[] contents;
    }

    /// <summary>
    /// Container of <see cref="GeminiPart"/>s. Used for both the system instruction
    /// (single content) and each turn inside <c>contents</c>.
    /// </summary>
    [Serializable]
    public class GeminiContent
    {
        // MOTIVO: multi-turno (chat) exige "user"/"model" por turno. JsonUtility
        // serializa string null como "" — por isso o role é SEMPRE preenchido
        // explicitamente por quem monta o request (laudo e chat).
        public string role;
        public GeminiPart[] parts;
    }

    /// <summary>
    /// Smallest unit of content. The plugin only sends/receives plain text, so
    /// <c>text</c> is the single field exposed.
    /// </summary>
    [Serializable]
    public class GeminiPart
    {
        public string text;
    }

    /// <summary>
    /// Top-level response body. The textual answer lives at
    /// <c>response.candidates[0].content.parts[0].text</c>.
    /// </summary>
    [Serializable]
    public class GeminiResponse
    {
        public GeminiCandidate[] candidates;
    }

    /// <summary>
    /// Single answer candidate. The plugin always reads <c>candidates[0]</c>.
    /// </summary>
    [Serializable]
    public class GeminiCandidate
    {
        public GeminiContent content;
    }

    /// <summary>
    /// Single diagnosed problem inside <see cref="GeminiAdvisoryReport.findings"/>.
    /// One per distinct root cause (CPU, GC, DrawCalls, GPU, Memoria, Geral).
    /// </summary>
    [Serializable]
    public class GeminiFinding
    {
        public string tipo;                  // "CPU" | "GC" | "DrawCalls" | "GPU" | "Memoria" | "Geral"
        public string causa_raiz;
        public string codigo_refatorado;
        public string explicacao_didatica;
    }

    /// <summary>
    /// Strongly-typed projection of the JSON the model is instructed to emit
    /// (see system prompt in Module 3). Kept here next to the wire types so the
    /// REST bridge can deserialize the inner payload right after parsing
    /// <see cref="GeminiResponse"/>.
    /// </summary>
    [Serializable]
    public class GeminiAdvisoryReport
    {
        public GeminiFinding[] findings;
        public string dicas_level_design;
    }

#pragma warning restore IDE1006
}
