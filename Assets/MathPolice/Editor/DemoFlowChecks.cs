using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MathPolice.Editor
{
    public static class DemoFlowChecks
    {
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        public static string Status { get; private set; } = "Not run";
        [MenuItem("Math Police/Run menu and presentation checks")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            if (Status == "Running") throw new InvalidOperationException("Checks already running.");
            var d = UnityEngine.Object.FindAnyObjectByType<MathPoliceDemo>();
            d.StartCoroutine(Check(d));
        }
        static IEnumerator Check(MathPoliceDemo d)
        {
            Status = "Running";
            var original = d.CaptureState(); string prefs = JsonUtility.ToJson(d.Preferences);
            bool wasMenu = d.IsMainMenu, background = Application.runInBackground;
            string directory = d.ValidationSaveDirectory; var configuredLibrary = d.caseFiles;
            // Keep these original interaction checks on their stable eight-line tutorial fixture.
            var library = new[] { AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/MathPolice/Cases/case_001.json") };
            d.caseFiles = library;
            var mouse = InputSystem.AddDevice<Mouse>("MathPoliceFlowCheck");
            bool passed = false;
            Application.runInBackground = true;
            try
            {
                yield return Settled(d);
                Invoke(d, "ShowMainMenuImmediate");
                Queue(mouse, new Vector2(320, 380), false); yield return new WaitForSecondsRealtime(.25f);
                Require(d.LastEffect == "button1" && ((float[])Get(d, "menuScales"))[0] > 1, "menu hover sound and enlargement");
                int serial = d.EffectSerial; yield return new WaitForSecondsRealtime(.2f);
                Require(serial == d.EffectSerial, "hover plays only on entry");
                Queue(mouse, new Vector2(320, 380), true); yield return new WaitForSecondsRealtime(.12f);
                Require(((float[])Get(d, "menuScales"))[0] < 1, "menu press scale");
                Queue(mouse, new Vector2(320, 380), false); yield return new WaitForSecondsRealtime(.25f);
                Require(d.LastEffect == "button2" && d.TransitionBusy && (float)Get(d, "blackFade") > 0, "new game click and fade");
                yield return Settled(d);
                Require(!d.IsMainMenu && d.DialogueIndex == 0 && d.CurrentMusic == "game_normal", "new game enters tutorial station");
                Require(d.PortraitIds[0].StartsWith("boy/") && d.PortraitIds[1].StartsWith("girl/") && d.PortraitIds[0] != d.PortraitIds[2], "gender pools and nonrepeating initial portraits");
                Require(d.Case.Solve().SequenceEqual(new[] { 0 }), "unique tutorial culprit");
                yield return Click(mouse, 750, 740); Require(d.LastEffect == "button2", "dialogue click audio");
                while (d.DialogueIndex < 3) { d.AdvanceDialogue(); yield return null; }
                Set(d, "characters", 10000f); d.AdvanceDialogue(); yield return new WaitForSecondsRealtime(.16f);
                Require(d.TransitionBusy && (float)Get(d, "panelAlpha") < 1 && (float)Get(d, "actorAlpha") < 1, "enter interrogation fades actors and panel");
                yield return Settled(d);
                Require(d.DialogueIndex == 4 && d.InInterrogation && d.CurrentMusic == "game_puzzle", "interrogation background and music");
                Set(d, "characters", 10000f); d.AdvanceDialogue(); yield return new WaitForSecondsRealtime(.12f);
                Require(d.TransitionBusy && Mathf.Approximately((float)Get(d, "panelAlpha"), 1) && (float)Get(d, "actorAlpha") < 1, "suspect handoff fades portrait only");
                yield return Settled(d);
                d.SkipDialogue(); yield return Until(() => d.Investigating && !d.TransitionBusy, 10);
                Require(d.HistoryCount == 8 && d.InInterrogation, "skip retains testimony history and interrogation scene");
                Queue(mouse, d.SuspectRect(1).center, false); yield return new WaitForSecondsRealtime(.15f);
                Require(d.LastEffect == "button2" && (int)Get(d, "suspectHovered") == 1, "suspect alpha hover and sound");
                yield return Click(mouse, d.SuspectRect(1).center);
                Require(d.SelectedSuspect == 1, "portrait selection");
                Queue(mouse, new Vector2(25, 500), false); yield return null; yield return null;
                Require(d.SelectedSuspect == 1, "selection persists after pointer leaves");
                yield return Click(mouse, 1100, 800); Require(!d.Solved && d.LastEffect == "wrong", "wrong verdict audio");
                d.OpenBook(1); Require(!d.NotesTable, "sketch is default");
                yield return Drag(mouse, new Vector2(930, 420), new Vector2(1170, 580));
                int ink = InkPixels(d); Require(ink > 0, "right page drawing input");
                yield return Click(mouse, 970, 748); Require(d.NotesTable, "switch to table");
                yield return Click(mouse, 1060, 370); yield return Click(mouse, 1154, 370);
                Require(d.TruthMarks[0] == 2, "mutually exclusive truth marks");
                yield return Click(mouse, 1250, 370); Require(d.CulpritMarks[0], "culprit mark");
                yield return Click(mouse, 970, 748); Require(!d.NotesTable && InkPixels(d) == ink, "sketch preserved across tabs");
                yield return Click(mouse, 1040, 235);
                yield return Drag(mouse, new Vector2(1000, 467), new Vector2(1080, 520));
                Require(InkPixels(d) < ink, "right page eraser");
                ink = InkPixels(d); yield return Click(mouse, 970, 748);
                Require(d.TruthMarks[0] == 2 && d.CulpritMarks[0], "table preserved across tabs");
                d.OpenSettings(); Set(d, "settingsTab", 2);
                bool full = d.Preferences.fullscreen; yield return Click(mouse, 1200, 354);
                Require(d.Preferences.fullscreen != full, "fullscreen toggle input");
                yield return new WaitForSecondsRealtime(.3f);
                Require(Mathf.Approximately((float)Get(d, "fullscreenBlend"), d.Preferences.fullscreen ? 1 : 0), "fullscreen knob animation completes");
                Invoke(d, "CancelSettings"); Require(d.Preferences.fullscreen == full, "cancel restores fullscreen preference");
                d.ValidationSaveDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/MathPoliceFlowChecks-" + Guid.NewGuid().ToString("N")));
                Invoke(d, "ReadSlots");
                d.OpenSettings(); Set(d, "settingsTab", 4);
                yield return Click(mouse, 700, 425); yield return new WaitForSecondsRealtime(.4f);
                string path = Path.Combine(d.ValidationSaveDirectory, "slot_1.json");
                Require(File.Exists(path), "disk save via slot button");
                string before = File.ReadAllText(path); var saved = JsonUtility.FromJson<DemoSave>(before);
                Require(saved.version == 3 && saved.portraitIds.SequenceEqual(d.PortraitIds) && !string.IsNullOrEmpty(saved.thumbnailPng), "save portraits and thumbnail");
                yield return Click(mouse, 700, 425); Require((string)Get(d, "confirmation") == "overwrite", "overwrite confirmation");
                yield return Click(mouse, 650, 500); Require(File.ReadAllText(path) == before, "overwrite No preserves file");
                yield return Click(mouse, 700, 425); yield return Click(mouse, 930, 500); yield return new WaitForSecondsRealtime(.4f);
                Require((string)Get(d, "confirmation") == "", "overwrite Yes");
                d.ReturnToMainMenu(); yield return Settled(d);
                Require(d.IsMainMenu && d.CurrentMusic == "start", "return menu and music");
                d.OpenSettings(); Require((int)Get(d, "settingsTab") < 4, "menu settings omit in-game actions");
                yield return Click(mouse, 410, 501); Require((int)Get(d, "settingsTab") < 4 && d.Overlay == "settings", "no hidden menu save action");
                Invoke(d, "CancelSettings");
                yield return Click(mouse, 320, 470); yield return new WaitForSecondsRealtime(.2f);
                Require(d.IsMainMenu && d.Overlay == "load", "standalone load screen");
                yield return Click(mouse, 600, 410); yield return new WaitForSecondsRealtime(.12f);
                Require(d.TransitionBusy && (float)Get(d, "blackFade") > 0, "load uses black fade");
                yield return Settled(d);
                Require(!d.IsMainMenu && d.Investigating && d.InInterrogation && d.PortraitIds.SequenceEqual(saved.portraitIds) &&
                    d.TruthMarks[0] == 2 && d.CulpritMarks[0] && InkPixels(d) == ink && d.NotesTable, "load restores portraits, room, sketch and marks");
                var invalid = d.CaptureState(); invalid.dialogueIndex = 999; bool rejected = false;
                try { d.RestoreState(invalid); } catch (ArgumentException) { rejected = true; }
                Require(rejected && d.Investigating, "corrupt save rejected before replacing progress");
                var legacy = JsonUtility.FromJson<DemoSave>(before); legacy.version = 1; legacy.portraitIds = null;
                d.RestoreState(legacy); Require(d.Investigating && d.PortraitIds.Length == 3, "legacy save migration");
                d.RestoreState(saved);
                yield return Click(mouse, d.SuspectRect(0).center); yield return Click(mouse, 1100, 800);
                Require(d.Solved && d.LastEffect == "correct", "correct verdict audio");
                // A larger data-driven cast must not shrink beyond readability or overlap the footer.
                var many = JsonUtility.FromJson<CaseDefinition>(library[0].text); many.id = "validation_9"; many.suspectCount = 9;
                many.suspects = Enumerable.Range(0, 9).Select(i => new SuspectDefinition { gender = i % 2 == 0 ? "male" : "female",
                    name = new LocalizedText { zh = "嫌疑人" + i, en = "Suspect " + i }, testimony = new LocalizedText { zh = "我没有拿走奖章。", en = "I did not take the medal." } }).ToArray();
                d.caseFiles = new[] { new TextAsset(JsonUtility.ToJson(many)) }; d.StartCase(0); d.SkipDialogue();
                yield return Until(() => d.Investigating && !d.TransitionBusy, 15);
                for (int i = 0; i < 5; i++) { Rect r = d.SuspectRect(i); Require(r.width >= 240 && r.xMin >= 0 && r.xMax <= 1600 && r.yMax < 777, "adaptive portrait bounds"); }
                yield return Click(mouse, 830, 800); Require((int)Get(d, "lineupPage") == 1, "large cast pagination");
                Require(d.SuspectRect(5).xMin >= 0 && d.SuspectRect(8).xMax <= 1600, "second page cast bounds");
                passed = true; Status = "PASS";
                Debug.Log("DEMO_FLOW_PASS: menu hover/press/audio, fades, actor handoff, genders, portrait selection, music, notebook preservation, fullscreen toggle, save/load/overwrite, legacy saves, verdict audio, 9-suspect paging.");
            }
            finally
            {
                if (!passed) Status = "FAIL (see Console)";
                InputSystem.RemoveDevice(mouse); d.caseFiles = configuredLibrary; d.ValidationSaveDirectory = directory;
                d.RestoreState(original); JsonUtility.FromJsonOverwrite(prefs, d.Preferences);
                if (wasMenu) Invoke(d, "ShowMainMenuImmediate");
                Application.runInBackground = background;
            }
        }
        static IEnumerator Settled(MathPoliceDemo d) { yield return Until(() => !d.TransitionBusy, 8); yield return null; }
        static IEnumerator Until(Func<bool> predicate, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!predicate()) { if (Time.realtimeSinceStartup > deadline) throw new Exception("Demo flow timed out."); yield return null; }
        }
        static void Queue(Mouse mouse, Vector2 point, bool held)
        {
            float scale = Mathf.Min(Screen.width / 1600f, Screen.height / 900f);
            var pixel = new Vector2((Screen.width - 1600 * scale) / 2 + point.x * scale, Screen.height - ((Screen.height - 900 * scale) / 2 + point.y * scale));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pixel }.WithButton(MouseButton.Left, held));
        }
        static IEnumerator Click(Mouse mouse, float x, float y) => Click(mouse, new Vector2(x, y));
        static IEnumerator Click(Mouse mouse, Vector2 p)
        {
            Queue(mouse, p, true); yield return null; yield return null;
            Queue(mouse, p, false); yield return null; yield return null;
        }
        static IEnumerator Drag(Mouse mouse, Vector2 a, Vector2 b)
        {
            Queue(mouse, a, true); yield return null; yield return null;
            for (int i = 1; i <= 8; i++) { Queue(mouse, Vector2.Lerp(a, b, i / 8f), true); yield return null; yield return null; }
            Queue(mouse, b, false); yield return null; yield return null;
        }
        static object Get(MathPoliceDemo d, string name) => d.GetType().GetField(name, Flags).GetValue(d);
        static void Set(MathPoliceDemo d, string name, object value) => d.GetType().GetField(name, Flags).SetValue(d, value);
        static void Invoke(MathPoliceDemo d, string name) => d.GetType().GetMethod(name, Flags).Invoke(d, null);
        static int InkPixels(MathPoliceDemo d) => ((Texture2D)Get(d, "drawing")).GetPixels32().Count(p => p.a > 0);
        static void Require(bool condition, string label) { if (!condition) throw new Exception("Demo flow failed: " + label); }
    }
}
