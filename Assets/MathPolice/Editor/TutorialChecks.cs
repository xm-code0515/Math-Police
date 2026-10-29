using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MathPolice.Editor
{
    public static class TutorialChecks
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        public static string Status { get; private set; } = "Not run";
        [MenuItem("Math Police/Run tutorial and interaction fix checks")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || Status == "Running") throw new InvalidOperationException("Enter Play Mode, then run once.");
            var d = UnityEngine.Object.FindAnyObjectByType<MathPoliceDemo>(); d.StartCoroutine(Check(d));
        }
        static IEnumerator Check(MathPoliceDemo d)
        {
            Status = "Running";
            var original = d.CaptureState(); bool menu = d.IsMainMenu, background = Application.runInBackground;
            var settings = InputSystem.settings; var oldMode = settings.editorInputBehaviorInPlayMode; var oldBackground = settings.backgroundBehavior;
            var mouse = InputSystem.AddDevice<Mouse>("TutorialChecks"); bool passed = false;
            try
            {
                Application.runInBackground = true;
                settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                Invoke(d, "ShowMainMenuImmediate"); yield return null;
                d.StartNewGame(); yield return Wait(() => !d.TransitionBusy);
                Require((bool)Get(d, "introComicVisible"), "new game shows comic");
                yield return new WaitForSecondsRealtime(.2f);
                Require(d.DialogueIndex == 0 && (float)Get(d, "characters") == 0, "comic pauses dialogue");
                yield return Click(mouse, 800, 450); Require((int)Get(d, "comicPanel") == 0, "comic ignores clicks during reveal");
                for (int panel = 0; panel < 6; panel++)
                {
                    yield return new WaitForSecondsRealtime(2.1f);
                    Require((int)Get(d, "comicPanel") == panel, "comic panel order");
                    yield return Click(mouse, 800, 450);
                }
                Require(d.TransitionBusy, "last comic click starts fade");
                yield return Wait(() => !d.TransitionBusy);
                Require(!(bool)Get(d, "introComicVisible") && d.DialogueIndex == 0, "comic enters first dialogue");
                // The real on-screen control must cancel skip even during a portrait handoff.
                d.StartCase(1); var state = d.CaptureState();
                state.dialogueIndex = Array.FindIndex(d.Case.script, l => l.stage == "interrogation"); state.revealedCharacters = 10000;
                d.RestoreState(state); yield return null;
                yield return Click(mouse, 1018, 866); Require((bool)Get(d, "skipping"), "skip button starts");
                yield return Wait(() => d.TransitionBusy);
                yield return Click(mouse, 1018, 866); Require(!(bool)Get(d, "skipping"), "skip cancels during transition");
                yield return Wait(() => !d.TransitionBusy);
                int stoppedAt = d.DialogueIndex; yield return new WaitForSecondsRealtime(.35f);
                Require(d.DialogueIndex == stoppedAt, "cancel stays stopped");
                // First orange line waits until its full text is visible, and blocks click-through.
                d.StartCase(0); state = d.CaptureState();
                int key = Array.FindIndex(d.Case.script, l => l.role == "suspect" && l.text.zh.Contains(d.Case.suspects[0].testimony.zh));
                state.dialogueIndex = key; state.revealedCharacters = 0; d.RestoreState(state);
                yield return null; Require(Step(d) == 0, "no tutorial before text completes");
                Set(d, "characters", 10000f); yield return Wait(() => Step(d) == 1 && !(bool)Get(d, "tutorialTransition"));
                var mid = d.CaptureState(); d.RestoreState(mid); yield return null;
                Require(Step(d) == 1 && (float)Get(d, "tutorialDim") == 1, "save restores active key lesson");
                yield return Click(mouse, 800, 700); yield return TutorialSettled(d);
                Require(Step(d) == 0 && d.DialogueIndex == key, "dismiss does not advance dialogue");
                Require((bool)Get(d, "keyTutorialSeen"), "key lesson remembered");
                state = d.CaptureState(); state.dialogueIndex = d.Case.script.Length - 1; state.revealedCharacters = 10000;
                d.RestoreState(state); d.AdvanceDialogue();
                yield return Wait(() => Step(d) == 2 && !(bool)Get(d, "tutorialTransition"));
                yield return Click(mouse, 800, 400); yield return TutorialSettled(d);
                Require(Step(d) == 3 && d.SelectedSuspect == -1 && (float)Get(d, "tutorialDim") == 1, "lineup lesson advances without selecting");
                yield return Click(mouse, 1100, 800); Require(Step(d) == 3, "wrong target is blocked");
                yield return Click(mouse, 460, 801); yield return TutorialSettled(d);
                Require(Step(d) == 4 && d.Overlay == "book" && !d.NotesTable, "open notebook target");
                yield return Click(mouse, 800, 450); yield return TutorialSettled(d); Require(Step(d) == 5, "testimonies to sketch");
                yield return Click(mouse, 800, 450); yield return TutorialSettled(d); Require(Step(d) == 6, "sketch to switch");
                yield return Click(mouse, 970, 748); yield return TutorialSettled(d);
                Require(Step(d) == 7 && d.NotesTable, "table toggle target");
                mid = d.CaptureState(); d.RestoreState(mid); yield return null;
                Require(Step(d) == 7 && d.Overlay == "book" && d.NotesTable, "save restores notebook lesson");
                yield return Click(mouse, 217, 220); yield return TutorialSettled(d);
                Require(Step(d) == 8 && (int)Get(d, "bookTab") == 0, "records target");
                yield return Click(mouse, 1310, 173); yield return TutorialSettled(d);
                Require(Step(d) == 9 && d.Overlay == "", "close target to shortcut");
                yield return Click(mouse, 800, 450); yield return TutorialSettled(d);
                Require(Step(d) == 0 && (float)Get(d, "tutorialDim") == 0 && (bool)Get(d, "notebookTutorialSeen"), "tutorial fully completed");
                mid = d.CaptureState(); d.RestoreState(mid); yield return null;
                Require(Step(d) == 0, "completed lesson stays completed on load");
                // A solved lineup can be reviewed but never edited, including old inconsistent saves.
                d.SubmitVerdict(d.Case.culpritIndex); d.OpenBook(0); d.CloseOverlay();
                int correct = d.SelectedSuspect, effects = d.EffectSerial;
                yield return Click(mouse, d.SuspectRect(1).center.x, d.SuspectRect(1).center.y);
                Require(d.SelectedSuspect == correct && d.EffectSerial == effects && (int)Get(d, "suspectHovered") == -1, "solved portraits ignore hover and click");
                yield return Click(mouse, 1100, 800);
                Require(d.Overlay == "result" && d.SelectedSuspect == correct && d.LastEffect != "wrong", "review only opens explanation");
                mid = d.CaptureState(); mid.version = 2; mid.selectedSuspect = 1; d.RestoreState(mid);
                Require(d.SelectedSuspect == d.Case.culpritIndex && d.Solved, "old solved save normalizes selection");
                d.StartCase(1); Require(Step(d) == 0 && !(bool)Get(d, "introComicVisible"), "later chapters have no comic or tutorial");
                passed = true; Status = "PASS";
                Debug.Log("TUTORIAL_CHECK_PASS: comic fade/click, skip cancellation during transition, all nine lesson steps, input blocking, tutorial save restore, solved lineup lock and review.");
            }
            finally
            {
                if (!passed) Status = "FAIL (see Console)";
                InputSystem.RemoveDevice(mouse); settings.editorInputBehaviorInPlayMode = oldMode; settings.backgroundBehavior = oldBackground;
                d.RestoreState(original); if (menu) Invoke(d, "ShowMainMenuImmediate"); Application.runInBackground = background;
            }
        }
        static IEnumerator TutorialSettled(MathPoliceDemo d) { yield return Wait(() => !(bool)Get(d, "tutorialTransition")); yield return null; }
        static IEnumerator Wait(Func<bool> ready)
        {
            float deadline = Time.realtimeSinceStartup + 12;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Require(ready(), "wait timed out"); yield return null;
        }
        static IEnumerator Click(Mouse mouse, float x, float y)
        {
            Queue(mouse, x, y, true); yield return null; yield return null;
            Queue(mouse, x, y, false); yield return null; yield return null;
        }
        static void Queue(Mouse mouse, float x, float y, bool held)
        {
            float scale = Mathf.Min(Screen.width / 1600f, Screen.height / 900f);
            var point = new Vector2((Screen.width - 1600 * scale) / 2 + x * scale, Screen.height - ((Screen.height - 900 * scale) / 2 + y * scale));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left, held));
        }
        static int Step(MathPoliceDemo d) => Convert.ToInt32(Get(d, "tutorialStep"));
        static object Get(MathPoliceDemo d, string name) => d.GetType().GetField(name, Flags).GetValue(d);
        static void Set(MathPoliceDemo d, string name, object value) => d.GetType().GetField(name, Flags).SetValue(d, value);
        static void Invoke(MathPoliceDemo d, string name) => d.GetType().GetMethod(name, Flags).Invoke(d, null);
        static void Require(bool condition, string message) { if (!condition) throw new Exception("Tutorial check failed: " + message); }
    }
}
