using System;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Um turno da conversa da aba Chat. Serializável para o histórico sobreviver
    /// a Play Mode e recompilações dentro da mesma sessão do Editor.
    /// </summary>
    [Serializable]
    public class ChatMessage
    {
        public const string ROLE_USER  = "user";
        public const string ROLE_MODEL = "model";

        public string role;     // "user" | "model" — mesmo formato da API do Gemini
        public string text;
        // MOTIVO: balões de erro ficam no histórico visual mas NUNCA entram no
        // payload — a API só aceita turnos user/model.
        public bool   isError;
    }
}
