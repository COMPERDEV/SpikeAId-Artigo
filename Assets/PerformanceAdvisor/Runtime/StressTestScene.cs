using System;
using UnityEngine;

namespace PerformanceAdvisor.StressTest
{
    /// <summary>
    /// Intentionally bad MonoBehaviour to validate the Performance Advisor.
    /// Every <c>Update</c> tick triggers one or more pathological patterns —
    /// toggle them in the Inspector to isolate signals.
    /// </summary>
    /// <remarks>
    /// NOTE: Lives outside any Editor folder because we want it inside the
    /// regular game build for the validation flow Play → Stop → Analisar.
    /// </remarks>
    public class StressTestScene : MonoBehaviour
    {
        [Header("Toggles")]
        [Tooltip("Forces ~1 MB / frame of GC pressure via string concatenation.")]
        public bool stressGc = true;

        [Tooltip("Forces an O(n) scene scan every frame.")]
        public bool stressCpu = true;

        [Tooltip("Spawns one prefab per frame without pooling — pushes draw calls up.")]
        public bool stressDraw = false;

        [Header("Draw-call test")]
        public GameObject testPrefab;

        [Range(1, 20)]
        public float spawnRadius = 10f;

        [Tooltip("Quantos cubos spawnar por frame quando Stress Draw está ligado. " +
                 "Aumentar acelera a chegada ao limite de Draw Calls.")]
        [Range(1, 50)]
        public int cubesPerFrame = 1;

        [Tooltip("Atribui cor aleatória única em cada cubo. Derrota o auto-batching " +
                 "da Unity (Static/Dynamic/GPU Instancing), forçando cada objeto a virar " +
                 "1 draw call separado. Sem isso, N cubos idênticos ainda viram 1 só draw call.")]
        public bool useUniqueMaterials = false;

        [Header("Artigo: intensidade controlada")]
        [Tooltip("Quantas varreduras FindObjectsOfType por frame quando Stress CPU está ligado.")]
        [Range(1, 512)]
        public int cpuScansPerFrame = 1;

        [Tooltip("GameObjects vazios criados no Start — aumentam o custo de cada varredura sem desenhar nada.")]
        public int emptyObjects = 0;

        [Tooltip("Iterações da concatenação de string por frame quando Stress GC está ligado.")]
        public int gcConcatIterations = 1000;

        [Tooltip("Arrays de 10.000 referências (~80 KB cada) alocados e descartados por frame quando Stress GC está ligado.")]
        public int gcArrayAllocs = 0;

        [Tooltip("Camadas de quad em tela cheia, transparentes, na frente da câmera — custo de GPU (overdraw).")]
        public int overdrawLayers = 0;

        [Tooltip("Material das camadas de overdraw (shader PerformanceAdvisor/StressOverdraw).")]
        public Material overdrawMaterial;

        [Header("Pré-população (cena já 'pesada' antes do gameplay)")]
        [Tooltip("Spawna N cubos no Start(), simulando uma cena já carregada com muitos objetos. " +
                 "Útil pra forçar Draw Calls acima do budget de cara, sem esperar o Update acumular.")]
        [Range(0, 2000)]
        public int prepopulateCount = 0;

        private void Start()
        {
            // Opção C: cena já "pesada" desde o frame 0.
            // Útil pra simular um nível recém-carregado com muitos objetos —
            // o gargalo de Draw Calls fica visível imediatamente, sem esperar o Update acumular.
            if (prepopulateCount > 0 && testPrefab != null)
            {
                for (int i = 0; i < prepopulateCount; i++)
                {
                    SpawnOne();
                }
            }

            for (int i = 0; i < emptyObjects; i++) new GameObject("Empty_" + i);

            if (overdrawLayers > 0 && overdrawMaterial != null) BuildOverdrawLayers();
        }

        // ❌ Anti-pattern on purpose: N quads em tela cheia, transparentes, um sobre o outro.
        // Cada pixel da tela é pintado N vezes — custo de GPU (preenchimento), não de CPU.
        private void BuildOverdrawLayers()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            for (int i = 0; i < overdrawLayers; i++)
            {
                float d = cam.nearClipPlane + 0.05f + i * 0.001f;
                float h = 2f * d * Mathf.Tan(halfFov) * 1.1f;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                quad.name = "Overdraw_" + i;
                quad.transform.SetParent(cam.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, d);
                quad.transform.localScale = new Vector3(h * cam.aspect, h, 1f);
                quad.GetComponent<Renderer>().sharedMaterial = overdrawMaterial;
            }
        }

        private void Update()
        {
            if (stressGc)
            {
                // ❌ Anti-pattern on purpose: forces a heap allocation chain.
                string s = "";
                for (int i = 0; i < gcConcatIterations; i++) s += i.ToString();
                GC.KeepAlive(s);

                // ❌ Anti-pattern on purpose: blocos grandes alocados e jogados fora todo frame.
                // MOTIVO: só a concatenação não fez a coleta aparecer nos frames estourados (Unity 6,
                // GC incremental); blocos grandes, como os das varreduras de CPU, fazem.
                for (int i = 0; i < gcArrayAllocs; i++)
                {
                    var block = new object[10000];
                    GC.KeepAlive(block);
                }
            }

            if (stressCpu)
            {
                // ❌ Anti-pattern on purpose: scans every GameObject every frame.
                for (int k = 0; k < cpuScansPerFrame; k++)
                {
                    var allObjects = GameObject.FindObjectsOfType<GameObject>();
                    GC.KeepAlive(allObjects);
                }
            }

            // Opção A: spawn N cubos por frame em vez de 1.
            // Acelera a chegada ao budget de Draw Calls e simula um padrão de spawn
            // descontrolado (ex: emissão de partículas, balas sem pooling).
            if (stressDraw && testPrefab != null)
            {
                int n = Mathf.Max(1, cubesPerFrame);
                for (int i = 0; i < n; i++) SpawnOne();
            }
        }

        // Helper para evitar duplicar a lógica de unique materials.
        private void SpawnOne()
        {
            var clone = Instantiate(
                testPrefab,
                UnityEngine.Random.insideUnitSphere * spawnRadius,
                Quaternion.identity);

            if (useUniqueMaterials)
            {
                var renderer = clone.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // MOTIVO: acessar `.material` (não `.sharedMaterial`) força a Unity
                    // a criar uma instância única de material por GameObject — exatamente
                    // o que quebra o batching e faz cada cubo virar seu próprio draw call.
                    renderer.material.color = new Color(
                        UnityEngine.Random.value,
                        UnityEngine.Random.value,
                        UnityEngine.Random.value);
                }
            }
        }
    }
}
