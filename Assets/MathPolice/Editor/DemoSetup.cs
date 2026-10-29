using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MathPolice.Editor
{
    public static class DemoSetup
    {
        [MenuItem("Math Police/Set up demo in current scene")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before setup.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.name != "SampleScene") throw new InvalidOperationException("Open SampleScene first.");
            var demo = UnityEngine.Object.FindAnyObjectByType<MathPoliceDemo>();
            if (!demo)
            {
                var root = new GameObject("MathPolice_Demo");
                Undo.RegisterCreatedObjectUndo(root, "Create Math Police demo");
                demo = Undo.AddComponent<MathPoliceDemo>(root);
            }
            Undo.RecordObject(demo, "Configure demo assets");
            if (demo.caseFiles == null || demo.caseFiles.Length == 0)
                demo.caseFiles = Enumerable.Range(1, 5).Select(i => AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/MathPolice/Cases/chapter_{i:00}.json")).Where(t => t).ToArray();
            if (demo.legacyCaseFiles == null || demo.legacyCaseFiles.Length == 0)
                demo.legacyCaseFiles = new[] { AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/MathPolice/Cases/case_001.json") };
            demo.background = Texture("scene2.png");
            demo.player = Texture("player_thinking.png");
            demo.officer = Texture("police.png");
            demo.happyOfficer = Texture("police_laugh.png");
            demo.dialogueFrame = Texture("ui/box-body (1).png");
            demo.notebook = Texture("ui/opened-journal-section.png");
            demo.settingsIcon = Texture("ui/Vector.png");
            demo.recordIcon = Texture("ui/icon-container-2.png");
            demo.menuBackground = Texture("start.jpg");
            demo.titleArt = Texture("titile.png");
            demo.menuIdle = Texture("ui/button-back.png");
            demo.menuHover = Texture("ui/button-continue.png");
            demo.loadFrame = Texture("ui/icon-bg (1).png");
            demo.toggleOn = Texture("ui/Toggle.png");
            demo.toggleOff = Texture("ui/Toggle (1).png");
            demo.interrogationBackground = Texture("scene1.png");
            demo.introComic = Texture("tutorial-intro.png");
            demo.tutorialArrow = Texture("ui/Vector (9).png");
            demo.tutorialHintIcon = Texture("ui/Vector (14).png");
            demo.comicBackground = Texture("open/background.png");
            demo.comicPanels = Enumerable.Range(1, 6).Select(i => Texture($"open/{i}.png")).ToArray();
            demo.comicElements = new[] { "yinfu", "yinfu2", "wenhao", "imagine", "imagine2", "sticker" }.Select(n => Texture("open/" + n + ".png")).ToArray();
            demo.playerNormal = Texture("player_normal.png");
            demo.playerSmile = Texture("player_smile.png");
            demo.officerSmile = Texture("police_smile.png");
            demo.officerSmile2 = Texture("police_smile2.png");
            demo.boyPortraits = Portraits("boy");
            demo.girlPortraits = Portraits("girl");
            demo.menuMusic = Audio("start"); demo.stationMusic = Audio("game_normal"); demo.puzzleMusic = Audio("game_puzzle");
            demo.buttonHover = Audio("button1"); demo.buttonClick = Audio("button2");
            demo.correctSound = Audio("correct"); demo.wrongSound = Audio("wrong");
            EditorUtility.SetDirty(demo);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!EditorBuildSettings.scenes.Any(s => s.path == scene.path))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(scene.path, true) }).ToArray();
            Debug.Log("Math Police demo installed. Press Play in SampleScene.");
        }
        static Texture2D Texture(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/" + path);
        static AudioClip Audio(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/music/" + name + ".mp3");
        static Texture2D[] Portraits(string gender) => AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art/suspect/" + gender })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal).Select(AssetDatabase.LoadAssetAtPath<Texture2D>).ToArray();

        [MenuItem("Math Police/Validate case library")]
        public static void ValidateCases()
        {
            var demo = UnityEngine.Object.FindAnyObjectByType<MathPoliceDemo>();
            if (!demo || demo.caseFiles == null) throw new InvalidOperationException("Demo is not configured.");
            foreach (var file in demo.caseFiles)
            {
                var data = JsonUtility.FromJson<CaseDefinition>(file.text); data.Validate();
                Debug.Log(file.name + ": valid, " + data.suspectCount + " suspects; culprit index " + data.culpritIndex);
            }
        }

        // Runs against the actual runtime model and restores its state afterward; never touches player save slots.
        [MenuItem("Math Police/Run runtime smoke checks")]
        public static void SmokeChecks() => DemoFlowChecks.Run();
    }
}

