using System;
using System.Collections;
using UnityEngine;

namespace MathPolice
{
    public sealed partial class MathPoliceDemo
    {
        [Header("First case tutorial")]
        public Texture2D introComic, tutorialArrow, tutorialHintIcon, comicBackground;
        public Texture2D[] comicPanels, comicElements;
        int comicPanel;
        float comicPanelStarted;
        readonly float[] comicShownAt = new float[6];
        float ComicAge => Time.unscaledTime - comicPanelStarted;
        float ComicDuration => comicPanel == 0 ? 1.6f : comicPanel == 1 ? 1.2f : comicPanel == 4 ? 2f : .55f;
        void BeginComic()
        {
            introComicVisible = TutorialEnabled && (introComic || comicBackground);
            comicPanel = 0; comicPanelStarted = Time.unscaledTime + .35f;
            Array.Clear(comicShownAt, 0, comicShownAt.Length); comicShownAt[0] = comicPanelStarted;
        }
        void AdvanceComic()
        {
            if (ComicAge < ComicDuration || TransitionBusy) return;
            PlayEffect(buttonClick);
            if (comicPanel == 5) BeginScreenTransition(() => introComicVisible = false);
            else { comicPanel++; comicPanelStarted = Time.unscaledTime; comicShownAt[comicPanel] = comicPanelStarted; }
        }
        void DrawComic(Rect screen)
        {
            Box(screen, Color.white, 0);
            float scale = Mathf.Min(screen.width / 1536f, screen.height / 1024f);
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3((screen.width - 1536 * scale) / 2, (screen.height - 1024 * scale) / 2, 0), Quaternion.identity, Vector3.one * scale);
            if (comicBackground) Draw(comicBackground, new Rect(0, 0, 1536, 1024));
            for (int i = 0; i <= comicPanel; i++)
            {
                if (comicPanels == null || i >= comicPanels.Length) continue;
                float age = Time.unscaledTime - comicShownAt[i];
                Draw(comicPanels[i], new Rect(8 + i % 3 * 510, 8 + i / 3 * 510, 500, 500), Mathf.Clamp01(age / .5f));
                if (i == 0) { ComicElement(1, new Rect(270, 146, 9, 22), age, .65f); ComicElement(0, new Rect(286, 134, 6.5f, 15), age, 1.05f); }
                if (i == 1) ComicElement(2, new Rect(851, 66, 49, 83), age, .65f);
                if (i == 4) { ComicElement(3, new Rect(781, 752, 10, 8), age, .65f); ComicElement(4, new Rect(786, 736, 17, 14), age, 1.05f); ComicElement(5, new Rect(816, 568, 180, 212), age, 1.45f); }
            }
            GUI.matrix = matrix;
        }
        void ComicElement(int index, Rect r, float age, float delay)
        {
            if (comicElements != null && index < comicElements.Length) Draw(comicElements[index], r, Mathf.Clamp01((age - delay) / .4f));
        }
        enum TutorialStep { None, KeyStatement, ChooseSuspect, OpenNotes, Testimonies, Sketch, SwitchTable, Records, CloseNotes, NotebookShortcut }
        TutorialStep tutorialStep;
        bool keyTutorialSeen, notebookTutorialSeen, introComicVisible, tutorialTransition;
        float tutorialDim, tutorialDetail, keyReadyAt = -1;
        Rect dialogueTextRect;
        Coroutine tutorialRoutine;
        #if UNITY_EDITOR
        [NonSerialized] public bool SuppressTutorialsForValidation;
        #endif
        bool TutorialEnabled
        {
            get
            {
                #if UNITY_EDITOR
                if (SuppressTutorialsForValidation) return false;
                #endif
                return Case != null && Case.tutorial;
            }
        }
        bool TutorialBlocking => tutorialStep != TutorialStep.None || tutorialTransition || tutorialDim > .001f;
        string TutorialNotebookHint => L(
            "由于只有一个人说真话，可以分别假设三个人各自的供词的是真话时，其他人的供词是否有矛盾，使用表格可以快速勾选不同的情况，快去试试吧！",
            "Only one person tells the truth. Assume each person's statement is true in turn, then check the others for contradictions. Use the table to mark the different possibilities. Give it a try!");

        void ResetTutorialPresentation()
        {
            if (tutorialRoutine != null) StopCoroutine(tutorialRoutine);
            tutorialRoutine = null; tutorialStep = TutorialStep.None;
            tutorialDim = tutorialDetail = 0; tutorialTransition = introComicVisible = false; keyReadyAt = -1;
        }
        void ResetTutorial() { ResetTutorialPresentation(); keyTutorialSeen = notebookTutorialSeen = false; }

        bool HasKeyStatement(DialogueDefinition line)
        {
            if (line.role != "suspect") return false;
            foreach (var suspect in Case.suspects)
                if (line.text.zh.Contains(suspect.testimony.zh)) return true;
            return false;
        }
        bool TryBeginKeyTutorial()
        {
            if (!TutorialEnabled || keyTutorialSeen || Investigating || IsMainMenu || introComicVisible || TutorialBlocking) return false;
            var line = dialogue[DialogueIndex];
            if (!HasKeyStatement(line) || characters < line.text.Get(Preferences.english).Length) return false;
            if (keyReadyAt < 0) keyReadyAt = Time.unscaledTime + .3f;
            if (Time.unscaledTime >= keyReadyAt)
            {
                keyTutorialSeen = true;
                BeginTutorial(TutorialStep.KeyStatement);
            }
            return true;
        }
        void UpdateTutorial()
        {
            if (!TutorialEnabled || IsMainMenu || introComicVisible || TutorialBlocking || TransitionBusy || Overlay != "" || capturing) return;
            if (Investigating && !Solved && !notebookTutorialSeen) BeginTutorial(TutorialStep.ChooseSuspect);
        }
        void BeginTutorial(TutorialStep step)
        {
            auto = skipping = false; autoClock = skipClock = 0; pressedControl = null; drawingDrag = false;
            tutorialStep = step;
            tutorialRoutine = StartCoroutine(FadeTutorialIn());
        }
        IEnumerator FadeTutorialIn()
        {
            tutorialTransition = true;
            yield return Animate(.3f, t => { tutorialDim = t; tutorialDetail = t; });
            tutorialTransition = false; tutorialRoutine = null;
        }
        void AdvanceTutorial()
        {
            if (tutorialTransition || tutorialStep == TutorialStep.None) return;
            pressedControl = null;
            tutorialRoutine = StartCoroutine(ChangeTutorialStep());
        }
        IEnumerator ChangeTutorialStep()
        {
            tutorialTransition = true;
            bool finished = tutorialStep == TutorialStep.KeyStatement || tutorialStep == TutorialStep.NotebookShortcut;
            if (tutorialStep == TutorialStep.NotebookShortcut) notebookTutorialSeen = true;
            yield return Animate(.24f, t => { tutorialDetail = 1 - t; if (finished) tutorialDim = 1 - t; });
            if (finished) tutorialStep = TutorialStep.None;
            else
            {
                switch (tutorialStep)
                {
                    case TutorialStep.OpenNotes: OpenBook(1); notesTable = false; penOptions = false; testimonyScroll = Vector2.zero; break;
                    case TutorialStep.SwitchTable: if (!notesTable) ToggleNotes(); break;
                    case TutorialStep.Records: OpenBook(0); break;
                    case TutorialStep.CloseNotes: CloseOverlay(); break;
                }
                tutorialStep++;
                yield return Animate(.24f, t => tutorialDetail = t);
            }
            tutorialTransition = false; tutorialRoutine = null;
        }

        void RestoreTutorial(DemoSave save)
        {
            if (!TutorialEnabled) return;
            int firstKey = dialogue.FindIndex(HasKeyStatement);
            keyTutorialSeen = save.version >= 3 ? save.keyTutorialSeen : save.investigating || save.solved || (firstKey >= 0 && DialogueIndex > firstKey);
            notebookTutorialSeen = save.version >= 3 ? save.notebookTutorialSeen : save.solved;
            introComicVisible = save.version >= 3 && save.introComicVisible && !save.solved;
            if (save.solved) { keyTutorialSeen = notebookTutorialSeen = true; return; }
            if (save.version < 3 || save.tutorialStep == 0) return;
            tutorialStep = (TutorialStep)save.tutorialStep;
            tutorialDim = tutorialDetail = 1;
            if (tutorialStep >= TutorialStep.Testimonies && tutorialStep <= TutorialStep.Records)
            { OpenBook(1); notesTable = tutorialStep == TutorialStep.Records; }
            else if (tutorialStep == TutorialStep.CloseNotes) OpenBook(0);
        }

        Rect TutorialFocus()
        {
            switch (tutorialStep)
            {
                case TutorialStep.KeyStatement:
                    var line = dialogue[DialogueIndex]; string text = line.text.Get(Preferences.english);
                    foreach (var suspect in Case.suspects)
                    {
                        string key = suspect.testimony.Get(Preferences.english); int start = text.IndexOf(key, StringComparison.Ordinal);
                        if (start < 0) continue;
                        var style = new GUIStyle(TextStyle(Mathf.RoundToInt(Preferences.fontSize), Ink, TextAnchor.UpperLeft)) { fontStyle = FontStyle.Bold };
                        Vector2 a = style.GetCursorPixelPosition(dialogueTextRect, new GUIContent(text), start);
                        Vector2 b = style.GetCursorPixelPosition(dialogueTextRect, new GUIContent(text), start + key.Length);
                        bool wrapped = b.y > a.y + 1;
                        return new Rect((wrapped ? dialogueTextRect.x : a.x) - 7, a.y - 6,
                            (wrapped ? dialogueTextRect.width : b.x - a.x) + 7, b.y - a.y + style.lineHeight + 12);
                    }
                    return dialogueTextRect;
                case TutorialStep.ChooseSuspect: return new Rect(100, 322, 1400, 426);
                case TutorialStep.OpenNotes: return new Rect(342, 777, 235, 49);
                case TutorialStep.Testimonies: return new Rect(280, 211, 455, 326);
                case TutorialStep.Sketch: return new Rect(850, 160, 478, 558);
                case TutorialStep.SwitchTable: return new Rect(854, 727, 245, 43);
                case TutorialStep.Records: return new Rect(187, 191, 60, 64);
                case TutorialStep.CloseNotes: return new Rect(1287, 152, 49, 43);
                case TutorialStep.NotebookShortcut: return new Rect(120, 34, 66, 62);
                default: return new Rect(0, 0, 0, 0);
            }
        }
        string TutorialMessage()
        {
            switch (tutorialStep)
            {
                case TutorialStep.KeyStatement: return L("注意，犯人的关键供词为橙色，收集每一个嫌疑人的关键证词一定能推理出真正的犯人！", "Key statements appear in orange. Collect every suspect's key statement to work out who the real culprit is!");
                case TutorialStep.ChooseSuspect: return L("点击选择真正的犯人并提交给警长，漂亮的侦破案件吧！", "Select the real culprit and submit your judgment to the Chief. Let's solve this case!");
                case TutorialStep.OpenNotes: return L("推理本是侦破案件的好帮手，点开看看吧！", "The investigation notebook is a great help. Open it and take a look!");
                case TutorialStep.Testimonies: return L("这里总结了嫌疑人的关键供词。", "The suspects' key statements are collected here.");
                case TutorialStep.Sketch: return L("这里是你的草稿笔记，可以自由的演算推理。", "This is your sketch page. Work through your reasoning however you like.");
                case TutorialStep.SwitchTable: return L("这里可以选择在推演草稿和表格中自由切换，选择你最拿手的方法吧！", "Switch freely between the sketch and the table. Choose whichever method suits you best!");
                case TutorialStep.Records: return L("这里记录着本关（该档案）的所有对话记录，需要的话可以查阅哟", "This keeps all dialogue from the current case. Consult it whenever you need.");
                case TutorialStep.CloseNotes: return L("点击这里关闭记录，回到案件。", "Close the records here to return to the case.");
                case TutorialStep.NotebookShortcut: return L("记录和笔记也可以随时在这里打开哦", "You can open your records and notes here at any time.");
                default: return "";
            }
        }
        Rect TutorialCallout()
        {
            switch (tutorialStep)
            {
                case TutorialStep.KeyStatement: return new Rect(490, 453, 680, 165);
                case TutorialStep.ChooseSuspect: return new Rect(470, 112, 660, 155);
                case TutorialStep.OpenNotes: return new Rect(650, 594, 650, 150);
                case TutorialStep.Testimonies: return new Rect(840, 408, 495, 160);
                case TutorialStep.Sketch: return new Rect(272, 454, 470, 165);
                case TutorialStep.SwitchTable: return new Rect(693, 495, 640, 172);
                case TutorialStep.Records: return new Rect(323, 226, 660, 168);
                case TutorialStep.CloseNotes: return new Rect(700, 264, 600, 150);
                default: return new Rect(265, 150, 680, 160);
            }
        }
        bool TutorialNeedsTarget => tutorialStep == TutorialStep.OpenNotes || tutorialStep == TutorialStep.SwitchTable || tutorialStep == TutorialStep.Records || tutorialStep == TutorialStep.CloseNotes;
        bool TutorialHighlightsButton => TutorialNeedsTarget || tutorialStep == TutorialStep.NotebookShortcut;

        void DrawTutorialButton(Rect rect)
        {
            bool input = processInput, enabled = GUI.enabled;
            processInput = false; GUI.enabled = true;
            switch (tutorialStep)
            {
                case TutorialStep.OpenNotes: Button(rect, L("打开推理本", "Investigation notes")); break;
                case TutorialStep.SwitchTable: Button(rect, L("切换表格", "Switch to table")); break;
                case TutorialStep.CloseNotes: Button(rect, "×"); break;
                case TutorialStep.Records:
                    Button(rect, recordIcon ? "" : "▤");
                    if (recordIcon) Draw(recordIcon, new Rect(203, 209, 28, 28));
                    break;
                case TutorialStep.NotebookShortcut:
                    Button(rect, recordIcon ? "" : "▤");
                    if (recordIcon) Draw(recordIcon, new Rect(139, 51, 28, 28));
                    break;
            }
            processInput = input; GUI.enabled = enabled;
        }

        void DrawTutorial()
        {
            if (!TutorialBlocking || IsMainMenu || introComicVisible) return;
            Rect focus = TutorialFocus(); var dim = new Color(0, 0, 0, .67f * tutorialDim);
            // Four panels create a real spotlight; the highlighted UI remains visible and undimmed.
            if (tutorialStep == TutorialStep.KeyStatement || TutorialHighlightsButton) Box(new Rect(0, 0, W, H), dim, 0);
            else
            {
            Box(new Rect(0, 0, W, focus.yMin), dim, 0);
            Box(new Rect(0, focus.yMax, W, H - focus.yMax), dim, 0);
            Box(new Rect(0, focus.yMin, focus.xMin, focus.height), dim, 0);
            Box(new Rect(focus.xMax, focus.yMin, W - focus.xMax, focus.height), dim, 0);
            }
            Color old = GUI.color; GUI.color = new Color(1, 1, 1, tutorialDetail);
            if (TutorialHighlightsButton) DrawTutorialButton(focus);
            if (tutorialStep == TutorialStep.KeyStatement)
            {
                // A complete frame avoids slice seams. Its right edge stops at the
                // statement's final character; repaint only the text inside the spotlight.
                Draw(dialogueFrame, focus);
                GUI.BeginGroup(focus);
                var textRect = dialogueTextRect;
                textRect.position -= focus.position;
                var line = dialogue[DialogueIndex];
                RichLabel(textRect, FormatSpeech(line, 0, line.text.Get(Preferences.english).Length), Mathf.RoundToInt(Preferences.fontSize));
                GUI.EndGroup();
            }
            else if (tutorialStep == TutorialStep.ChooseSuspect || tutorialStep == TutorialStep.Testimonies || tutorialStep == TutorialStep.Sketch)
                TutorialFrame(new Rect(focus.x - 16, focus.y - 16, focus.width + 32, focus.height + 32));
            Rect callout = TutorialCallout(); Draw(dialogueFrame, callout);
            Label(new Rect(callout.x + 30, callout.y + 25, callout.width - 60, callout.height - 56), TutorialMessage(), 23, Ink, TextAnchor.MiddleLeft);
            if (!TutorialNeedsTarget) Label(new Rect(callout.x + 28, callout.yMax - 40, callout.width - 56, 24), L("点击继续", "Click to continue"), 16, Muted, TextAnchor.MiddleRight);
            Vector2 target = new Vector2(Mathf.Clamp(callout.center.x, focus.xMin, focus.xMax), Mathf.Clamp(callout.center.y, focus.yMin, focus.yMax));
            Vector2 direction = (target - callout.center).normalized;
            Vector2 arrowCenter = target - direction * (35 + 4 * Mathf.Sin(Time.unscaledTime * 4));
            if (tutorialArrow)
            {
                var matrix = GUI.matrix;
                GUI.matrix = matrix * Matrix4x4.TRS(arrowCenter, Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 180), Vector3.one);
                Draw(tutorialArrow, new Rect(-24, -24, 48, 48)); GUI.matrix = matrix;
            }
            GUI.color = old;
            if (!tutorialTransition && tutorialDetail >= .99f && Hit(TutorialNeedsTarget ? focus : new Rect(0, 0, W, H)))
            { PlayEffect(buttonClick); AdvanceTutorial(); }
        }
        void TutorialFrame(Rect r)
        {
            if (!dialogueFrame) return;
            const float sourceEdge = 64, border = 16;
            float[] sx = { 0, sourceEdge, dialogueFrame.width - sourceEdge, dialogueFrame.width };
            float[] sy = { 0, sourceEdge, dialogueFrame.height - sourceEdge, dialogueFrame.height };
            float[] dx = { r.x, r.x + border, r.xMax - border, r.xMax };
            float[] dy = { r.y, r.y + border, r.yMax - border, r.yMax };
            // Nine-slice the supplied box, excluding its opaque center so the evidence stays readable.
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
            {
                if (x == 1 && y == 1) continue;
                GUI.DrawTextureWithTexCoords(new Rect(dx[x], dy[y], dx[x + 1] - dx[x], dy[y + 1] - dy[y]), dialogueFrame,
                    new Rect(sx[x] / dialogueFrame.width, 1 - sy[y + 1] / dialogueFrame.height,
                        (sx[x + 1] - sx[x]) / dialogueFrame.width, (sy[y + 1] - sy[y]) / dialogueFrame.height));
            }
        }
    }
}
