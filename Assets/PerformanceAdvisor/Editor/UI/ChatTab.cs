using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Estado + desenho da aba Chat. [Serializable] num campo [SerializeField] da
    /// janela: o histórico sobrevive a Play Mode e recompilações, e zera ao fechar
    /// o Editor (decisão de design). Não fala com HTTP — só com GeminiChatService.
    /// </summary>
    [Serializable]
    public class ChatTab
    {
        // MOTIVO: acima disso o anexo estoura o budget de tokens da requisição.
        private const int MAX_ATTACH_BYTES = 100 * 1024;

        [SerializeField] private List<ChatMessage> _messages = new List<ChatMessage>();
        [SerializeField] private string  _input = "";
        [SerializeField] private string  _attachedName = "";
        [SerializeField] private string  _attachedContent = "";
        [SerializeField] private Vector2 _scroll;

        // MOTIVO: NonSerialized de propósito — se um domain reload acontecer com
        // requisição em voo, o callback se perde; resetar o busy destrava a UI.
        [NonSerialized] private bool   _isBusy;
        [NonSerialized] private string _retryNotice;
        [NonSerialized] private GeminiAdvisoryReport _currentReport;
        [NonSerialized] private Action _repaint;

        public bool IsBusy => _isBusy;

        /// <summary>Pré-preenche a pergunta (botão "Tirar dúvida" de um finding do laudo).</summary>
        public void PrefillQuestion(string question) => _input = question;

        public void Draw(GeminiAdvisoryReport lastReport, Action repaint)
        {
            _currentReport = lastReport;
            _repaint = repaint;

            if (!AiSettings.IsReady)
            {
                EditorGUILayout.HelpBox(Loc.T("Configure o provedor de IA e o modelo na aba Configuração.",
                                              "Set up the AI provider and model in the Settings tab.",
                                              "Configura el proveedor de IA y el modelo en la pestaña Configuración."),
                                        MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(Loc.T("Converse com a IA sobre o laudo", "Talk to the AI about the report",
                                                 "Conversa con la IA sobre el informe"), EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(_messages.Count == 0 || _isBusy))
                {
                    if (GUILayout.Button(Loc.T("Limpar conversa", "Clear chat", "Borrar conversación"), GUILayout.Width(120))
                        && EditorUtility.DisplayDialog("Performance Advisor",
                            Loc.T("Apagar todo o histórico da conversa?", "Delete the whole chat history?",
                                  "¿Borrar todo el historial de la conversación?"),
                            Loc.T("Apagar", "Delete", "Borrar"), Loc.T("Cancelar", "Cancel", "Cancelar")))
                    {
                        _messages.Clear();
                    }
                }
            }

            if (lastReport == null)
                EditorGUILayout.HelpBox(Loc.T(
                    "Ainda não há laudo nesta sessão — o chat funciona, mas as respostas ficam " +
                    "melhores depois de rodar uma análise.",
                    "There is no report in this session yet — the chat works, but the answers get " +
                    "better after you run an analysis.",
                    "Todavía no hay informe en esta sesión: el chat funciona, pero las respuestas son " +
                    "mejores después de ejecutar un análisis."), MessageType.Info);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _messages.Count; i++) DrawBubble(_messages[i]);
            EditorGUILayout.EndScrollView();

            if (_isBusy)
                EditorGUILayout.HelpBox(_retryNotice ?? Thinking(), MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_isBusy))
                {
                    if (GUILayout.Button(Loc.T("📎 Anexar script", "📎 Attach script", "📎 Adjuntar script"),
                                         GUILayout.Width(120))) AttachScript();
                }
                if (!string.IsNullOrEmpty(_attachedName))
                {
                    int lines = 1 + CountChar(_attachedContent, '\n');
                    EditorGUILayout.LabelField($"📎 {_attachedName} ({lines} {Loc.T("linhas", "lines", "líneas")})",
                                               EditorStyles.miniLabel);
                    if (GUILayout.Button("X", GUILayout.Width(22))) { _attachedName = ""; _attachedContent = ""; }
                }
            }

            using (new EditorGUI.DisabledScope(_isBusy))
            {
                _input = EditorGUILayout.TextArea(_input, GUILayout.MinHeight(50));
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_input)))
                {
                    if (GUILayout.Button(_isBusy ? Thinking() : Loc.T("Enviar", "Send", "Enviar"), GUILayout.Height(28)))
                        Send();
                }
            }
        }

        private void DrawBubble(ChatMessage m)
        {
            bool isUser = m.role == ChatMessage.ROLE_USER && !m.isError;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (isUser) GUILayout.Space(40);   // balão do dev encostado à direita
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    string header = m.isError ? Loc.T("⚠ Erro", "⚠ Error", "⚠ Error")
                                  : isUser    ? Loc.T("Você", "You", "Tú")
                                  : Loc.T("IA", "AI", "IA");
                    EditorGUILayout.LabelField(header, EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField(m.text, EditorStyles.wordWrappedLabel);
                    if (!isUser && !m.isError && GUILayout.Button(Loc.T("Copiar", "Copy", "Copiar"), GUILayout.Width(60)))
                        GUIUtility.systemCopyBuffer = m.text;
                    if (m.isError && !_isBusy && GUILayout.Button(Loc.T("Tentar de novo", "Try again", "Reintentar"),
                                                                  GUILayout.Width(110)))
                        Retry();
                }
                if (!isUser) GUILayout.Space(40);  // balão da IA encostado à esquerda
            }
            EditorGUILayout.Space(2);
        }

        private void AttachScript()
        {
            string path = EditorUtility.OpenFilePanel(Loc.T("Anexar script C#", "Attach C# script", "Adjuntar script C#"),
                                                      Application.dataPath, "cs");
            if (string.IsNullOrEmpty(path)) return;
            var info = new FileInfo(path);
            if (info.Length > MAX_ATTACH_BYTES)
            {
                EditorUtility.DisplayDialog("Performance Advisor",
                    Loc.T($"Arquivo muito grande ({info.Length / 1024} KB). Limite: {MAX_ATTACH_BYTES / 1024} KB.",
                          $"File too large ({info.Length / 1024} KB). Limit: {MAX_ATTACH_BYTES / 1024} KB.",
                          $"Archivo demasiado grande ({info.Length / 1024} KB). Límite: {MAX_ATTACH_BYTES / 1024} KB."), "OK");
                return;
            }
            _attachedName    = Path.GetFileName(path);
            _attachedContent = File.ReadAllText(path);
        }

        private void Send()
        {
            string text = _input.Trim();
            if (!string.IsNullOrEmpty(_attachedName))
            {
                // Demarcação que o CHAT_SYSTEM_PROMPT instrui o modelo a reconhecer.
                text += $"\n\n--- script anexado: {_attachedName} ---\n{_attachedContent}";
                _attachedName = ""; _attachedContent = "";
            }
            _messages.Add(new ChatMessage { role = ChatMessage.ROLE_USER, text = text });
            _input = "";
            Dispatch();
        }

        // Reenvia o histórico atual — usado pelo Enviar e pelo Tentar de novo.
        private void Dispatch()
        {
            _isBusy = true;
            _retryNotice = null;
            GeminiChatService.Ask(GeminiChatService.BuildSessionContext(_currentReport), _messages,
                onAnswer: ans =>
                {
                    _messages.Add(new ChatMessage { role = ChatMessage.ROLE_MODEL, text = ans });
                    _isBusy = false;
                    _scroll.y = float.MaxValue;   // rola pro fim
                    _repaint?.Invoke();
                },
                onError: err =>
                {
                    _messages.Add(new ChatMessage { role = ChatMessage.ROLE_MODEL, text = err, isError = true });
                    _isBusy = false;
                    _repaint?.Invoke();
                },
                onRetryNotice: msg => { _retryNotice = msg; _repaint?.Invoke(); });
        }

        private void Retry()
        {
            while (_messages.Count > 0 && _messages[_messages.Count - 1].isError)
                _messages.RemoveAt(_messages.Count - 1);
            if (_messages.Count == 0) return;
            Dispatch();
        }

        private static string Thinking() => Loc.T("Pensando...", "Thinking...", "Pensando...");

        private static int CountChar(string s, char c)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++) if (s[i] == c) n++;
            return n;
        }
    }
}
