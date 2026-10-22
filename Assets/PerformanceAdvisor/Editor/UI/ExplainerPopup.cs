using UnityEditor;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Small window that holds the long explanation behind a short UI notice.
    /// </summary>
    /// <remarks>
    /// MOTIVO: os avisos do painel viraram parágrafos de cinco linhas cheios de termos
    /// que só fazem sentido pra quem já conhece o problema — "domínio C# do Editor",
    /// "índice que converta para o SoC", "cobertura curada". O público-alvo do plugin são
    /// desenvolvedores iniciantes, e um aviso que o leitor não entende não avisa nada:
    /// vira ruído que ele aprende a pular.
    ///
    /// A justificativa longa continua existindo, porque ela é o que sustenta o número na
    /// banca e o que o dev precisa quando quiser conferir. Só deixa de ser a PRIMEIRA
    /// coisa na tela. Frase curta na UI, detalhe a um clique.
    /// </remarks>
    public class ExplainerPopup : EditorWindow
    {
        private string _body;
        private Vector2 _scroll;

        /// <summary>Opens a utility window with the full explanation.</summary>
        public static void Show(string title, string body)
        {
            var w = CreateInstance<ExplainerPopup>();
            w.titleContent = new GUIContent(title);
            w._body = body;
            // MOTIVO: ShowUtility (e não ShowModal) porque o dev pode querer ler o texto
            // enquanto mexe no painel — fechar pra continuar seria hostil.
            w.ShowUtility();
            w.minSize = new Vector2(460f, 240f);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            var style = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false };
            EditorGUILayout.LabelField(_body, style);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(4);

            if (GUILayout.Button(Loc.T("Fechar", "Close", "Cerrar"), GUILayout.Height(24))) Close();
            EditorGUILayout.Space(4);
        }
    }
}
