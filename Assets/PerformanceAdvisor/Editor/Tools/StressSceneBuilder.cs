using PerformanceAdvisor.StressTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PerformanceAdvisor
{
/// <summary>
/// One-click scene builder for the Performance Advisor stress tests.
/// Vive sob o asmdef PerformanceAdvisor (Editor-only) e referencia
/// <see cref="StressTestScene"/> via o asmdef runtime "StressTest"
/// listado em PerformanceAdvisor.asmdef.references.
/// </summary>
public static class StressSceneBuilder
{
    private const string SCENE_DIR    = "Assets/PerformanceAdvisor/StressScenes";
    private const string PREFAB_DIR   = "Assets/PerformanceAdvisor/Runtime";
    private const string CUBE_PREFAB  = "Assets/PerformanceAdvisor/Runtime/StressCube.prefab";

    [MenuItem("Tools/Performance Advisor/Build Draw-Call Stress Scene", false, 100)]
    public static void BuildDrawCallScene()
    {
        BuildScene(
            sceneName: "StressTestDrawCalls",
            stressGc: false,
            stressCpu: false,
            stressDraw: true,
            useUniqueMaterials: true,
            spawnRadius: 15f,
            cubesPerFrame: 10,        // Opção A: 10/frame derruba os 500 draws em ~5s
            prepopulateCount: 600,    // Opção C: já começa acima do budget de 500
            description: Loc.T("Draw Calls (600 cubos pré-spawnados + 10/frame, materiais únicos)",
                               "Draw Calls (600 pre-spawned cubes + 10/frame, unique materials)",
                               "Draw Calls (600 cubos pre-generados + 10/frame, materiales únicos)"));
    }

    [MenuItem("Tools/Performance Advisor/Build CPU Stress Scene", false, 101)]
    public static void BuildCpuScene()
    {
        BuildScene(
            sceneName: "StressTestCPU",
            stressGc: false,
            stressCpu: true,
            stressDraw: false,
            useUniqueMaterials: false,
            spawnRadius: 10f,
            cubesPerFrame: 1,
            prepopulateCount: 0,
            description: Loc.T("Teste de CPU (FindObjectsOfType por frame)", "CPU test (FindObjectsOfType per frame)",
                               "Prueba de CPU (FindObjectsOfType por frame)"));
    }

    [MenuItem("Tools/Performance Advisor/Build GC Stress Scene", false, 102)]
    public static void BuildGcScene()
    {
        BuildScene(
            sceneName: "StressTestGC",
            stressGc: true,
            stressCpu: false,
            stressDraw: false,
            useUniqueMaterials: false,
            spawnRadius: 10f,
            cubesPerFrame: 1,
            prepopulateCount: 0,
            description: Loc.T("Teste de GC (concatenação de string em loop)", "GC test (string concatenation in a loop)",
                               "Prueba de GC (concatenación de strings en bucle)"));
    }

    [MenuItem("Tools/Performance Advisor/Build All-In Stress Scene", false, 103)]
    public static void BuildAllScene()
    {
        BuildScene(
            sceneName: "StressTestAllIn",
            stressGc: true,
            stressCpu: true,
            stressDraw: true,
            useUniqueMaterials: true,
            spawnRadius: 15f,
            cubesPerFrame: 10,        // A: spawn agressivo
            prepopulateCount: 600,    // C: já começa pesada
            description: Loc.T("Caos total — CPU + GC + 600 cubos pré-spawnados + 10/frame com materiais únicos",
                               "Total chaos — CPU + GC + 600 pre-spawned cubes + 10/frame with unique materials",
                               "Caos total: CPU + GC + 600 cubos pre-generados + 10/frame con materiales únicos"));
    }

    // ─── Implementation ────────────────────────────────────────────────────

    private static void BuildScene(
        string sceneName,
        bool stressGc,
        bool stressCpu,
        bool stressDraw,
        bool useUniqueMaterials,
        float spawnRadius,
        int cubesPerFrame,
        int prepopulateCount,
        string description)
    {
        // Confirm — building a new scene replaces the open one if unsaved.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[StressSceneBuilder] " + Loc.T("Cancelado — cena atual não foi salva.",
                                                      "Cancelled — the current scene was not saved.",
                                                      "Cancelado: la escena actual no se guardó."));
            return;
        }

        EnsureFolder(SCENE_DIR);
        EnsureFolder(PREFAB_DIR);

        // 1) Fresh scene with default lighting + camera.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // 2) Reposition the default Main Camera so we can actually see the swarm.
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 18f, -28f);
            cam.transform.rotation = Quaternion.Euler(28f, 0f, 0f);
            cam.farClipPlane       = 200f;
            cam.backgroundColor    = new Color(0.12f, 0.12f, 0.14f, 1f);
            cam.clearFlags         = CameraClearFlags.SolidColor;
        }

        // 3) Floor for visual reference.
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(8f, 1f, 8f);

        // 4) Cube prefab (created once, reused across all stress scenes).
        GameObject cubePrefab = LoadOrCreateCubePrefab();

        // 5) The spawner GameObject with a pre-configured StressTestScene.
        var spawner = new GameObject("StressTestSpawner");
        var stress  = spawner.AddComponent<StressTestScene>();
        stress.stressGc           = stressGc;
        stress.stressCpu          = stressCpu;
        stress.stressDraw         = stressDraw;
        stress.testPrefab         = cubePrefab;
        stress.spawnRadius        = spawnRadius;
        stress.useUniqueMaterials = useUniqueMaterials;
        stress.cubesPerFrame      = cubesPerFrame;
        stress.prepopulateCount   = prepopulateCount;

        // 6) Save the scene to disk.
        string scenePath = $"{SCENE_DIR}/{sceneName}.unity";
        bool   saved     = EditorSceneManager.SaveScene(scene, scenePath);

        if (!saved)
        {
            EditorUtility.DisplayDialog(
                "Performance Advisor",
                Loc.T("Falha ao salvar a cena. Verifique permissões de arquivo.",
                      "Failed to save the scene. Check file permissions.",
                      "Error al guardar la escena. Revisa los permisos de archivo."),
                "OK");
            return;
        }

        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Performance Advisor",
            Loc.T($"Cena criada:\n{scenePath}\n\n" +
                  $"Configuração:\n• {description}\n\n" +
                  "Próximos passos:\n" +
                  "1. Aperte Play ▶️\n" +
                  "2. Abra Tools → Performance Advisor → Open Dashboard\n" +
                  "3. Aba Monitor — clique em Monitorar e observe os semáforos\n" +
                  "4. Stop ⏹️ → aba Snapshots → Analisar com IA",
                  $"Scene created:\n{scenePath}\n\n" +
                  $"Setup:\n• {description}\n\n" +
                  "Next steps:\n" +
                  "1. Press Play ▶️\n" +
                  "2. Open Tools → Performance Advisor → Open Dashboard\n" +
                  "3. Monitor tab — click Monitor and watch the indicators\n" +
                  "4. Stop ⏹️ → Snapshots tab → Analyze with AI",
                  $"Escena creada:\n{scenePath}\n\n" +
                  $"Configuración:\n• {description}\n\n" +
                  "Próximos pasos:\n" +
                  "1. Pulsa Play ▶️\n" +
                  "2. Abre Tools → Performance Advisor → Open Dashboard\n" +
                  "3. Pestaña Monitor: haz clic en Monitorear y observa los semáforos\n" +
                  "4. Stop ⏹️ → pestaña Snapshots → Analizar con IA"),
            "OK");
    }

    private static GameObject LoadOrCreateCubePrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(CUBE_PREFAB);
        if (existing != null) return existing;

        // Build a temporary cube in the scene, save it as a prefab, then destroy the temp.
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var prefab = PrefabUtility.SaveAsPrefabAsset(temp, CUBE_PREFAB);
        Object.DestroyImmediate(temp);

        if (prefab == null)
            Debug.LogError($"[StressSceneBuilder] Falha ao criar prefab em {CUBE_PREFAB}");

        return prefab;
    }

    private static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath)) return;

        // CreateFolder requires (parent, newFolderName) — split the path.
        int    slash  = assetPath.LastIndexOf('/');
        string parent = slash > 0 ? assetPath.Substring(0, slash) : "Assets";
        string name   = slash > 0 ? assetPath.Substring(slash + 1) : assetPath;

        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
}
