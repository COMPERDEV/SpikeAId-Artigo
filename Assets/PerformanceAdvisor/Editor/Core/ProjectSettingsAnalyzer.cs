using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Static check of the render settings that ship with the build for the chosen target.
    /// </summary>
    /// <remarks>
    /// MOTIVO: etapa 3 do roadmap, com a lista fechada que o orientador deu — Probe
    /// Blending, Soft Shadows, SRP Batcher, Depth Texture, Opaque Texture e
    /// post-processing em VR.
    ///
    /// A Unity já tem o Project Auditor (módulo do Editor no 6000.4), mas das seis regras
    /// ele cobre só o SRP Batcher (PAS1008). As outras cinco são custo de renderização da
    /// URP e ficam descobertas — é essa lacuna que este analisador cobre, e não
    /// configuração genérica de projeto, onde o Auditor é melhor.
    ///
    /// PROCEDÊNCIA (regra do projeto): cada recomendação cita a página que a sustenta.
    /// São duas fontes, ambas guias publicados pelo fornecedor (procedência 3):
    ///  - Unity, "Configure for better performance" (URP) — Depth/Opaque Texture, Soft
    ///    Shadows, Probe Blending (mobile de entrada), SRP Batcher.
    ///  - Android Developers, "Optimize rendering using URP Asset settings" (XR) —
    ///    post-processing em XR mobile, Depth/Opaque Texture, SRP Batcher.
    /// Nenhuma regra é aplicada a uma plataforma que a fonte não cobre: Probe Blending é
    /// "mobile de entrada" na página da Unity e não vale para PC; post-processing vem da
    /// página de XR e vale só para VR.
    ///
    /// DOIS CUIDADOS DE LEITURA:
    ///  1. O asset analisado é o da PLATAFORMA ALVO, não o ativo no Editor. Um projeto
    ///     URP costuma ter um asset por nível de qualidade (este tem PC_RPAsset e
    ///     Mobile_RPAsset), e a build Android leva só o do nível padrão do Android.
    ///     Analisar o asset errado daria a resposta certa para a pergunta errada.
    ///  2. Os campos são lidos pelo nome SERIALIZADO, não pelos tipos da URP. Referenciar
    ///     o assembly da URP faria o plugin não compilar em projetos com outro pipeline.
    ///     Campo que não existe (outra versão da URP) aparece como ilegível, nunca como OK.
    ///
    /// Não altera nada no projeto: Depth e Opaque Texture, por exemplo, são necessários
    /// quando algum shader amostra a cena, e só o dev sabe se é o caso.
    /// </remarks>
    public static class ProjectSettingsAnalyzer
    {
        public enum Status
        {
            Ok,            // já está como a fonte recomenda
            Recomendacao,  // difere da recomendação publicada
            NaoSeAplica,   // a fonte não cobre esta plataforma
            Ilegivel       // não foi possível ler o valor — nunca tratado como OK
        }

        public struct Finding
        {
            public string Title;
            public Status Status;
            public string Current;         // valor lido, em palavras
            public string Recommendation;  // o que a fonte recomenda
            public string Why;             // motivo declarado pela fonte
            public string SourceName;
            public string SourceUrl;
        }

        public const string URL_UNITY_URP =
            "https://docs.unity3d.com/6000.0/Documentation/Manual/urp/configure-for-better-performance.html";
        public const string URL_ANDROID_XR =
            "https://developer.android.com/develop/xr/unity/performance/urp-asset-settings";

        private const string SRC_UNITY   = "Unity — URP: Configure for better performance";
        private const string SRC_ANDROID = "Android Developers — URP Asset settings para XR";

        /// <summary>Result of reading which render pipeline asset the target build uses.</summary>
        public struct TargetAsset
        {
            public RenderPipelineAsset Asset;
            public string QualityLevelName;
            public string Problem; // null quando deu certo
        }

        /// <summary>Finds the render pipeline asset the build for this platform will ship.</summary>
        public static TargetAsset ResolveAsset(TargetPlatform platform)
        {
            var result = new TargetAsset();

            // MOTIVO: o nível de qualidade padrão por plataforma mora num mapa serializado
            // do QualitySettings (m_PerPlatformDefaultQuality). É o mesmo dado que a
            // janela Project Settings → Quality mostra na linha "Default" de cada
            // plataforma, e é o que decide qual asset de URP entra na build.
            Object[] qsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (qsAssets == null || qsAssets.Length == 0)
            {
                result.Problem = Loc.T("Não foi possível ler ProjectSettings/QualitySettings.asset.",
                                       "Could not read ProjectSettings/QualitySettings.asset.",
                                       "No se pudo leer ProjectSettings/QualitySettings.asset.");
                return result;
            }

            var so = new SerializedObject(qsAssets[0]);
            SerializedProperty perPlatform = so.FindProperty("m_PerPlatformDefaultQuality");
            SerializedProperty levels      = so.FindProperty("m_QualitySettings");
            if (perPlatform == null || levels == null)
            {
                result.Problem = Loc.T("Formato do QualitySettings não reconhecido nesta versão da Unity.",
                                       "QualitySettings format not recognized in this Unity version.",
                                       "Formato de QualitySettings no reconocido en esta versión de Unity.");
                return result;
            }

            // PC → "Standalone"; Android e Quest → "Android" (o Quest builda como Android).
            string key = platform == TargetPlatform.PC ? "Standalone" : "Android";
            int levelIndex = -1;
            for (int i = 0; i < perPlatform.arraySize; i++)
            {
                SerializedProperty pair = perPlatform.GetArrayElementAtIndex(i);
                SerializedProperty first = pair.FindPropertyRelative("first");
                if (first != null && first.stringValue == key)
                {
                    levelIndex = pair.FindPropertyRelative("second").intValue;
                    break;
                }
            }

            if (levelIndex < 0 || levelIndex >= levels.arraySize)
            {
                result.Problem = Loc.T($"Nenhum nível de qualidade padrão definido para {key}.",
                                       $"No default quality level set for {key}.",
                                       $"No hay un nivel de calidad por defecto definido para {key}.");
                return result;
            }

            SerializedProperty level = levels.GetArrayElementAtIndex(levelIndex);
            result.QualityLevelName = level.FindPropertyRelative("name")?.stringValue
                                      ?? Loc.T($"nível {levelIndex}", $"level {levelIndex}", $"nivel {levelIndex}");

            // Nível sem pipeline próprio herda o padrão de Graphics Settings.
            var levelRp = level.FindPropertyRelative("customRenderPipeline")?.objectReferenceValue as RenderPipelineAsset;
            result.Asset = levelRp != null ? levelRp : GraphicsSettings.defaultRenderPipeline;

            if (result.Asset == null)
                result.Problem = Loc.T("O projeto não usa Scriptable Render Pipeline — estas regras são da URP.",
                                       "The project does not use a Scriptable Render Pipeline — these rules are for URP.",
                                       "El proyecto no usa Scriptable Render Pipeline: estas reglas son de URP.");
            else if (result.Asset.GetType().Name != "UniversalRenderPipelineAsset")
            {
                string rp = result.Asset.GetType().Name;
                result.Problem = Loc.T($"O pipeline do alvo é {rp}, não URP — estas regras são da URP.",
                                       $"The target's pipeline is {rp}, not URP — these rules are for URP.",
                                       $"El pipeline del objetivo es {rp}, no URP: estas reglas son de URP.");
            }

            return result;
        }

        /// <summary>Runs the six checks for the given target.</summary>
        public static List<Finding> Analyze(TargetPlatform platform, TargetAsset target)
        {
            var list = new List<Finding>(6);
            if (target.Problem != null || target.Asset == null) return list;

            var so = new SerializedObject(target.Asset);

            // 1. SRP Batcher — ligar. Todas as plataformas.
            list.Add(BoolCheck(so, "m_UseSRPBatcher", "SRP Batcher", wantOn: true,
                recommendation: On(),
                why: Loc.T("Reduz o trabalho de preparação da GPU entre draw calls — menos tempo de CPU e " +
                           "GPU. (Também verificado pelo Project Auditor da Unity, regra PAS1008.)",
                           "Reduces GPU setup work between draw calls — less CPU and GPU time. " +
                           "(Also checked by Unity's Project Auditor, rule PAS1008.)",
                           "Reduce el trabajo de preparación de la GPU entre draw calls: menos tiempo de CPU y " +
                           "GPU. (También lo comprueba el Project Auditor de Unity, regla PAS1008.)"),
                source: SRC_UNITY, url: URL_UNITY_URP));

            // 2 e 3. Depth e Opaque Texture — desligar a menos que um shader precise.
            list.Add(BoolCheck(so, "m_RequireDepthTexture", "Depth Texture", wantOn: false,
                recommendation: Loc.T("Desligado, a menos que algum shader amostre a profundidade da cena",
                                      "Off, unless some shader samples the scene depth",
                                      "Desactivado, salvo que algún shader muestree la profundidad de la escena"),
                why: Loc.T("Evita guardar uma textura de profundidade que ninguém usa. Em XR mobile, " +
                           "elimina cópias extras de textura que gastam tempo de GPU.",
                           "Avoids keeping a depth texture nobody uses. On mobile XR, it removes " +
                           "extra texture copies that cost GPU time.",
                           "Evita guardar una textura de profundidad que nadie usa. En XR móvil, " +
                           "elimina copias extra de textura que gastan tiempo de GPU."),
                source: platform == TargetPlatform.VR ? SRC_ANDROID : SRC_UNITY,
                url: platform == TargetPlatform.VR ? URL_ANDROID_XR : URL_UNITY_URP));

            list.Add(BoolCheck(so, "m_RequireOpaqueTexture", "Opaque Texture", wantOn: false,
                recommendation: Loc.T("Desligado, a menos que algum shader leia a cena opaca (ex.: refração)",
                                      "Off, unless some shader reads the opaque scene (e.g. refraction)",
                                      "Desactivado, salvo que algún shader lea la escena opaca (p. ej., refracción)"),
                why: Loc.T("Evita que a URP guarde uma cópia dos objetos opacos a cada frame — menos memória " +
                           "e, em XR mobile, menos cópias de textura.",
                           "Stops URP from keeping a copy of the opaque objects every frame — less memory " +
                           "and, on mobile XR, fewer texture copies.",
                           "Evita que URP guarde una copia de los objetos opacos en cada frame: menos memoria " +
                           "y, en XR móvil, menos copias de textura."),
                source: platform == TargetPlatform.VR ? SRC_ANDROID : SRC_UNITY,
                url: platform == TargetPlatform.VR ? URL_ANDROID_XR : URL_UNITY_URP));

            // 4. Soft Shadows — desligar ou baixar a qualidade.
            Finding soft = BoolCheck(so, "m_SoftShadowsSupported", "Soft Shadows", wantOn: false,
                recommendation: Loc.T("Desligado, ou ligado com qualidade baixa", "Off, or on at low quality",
                                      "Desactivado, o activado con calidad baja"),
                why: Loc.T("Sombra suave exige processamento extra do mapa de sombras — reduz tempo de GPU.",
                           "Soft shadows need extra processing of the shadow map — turning them down saves GPU time.",
                           "La sombra suave exige procesamiento extra del mapa de sombras: reducirla ahorra tiempo de GPU."),
                source: SRC_UNITY, url: URL_UNITY_URP);
            SerializedProperty quality = so.FindProperty("m_SoftShadowQuality");
            if (soft.Status == Status.Recomendacao && quality != null)
                soft.Current += $" ({Loc.T("qualidade", "quality", "calidad")} {SoftShadowQualityName(quality.intValue)})";
            list.Add(soft);

            // 5. Probe Blending — só mobile: a fonte fala em "mobile de entrada".
            if (platform == TargetPlatform.Mobile)
            {
                SerializedProperty blend = so.FindProperty("m_ReflectionProbeBlending");
                SerializedProperty box   = so.FindProperty("m_ReflectionProbeBoxProjection");
                var f = new Finding
                {
                    // MOTIVO: título curto — "Probe Blending / Box Projection" não cabia na coluna e
                    // se sobrepunha ao valor. O Box Projection segue no valor e na explicação.
                    Title = "Probe Blending",
                    Recommendation = Loc.T("Ambos desligados em aparelho de entrada", "Both off on entry-level devices",
                                           "Ambos desactivados en dispositivos de gama de entrada"),
                    Why = Loc.T("Misturar reflection probes e projetar em caixa custa processamento de CPU; a " +
                                "Unity recomenda desligar os dois em mobile de entrada.",
                                "Blending reflection probes and box projection cost CPU processing; " +
                                "Unity recommends turning both off on entry-level mobile.",
                                "Mezclar reflection probes y proyectar en caja cuesta procesamiento de CPU; " +
                                "Unity recomienda desactivar ambos en móviles de gama de entrada."),
                    SourceName = SRC_UNITY,
                    SourceUrl = URL_UNITY_URP
                };
                if (blend == null || box == null)
                {
                    f.Status = Status.Ilegivel;
                    f.Current = Unreadable();
                }
                else
                {
                    f.Current = $"Blending {OnOffLower(blend.boolValue)}, " +
                                $"Box Projection {OnOffLower(box.boolValue)}";
                    f.Status = (blend.boolValue || box.boolValue) ? Status.Recomendacao : Status.Ok;
                }
                list.Add(f);
            }
            else
            {
                list.Add(NotApplicable("Probe Blending",
                    platform == TargetPlatform.VR
                        ? Loc.T("A recomendação publicada é para mobile de entrada; não há fonte para headset.",
                                "The published recommendation is for entry-level mobile; there is no source for headsets.",
                                "La recomendación publicada es para móviles de gama de entrada; no hay fuente para visores.")
                        : Loc.T("A recomendação publicada é para mobile de entrada; não há fonte para PC.",
                                "The published recommendation is for entry-level mobile; there is no source for PC.",
                                "La recomendación publicada es para móviles de gama de entrada; no hay fuente para PC."),
                    SRC_UNITY, URL_UNITY_URP));
            }

            // 6. Post-processing — só VR: a fonte é a página de XR.
            if (platform == TargetPlatform.VR)
                list.Add(PostProcessingCheck());
            else
                list.Add(NotApplicable("Post-processing",
                    platform == TargetPlatform.PC
                        ? Loc.T("A recomendação publicada é para XR mobile; em PC o custo depende do efeito.",
                                "The published recommendation is for mobile XR; on PC the cost depends on the effect.",
                                "La recomendación publicada es para XR móvil; en PC el costo depende del efecto.")
                        : Loc.T("A recomendação publicada é para XR mobile; em celular o custo depende do efeito.",
                                "The published recommendation is for mobile XR; on phones the cost depends on the effect.",
                                "La recomendación publicada es para XR móvil; en teléfonos el costo depende del efecto."),
                    SRC_ANDROID, URL_ANDROID_XR));

            return list;
        }

        // ── Regras ─────────────────────────────────────────────────────────

        private static Finding BoolCheck(SerializedObject so, string field, string title, bool wantOn,
                                         string recommendation, string why, string source, string url)
        {
            var f = new Finding
            {
                Title = title,
                Recommendation = recommendation,
                Why = why,
                SourceName = source,
                SourceUrl = url
            };

            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                // MOTIVO: campo ausente é outra versão da URP, não "está certo". Ilegível
                // nunca pode aparecer como OK — seria o zero verde de novo.
                f.Status = Status.Ilegivel;
                f.Current = Unreadable();
                return f;
            }

            f.Current = p.boolValue ? On() : Off();
            f.Status = p.boolValue == wantOn ? Status.Ok : Status.Recomendacao;
            return f;
        }

        private static Finding NotApplicable(string title, string why, string source, string url)
        {
            return new Finding
            {
                Title = title,
                Status = Status.NaoSeAplica,
                Current = "—",
                Recommendation = Loc.T("sem recomendação publicada para este alvo",
                                       "no published recommendation for this target",
                                       "sin recomendación publicada para este objetivo"),
                Why = why,
                SourceName = source,
                SourceUrl = url
            };
        }

        /// <summary>Looks for cameras with post-processing enabled in the build's scenes.</summary>
        /// <remarks>
        /// MOTIVO: na URP o post-processing é ligado por CÂMERA, não no asset. Ler os
        /// arquivos das cenas da build como texto cobre todas sem abri-las — abrir cena por
        /// cena trocaria o que o dev está editando. O campo m_RenderPostProcessing é da
        /// UniversalAdditionalCameraData e só aparece ali. Cena salva em formato binário
        /// não é legível assim, e isso é declarado em vez de assumido como "sem post".
        /// </remarks>
        private static Finding PostProcessingCheck()
        {
            var f = new Finding
            {
                Title = "Post-processing",
                Recommendation = Loc.T("Desligado nas câmeras", "Off on the cameras", "Desactivado en las cámaras"),
                Why = Loc.T("Post-processing é caro em hardware XR mobile e costuma trazer pouco ganho visual " +
                            "perto do custo.",
                            "Post-processing is expensive on mobile XR hardware and usually brings little visual gain " +
                            "for the cost.",
                            "El post-procesado es caro en hardware XR móvil y suele aportar poca ganancia visual " +
                            "para su costo."),
                SourceName = SRC_ANDROID,
                SourceUrl = URL_ANDROID_XR
            };

            var withPost = new List<string>();
            int unreadable = 0, scanned = 0;

            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (!s.enabled || string.IsNullOrEmpty(s.path) || !File.Exists(s.path)) continue;
                string text;
                try { text = File.ReadAllText(s.path); }
                catch { unreadable++; continue; }

                if (!text.StartsWith("%YAML")) { unreadable++; continue; }
                scanned++;
                if (text.Contains("m_RenderPostProcessing: 1"))
                    withPost.Add(Path.GetFileNameWithoutExtension(s.path));
            }

            if (scanned == 0)
            {
                f.Status = Status.Ilegivel;
                f.Current = unreadable > 0
                    ? Loc.T("cenas da build em formato binário — não foi possível ler",
                            "build scenes in binary format — could not read them",
                            "escenas de la build en formato binario: no se pudieron leer")
                    : Loc.T("nenhuma cena marcada na build", "no scene ticked in the build", "ninguna escena marcada en la build");
            }
            else if (withPost.Count > 0)
            {
                f.Status = Status.Recomendacao;
                f.Current = Loc.T("ligado em: ", "on in: ", "activado en: ") + string.Join(", ", withPost);
            }
            else
            {
                f.Status = Status.Ok;
                f.Current = Loc.T($"desligado nas {scanned} cena(s) da build",
                                  $"off in the build's {scanned} scene(s)",
                                  $"desactivado en las {scanned} escena(s) de la build") +
                            (unreadable > 0
                                ? " (" + Loc.T($"{unreadable} ilegível(is)", $"{unreadable} unreadable",
                                               $"{unreadable} ilegible(s)") + ")"
                                : "");
            }
            return f;
        }

        private static string On()         => Loc.T("Ligado", "On", "Activado");
        private static string Off()        => Loc.T("Desligado", "Off", "Desactivado");
        private static string OnOffLower(bool on) =>
            on ? Loc.T("ligado", "on", "activado") : Loc.T("desligado", "off", "desactivado");
        private static string Unreadable() => Loc.T("não foi possível ler", "could not read", "no se pudo leer");

        private static string SoftShadowQualityName(int v)
        {
            // Valores do enum SoftShadowQuality da URP 17: 0 UsePipelineSettings, 1 Low,
            // 2 Medium, 3 High.
            switch (v)
            {
                case 1:  return Loc.T("baixa", "low", "baja");
                case 2:  return Loc.T("média", "medium", "media");
                case 3:  return Loc.T("alta", "high", "alta");
                default: return v.ToString();
            }
        }
    }
}
