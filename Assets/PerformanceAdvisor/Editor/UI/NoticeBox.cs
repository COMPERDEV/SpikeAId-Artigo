using UnityEditor;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>Severity of a <see cref="NoticeBox"/> message.</summary>
    public enum NoticeLevel
    {
        Info,
        Warning,
        Error,
        Success
    }

    /// <summary>
    /// Replacement for <c>EditorGUILayout.HelpBox</c> on the plugin's main flow:
    /// same call shape, but legible at a glance.
    /// </summary>
    /// <remarks>
    /// MOTIVO: o HelpBox padrão da Unity usa fonte miúda e fundo quase igual ao da
    /// janela — na revisão o orientador relatou que os avisos do plugin passavam
    /// despercebidos durante o uso. Aqui o texto cresce, ganha respiro e o fundo é
    /// tingido por severidade, então o aviso disputa atenção de verdade.
    ///
    /// O tingimento usa <see cref="GUI.backgroundColor"/> em vez de texturas próprias
    /// para continuar respeitando o tema claro/escuro do Editor sem carregar asset algum.
    /// </remarks>
    public static class NoticeBox
    {
        private static GUIStyle _style;

        // MOTIVO: cores de baixa saturação. O fundo do HelpBox é multiplicado por
        // este tint, então valores fortes deixariam o texto ilegível no tema claro.
        private static readonly Color INFO    = new Color(0.55f, 0.78f, 1.00f);
        private static readonly Color WARNING = new Color(1.00f, 0.80f, 0.35f);
        private static readonly Color ERROR   = new Color(1.00f, 0.50f, 0.45f);
        private static readonly Color SUCCESS = new Color(0.55f, 0.95f, 0.60f);

        /// <summary>
        /// Draws a tinted, larger notice box. Call from inside OnGUI only —
        /// the style is built lazily because <see cref="EditorStyles"/> is not
        /// available during deserialization.
        /// </summary>
        public static void Draw(string message, NoticeLevel level = NoticeLevel.Info)
        {
            if (string.IsNullOrEmpty(message)) return;

            if (_style == null)
            {
                _style = new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize  = Mathf.Max(EditorStyles.helpBox.fontSize + 2, 12),
                    wordWrap  = true,
                    richText  = true,
                    alignment = TextAnchor.MiddleLeft,
                    padding   = new RectOffset(10, 10, 8, 8)
                };
            }

            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = TintFor(level);
            EditorGUILayout.LabelField($"{IconFor(level)}  {message}", _style);
            GUI.backgroundColor = previous;
        }

        private static Color TintFor(NoticeLevel level)
        {
            switch (level)
            {
                case NoticeLevel.Warning: return WARNING;
                case NoticeLevel.Error:   return ERROR;
                case NoticeLevel.Success: return SUCCESS;
                default:                  return INFO;
            }
        }

        // MOTIVO: ícone além da cor — daltonismo vermelho/verde é comum o bastante
        // para que cor sozinha não seja o único canal de informação.
        private static string IconFor(NoticeLevel level)
        {
            switch (level)
            {
                case NoticeLevel.Warning: return "⚠️";
                case NoticeLevel.Error:   return "⛔";
                case NoticeLevel.Success: return "✅";
                default:                  return "ℹ️";
            }
        }
    }
}
