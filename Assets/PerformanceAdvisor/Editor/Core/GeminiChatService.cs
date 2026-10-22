using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Domínio "chat" — conversa multi-turno com o Gemini sobre o laudo.
    /// Stateless: o histórico pertence à UI (ChatTab); aqui só se monta o payload
    /// e se interpreta a resposta como texto livre (sem parse de findings).
    /// </summary>
    public static class GeminiChatService
    {
        // MOTIVO: persona didática porque o público-alvo são devs iniciantes — a
        // motivação da feature é o codigo_refatorado do laudo ser genérico demais.
        private const string CHAT_SYSTEM_PROMPT =
@"Assuma o papel de um Engenheiro de Software Unity Sênior, especializado em
otimização de desempenho para o hardware alvo informado, atuando como tutor.

Você está dentro do plugin Performance Advisor, conversando com um desenvolvedor
que recebeu um laudo de performance gerado por você. Seu objetivo é ajudá-lo a
ENTENDER e APLICAR as correções no projeto real dele.

Regras:
- Responda em tom didático e encorajador, no idioma indicado ao final.
- Quando o desenvolvedor anexar um script (bloco '--- script anexado: ... ---'),
  baseie a resposta no código REAL dele: aponte métodos e trechos específicos e
  mostre o código reescrito.
- Respostas curtas e práticas (até ~300 palavras), com código C# quando ajudar.
- Se a pergunta fugir de Unity/performance, redirecione gentilmente ao tema.";

        /// <summary>
        /// Monta o bloco de contexto da sessão: resumo de violações + último laudo
        /// (quando existir). Vai dentro do system_instruction de toda conversa.
        /// </summary>
        public static string BuildSessionContext(GeminiAdvisoryReport lastReport)
        {
            var sb = new StringBuilder(2048);
            var v = SnapshotHeuristicsController.GetViolationSummary();
            sb.Append("CONTEXTO DA SESSÃO DE PROFILING:\n");

            // MOTIVO: o chat recebia um resumo mais pobre que o laudo — sem a GPU e sem o
            // aparelho. A GPU já tinha entrado no payload do laudo, e o chat SOBRE esse
            // laudo continuava cego para ela: o dev perguntava de um finding de GPU e o
            // modelo não tinha o número. Os dois contextos precisam descrever a mesma
            // sessão.
            sb.Append("hardware_budget: ").Append(HardwareBuildSettings.Description).Append('\n');
            if (TelemetryMonitorCore.IsRemote && !string.IsNullOrEmpty(TelemetryMonitorCore.SessionTargetName))
                sb.Append("medido_em: ").Append(TelemetryMonitorCore.SessionTargetName).Append('\n');

            sb.Append("violations_summary: {cpu_violations:").Append(v.cpu)
              .Append(", cpu_max_ms:").Append(v.cpuMaxMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
              .Append(", gpu_violations:").Append(v.gpu)
              .Append(", gpu_max_ms:").Append(v.gpuMaxMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
              .Append(", gc_violations:").Append(v.gc)
              .Append(", gc_max_bytes:").Append(v.gcMaxBytes)
              .Append(", drawcalls_violations:").Append(v.draw)
              .Append(", drawcalls_max:").Append(v.drawMax)
              .Append(", memory_violations:").Append(v.mem)
              .Append(", memory_max_bytes:").Append(v.memMaxBytes)
              .Append("}\n");

            if (lastReport != null)
            {
                sb.Append("ÚLTIMO LAUDO GERADO (JSON):\n").Append(JsonUtility.ToJson(lastReport));
            }
            else
            {
                // MOTIVO: a instrução antiga dizia "Play → Monitorar → Stop", que está errada
                // no modo build (o Editor nunca entra em Play) e em Android, que só mede na
                // build. O modelo repetiria o passo errado para o dev.
                sb.Append("(Ainda não há laudo nesta sessão — se fizer sentido, oriente o dev a medir ")
                  .Append("pela aba Build (\"Compilar e executar\") ou em Play Mode, parar o monitoramento ")
                  .Append("e clicar em Analisar na aba Snapshots.)");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Envia o histórico (último item = pergunta atual) e observa os callbacks.
        /// Mensagens com isError são puladas no payload.
        /// </summary>
        public static void Ask(string sessionContext, IReadOnlyList<ChatMessage> history,
            Action<string> onAnswer, Action<string> onError, Action<string> onRetryNotice)
        {
            if (!AiSettings.IsReady)
            {
                onError?.Invoke(NotReadyMessage());
                return;
            }
            if (history == null || history.Count == 0)
            {
                onError?.Invoke(Loc.T("Nada para enviar.", "Nothing to send.", "Nada que enviar."));
                return;
            }

            // MOTIVO: turnos neutros. O papel da resposta se chama "model" no Gemini e
            // "assistant" nas outras duas APIs — a tradução vive no adaptador, e o chat
            // não precisa saber com quem está falando.
            var turns = new List<AiTurn>(history.Count);
            for (int i = 0; i < history.Count; i++)
            {
                ChatMessage m = history[i];
                if (m.isError) continue;
                turns.Add(new AiTurn(m.role, m.text));
            }

            AiHttpClient.Chat(CHAT_SYSTEM_PROMPT + "\n\n" + Loc.AiLanguageInstruction() + "\n\n" + sessionContext, turns,
                onAnswer:      onAnswer,
                onError:       onError,
                onRetryNotice: onRetryNotice);
        }

        /// <summary>Message shown when the provider or model is not configured.</summary>
        public static string NotReadyMessage()
        {
            return AiSettings.IsLocal(AiSettings.Provider)
                ? Loc.T("Escolha um modelo do Ollama em 'Configuração'.",
                        "Pick an Ollama model in 'Settings'.",
                        "Elige un modelo de Ollama en 'Configuración'.")
                : Loc.T("Chave de API ou modelo não configurados. Vá em 'Configuração'.",
                        "API key or model not set. Go to 'Settings'.",
                        "Clave de API o modelo no configurados. Ve a 'Configuración'.");
        }
        // MOTIVO: o ParseAnswer local foi removido. Ele desembrulhava o envelope do
        // Gemini (candidates → content → parts → text), que é específico de UM provedor.
        // Desembrulhar virou trabalho do AiProviderAdapter, que sabe se a resposta vem
        // em `candidates`, `choices` ou `content` — o chat agora já recebe texto.
    }
}
