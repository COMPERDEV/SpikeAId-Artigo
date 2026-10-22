using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Networking;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Sends chat requests and fetches model lists, for whichever provider is active.
    /// </summary>
    /// <remarks>
    /// MOTIVO: generaliza o antigo <c>GeminiHttpClient</c>. A máquina de timeout e
    /// re-tentativa dele não foi reescrita porque não estava errada — ela veio de medição
    /// (em dias de sobrecarga o 5xx do Gemini era intermitente, ~1 em 3 requisições) e
    /// vale igual para qualquer provedor: 5xx é problema do outro lado, e esperar e
    /// repetir é a resposta certa. O que mudou é DE QUEM se tenta de novo.
    ///
    /// O fallback automático de modelo saiu. Ele trocava o modelo preview pelo estável
    /// sozinho, o que fazia sentido com dois modelos fixos no código — mas agora o dev
    /// escolhe o modelo de uma lista que veio da API dele, e trocar por baixo seria
    /// devolver um laudo de um modelo que ele não pediu, sem ele saber. O aviso de
    /// re-tentativa continua; a substituição silenciosa, não.
    /// </remarks>
    public static class AiHttpClient
    {
        // MOTIVO: 120s porque respostas longas (laudo multi-finding, chat com código)
        // exigem muitos tokens de saída; 30s estourava com frequência.
        // MOTIVO do valor: com o código dos scripts anexado, o modelo raciocina mais; na coleta do
        // artigo, 3 de 150 respostas passaram de 120 s e se perderam por tempo, não por erro.
        private const int TIMEOUT_SECONDS = 300;

        // MOTIVO: modelo local não sofre sobrecarga de servidor de terceiro, mas PODE
        // demorar bem mais — CPU do dev, sem GPU dedicada, modelo grande. O tempo maior
        // evita cancelar uma resposta que estava vindo.
        private const int TIMEOUT_SECONDS_LOCAL = 600;

        private const int    RETRIES             = 3;
        private const double RETRY_DELAY_SECONDS = 5.0;

        /// <summary>Sends a chat request to the active provider and returns the answer text.</summary>
        public static void Chat(string systemPrompt, IReadOnlyList<AiTurn> turns,
            Action<string> onAnswer, Action<string> onError, Action<string> onRetryNotice,
            string geminiGenerationConfig = null)
        {
            AiProviderKind p = AiSettings.Provider;
            string model = AiSettings.SelectedModel;

            if (!AiSettings.IsReady)
            {
                onError?.Invoke(AiSettings.IsLocal(p)
                    ? Loc.T("Escolha um modelo do Ollama na aba Configuração.",
                            "Pick an Ollama model in the Settings tab.",
                            "Elige un modelo de Ollama en la pestaña Configuración.")
                    : Loc.T("Configure a chave de API e escolha um modelo na aba Configuração.",
                            "Set the API key and pick a model in the Settings tab.",
                            "Configura la clave de API y elige un modelo en la pestaña Configuración."));
                return;
            }

            string body = AiProviderAdapter.BuildChatBody(p, systemPrompt, turns, model, geminiGenerationConfig);

            Dispatch(p, AiProviderAdapter.ChatEndpoint(p, model), "POST", body, RETRIES,
                raw =>
                {
                    string text = AiProviderAdapter.ParseAnswer(p, raw);
                    if (string.IsNullOrEmpty(text))
                    {
                        // MOTIVO: resposta vazia quase nunca é "a IA não tinha o que
                        // dizer" — é formato inesperado, filtro de conteúdo ou modelo
                        // que não aceita esse tipo de chamada. Dizer "vazia" e parar
                        // deixaria o dev sem o que investigar.
                        onError?.Invoke(
                            Loc.T($"Resposta vazia ou em formato inesperado de {AiSettings.DisplayName(p)}. " +
                                  "Se o modelo foi trocado agora, confirme que ele aceita chamadas de chat.",
                                  $"Empty or unexpectedly formatted answer from {AiSettings.DisplayName(p)}. " +
                                  "If the model was just changed, make sure it accepts chat calls.",
                                  $"Respuesta vacía o con formato inesperado de {AiSettings.DisplayName(p)}. " +
                                  "Si acabas de cambiar el modelo, confirma que acepta llamadas de chat."));
                        return;
                    }
                    onAnswer?.Invoke(text.Trim());
                },
                onError, onRetryNotice);
        }

        /// <summary>Asks the provider which text models it offers to this key.</summary>
        /// <remarks>
        /// MOTIVO: é o conserto do apontamento de que a lista de modelos estava obsoleta.
        /// Em vez de um enum no código que envelhece sozinho, pergunta-se ao provedor com
        /// a chave DO DEV. Cuidado com o que isso prova: a listagem diz o que o provedor
        /// OFERECE, não a cota da conta — um modelo listado ainda pode responder 429.
        /// Quem confirma o uso é <see cref="ProbeModel"/>.
        /// </remarks>
        public static void ListModels(Action<string[]> onModels, Action<string> onError)
        {
            AiProviderKind p = AiSettings.Provider;

            if (!AiSettings.IsLocal(p) && string.IsNullOrEmpty(AiSettings.GetApiKey(p)))
            {
                onError?.Invoke(Loc.T("Informe a chave de API antes de buscar os modelos.",
                                      "Enter the API key before fetching the models.",
                                      "Indica la clave de API antes de buscar los modelos."));
                return;
            }

            Dispatch(p, AiProviderAdapter.ModelsEndpoint(p), "GET", null, 0,
                raw =>
                {
                    string[] ids = AiProviderAdapter.ParseModels(p, raw);
                    if (ids.Length == 0)
                    {
                        onError?.Invoke(AiSettings.IsLocal(p)
                            ? Loc.T("O Ollama respondeu, mas sem nenhum modelo instalado. " +
                                    "Baixe um com 'ollama pull <modelo>' no terminal.",
                                    "Ollama answered, but has no model installed. " +
                                    "Download one with 'ollama pull <model>' in a terminal.",
                                    "Ollama respondió, pero sin ningún modelo instalado. " +
                                    "Descarga uno con 'ollama pull <modelo>' en la terminal.")
                            : Loc.T("Nenhum modelo de chat disponível para esta chave.",
                                    "No chat model available for this key.",
                                    "Ningún modelo de chat disponible para esta clave."));
                        return;
                    }
                    onModels?.Invoke(ids);
                },
                onError, null);
        }

        /// <summary>
        /// Calls one model with a minimal prompt to find out whether this key can use it.
        /// </summary>
        /// <remarks>
        /// MOTIVO: nenhum provedor informa, na listagem, quais modelos a conta do dev tem
        /// cota para usar — o Gemini devolve o mesmo catálogo para qualquer chave válida,
        /// e um modelo Pro pode aparecer e responder 429 no plano gratuito. A única forma
        /// de saber é chamar. Testar a lista inteira gastaria a cota do próprio dev (no
        /// plano gratuito, poucas dezenas de requisições por dia em alguns modelos) e
        /// daria falso negativo assim que o limite por minuto estourasse. Por isso testa
        /// só o modelo ESCOLHIDO, uma vez, sem re-tentativa.
        /// </remarks>
        public static void ProbeModel(string model, Action onOk, Action<string> onFail)
        {
            AiProviderKind p = AiSettings.Provider;

            if (string.IsNullOrEmpty(model))
            {
                onFail?.Invoke(Loc.T("Nenhum modelo escolhido.", "No model chosen.", "Ningún modelo elegido."));
                return;
            }
            if (!AiSettings.IsLocal(p) && string.IsNullOrEmpty(AiSettings.GetApiKey(p)))
            {
                onFail?.Invoke(Loc.T("Informe a chave de API antes de testar o modelo.",
                                     "Enter the API key before testing the model.",
                                     "Indica la clave de API antes de probar el modelo."));
                return;
            }

            var turns = new[] { new AiTurn(AiTurn.ROLE_USER, "ok") };
            string body = AiProviderAdapter.BuildChatBody(p, PROBE_PROMPT, turns, model);

            Dispatch(p, AiProviderAdapter.ChatEndpoint(p, model), "POST", body, 0,
                raw =>
                {
                    if (string.IsNullOrEmpty(AiProviderAdapter.ParseAnswer(p, raw)))
                        onFail?.Invoke(Loc.T("O modelo respondeu, mas não em formato de chat. Escolha outro.",
                                             "The model answered, but not in chat format. Pick another one.",
                                             "El modelo respondió, pero no en formato de chat. Elige otro."));
                    else
                        onOk?.Invoke();
                },
                msg => onFail?.Invoke(DescribeProbeFailure(LastErrorStatus, msg)),
                null);
        }

        // MOTIVO: o menor pedido que ainda exige uma resposta de chat. Prompt curto =
        // custo mínimo na cota do dev; exigir texto de volta é o que prova que o modelo
        // atende o laudo, e não só que existe.
        private const string PROBE_PROMPT = "Responda apenas: ok";

        // MOTIVO: no teste, os códigos significam outra coisa do que numa chamada de laudo.
        // Lá, 403 é "chave recusada"; aqui a chave já foi aceita na listagem, então 403
        // e 404 dizem que ESTE modelo não está liberado para ela. E 429 no teste é quase
        // sempre cota zero no plano — o caso exato que o teste existe para pegar.
        private static string DescribeProbeFailure(long status, string fallback)
        {
            switch (status)
            {
                case 401: return Loc.T("Chave recusada pelo provedor. Confira a chave salva.",
                                       "Key rejected by the provider. Check the saved key.",
                                       "Clave rechazada por el proveedor. Revisa la clave guardada.");
                case 403:
                case 404: return Loc.T("Esta chave não tem acesso a este modelo.",
                                       "This key has no access to this model.",
                                       "Esta clave no tiene acceso a este modelo.");
                case 429: return Loc.T("Sem cota para este modelo na sua conta, ou limite por minuto atingido. " +
                                       "Escolha outro, ou teste de novo mais tarde.",
                                       "No quota for this model on your account, or the per-minute limit was reached. " +
                                       "Pick another one, or test again later.",
                                       "Sin cuota para este modelo en tu cuenta, o se alcanzó el límite por minuto. " +
                                       "Elige otro, o prueba de nuevo más tarde.");
                default:  return fallback;
            }
        }

        /// <summary>HTTP status of the last failed request; 0 when it never reached the server.</summary>
        public static long LastErrorStatus { get; private set; }

        /// <summary>Response body of the last failed request (the provider's own error), or null.</summary>
        public static string LastErrorBody { get; private set; }

        /// <summary>
        /// Sends an already-built body to a model of the given provider and hands back the
        /// raw HTTP response, untouched — for callers that must keep the whole answer.
        /// </summary>
        /// <remarks>
        /// MOTIVO: o experimento do artigo publica as respostas brutas (texto, motivo de
        /// término, versão do modelo, tokens). O caminho normal só devolve o texto extraído.
        /// O transporte é o mesmo — cabeçalhos, timeout, re-tentativa em 5xx.
        /// </remarks>
        public static void SendRaw(AiProviderKind p, string model, string body,
                                   Action<string> onRaw, Action<string> onError)
        {
            Dispatch(p, AiProviderAdapter.ChatEndpoint(p, model), "POST", body, RETRIES, onRaw, onError, null);
        }

        // ── Transporte ──────────────────────────────────────────────────────

        private static void Dispatch(AiProviderKind p, string url, string verb, string body,
            int retriesLeft, Action<string> onRaw, Action<string> onError, Action<string> onRetryNotice)
        {
            UnityWebRequest req;
            try
            {
                // MOTIVO: UnityWebRequest segura um handle nativo — somos donos do
                // lifetime e o Dispose acontece no finally de HandleResponse.
                req = new UnityWebRequest(url, verb)
                {
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout         = AiSettings.IsLocal(p) ? TIMEOUT_SECONDS_LOCAL : TIMEOUT_SECONDS
                };
                if (body != null)
                    req.uploadHandler = new UploadHandlerRaw(AiProviderAdapter.Utf8(body));

                AiProviderAdapter.ApplyHeaders(p, req);
            }
            catch (Exception ex)
            {
                onError?.Invoke(Loc.T("Falha ao preparar a requisição: ", "Failed to prepare the request: ",
                                      "Error al preparar la solicitud: ") + ex.Message);
                return;
            }

            var op = req.SendWebRequest();
            op.completed += _ => HandleResponse(req, p, url, verb, body, retriesLeft,
                                                onRaw, onError, onRetryNotice);
        }

        /// <summary>HTTP error with the provider's own reason, when the body carries one.</summary>
        private static string DescribeHttpError(long status, string statusLine, string body)
        {
            string detail = AiProviderAdapter.ProviderErrorMessage(body);
            string head = Loc.T("Erro HTTP", "HTTP error", "Error HTTP") + $" {status}";
            if (string.IsNullOrEmpty(detail)) return $"{head}: {statusLine}";

            // MOTIVO: a Anthropic responde 400 (e não 402 ou 403) quando a conta não tem
            // crédito — a chave é válida e lista os modelos, mas nenhuma chamada passa.
            // É o caso mais provável de 400 com um corpo correto, e a mensagem original,
            // em inglês, não diz onde resolver.
            if (detail.IndexOf("credit balance", StringComparison.OrdinalIgnoreCase) >= 0)
                return Loc.T("Sem créditos na conta do provedor. A chave é válida, mas a conta precisa de saldo " +
                             "(na Anthropic: console.anthropic.com → Plans & Billing).",
                             "No credits on the provider account. The key is valid, but the account needs a balance " +
                             "(on Anthropic: console.anthropic.com → Plans & Billing).",
                             "Sin créditos en la cuenta del proveedor. La clave es válida, pero la cuenta necesita saldo " +
                             "(en Anthropic: console.anthropic.com → Plans & Billing).") +
                       $"\n{head}: {detail}";

            return $"{head}: {detail}";
        }

        // MOTIVO: EditorApplication.delayCall roda no próximo tick (sem espera);
        // pra esperar segundos de verdade, poll via EditorApplication.update.
        private static void ScheduleRetry(AiProviderKind p, string url, string verb, string body,
            int retriesLeft, Action<string> onRaw, Action<string> onError, Action<string> onRetryNotice)
        {
            double due = EditorApplication.timeSinceStartup + RETRY_DELAY_SECONDS;
            EditorApplication.CallbackFunction cb = null;
            cb = () =>
            {
                if (EditorApplication.timeSinceStartup < due) return;
                EditorApplication.update -= cb;
                Dispatch(p, url, verb, body, retriesLeft, onRaw, onError, onRetryNotice);
            };
            EditorApplication.update += cb;
        }

        private static void HandleResponse(UnityWebRequest req, AiProviderKind p,
            string url, string verb, string body, int retriesLeft,
            Action<string> onRaw, Action<string> onError, Action<string> onRetryNotice)
        {
            try
            {
                // MOTIVO: os callbacks de erro recebem só a mensagem. Quem precisa do
                // código para interpretar de outro jeito (o teste de modelo) lê daqui —
                // é gravado antes de qualquer onError, e tudo roda no mesmo thread.
                LastErrorStatus = req.result == UnityWebRequest.Result.ConnectionError ? 0 : req.responseCode;
                LastErrorBody = req.result == UnityWebRequest.Result.Success ? null : req.downloadHandler?.text;

                if (req.result == UnityWebRequest.Result.ConnectionError)
                {
                    // MOTIVO: com Ollama, erro de conexão quase sempre significa que o
                    // servidor local não está rodando — causa diferente e conserto
                    // diferente do "sua internet caiu" que serve para os outros três.
                    if (AiSettings.IsLocal(p))
                    {
                        onError?.Invoke(
                            Loc.T($"Não foi possível falar com o Ollama em {AiSettings.OllamaUrl}. " +
                                  "Confirme que ele está rodando (comando 'ollama serve').",
                                  $"Could not reach Ollama at {AiSettings.OllamaUrl}. " +
                                  "Make sure it is running (command 'ollama serve').",
                                  $"No se pudo conectar con Ollama en {AiSettings.OllamaUrl}. " +
                                  "Confirma que se está ejecutando (comando 'ollama serve')."));
                        return;
                    }
                    if (req.error != null && req.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                        onError?.Invoke(Loc.T($"Tempo limite excedido (>{TIMEOUT_SECONDS}s). Tente novamente.",
                                              $"Timed out (>{TIMEOUT_SECONDS}s). Try again.",
                                              $"Tiempo de espera agotado (>{TIMEOUT_SECONDS}s). Inténtalo de nuevo."));
                    else
                        onError?.Invoke(Loc.T("Erro de conexão: ", "Connection error: ", "Error de conexión: ") + req.error);
                    return;
                }

                switch (req.responseCode)
                {
                    case 401:
                    case 403:
                        onError?.Invoke(Loc.T($"Chave de API recusada por {AiSettings.DisplayName(p)}. " +
                                              "Confira na aba Configuração.",
                                              $"API key rejected by {AiSettings.DisplayName(p)}. " +
                                              "Check it in the Settings tab.",
                                              $"Clave de API rechazada por {AiSettings.DisplayName(p)}. " +
                                              "Revísala en la pestaña Configuración."));
                        return;
                    case 404:
                        // MOTIVO: 404 aqui costuma ser modelo que não existe (ou que a
                        // conta não alcança), não URL errada — e é o erro mais provável
                        // logo depois de trocar de modelo na lista.
                        onError?.Invoke(Loc.T($"O modelo \"{AiSettings.SelectedModel}\" não foi encontrado em " +
                                              $"{AiSettings.DisplayName(p)}. Busque a lista de modelos novamente.",
                                              $"The model \"{AiSettings.SelectedModel}\" was not found on " +
                                              $"{AiSettings.DisplayName(p)}. Fetch the model list again.",
                                              $"El modelo \"{AiSettings.SelectedModel}\" no se encontró en " +
                                              $"{AiSettings.DisplayName(p)}. Vuelve a buscar la lista de modelos."));
                        return;
                    case 429:
                        onError?.Invoke(Loc.T("Limite de requisições atingido. Aguarde e tente novamente.",
                                              "Request limit reached. Wait and try again.",
                                              "Límite de solicitudes alcanzado. Espera e inténtalo de nuevo."));
                        return;
                    case 500:
                    case 502:
                    case 503:
                    case 504:
                    case 529: // MOTIVO: "overloaded" da Anthropic — mesmo caso do 503 dos outros.
                        // MOTIVO: 5xx é problema do lado do provedor, não do código.
                        if (retriesLeft > 0)
                        {
                            onRetryNotice?.Invoke(
                                Loc.T($"{AiSettings.DisplayName(p)} sobrecarregado (HTTP {req.responseCode}) — " +
                                      $"nova tentativa em {RETRY_DELAY_SECONDS:0}s ({retriesLeft} restante(s))...",
                                      $"{AiSettings.DisplayName(p)} overloaded (HTTP {req.responseCode}) — " +
                                      $"retrying in {RETRY_DELAY_SECONDS:0}s ({retriesLeft} left)...",
                                      $"{AiSettings.DisplayName(p)} sobrecargado (HTTP {req.responseCode}): " +
                                      $"nuevo intento en {RETRY_DELAY_SECONDS:0}s ({retriesLeft} restante(s))..."));
                            ScheduleRetry(p, url, verb, body, retriesLeft - 1, onRaw, onError, onRetryNotice);
                            return;
                        }
                        onError?.Invoke(Loc.T($"{AiSettings.DisplayName(p)} segue sobrecarregado após várias " +
                                              "tentativas. Aguarde um minuto e tente de novo.",
                                              $"{AiSettings.DisplayName(p)} is still overloaded after several " +
                                              "attempts. Wait a minute and try again.",
                                              $"{AiSettings.DisplayName(p)} sigue sobrecargado tras varios " +
                                              "intentos. Espera un minuto y vuelve a intentarlo."));
                        return;
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(DescribeHttpError(req.responseCode, req.error, req.downloadHandler?.text));
                    return;
                }

                onRaw?.Invoke(req.downloadHandler.text);
            }
            finally
            {
                req.Dispose();
            }
        }
    }
}
