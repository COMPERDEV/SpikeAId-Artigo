using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PerformanceAdvisor
{
/// <summary>
/// Constrói uma "cena cidade fake" a partir de QUALQUER pasta de prefabs.
/// Pega prefabs variados, espalha num grid, adiciona luzes com sombra, e
/// gera uma cena pesada de verdade — varia mesh, material e shader, o que
/// derrota o SRP Batcher e força Draw Calls altos em URP.
/// </summary>
public static class CitySceneBuilder
{
    private const string SCENE_PATH = "Assets/PerformanceAdvisor/StressScenes/StressTestCity.unity";

    [MenuItem("Tools/Performance Advisor/Build Fake City from Folder", false, 104)]
    public static void BuildCityFromFolder()
    {
        // 1) Pede ao usuário a pasta com os prefabs do asset pack
        string folder = EditorUtility.OpenFolderPanel(
            Loc.T("Selecione a pasta com os prefabs do asset pack", "Select the folder with the asset pack's prefabs",
                  "Selecciona la carpeta con los prefabs del asset pack"),
            "Assets",
            "");

        if (string.IsNullOrEmpty(folder))
        {
            return; // usuário cancelou
        }

        // 2) Converte caminho absoluto pra relativo ao projeto
        if (!folder.StartsWith(Application.dataPath))
        {
            EditorUtility.DisplayDialog(
                "Performance Advisor",
                Loc.T("A pasta precisa estar dentro do projeto Unity (Assets/).",
                      "The folder must be inside the Unity project (Assets/).",
                      "La carpeta tiene que estar dentro del proyecto Unity (Assets/)."),
                "OK");
            return;
        }
        string relativePath = "Assets" + folder.Substring(Application.dataPath.Length);

        // 3) Procura todos os prefabs com Renderer dentro da pasta
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { relativePath });
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Performance Advisor",
                Loc.T($"Nenhum prefab encontrado em:\n{relativePath}\n\n" +
                      "Importe um asset pack (POLYGON Starter, Kenney City Kit, etc.) e tente de novo.",
                      $"No prefab found in:\n{relativePath}\n\n" +
                      "Import an asset pack (POLYGON Starter, Kenney City Kit, etc.) and try again.",
                      $"No se encontró ningún prefab en:\n{relativePath}\n\n" +
                      "Importa un asset pack (POLYGON Starter, Kenney City Kit, etc.) y vuelve a intentarlo."),
                "OK");
            return;
        }

        var prefabs = new List<GameObject>(guids.Length);
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab != null && prefab.GetComponentInChildren<Renderer>(true) != null)
            {
                prefabs.Add(prefab);
            }
        }

        if (prefabs.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Performance Advisor",
                Loc.T("A pasta tem prefabs, mas nenhum deles tem Renderer.\n\n" +
                      "Verifique se importou os modelos 3D corretamente.",
                      "The folder has prefabs, but none of them has a Renderer.\n\n" +
                      "Check that the 3D models were imported correctly.",
                      "La carpeta tiene prefabs, pero ninguno tiene Renderer.\n\n" +
                      "Comprueba que importaste correctamente los modelos 3D."),
                "OK");
            return;
        }

        // 4) Confirma e cria
        bool ok = EditorUtility.DisplayDialog(
            "Build Fake City",
            Loc.T($"Encontrei {prefabs.Count} prefabs em:\n{relativePath}\n\n" +
                  "Vou montar uma cidade de 15×15 (225 objetos) num grid, com 4 luzes e sombras ativas.\n\n" +
                  "A cena atual (se aberta) será fechada — salve antes se importar.",
                  $"Found {prefabs.Count} prefabs in:\n{relativePath}\n\n" +
                  "I will build a 15×15 city (225 objects) on a grid, with 4 lights and shadows on.\n\n" +
                  "The current scene (if open) will be closed — save it first if it matters.",
                  $"Encontré {prefabs.Count} prefabs en:\n{relativePath}\n\n" +
                  "Voy a montar una ciudad de 15×15 (225 objetos) en una cuadrícula, con 4 luces y sombras activas.\n\n" +
                  "La escena actual (si está abierta) se cerrará: guárdala antes si te importa."),
            Loc.T("Construir", "Build", "Construir"),
            Loc.T("Cancelar", "Cancel", "Cancelar"));

        if (!ok) return;

        BuildScene(prefabs.ToArray());
    }

    private static void BuildScene(GameObject[] prefabs)
    {
        // MOTIVO: tudo o que o plugin gera fica DENTRO da pasta dele. Criar pastas soltas
        // em Assets/ faz o plugin espalhar arquivos no projeto de quem o importa — foi a
        // crítica do orientador ao importar o package e encontrar duas árvores.
        if (!AssetDatabase.IsValidFolder("Assets/PerformanceAdvisor/StressScenes"))
        {
            AssetDatabase.CreateFolder("Assets/PerformanceAdvisor", "StressScenes");
        }

        // Confirma se quer salvar a cena atual
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        // 1) Cena nova com lighting + camera default
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // 2) Ajusta a câmera pra ver a cidade de cima/lado
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 35f, -55f);
            cam.transform.rotation = Quaternion.Euler(28f, 0f, 0f);
            cam.farClipPlane       = 500f;
            cam.backgroundColor    = new Color(0.4f, 0.55f, 0.7f, 1f); // céu claro
            cam.clearFlags         = CameraClearFlags.SolidColor;
        }

        // 3) Chão grande
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(20f, 1f, 20f);

        // 4) 4 luzes com sombra ativa — força extra render passes
        // Importante: cada luz com shadows = +1 pass por objeto que recebe sombra
        // É outro vetor que infla Draw Calls em jogos reais.
        AddPointLight(new Vector3( 25f, 8f,  25f), Color.white,        2f);
        AddPointLight(new Vector3(-25f, 8f,  25f), new Color(1, .8f, .6f), 2f);
        AddPointLight(new Vector3( 25f, 8f, -25f), new Color(.6f, .8f, 1f), 2f);
        AddPointLight(new Vector3(-25f, 8f, -25f), new Color(1, .6f, .8f), 2f);

        // 5) Spawn da cidade — grid 15x15 com prefabs aleatórios
        var cityRoot = new GameObject("CityRoot").transform;
        const int GRID = 15;
        const float SPACING = 4f;
        var rng = new System.Random(42); // determinístico — mesma cena toda vez

        for (int x = -GRID / 2; x < GRID / 2; x++)
        {
            for (int z = -GRID / 2; z < GRID / 2; z++)
            {
                var prefab = prefabs[rng.Next(prefabs.Length)];
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                if (instance == null) continue;

                instance.transform.SetParent(cityRoot);
                instance.transform.position = new Vector3(x * SPACING, 0f, z * SPACING);
                instance.transform.rotation = Quaternion.Euler(0f, rng.Next(0, 360), 0f);

                // Variação de escala pra variar a silhueta da cidade
                float scale = 0.8f + (float)rng.NextDouble() * 0.6f;
                instance.transform.localScale = Vector3.one * scale;
            }
        }

        // 6) Salva
        bool saved = EditorSceneManager.SaveScene(scene, SCENE_PATH);

        if (!saved)
        {
            EditorUtility.DisplayDialog(
                "Performance Advisor",
                Loc.T($"Falha ao salvar a cena em {SCENE_PATH}", $"Failed to save the scene to {SCENE_PATH}",
                      $"Error al guardar la escena en {SCENE_PATH}"),
                "OK");
            return;
        }

        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Performance Advisor",
            Loc.T($"Cidade fake criada em:\n{SCENE_PATH}\n\n" +
                  "Configuração:\n" +
                  "• 225 prefabs aleatórios em grid 15×15\n" +
                  "• 4 luzes com sombras\n" +
                  $"• {prefabs.Length} prefabs distintos no pool\n\n" +
                  "Próximos passos:\n" +
                  "1. Aperte Play ▶️\n" +
                  "2. Abra Tools → Performance Advisor → Open Dashboard\n" +
                  "3. Aba Monitor — clique em Monitorar e observe os draw calls subirem\n" +
                  "4. Stop ⏹️ → aba Snapshots → Analisar com IA",
                  $"Fake city created at:\n{SCENE_PATH}\n\n" +
                  "Setup:\n" +
                  "• 225 random prefabs on a 15×15 grid\n" +
                  "• 4 lights with shadows\n" +
                  $"• {prefabs.Length} distinct prefabs in the pool\n\n" +
                  "Next steps:\n" +
                  "1. Press Play ▶️\n" +
                  "2. Open Tools → Performance Advisor → Open Dashboard\n" +
                  "3. Monitor tab — click Monitor and watch the draw calls go up\n" +
                  "4. Stop ⏹️ → Snapshots tab → Analyze with AI",
                  $"Ciudad falsa creada en:\n{SCENE_PATH}\n\n" +
                  "Configuración:\n" +
                  "• 225 prefabs aleatorios en una cuadrícula de 15×15\n" +
                  "• 4 luces con sombras\n" +
                  $"• {prefabs.Length} prefabs distintos en el pool\n\n" +
                  "Próximos pasos:\n" +
                  "1. Pulsa Play ▶️\n" +
                  "2. Abre Tools → Performance Advisor → Open Dashboard\n" +
                  "3. Pestaña Monitor: haz clic en Monitorear y observa cómo suben los draw calls\n" +
                  "4. Stop ⏹️ → pestaña Snapshots → Analizar con IA"),
            "OK");
    }

    private static void AddPointLight(Vector3 position, Color color, float intensity)
    {
        var go = new GameObject("CityLight");
        go.transform.position = position;
        var light = go.AddComponent<Light>();
        light.type      = LightType.Point;
        light.color     = color;
        light.intensity = intensity;
        light.range     = 30f;
        light.shadows   = LightShadows.Soft;
    }
}
}
