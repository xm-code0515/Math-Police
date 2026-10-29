using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MathPolice
{
    public sealed partial class MathPoliceDemo : MonoBehaviour
    {
        [Header("Case library — add JSON cases here")]
        public TextAsset[] caseFiles;
        [Header("Archived cases — available to existing saves only")]
        public TextAsset[] legacyCaseFiles;
        [Header("Existing Art assets")]
        public Texture2D background, player, officer, happyOfficer, dialogueFrame, notebook;
        public Texture2D settingsIcon, recordIcon;
        public CaseDefinition Case { get; private set; }
        public DemoPreferences Preferences { get; private set; } = new DemoPreferences();
        public bool Investigating { get; private set; }
        public bool Solved { get; private set; }
        public int DialogueIndex { get; private set; }
        public int SelectedSuspect { get; private set; } = -1;
        public string Overlay { get; private set; } = "";
        public int[] TruthMarks { get; private set; }
        public bool[] CulpritMarks { get; private set; }
        public int HistoryCount => history.Count;

        const float W = 1600, H = 900;
        static readonly Color Ink = new Color32(35, 69, 112, 255);
        static readonly Color Blue = new Color32(81, 137, 195, 255);
        static readonly Color Pale = new Color32(237, 245, 253, 255);
        static readonly Color Paper = new Color32(255, 252, 244, 255);
        static readonly Color Muted = new Color32(105, 136, 165, 255);
        readonly List<DialogueDefinition> dialogue = new List<DialogueDefinition>();
        readonly List<int> history = new List<int>();
        readonly Dictionary<string, GUIStyle> styles = new Dictionary<string, GUIStyle>();
        readonly DemoSave[] slots = new DemoSave[4];
        readonly Texture2D[] thumbnails = new Texture2D[4];
        Font font;
        Texture2D buttonTexture, activeTexture, hoverTexture, drawing;
        AudioSource music, effects;
        float characters, autoClock, skipClock, toastUntil;
        bool auto, skipping, drawingDrag, eraser, penOptions, capturing;
        int settingsTab, bookTab, spread, caseIndex, dropdown = -1, confirmSlot = -1;
        string confirmation = "", toast = "", fatal = "";
        DemoPreferences preferenceBackup;
        Color penColor = Ink;
        float penWidth = 4;
        Vector2 lastPoint, suspectScroll, tableScroll;
        Vector2 pointer, groupOrigin;
        bool pointerDown, pointerUp, pointerHeld, processInput;
        float wheel;
        int inputFrame = -1;
        string pressedControl;
        readonly List<string> pages = new List<string>();
        string paginationKey;
        #if UNITY_EDITOR
        [NonSerialized] public string ValidationSaveDirectory;
        #endif
        string SaveDirectory
        {
            get
            {
                #if UNITY_EDITOR
                if (!string.IsNullOrEmpty(ValidationSaveDirectory)) return ValidationSaveDirectory;
                #endif
                return Path.Combine(Application.persistentDataPath, "MathPoliceDemo");
            }
        }
        string PrefPath => Path.Combine(SaveDirectory, "preferences.json");
        string SlotPath(int slot) => Path.Combine(SaveDirectory, "slot_" + (slot + 1) + ".json");
        string L(string zh, string en) => Preferences.english ? en : zh;

        void Awake()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Arial" }, 28);
            buttonTexture = Rounded(Pale); activeTexture = Rounded(Blue); hoverTexture = Rounded(new Color32(211, 230, 248, 255));
            drawing = new Texture2D(860, 730, TextureFormat.RGBA32, false);
            ClearDrawing();
            try { if (File.Exists(PrefPath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(PrefPath), Preferences); }
            catch (Exception) { Preferences = new DemoPreferences(); }
            ClampPreferences();
            SetupAudio();
            try { StartCase(0); } catch (Exception e) { fatal = e.Message; Debug.LogError(e); }
            ReadSlots();
            ShowMainMenuImmediate();
            fullscreenBlend = Preferences.fullscreen ? 1 : 0;
            ApplyDisplay();
        }

        public void StartCase(int index)
        {
            if (caseFiles == null || index < 0 || index >= caseFiles.Length || !caseFiles[index]) throw new ArgumentException("Assign at least one case JSON asset.");
            var next = JsonUtility.FromJson<CaseDefinition>(caseFiles[index].text);
            InitializeCase(next, index);
        }

        void InitializeCase(CaseDefinition next, int index)
        {
            next.Validate();
            Case = next; caseIndex = index;
            ResetTutorial();
            BuildDialogue();
            history.Clear(); DialogueIndex = 0; characters = autoClock = skipClock = 0;
            Investigating = Solved = auto = skipping = false; SelectedSuspect = -1;
            TruthMarks = new int[Case.suspectCount]; CulpritMarks = new bool[Case.suspectCount];
            ClearDrawing(); Overlay = ""; confirmation = ""; spread = 0; paginationKey = null;
            toastUntil = 0;
            suspectScroll = tableScroll = Vector2.zero;
            RecordCurrent();
            IsMainMenu = false; notesTable = false; lineupPage = 0;
            AssignPortraits(null);
            SetVisualState();
        }

        void ClampPreferences()
        {
            Preferences.textSpeed = Mathf.Clamp(Preferences.textSpeed, 10, 90);
            Preferences.autoDelay = Mathf.Clamp(Preferences.autoDelay, .4f, 4);
            Preferences.musicVolume = Mathf.Clamp01(Preferences.musicVolume);
            Preferences.sfxVolume = Mathf.Clamp01(Preferences.sfxVolume);
            Preferences.fontSize = Mathf.Clamp(Preferences.fontSize, 20, 34);
            Preferences.opacity = Mathf.Clamp(Preferences.opacity, .25f, 1);
            Preferences.resolution = Mathf.Clamp(Preferences.resolution, 0, 3);
        }

        void Update()
        {
            PollInput();
            UpdatePresentation();
            UpdateTutorial();
            if (Case == null || IsMainMenu || introComicVisible || TutorialBlocking || TransitionBusy || Investigating || Overlay != "" || confirmation != "" || capturing) return;
            var text = dialogue[DialogueIndex].text.Get(Preferences.english);
            characters = Mathf.Min(text.Length, characters + Time.unscaledDeltaTime * Preferences.textSpeed);
            if (skipping) characters = text.Length;
            if (TryBeginKeyTutorial()) return;
            if (skipping)
            {
                characters = text.Length; skipClock += Time.unscaledDeltaTime;
                if (skipClock >= .12f) { skipClock = 0; AdvanceDialogue(); }
            }
            else if (auto && characters >= text.Length)
            {
                autoClock += Time.unscaledDeltaTime;
                if (autoClock >= Preferences.autoDelay) AdvanceDialogue();
            }
        }

        // IMGUI is used for rendering only. The project's new Input System drives all interaction.
        void PollInput()
        {
            var mouse = Mouse.current;
            pointerDown = pointerUp = pointerHeld = false; wheel = 0;
            Vector2 position = Vector2.zero;
            if (mouse != null)
            {
                position = mouse.position.ReadValue(); pointerDown = mouse.leftButton.wasPressedThisFrame;
                pointerUp = mouse.leftButton.wasReleasedThisFrame; pointerHeld = mouse.leftButton.isPressed;
                wheel = mouse.scroll.ReadValue().y;
            }
            var touch = Touchscreen.current?.primaryTouch;
            if (touch != null && (touch.press.isPressed || touch.press.wasReleasedThisFrame))
            { position = touch.position.ReadValue(); pointerDown = touch.press.wasPressedThisFrame; pointerUp = touch.press.wasReleasedThisFrame; pointerHeld = touch.press.isPressed; }
            float scale = Mathf.Min(Screen.width / W, Screen.height / H);
            pointer = new Vector2((position.x - (Screen.width - W * scale) / 2) / scale,
                (Screen.height - position.y - (Screen.height - H * scale) / 2) / scale);
            if (TutorialBlocking || introComicVisible) return;
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                if (TransitionBusy) return;
                if (confirmation != "") confirmation = "";
                else if (Overlay == "settings") CancelSettings();
                else if (Overlay != "") CloseOverlay(); else OpenSettings();
            }
            if (Keyboard.current?.spaceKey.wasPressedThisFrame == true && !IsMainMenu && !TransitionBusy && Overlay == "" && confirmation == "") { PlayEffect(buttonClick); AdvanceDialogue(); }
        }

        void RecordCurrent() { if (!history.Contains(DialogueIndex)) history.Add(DialogueIndex); paginationKey = null; }
        public void AdvanceDialogue()
        {
            if (Investigating || IsMainMenu || introComicVisible || TutorialBlocking || TransitionBusy) return;
            if (characters < dialogue[DialogueIndex].text.Get(Preferences.english).Length && !skipping)
            { characters = dialogue[DialogueIndex].text.Get(Preferences.english).Length; return; }
            if (TryBeginKeyTutorial()) return;
            if (DialogueIndex < dialogue.Count - 1) AdvanceStagedDialogue(DialogueIndex + 1);
            else StartCoroutine(RevealLineup());
        }
        public void SetAuto(bool value) { auto = value; skipping = false; autoClock = 0; }
        public void SkipDialogue() { if (TutorialBlocking || introComicVisible) return; skipping = !skipping; auto = false; skipClock = 0; }
        public void OpenSettings() { preferenceBackup = JsonUtility.FromJson<DemoPreferences>(JsonUtility.ToJson(Preferences)); Overlay = "settings"; dropdown = -1; if (IsMainMenu) settingsTab = Mathf.Clamp(settingsTab, 0, 3); if (settingsTab >= 4) ReadSlots(); }
        public void OpenBook(int tab) { Overlay = "book"; bookTab = tab; drawingDrag = false; }
        public void CloseOverlay() { Overlay = ""; dropdown = -1; drawingDrag = false; }
        public void MarkTruth(int row, int value) { if (row >= 0 && row < TruthMarks.Length) TruthMarks[row] = TruthMarks[row] == value ? 0 : value; }
        public void MarkCulprit(int row) { if (row >= 0 && row < CulpritMarks.Length) CulpritMarks[row] = !CulpritMarks[row]; }
        public bool SubmitVerdict(int index)
        {
            if (Solved) { SelectedSuspect = Case.culpritIndex; Overlay = "result"; return true; }
            if (!Investigating || index < 0 || index >= Case.suspectCount) return false;
            SelectedSuspect = index;
            if (index != Case.culpritIndex) { PlayEffect(wrongSound); Notify(L("这个判断还不符合条件，再检验一下每句供词吧。", "That verdict does not fit the evidence. Check each statement again.")); return false; }
            if (!Solved) PlayEffect(correctSound);
            Solved = true; Overlay = "result"; auto = skipping = false; return true;
        }

        void OnGUI()
        {
            var oldMatrix = GUI.matrix;
            GUI.color = Color.white;
            DrawFullBackground();
            float scale = Mathf.Min(Screen.width / W, Screen.height / H);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - W * scale) / 2, (Screen.height - H * scale) / 2, 0), Quaternion.identity, Vector3.one * scale);
            if (!string.IsNullOrEmpty(fatal)) { Label(new Rect(120, 200, 1360, 300), "Case error: " + fatal, 28, Color.white); GUI.matrix = oldMatrix; return; }
            if (Case == null) { GUI.matrix = oldMatrix; return; }
            processInput = Event.current.type == EventType.Repaint && inputFrame != Time.frameCount;
            bool frameInput = processInput;
            if (TutorialBlocking) processInput = false;
            groupOrigin = Vector2.zero;
            bool underlying = Overlay == "" && confirmation == "" && !capturing && !TransitionBusy;
            GUI.enabled = underlying;
            if (introComicVisible)
            {
                if (Hit(new Rect(0, 0, W, H))) AdvanceComic();
            }
            else if (IsMainMenu) MainMenu();
            else
            {
                DrawActors(); TopBar();
                if (Investigating) DrawLineup(); else DialoguePanel();
            }
            GUI.enabled = true;
            if (!capturing && Overlay != "")
            {
                Box(new Rect(0, 0, W, H), new Color(0.08f, .15f, .24f, .46f), 0);
                GUI.enabled = confirmation == "" && !TransitionBusy;
                switch (Overlay)
                {
                    case "settings": Settings(); break;
                    case "book": Notebook(); break;
                    case "load": StandaloneLoad(); break;
                    case "result": Result(); break;
                }
                GUI.enabled = true;
            }
            if (!capturing && confirmation != "") Confirmation();
            processInput = frameInput;
            if (!capturing) DrawTutorial();
            if (!capturing && Time.unscaledTime < toastUntil)
            {
                var toastColor = GUI.color;
                GUI.color = new Color(toastColor.r, toastColor.g, toastColor.b,
                    toastColor.a * Mathf.SmoothStep(0, 1, Mathf.Clamp01((toastUntil - Time.unscaledTime) / .7f)));
                Box(new Rect(350, 100, 900, 76), Ink);
                Label(new Rect(370, 110, 860, 56), toast, 20, Color.white, TextAnchor.MiddleCenter);
                GUI.color = toastColor;
            }
            GUI.matrix = oldMatrix;
            if (blackFade > 0) Box(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, blackFade), 0);
            if (processInput) { inputFrame = Time.frameCount; if (pointerUp) pressedControl = null; }
        }

        void TopBar()
        {
            if (Button(new Rect(40, 34, 66, 62), settingsIcon ? "" : "⚙")) OpenSettings();
            if (settingsIcon) DrawTinted(settingsIcon, new Rect(59, 51, 28, 28), Ink);
            if (Button(new Rect(120, 34, 66, 62), recordIcon ? "" : "▤")) OpenBook(0);
            if (recordIcon) Draw(recordIcon, new Rect(139, 51, 28, 28));
            Box(new Rect(1130, 34, 425, 60), new Color(1, 1, 1, .91f));
            Label(new Rect(1145, 34, 395, 60), L("档案 ", "CASE ") + (Mathf.Max(0, caseIndex) + 1).ToString("00") + "  /  " + Case.title.Get(Preferences.english), 23, Ink, TextAnchor.MiddleCenter);
        }

        void DialoguePanel()
        {
            var entry = dialogue[DialogueIndex];
            var oldColor = GUI.color; GUI.color = new Color(1, 1, 1, panelAlpha);
            string full = entry.text.Get(Preferences.english);
            int fontSize = Mathf.RoundToInt(Preferences.fontSize);
            float textHeight = Mathf.Max(124, Mathf.Ceil(RichStyle(fontSize).CalcHeight(new GUIContent(FormatSpeech(entry, 0, full.Length)), 932)));
            float extraHeight = textHeight - 124;
            dialogueTextRect = new Rect(333, 688 - extraHeight, 932, textHeight);
            bool suspect = entry.role == "suspect";
            if (suspect)
            {
                Box(new Rect(292, 653 - extraHeight, 1016, 186 + extraHeight), new Color(StatementOrange.r, StatementOrange.g, StatementOrange.b, Preferences.opacity), 24);
                Box(new Rect(297, 658 - extraHeight, 1006, 176 + extraHeight), new Color(1, .988f, .956f, Preferences.opacity), 21);
            }
            else Draw(dialogueFrame, new Rect(282, 644 - extraHeight, 1036, 210 + extraHeight), Preferences.opacity);
            Box(new Rect(321, 621 - extraHeight, 230, 51), suspect ? StatementOrange : Blue);
            Label(new Rect(331, 625 - extraHeight, 210, 43), entry.speaker.Get(Preferences.english), 23, Color.white, TextAnchor.MiddleCenter);
            RichLabel(new Rect(333, 688 - extraHeight, 932, textHeight), FormatSpeech(entry, 0, Mathf.Clamp((int)characters, 0, full.Length)), fontSize);
            if (Hit(new Rect(296, 675 - extraHeight, 1004, 150 + extraHeight))) { PlayEffect(buttonClick); AdvanceDialogue(); }
            GUI.color = oldColor;
            bool normalInput = GUI.enabled;
            GUI.enabled = Overlay == "" && confirmation == "" && !capturing && !screenTransition && !menuActionPending && !TutorialBlocking && (!stageTransition || skipping);
            if (Button(new Rect(928, 845, 180, 43), skipping ? L("Ⅱ  停止跳过", "Ⅱ  Stop skip") : L("▷▷  跳过", "▷▷  Skip"), skipping, allowDuringTransition: skipping)) SkipDialogue();
            GUI.enabled = normalInput;
            if (Button(new Rect(1128, 845, 180, 43), auto ? L("Ⅱ  自动中", "Ⅱ  Auto on") : L("▷  自动", "▷  Auto"), auto)) SetAuto(!auto);
            GUI.color = oldColor;
        }

        void Settings()
        {
            Box(new Rect(286, 138, 1028, 650), Color.white);
            Box(new Rect(286, 138, 1028, 70), Blue);
            Label(new Rect(320, 150, 800, 48), L("设置", "Settings"), 29, Color.white);
            if (Button(new Rect(1243, 151, 47, 43), "×")) CancelSettings();
            Box(new Rect(288, 209, 240, 577), Pale);
            string[] labels = { L("常规", "General"), L("音频", "Audio"), L("显示", "Display"), L("文字", "Text"), L("保存游戏", "Save game"), L("读取游戏", "Load game"), L("退出至主菜单", "Main menu"), L("退出游戏", "Quit game") };
            for (int i = 0; i < (IsMainMenu ? 4 : labels.Length); i++)
                if (Button(new Rect(305, 231 + i * 61, 207, 48), labels[i], settingsTab == i, 21))
                {
                    dropdown = -1;
                    if (i == 6) confirmation = "menu";
                    else if (i == 7) confirmation = "quit";
                    else { settingsTab = i; if (i >= 4) ReadSlots(); }
                }
            Label(new Rect(565, 233, 610, 44), labels[settingsTab], 28);
            Box(new Rect(565, 290, 695, 2), new Color32(215, 230, 244, 255), 0);
            switch (settingsTab)
            {
                case 0:
                    Label(new Rect(565, 329, 245, 40), L("语言", "Language"), 23);
                    if (Button(new Rect(877, 326, 379, 43), Preferences.english ? "English     ▾" : "简体中文     ▾")) dropdown = dropdown == 0 ? -1 : 0;
                    bool rowsEnabled = GUI.enabled; GUI.enabled = rowsEnabled && dropdown != 0;
                    Preferences.textSpeed = SliderRow(424, L("文字显示速度", "Text speed"), Preferences.textSpeed, 10, 90, Mathf.RoundToInt(Preferences.textSpeed) + L(" 字/秒", " cps"));
                    float speed = 4.4f - Preferences.autoDelay;
                    speed = SliderRow(520, L("自动对话速度", "Auto-play speed"), speed, .4f, 4, Preferences.autoDelay.ToString("0.0") + L(" 秒停顿", " s delay"));
                    Preferences.autoDelay = 4.4f - speed;
                    GUI.enabled = rowsEnabled;
                    if (dropdown == 0)
                    {
                        Box(new Rect(875, 373, 383, 101), Color.white);
                        if (Button(new Rect(879, 376, 375, 45), "简体中文", !Preferences.english)) ChangeLanguage(false);
                        if (Button(new Rect(879, 424, 375, 45), "English", Preferences.english)) ChangeLanguage(true);
                    }
                    break;
                case 1:
                    Preferences.musicVolume = SliderRow(354, L("音乐音量", "Music volume"), Preferences.musicVolume, 0, 1, Mathf.RoundToInt(Preferences.musicVolume * 100) + "%");
                    Preferences.sfxVolume = SliderRow(454, L("音效音量", "Sound effects"), Preferences.sfxVolume, 0, 1, Mathf.RoundToInt(Preferences.sfxVolume * 100) + "%");
                    break;
                case 2:
                    Label(new Rect(565, 330, 270, 42), L("全屏", "Fullscreen"), 23);
                    FullscreenToggle(new Rect(1147, 324, 104, 60));
                    Label(new Rect(565, 428, 270, 42), L("游戏分辨率", "Resolution"), 23);
                    string[] resolutions = { "1280 × 720", "1600 × 900", "1920 × 1080", "2560 × 1440" };
                    if (Button(new Rect(877, 426, 379, 46), resolutions[Preferences.resolution] + "     ▾")) dropdown = dropdown == 1 ? -1 : 1;
                    if (dropdown == 1)
                        for (int i = 0; i < resolutions.Length; i++)
                            if (Button(new Rect(877, 481 + i * 48, 379, 45), resolutions[i], Preferences.resolution == i)) { Preferences.resolution = i; dropdown = -1; }
                    break;
                case 3:
                    Preferences.fontSize = SliderRow(355, L("对话字体大小", "Dialogue font size"), Preferences.fontSize, 20, 34, Mathf.RoundToInt(Preferences.fontSize) + " px");
                    Preferences.opacity = SliderRow(455, L("对话框不透明度", "Dialogue opacity"), Preferences.opacity, .25f, 1, Mathf.RoundToInt(Preferences.opacity * 100) + "%");
                    Draw(dialogueFrame, new Rect(565, 556, 690, 112), Preferences.opacity);
                    Label(new Rect(593, 584, 630,  60), L("每一条线索，都值得仔细推敲。", "Every clue deserves a closer look."), Mathf.RoundToInt(Preferences.fontSize));
                    break;
                case 4: Slots(true); break;
                case 5: Slots(false); break;
            }
            if (settingsTab < 4)
            {
                if (Button(new Rect(818, 716, 129, 44), L("重置", "Reset"))) { Preferences = new DemoPreferences(); paginationKey = null; }
                if (Button(new Rect(965, 716, 129, 44), L("取消", "Cancel"))) CancelSettings();
                if (Button(new Rect(1112, 716, 145, 44), L("确定", "OK"), true)) ApplySettings();
            }
        }
        public void ChangeLanguage(bool english) { Preferences.english = english; dropdown = -1; paginationKey = null; }
        void CancelSettings() { if (preferenceBackup != null) Preferences = preferenceBackup; paginationKey = null; CloseOverlay(); }
        void ApplySettings()
        {
            try
            {
                Directory.CreateDirectory(SaveDirectory); AtomicWrite(PrefPath, JsonUtility.ToJson(Preferences, true));
                ApplyDisplay();
                CloseOverlay();
            }
            catch (Exception) { Notify(L("无法保存设置，请检查磁盘权限。", "Could not save settings. Check disk permissions.")); }
        }

        void Notebook()
        {
            Draw(notebook, new Rect(206, 109, 1200, 733));
            if (Button(new Rect(187, 191,  60, 64), "", bookTab == 0)) { bookTab = 0; drawingDrag = false; }
            if (recordIcon) Draw(recordIcon, new Rect(203, 209, 28, 28)); else Label(new Rect(197, 204, 40, 40), "▤", 26);
            if (Button(new Rect(187, 266,  60, 64), "", bookTab == 1)) bookTab = 1;
            Magnifier(new Rect(203, 284, 28, 28), bookTab == 1 ? Color.white : Ink);
            Label(new Rect(283, 158, 411, 45), bookTab == 0 ? L("记录", "Records") : L("嫌疑人供词", "Testimonies"), 29);
            if (Button(new Rect(1287, 152, 49, 43), "×")) CloseOverlay();
            if (bookTab == 0) HistoryPages(); else CaseNotes();
        }

        void BuildPages()
        {
            string key = Preferences.english + ":" + HistoryCount;
            if (paginationKey == key) return;
            pages.Clear(); string current = "";
            var style = RichStyle(22);
            foreach (int entry in history)
            {
                var d = dialogue[entry];
                string plain = d.speaker.Get(Preferences.english) + "\n" + d.text.Get(Preferences.english) + "\n\n";
                string text = FormatHistory(d, 0, plain.Length);
                if (style.CalcHeight(new GUIContent(text), 440) <= 488)
                {
                    if (current.Length > 0 && style.CalcHeight(new GUIContent(current + text), 440) > 488)
                    { pages.Add(current); current = ""; }
                    current += text;
                    continue;
                }
                if (current.Length > 0) { pages.Add(current); current = ""; }
                int offset = 0;
                while (offset < plain.Length)
                {
                    int low = 1, high = plain.Length - offset, take = 1;
                    while (low <= high)
                    {
                        int middle = (low + high) / 2;
                        if (style.CalcHeight(new GUIContent(FormatHistory(d, offset, middle)), 440) <= 488) { take = middle; low = middle + 1; }
                        else high = middle - 1;
                    }
                    current = FormatHistory(d, offset, take); offset += take;
                    if (offset < plain.Length) { pages.Add(current); current = ""; }
                }
            }
            if (current.Length > 0 || pages.Count == 0) pages.Add(current);
            paginationKey = key; spread = Mathf.Clamp(spread, 0, (pages.Count - 1) / 2);
        }
        void HistoryPages()
        {
            BuildPages();
            Label(new Rect(874, 166, 390,  30), Case.title.Get(Preferences.english), 21, Muted);
            RichLabel(new Rect(287, 224, 440, 493), pages[spread * 2], 22);
            if (spread * 2 + 1 < pages.Count) RichLabel(new Rect(875, 224, 440, 493), pages[spread * 2 + 1], 22);
            bool enabled = GUI.enabled; GUI.enabled = enabled && spread > 0;
            if (Button(new Rect(286, 727, 144, 40), L("‹  上一页", "‹  Previous"))) spread--;
            GUI.enabled = enabled;
            GUI.enabled = enabled && (spread + 1) * 2 < pages.Count;
            if (Button(new Rect(1164, 727, 160, 40), L("下一页  ›", "Next  ›"))) spread++;
            GUI.enabled = enabled;
        }

        public void ClearDrawing()
        {
            if (!drawing) return;
            drawing.SetPixels32(new Color32[drawing.width * drawing.height]); drawing.Apply(); drawingDrag = false;
        }
        void HandleDrawing(Rect area)
        {
            if (!processInput) return;
            if (pointerUp) drawingDrag = false;
            if (!GUI.enabled || (penOptions && pointer.y < 377)) return;
            if (pointerDown && area.Contains(pointer))
            { drawingDrag = true; lastPoint = pointer; PaintLine(lastPoint, lastPoint, area); }
            else if (pointerHeld && drawingDrag)
            {
                if (area.Contains(pointer)) PaintLine(lastPoint, pointer, area);
                lastPoint = new Vector2(Mathf.Clamp(pointer.x, area.xMin, area.xMax), Mathf.Clamp(pointer.y, area.yMin, area.yMax));
            }
        }
        void PaintLine(Vector2 from, Vector2 to, Rect area)
        {
            Vector2 a = new Vector2((from.x - area.x) / area.width * drawing.width, (1 - (from.y - area.y) / area.height) * drawing.height);
            Vector2 b = new Vector2((to.x - area.x) / area.width * drawing.width, (1 - (to.y - area.y) / area.height) * drawing.height);
            int radius = Mathf.CeilToInt((eraser ? 15 : penWidth) * drawing.width / area.width / 2);
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
            Color color = eraser ? Color.clear : penColor;
            for (int n = 0; n <= steps; n++)
            {
                Vector2 p = Vector2.Lerp(a, b, (float)n / steps);
                for (int y = -radius; y <= radius; y++) for (int x = -radius; x <= radius; x++)
                {
                    int px = (int)p.x + x, py = (int)p.y + y;
                    if (x * x + y * y <= radius * radius && px >= 0 && py >= 0 && px < drawing.width && py < drawing.height) drawing.SetPixel(px, py, color);
                }
            }
            drawing.Apply();
        }

        void Result()
        {
            Box(new Rect(430, 219, 740, 476), Paper);
            Label(new Rect(476, 252, 647, 48), L("案件已破获！", "CASE CLOSED!"), 35, Ink, TextAnchor.MiddleCenter);
            Label(new Rect(476, 316, 647,  40), L("真正的犯人是：", "The culprit is: ") + Case.suspects[Case.culpritIndex].name.Get(Preferences.english), 27, Blue, TextAnchor.MiddleCenter);
            Label(new Rect(486, 391, 628, 190), Case.explanation.Get(Preferences.english), 24);
            if (Button(new Rect(492, 614, 259, 49), L("查看记录", "Review records"))) OpenBook(0);
            if (Button(new Rect(838, 614, 269, 49), L("下一档案", "Next case"), true)) StartNextCase();
        }

        void Slots(bool saving, float left = 565, float top = 302)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                Rect r = new Rect(left + i % 2 * 357, top + 48 + i / 2 * 191, 335, 177);
                Box(r, new Color32(186, 209, 234, 255), 10);
                bool enabled = GUI.enabled; GUI.enabled = enabled && (saving || slots[i] != null);
                if (Button(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), ""))
                {
                    if (!saving) { if (IsMainMenu) LoadSlot(i); else { confirmSlot = i; confirmation = "load"; } }
                    else if (slots[i] != null || File.Exists(SlotPath(i))) { confirmSlot = i; confirmation = "overwrite"; }
                    else StartCoroutine(SaveSlot(i));
                }
                GUI.enabled = enabled;
                if (thumbnails[i]) Draw(thumbnails[i], new Rect(r.x + 9, r.y + 8, 317, 112));
                else Label(new Rect(r.x + 12, r.y + 18, 311,  90), L("空白存档", "Empty slot"), 23, Muted, TextAnchor.MiddleCenter);
                Label(new Rect(r.x + 13, r.y + 122, 308,  20), L("存档 ", "SLOT ") + (i + 1), 16);
                Label(new Rect(r.x + 13, r.y + 146, 308,  20), slots[i] == null ? "—" : slots[i].savedAt, 16, Muted);
            }
        }

        public DemoSave CaptureState()
        {
            return new DemoSave { caseId = Case.id, savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), dialogueIndex = DialogueIndex,
                revealedCharacters = (int)characters, selectedSuspect = SelectedSuspect, investigating = Investigating, solved = Solved,
                history = history.ToArray(), truthMarks = (int[])TruthMarks.Clone(), culpritMarks = (bool[])CulpritMarks.Clone(),
                drawingPng = Convert.ToBase64String(drawing.EncodeToPNG()), portraitIds = (string[])portraitIds.Clone(), notesTable = notesTable,
                keyTutorialSeen = keyTutorialSeen, notebookTutorialSeen = notebookTutorialSeen, tutorialStep = (int)tutorialStep, introComicVisible = introComicVisible };
        }
        public void RestoreState(DemoSave save)
        {
            if (save == null || save.version < 1 || save.version > 3 || save.tutorialStep < 0 || save.tutorialStep > 9) throw new ArgumentException("Unsupported save version or tutorial step.");
            int index = Array.FindIndex(caseFiles, t => t && JsonUtility.FromJson<CaseDefinition>(t.text).id == save.caseId);
            TextAsset file = index >= 0 ? caseFiles[index] : Array.Find(legacyCaseFiles ?? Array.Empty<TextAsset>(), t => t && JsonUtility.FromJson<CaseDefinition>(t.text).id == save.caseId);
            if (!file) throw new ArgumentException("This save's case is unavailable.");
            var definition = JsonUtility.FromJson<CaseDefinition>(file.text);
            definition.Validate();
            int lineCount = definition.script != null && definition.script.Length > 0 ? definition.script.Length : (definition.introduction?.Length ?? 0) + definition.suspectCount + 1;
            if (save.dialogueIndex < 0 || save.dialogueIndex >= lineCount || save.truthMarks == null || save.truthMarks.Length != definition.suspectCount ||
                save.culpritMarks == null || save.culpritMarks.Length != definition.suspectCount || save.history == null ||
                Array.Exists(save.history, h => h < 0 || h >= lineCount) || Array.Exists(save.truthMarks, t => t < 0 || t > 2) ||
                save.selectedSuspect < -1 || save.selectedSuspect >= definition.suspectCount)
                throw new ArgumentException("Save data does not match the case.");
            Texture2D restored = new Texture2D(2, 2);
            try
            {
                if (!restored.LoadImage(Convert.FromBase64String(save.drawingPng)) || restored.width != 860 || restored.height != 730)
                    throw new ArgumentException("Invalid saved sketch.");
            }
            catch { Destroy(restored); throw; }
            InitializeCase(definition, index);
            DialogueIndex = save.dialogueIndex; characters = Mathf.Clamp(save.revealedCharacters, 0, dialogue[DialogueIndex].text.Get(Preferences.english).Length);
            SelectedSuspect = save.solved ? Case.culpritIndex : save.selectedSuspect; Investigating = save.investigating; Solved = save.solved;
            TruthMarks = (int[])save.truthMarks.Clone(); CulpritMarks = (bool[])save.culpritMarks.Clone();
            history.Clear(); history.AddRange(save.history); RecordCurrent();
            Destroy(drawing); drawing = restored; Overlay = "";
            AssignPortraits(save.portraitIds); notesTable = save.notesTable;
            SetVisualState();
            RestoreTutorial(save);
        }

        public IEnumerator SaveSlot(int slot)
        {
            if (capturing || TransitionBusy || IsMainMenu || slot < 0 || slot >= slots.Length) yield break;
            confirmation = ""; capturing = true;
            yield return new WaitForEndOfFrame();
            Texture2D capture = null, preview = null;
            RenderTexture target = null;
            try
            {
                DemoSave data = CaptureState();
                capture = ScreenCapture.CaptureScreenshotAsTexture();
                target = RenderTexture.GetTemporary(480, 270, 0);
                Graphics.Blit(capture, target);
                var old = RenderTexture.active;
                try { RenderTexture.active = target; preview = new Texture2D(480, 270, TextureFormat.RGB24, false); preview.ReadPixels(new Rect(0, 0, 480, 270), 0, 0); preview.Apply(); }
                finally { RenderTexture.active = old; }
                data.thumbnailPng = Convert.ToBase64String(preview.EncodeToPNG());
                Directory.CreateDirectory(SaveDirectory); AtomicWrite(SlotPath(slot), JsonUtility.ToJson(data));
                ReadSlots(); Notify(L("游戏已保存。", "Game saved."));
            }
            catch (Exception e) { Debug.LogWarning("Save failed: " + e.Message); Notify(L("保存失败，请检查磁盘空间或权限。", "Save failed. Check disk space or permissions.")); }
            finally { if (capture) Destroy(capture); if (preview) Destroy(preview); if (target) RenderTexture.ReleaseTemporary(target); capturing = false; }
        }
        public bool LoadSlot(int slot)
        {
            try
            {
                if (TransitionBusy) return false;
                var saved = JsonUtility.FromJson<DemoSave>(File.ReadAllText(SlotPath(slot)));
                BeginScreenTransition(() =>
                {
                    try { RestoreState(saved); Notify(L("已恢复存档。", "Save loaded.")); }
                    catch (Exception e) { Debug.LogWarning("Load failed: " + e.Message); Notify(L("无法读取该存档，当前进度未被替换。", "Could not load this save. Current progress is unchanged.")); }
                });
                return true;
            }
            catch (Exception e) { Debug.LogWarning("Load failed: " + e.Message); Notify(L("无法读取该存档，当前进度未被替换。", "Could not read this save. Your current progress is unchanged.")); return false; }
        }
        void ReadSlots()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = null; if (thumbnails[i]) Destroy(thumbnails[i]); thumbnails[i] = null;
                try
                {
                    if (!File.Exists(SlotPath(i))) continue;
                    slots[i] = JsonUtility.FromJson<DemoSave>(File.ReadAllText(SlotPath(i)));
                    if (!string.IsNullOrEmpty(slots[i].thumbnailPng))
                    { thumbnails[i] = new Texture2D(2, 2); thumbnails[i].LoadImage(Convert.FromBase64String(slots[i].thumbnailPng)); }
                }
                catch { slots[i] = null; }
            }
        }
        static void AtomicWrite(string path, string text)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }

        void Confirmation()
        {
            Box(new Rect(0, 0, W, H), new Color(0, .05f, .1f, .4f), 0);
            Box(new Rect(475, 317, 650, 250), Color.white);
            string message = confirmation == "overwrite" ? L("是否要覆盖该存档？", "Overwrite this save?") : confirmation == "load" ? L("读取该存档？未保存的进度将被替换。", "Load this save? Unsaved progress will be replaced.") : confirmation == "menu" ? L("返回主菜单？请先保存当前进度。", "Return to the menu? Save your progress first.") : L("退出游戏？请先保存当前进度。", "Quit the game? Save your progress first.");
            Label(new Rect(515, 348, 570,  90), message, 27, Ink, TextAnchor.MiddleCenter);
            if (Button(new Rect(568, 475, 190, 49), L("否", "No"))) confirmation = "";
            if (Button(new Rect(840, 475, 190, 49), L("是", "Yes"), true))
            {
                string action = confirmation; confirmation = "";
                if (action == "overwrite") StartCoroutine(SaveSlot(confirmSlot));
                else if (action == "load") LoadSlot(confirmSlot);
                else if (action == "menu") BeginScreenTransition(ShowMainMenuImmediate);
                else
                {
                    #if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
                    #else
                    Application.Quit();
                    #endif
                }
            }
        }
        void Notify(string message) { toast = message; toastUntil = Time.unscaledTime + 4; }
        void Draw(Texture texture, Rect r, float alpha = 1)
        { if (!texture) return; var previous = GUI.color; GUI.color = new Color(previous.r, previous.g, previous.b, previous.a * alpha); GUI.DrawTexture(r, texture, ScaleMode.StretchToFill, true); GUI.color = previous; }
        void DrawTinted(Texture texture, Rect r, Color tint)
        { var previous = GUI.color; GUI.color = tint; GUI.DrawTexture(r, texture, ScaleMode.StretchToFill, true); GUI.color = previous; }
        void Magnifier(Rect r, Color color)
        {
            Box(new Rect(r.x, r.y, 22, 22), color, 11);
            Box(new Rect(r.x + 3, r.y + 3, 16, 16), bookTab == 1 ? Blue : Pale, 8);
            for (int i = 0; i <= 7; i++) Box(new Rect(r.x + 17 + i, r.y + 17 + i, 4, 4), color, 2);
        }
        void Box(Rect r, Color color, float radius = 14) => GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color * GUI.color, 0, radius);
        GUIStyle TextStyle(int size, Color color, TextAnchor alignment)
        {
            string key = size + ":" + color + ":" + alignment;
            if (!styles.TryGetValue(key, out var style))
            {
                style = new GUIStyle { font = font, fontSize = size, wordWrap = true, alignment = alignment, richText = false, clipping = TextClipping.Clip };
                style.normal.textColor = color; styles[key] = style;
            }
            return style;
        }
        void Label(Rect r, string text, int size = 23, Color? color = null, TextAnchor alignment = TextAnchor.UpperLeft) => GUI.Label(r, text ?? "", TextStyle(size, color ?? Ink, alignment));
        bool Button(Rect r, string text, bool selected = false, int size = 22, bool allowDuringTransition = false)
        {
            var style = new GUIStyle(TextStyle(size, selected ? Color.white : Ink, TextAnchor.MiddleCenter));
            style.normal.background = selected ? activeTexture : buttonTexture;
            style.hover.background = selected ? activeTexture : hoverTexture;
            style.hover.textColor = selected ? Color.white : Ink;
            style.active.background = activeTexture; style.active.textColor = Color.white;
            style.border = new RectOffset(9, 9, 9, 9);
            style.padding = new RectOffset(7, 7, 2, 2);
            if (GUI.enabled && Contains(r)) style.normal.background = selected ? activeTexture : hoverTexture;
            GUI.Box(r, text, style);
            bool clicked = Hit(r, allowDuringTransition);
            if (clicked) PlayEffect(buttonClick);
            return clicked;
        }
        float SliderRow(float y, string title, float value, float min, float max, string output)
        {
            Label(new Rect(565, y, 290,  50), title, 22);
            value = Slider(new Rect(884, y + 5, 238,  20), value, min, max);
            Label(new Rect(1132, y - 2, 125,  40), output, 18, Muted, TextAnchor.MiddleRight);
            return value;
        }
        float Slider(Rect r, float value, float min, float max)
        {
            string key = "slider" + r.ToString() + groupOrigin;
            Rect hit = new Rect(r.x - 10, r.y - 10, r.width + 20, 40);
            if (processInput && GUI.enabled && pointerDown && Contains(hit)) pressedControl = key;
            if (processInput && GUI.enabled && pressedControl == key && (pointerHeld || pointerUp))
                value = Mathf.Lerp(min, max, Mathf.Clamp01((pointer.x - groupOrigin.x - r.x) / r.width));
            Box(new Rect(r.x, r.y + 7, r.width, 7), new Color32(210, 226, 240, 255), 4);
            Box(new Rect(r.x, r.y + 7, r.width * Mathf.InverseLerp(min, max, value), 7), Blue, 4);
            float x = r.x + r.width * Mathf.InverseLerp(min, max, value);
            Box(new Rect(x - 11, r.y - 1, 22, 22), Blue, 11);
            Box(new Rect(x - 8, r.y + 2, 16, 16), Color.white, 8);
            return value;
        }
        Rect scrollClip;
        bool Contains(Rect r) => r.Contains(pointer - groupOrigin) && (groupOrigin == Vector2.zero || scrollClip.Contains(pointer));
        bool Hit(Rect r, bool allowDuringTransition = false)
        {
            if (!processInput || !GUI.enabled || (allowDuringTransition ? screenTransition || menuActionPending : TransitionBusy)) return false;
            string key = "button" + r.ToString() + groupOrigin;
            if (pointerDown && Contains(r)) pressedControl = key;
            return pointerUp && pressedControl == key && Contains(r);
        }
        void BeginScroll(Rect viewport, ref Vector2 scroll, float contentHeight)
        {
            float max = Mathf.Max(0, contentHeight - viewport.height);
            if (processInput && GUI.enabled && viewport.Contains(pointer)) scroll.y = Mathf.Clamp(scroll.y - wheel * .35f, 0, max);
            if (max > 0)
            {
                Rect track = new Rect(viewport.xMax - 13, viewport.y, 11, viewport.height);
                string key = "scroll" + viewport;
                if (processInput && GUI.enabled && pointerDown && track.Contains(pointer)) pressedControl = key;
                if (processInput && pressedControl == key && (pointerHeld || pointerUp)) scroll.y = Mathf.Clamp01((pointer.y - viewport.y) / viewport.height) * max;
                Box(track, Pale, 4);
                float handle = Mathf.Max(30, viewport.height * viewport.height / contentHeight);
                Box(new Rect(track.x, track.y + scroll.y / max * (track.height - handle), 11, handle), Blue, 4);
            }
            scroll.y = Mathf.Clamp(scroll.y, 0, max);
            scrollClip = viewport; groupOrigin = viewport.position - new Vector2(0, scroll.y);
            GUI.BeginGroup(viewport); GUI.BeginGroup(new Rect(0, -scroll.y, viewport.width - 15, Mathf.Max(contentHeight, viewport.height)));
        }
        void EndScroll() { GUI.EndGroup(); GUI.EndGroup(); groupOrigin = Vector2.zero; }
        Texture2D Rounded(Color color)
        {
            var t = new Texture2D(32, 32, TextureFormat.RGBA32, false); t.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float dx = Mathf.Max(7 - x, x - 24, 0), dy = Mathf.Max(7 - y, y - 24, 0);
                Color c = color; c.a *= Mathf.Clamp01(8 - Mathf.Sqrt(dx * dx + dy * dy)); t.SetPixel(x, y, c);
            }
            t.Apply(); return t;
        }
        void OnDestroy()
        {
            foreach (var t in thumbnails) if (t) Destroy(t);
            if (drawing) Destroy(drawing); if (buttonTexture) Destroy(buttonTexture); if (activeTexture) Destroy(activeTexture); if (hoverTexture) Destroy(hoverTexture);
            if (font) Destroy(font);
            DisposePortraitVisuals();
        }
    }
}


