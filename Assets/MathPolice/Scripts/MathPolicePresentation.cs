using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MathPolice
{
    public sealed partial class MathPoliceDemo
    {
        [Header("Menu and presentation")]
        public Texture2D menuBackground, titleArt, menuIdle, menuHover, loadFrame, toggleOn, toggleOff, interrogationBackground;
        public Texture2D playerNormal, playerSmile, officerSmile, officerSmile2;
        public Texture2D[] boyPortraits, girlPortraits;
        [Header("Music and sound effects")]
        public AudioClip menuMusic, stationMusic, puzzleMusic, buttonHover, buttonClick, correctSound, wrongSound;
        public bool IsMainMenu { get; private set; }
        public bool TransitionBusy => screenTransition || stageTransition || menuActionPending;
        public bool InInterrogation => roomBlend > .99f;
        public bool NotesTable => notesTable;
        public string[] PortraitIds => (string[])portraitIds.Clone();
        public string CurrentMusic => music && music.clip ? music.clip.name : "";
        public string LastEffect { get; private set; }
        public int EffectSerial { get; private set; }

        bool screenTransition, stageTransition, menuActionPending, notesTable;
        float blackFade, actorAlpha = 1, panelAlpha = 1, roomBlend, roomTarget, fullscreenBlend;
        float playerBrightness = 1, officerBrightness = 1, musicBlend = 1;
        Texture2D shownPlayer, shownOfficer;
        int lineupPage, menuHovered = -1, suspectHovered = -1, menuPressed = -1;
        readonly float[] menuScales = { 1, 1, 1, 1 };
        Vector2 testimonyScroll;
        string[] portraitIds = Array.Empty<string>();
        Texture2D[] portraits = Array.Empty<Texture2D>();
        AudioSource outgoingMusic;
        readonly Dictionary<Texture2D, PortraitVisual> portraitVisuals = new Dictionary<Texture2D, PortraitVisual>();
        class PortraitVisual { public Texture2D outline; public Color32[] pixels; public int width, height; }
        static readonly Color OutlineBlue = new Color32(48, 133, 236, 255);
        const int PortraitOutlinePadding = 11;

        void ShowMainMenuImmediate()
        {
            IsMainMenu = true; Overlay = ""; confirmation = ""; auto = skipping = false;
            ResetTutorialPresentation();
            menuHovered = suspectHovered = -1; toastUntil = 0;
            SetMusic(menuMusic);
        }
        public void StartNewGame()
        {
            if (!TransitionBusy) BeginScreenTransition(() => { StartCase(0); BeginComic(); });
        }
        public void StartNextCase()
        {
            if (!Solved || TransitionBusy) return;
            int nextIndex = caseIndex + 1;
            if (caseFiles == null || nextIndex >= caseFiles.Length || !caseFiles[nextIndex])
            {
                Notify(L("暂无下一档案。", "No next case available yet."));
                return;
            }
            // Validate before fading so an invalid entry cannot strand the screen in black.
            try { JsonUtility.FromJson<CaseDefinition>(caseFiles[nextIndex].text).Validate(); }
            catch (Exception e)
            {
                Debug.LogWarning("Next case unavailable: " + e.Message);
                Notify(L("下一档案暂时无法打开。", "The next case could not be opened."));
                return;
            }
            BeginScreenTransition(() => StartCase(nextIndex));
        }
        public void ReturnToMainMenu() { if (!TransitionBusy) BeginScreenTransition(ShowMainMenuImmediate); }
        public void OpenLoadMenu() { ReadSlots(); Overlay = "load"; }
        public void ToggleNotes() { notesTable = !notesTable; drawingDrag = false; penOptions = false; }

        void BeginScreenTransition(Action action)
        {
            if (screenTransition || stageTransition) return;
            StartCoroutine(ScreenTransition(action));
        }
        IEnumerator ScreenTransition(Action action)
        {
            screenTransition = true; pressedControl = null; drawingDrag = false;
            yield return Animate(.32f, t => blackFade = t);
            blackFade = 1;
            action();
            yield return null;
            yield return Animate(.8f, t => blackFade = 1 - t);
            blackFade = 0; screenTransition = false;
        }
        IEnumerator Animate(float duration, Action<float> update)
        {
            for (float time = 0; time < duration; time += Time.unscaledDeltaTime)
            { update(Mathf.SmoothStep(0, 1, time / duration)); yield return null; }
            update(1);
        }

        void SetVisualState()
        {
            actorAlpha = panelAlpha = 1;
            roomTarget = roomBlend = Investigating ? 1 : RoomAt(DialogueIndex);
            narrationBlend = IsNarration ? 1 : 0;
            shownPlayer = player; shownOfficer = officer;
            // Replay cues to recover retained expressions when loading a mid-conversation save.
            for (int i = 0; i <= DialogueIndex; i++) ApplyExpression(dialogue[i]);
            lineupPage = SelectedSuspect >= 0 ? SelectedSuspect / 5 : 0;
            testimonyScroll = Vector2.zero;
            SetMusic(roomTarget > 0 ? puzzleMusic : stationMusic);
        }
        void ApplyExpression(DialogueDefinition line)
        {
            string role = line.role;
            if (line.speaker?.zh == "你" || line.speaker?.en == "You") role = "player";
            if (role == "player") shownPlayer = PlayerExpression(line.expression);
            else if (role == "officer") shownOfficer = OfficerExpression(line.expression);
            if (!string.IsNullOrEmpty(line.playerExpression)) shownPlayer = PlayerExpression(line.playerExpression);
            if (!string.IsNullOrEmpty(line.officerExpression)) shownOfficer = OfficerExpression(line.officerExpression);
        }
        Texture2D PlayerExpression(string expression) => expression == "smile" ? playerSmile : expression == "thinking" ? player : playerNormal;
        Texture2D OfficerExpression(string expression) => expression == "laugh" ? happyOfficer : expression == "smile2" ? officerSmile2 : expression == "smile" ? officerSmile : officer;

        void AdvanceStagedDialogue(int next)
        {
            var current = dialogue[DialogueIndex]; var following = dialogue[next];
            bool sameInterview = InterviewAt(DialogueIndex) && InterviewAt(next);
            bool castChanges = current.stage != following.stage || (sameInterview && current.suspectIndex != following.suspectIndex);
            if (castChanges) StartCoroutine(ChangeSpeaker(next, sameInterview));
            else { DialogueIndex = next; characters = autoClock = 0; RecordCurrent(); ApplyExpression(dialogue[next]); }
        }
        IEnumerator ChangeSpeaker(int next, bool portraitOnly)
        {
            stageTransition = true;
            roomTarget = RoomAt(next);
            SetMusic(roomTarget > 0 ? puzzleMusic : stationMusic);
            yield return Animate(portraitOnly ? .22f : .38f, t => { actorAlpha = 1 - t; if (!portraitOnly) panelAlpha = 1 - t; });
            DialogueIndex = next; characters = autoClock = 0; RecordCurrent(); ApplyExpression(dialogue[next]);
            yield return Animate(portraitOnly ? .28f : .48f, t => { actorAlpha = t; if (!portraitOnly) panelAlpha = t; });
            actorAlpha = panelAlpha = 1; stageTransition = false;
        }
        IEnumerator RevealLineup()
        {
            stageTransition = true; auto = skipping = false;
            yield return Animate(.4f, t => { actorAlpha = panelAlpha = 1 - t; });
            Investigating = true; lineupPage = 0; suspectHovered = -1;
            yield return Animate(.5f, t => actorAlpha = t);
            actorAlpha = 1; stageTransition = false;
        }

        void UpdatePresentation()
        {
            float dt = Time.unscaledDeltaTime;
            roomBlend = Mathf.MoveTowards(roomBlend, roomTarget, dt / .8f);
            narrationBlend = Mathf.MoveTowards(narrationBlend, IsNarration ? 1 : 0, dt / .65f);
            fullscreenBlend = Mathf.MoveTowards(fullscreenBlend, Preferences.fullscreen ? 1 : 0, dt / .22f);
            string role = Case == null || dialogue.Count == 0 ? "officer" : dialogue[DialogueIndex].role;
            if (Case != null && dialogue[DialogueIndex].speaker?.zh == "你") role = "player";
            playerBrightness = Mathf.Lerp(playerBrightness, role == "player" ? 1 : .55f, 1 - Mathf.Exp(-dt * 8));
            officerBrightness = Mathf.Lerp(officerBrightness, role == "player" ? .55f : 1, 1 - Mathf.Exp(-dt * 8));
            int hovered = -1;
            if (IsMainMenu && Overlay == "" && confirmation == "" && !TransitionBusy)
                for (int i = 0; i < 4; i++) if (MenuRect(i).Contains(pointer)) hovered = i;
            if (hovered != menuHovered && hovered >= 0) PlayEffect(buttonHover);
            menuHovered = hovered;
            for (int i = 0; i < 4; i++)
            {
                float target = menuPressed == i || (hovered == i && pointerHeld) ? .94f : hovered == i ? 1.065f : 1;
                menuScales[i] = Mathf.Lerp(menuScales[i], target, 1 - Mathf.Exp(-dt * 20));
            }
            musicBlend = Mathf.MoveTowards(musicBlend, 1, dt / .7f);
            if (music) music.volume = Preferences.musicVolume * musicBlend;
            if (outgoingMusic) { outgoingMusic.volume = Preferences.musicVolume * (1 - musicBlend); if (musicBlend >= 1) outgoingMusic.Stop(); }
            if (effects) effects.volume = Preferences.sfxVolume;
        }

        void DrawFullBackground()
        {
            Rect full = new Rect(0, 0, Screen.width, Screen.height);
            if (introComicVisible)
            {
                DrawComic(full);
                return;
            }
            if (IsMainMenu && menuBackground) GUI.DrawTexture(full, menuBackground, ScaleMode.ScaleAndCrop);
            else
            {
                if (background) GUI.DrawTexture(full, background, ScaleMode.ScaleAndCrop);
                if (interrogationBackground && roomBlend > 0)
                { GUI.color = new Color(1, 1, 1, roomBlend); GUI.DrawTexture(full, interrogationBackground, ScaleMode.ScaleAndCrop); GUI.color = Color.white; }
                if (narrationBlend > 0) Box(full, new Color(0, 0, 0, narrationBlend), 0);
            }
        }
        void DrawActors()
        {
            if (Investigating || IsNarration) return;
            var line = dialogue[DialogueIndex];
            if (InterviewAt(DialogueIndex) && line.suspectIndex >= 0 && line.suspectIndex < portraits.Length)
            {
                var portrait = portraits[line.suspectIndex];
                Draw(portrait, FitPortrait(portrait, new Rect(530, 155, 540, 540)), actorAlpha);
            }
            else
            {
                DrawTinted(shownPlayer ? shownPlayer : player, new Rect(150, 303, 335, 335f * 1191 / 458), new Color(playerBrightness, playerBrightness, playerBrightness, actorAlpha));
                DrawTinted(shownOfficer ? shownOfficer : officer, new Rect(1060, 104, 446, 446f * 1433 / 584), new Color(officerBrightness, officerBrightness, officerBrightness, actorAlpha));
            }
        }

        Rect MenuRect(int i) => new Rect(180, 349 + i * 87, 282, 65);
        void MainMenu()
        {
            Draw(titleArt, new Rect(24, 85, 580, 194));
            string[] texts = { L("开始游戏", "New game"), L("读取存档", "Load game"), L("设置", "Settings"), L("退出游戏", "Quit game") };
            for (int i = 0; i < 4; i++)
            {
                Rect hit = MenuRect(i), shown = ScaleRect(hit, menuScales[i]);
                bool hot = menuHovered == i || menuPressed == i;
                Draw(hot ? menuHover : menuIdle, shown);
                if (i == 2 && settingsIcon) DrawTinted(settingsIcon, new Rect(shown.x + 31, shown.center.y - 17, 28, 28), hot ? Color.white : Ink);
                else if (i == 3) ExitIcon(new Rect(shown.x + 32, shown.center.y - 16, 27, 27), hot ? Color.white : Blue);
                else Label(new Rect(shown.x + 27, shown.y, 37, shown.height - 6), i == 0 ? "▷" : i == 1 ? "▤" : "⚙", 25, hot ? Color.white : Blue, TextAnchor.MiddleCenter);
                Label(new Rect(shown.x + 67, shown.y, shown.width - 87, shown.height - 6), texts[i], 24, hot ? Color.white : Blue, TextAnchor.MiddleCenter);
                if (Hit(hit)) { PlayEffect(buttonClick); StartCoroutine(ActivateMenu(i)); }
            }
        }
        static Rect ScaleRect(Rect r, float scale) => new Rect(r.center.x - r.width * scale / 2, r.center.y - r.height * scale / 2, r.width * scale, r.height * scale);
        void ExitIcon(Rect r, Color color)
        {
            Box(new Rect(r.x, r.y, 11, 2), color, 0);
            Box(new Rect(r.x, r.y, 2, r.height), color, 0);
            Box(new Rect(r.x, r.yMax - 2, 11, 2), color, 0);
            Box(new Rect(r.x + 9, r.center.y - 1, 16, 2), color, 0);
            for (int i = 0; i <= 7; i++)
            {
                Box(new Rect(r.xMax - 3 - i, r.center.y - 1 - i, 2, 2), color, 0);
                Box(new Rect(r.xMax - 3 - i, r.center.y - 1 + i, 2, 2), color, 0);
            }
        }
        IEnumerator ActivateMenu(int index)
        {
            menuActionPending = true; menuPressed = index;
            yield return new WaitForSecondsRealtime(.12f);
            menuPressed = -1; menuActionPending = false;
            switch (index)
            {
                case 0: StartNewGame(); break;
                case 1: OpenLoadMenu(); break;
                case 2: OpenSettings(); break;
                case 3: confirmation = "quit"; break;
            }
        }
        void StandaloneLoad()
        {
            var frameStyle = new GUIStyle(); frameStyle.normal.background = loadFrame; frameStyle.border = new RectOffset(48, 48, 48, 48);
            GUI.Box(new Rect(403, 129, 795, 656), GUIContent.none, frameStyle);
            Label(new Rect(454, 181, 570, 45), L("读取存档", "Load game"), 31);
            if (Button(new Rect(1091, 172, 49, 43), "×")) CloseOverlay();
            Box(new Rect(454, 235, 692, 2), new Color32(211, 226, 243, 255), 0);
            Slots(false, 454, 258);
        }

        void FullscreenToggle(Rect r)
        {
            if (Hit(r)) { Preferences.fullscreen = !Preferences.fullscreen; PlayEffect(buttonClick); }
            // Use identical geometry at both endpoints and throughout the slide.
            // Switching between padded artwork and procedural shapes caused a size jump.
            Rect trackRect = new Rect(r.x, r.y, r.width, 56);
                Color track = Color.Lerp(new Color(.94f, .97f, 1), new Color32(54, 120, 191, 255), fullscreenBlend);
                Box(trackRect, Blue, 28);
                Box(new Rect(r.x + 2, r.y + 2, r.width - 4, 52), track, 26);
                Color thumb = Color.Lerp(new Color32(81, 148, 222, 255), Color.white, fullscreenBlend);
                Box(new Rect(r.x + 6 + fullscreenBlend * (r.width - 56), r.y + 6, 44, 44), thumb, 22);
        }
        void ApplyDisplay()
        {
            if (Application.isEditor) return;
            int[] widths = { 1280, 1600, 1920, 2560 }, heights = { 720, 900, 1080, 1440 };
            Screen.SetResolution(widths[Preferences.resolution], heights[Preferences.resolution], Preferences.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }

        bool Female(int index)
        {
            string gender = Case.suspects[index].gender?.ToLowerInvariant();
            return gender == "female" || gender == "girl" || gender == "女";
        }
        void AssignPortraits(string[] saved)
        {
            portraitIds = new string[Case.suspectCount]; portraits = new Texture2D[Case.suspectCount];
            var used = new HashSet<Texture2D>();
            for (int i = 0; i < portraits.Length; i++)
            {
                var pool = Female(i) ? girlPortraits : boyPortraits;
                if (pool == null || pool.Length == 0) throw new InvalidOperationException("Assign the boy and girl suspect libraries.");
                string prefix = Female(i) ? "girl/" : "boy/";
                Texture2D chosen = null;
                if (saved != null && i < saved.Length) chosen = Array.Find(pool, t => t && prefix + t.name == saved[i]);
                if (!chosen)
                {
                    var available = new List<Texture2D>(); foreach (var texture in pool) if (texture && !used.Contains(texture)) available.Add(texture);
                    if (available.Count == 0) foreach (var texture in pool) if (texture) available.Add(texture);
                    if (available.Count == 0) throw new InvalidOperationException("Suspect library contains no valid textures.");
                    chosen = available[UnityEngine.Random.Range(0, available.Count)];
                }
                portraits[i] = chosen; used.Add(chosen); portraitIds[i] = prefix + chosen.name;
            }
        }
        PortraitVisual GetPortraitVisual(Texture2D texture)
        {
            if (portraitVisuals.TryGetValue(texture, out var cached)) return cached;
            // Read a temporary GPU copy: source Art import settings and source files stay untouched.
            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            var old = RenderTexture.active; Texture2D copy = null;
            try
            {
                Graphics.Blit(texture, target); RenderTexture.active = target;
                copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); copy.Apply();
                var pixels = copy.GetPixels32(); const int padding = PortraitOutlinePadding;
                int width = texture.width + padding * 2, height = texture.height + padding * 2;
                var dilated = new Color32[width * height];
                for (int y = 0; y < texture.height; y++) for (int x = 0; x < texture.width; x++)
                {
                    if (pixels[y * texture.width + x].a < 35) continue;
                    // Only expand boundary pixels, keeping construction cheap for opaque silhouettes.
                    bool edge = x == 0 || y == 0 || x == texture.width - 1 || y == texture.height - 1 ||
                        pixels[y * texture.width + Math.Max(0, x - 1)].a < 35 || pixels[y * texture.width + Math.Min(texture.width - 1, x + 1)].a < 35 ||
                        pixels[Math.Max(0, y - 1) * texture.width + x].a < 35 || pixels[Math.Min(texture.height - 1, y + 1) * texture.width + x].a < 35;
                    if (!edge) continue;
                    for (int dy = -padding; dy <= padding; dy++) for (int dx = -padding; dx <= padding; dx++)
                        if (dx * dx + dy * dy <= padding * padding) dilated[(y + padding + dy) * width + x + padding + dx] = OutlineBlue;
                }
                var outline = new Texture2D(width, height, TextureFormat.RGBA32, false); outline.SetPixels32(dilated); outline.Apply();
                cached = new PortraitVisual { outline = outline, pixels = pixels, width = texture.width, height = texture.height };
                portraitVisuals.Add(texture, cached); return cached;
            }
            finally { RenderTexture.active = old; RenderTexture.ReleaseTemporary(target); if (copy) Destroy(copy); }
        }
        bool PortraitContains(Rect r, Texture2D texture)
        {
            if (!r.Contains(pointer)) return false;
            var visual = GetPortraitVisual(texture);
            int x = Mathf.Clamp((int)((pointer.x - r.x) / r.width * visual.width), 0, visual.width - 1);
            int y = Mathf.Clamp((int)((1 - (pointer.y - r.y) / r.height) * visual.height), 0, visual.height - 1);
            return visual.pixels[y * visual.width + x].a > 35;
        }
        public Rect SuspectRect(int index)
        {
            int start = lineupPage * 5, count = Mathf.Min(5, Case.suspectCount - start);
            float cell = 1440f / count, size = Mathf.Min(360, cell - 40);
            return FitPortrait(portraits[index], new Rect(80 + (index - start) * cell + (cell - size) / 2, 686 - size, size, size));
        }
        static Rect FitPortrait(Texture texture, Rect bounds)
        {
            if (!texture) return bounds;
            float scale = Mathf.Min(bounds.width / texture.width, bounds.height / texture.height);
            float width = texture.width * scale, height = texture.height * scale;
            return new Rect(bounds.center.x - width / 2, bounds.yMax - height, width, height);
        }
        void DrawLineup()
        {
            var old = GUI.color; GUI.color = new Color(1, 1, 1, actorAlpha);
            Box(new Rect(410, 113, 780,  60), new Color(1, .99f, .96f, .92f));
            Label(new Rect(430, 121, 740,  40), Solved ? L("案件已破获 · 可以回顾推理", "CASE CLOSED · Review your deduction") : L("选择嫌疑人，提交你的判断", "Select a suspect, then submit your verdict"), 27, Ink, TextAnchor.MiddleCenter);
            int start = lineupPage * 5, count = Mathf.Min(5, Case.suspectCount - start), hovered = -1;
            float cell = 1440f / count;
            for (int i = start; i < start + count; i++)
            {
                Rect portrait = SuspectRect(i);
                float width = Mathf.Min(cell - 18, 418), center = portrait.center.x;
                Rect bubble = new Rect(center - width / 2, 194, width, 132);
                Draw(dialogueFrame, bubble);
                string testimony = Case.suspects[i].testimony.Get(Preferences.english);
                int fontSize = count > 4 ? 20 : 23;
                // Long testimonies remain fully available in the notebook; fit short demo statements without clipping.
                Label(new Rect(bubble.x + 22, bubble.y + 18, bubble.width - 44, bubble.height - 36), testimony, fontSize, Ink, TextAnchor.MiddleCenter);
                bool hover = GUI.enabled && !Solved && !TutorialBlocking && PortraitContains(portrait, portraits[i]);
                if (hover) hovered = i;
                if (hover || SelectedSuspect == i)
                {
                    var visual = GetPortraitVisual(portraits[i]); float pad = portrait.width * PortraitOutlinePadding / visual.width;
                    Draw(visual.outline, new Rect(portrait.x - pad, portrait.y - pad, portrait.width + pad * 2, portrait.height + pad * 2));
                }
                Draw(portraits[i], portrait);
                string key = "suspect:" + i;
                if (processInput && GUI.enabled && !TransitionBusy && !Solved)
                {
                    if (pointerDown && hover) pressedControl = key;
                    if (pointerUp && hover && pressedControl == key) { SelectedSuspect = i; PlayEffect(buttonClick); }
                }
                Box(new Rect(center - width / 2 + 18, 701, width - 36, 47), SelectedSuspect == i ? Blue : Pale);
                Label(new Rect(center - width / 2 + 18, 701, width - 36, 47), Case.suspects[i].name.Get(Preferences.english), 25, SelectedSuspect == i ? Color.white : Ink, TextAnchor.MiddleCenter);
            }
            if (processInput) { if (hovered >= 0 && hovered != suspectHovered) PlayEffect(buttonClick); suspectHovered = hovered; }
            if (Button(new Rect(342, 777, 235, 49), L("打开推理本", "Investigation notes"))) OpenBook(1);
            bool enabled = GUI.enabled; GUI.enabled = enabled && SelectedSuspect >= 0;
            if (Button(new Rect(972, 777, 286, 49), Solved ? L("查看推理", "View deduction") : L("提交判断", "Submit verdict"), true))
            { if (Solved) Overlay = "result"; else SubmitVerdict(SelectedSuspect); }
            GUI.enabled = enabled;
            if (Case.suspectCount > 5)
            {
                GUI.enabled = enabled && lineupPage > 0;
                if (Button(new Rect(622, 785,  60, 40), "‹")) { lineupPage--; suspectHovered = -1; }
                GUI.enabled = enabled;
                Label(new Rect(697, 787,  90, 36), (lineupPage + 1) + " / " + Mathf.CeilToInt(Case.suspectCount / 5f), 20, Ink, TextAnchor.MiddleCenter);
                GUI.enabled = enabled && (lineupPage + 1) * 5 < Case.suspectCount;
                if (Button(new Rect(801, 785,  60, 40), "›")) { lineupPage++; suspectHovered = -1; }
                GUI.enabled = enabled;
            }
            Label(new Rect(160, 845, 1280, 32), Case.summary.Get(Preferences.english), 19, Ink, TextAnchor.MiddleCenter);
            GUI.color = old;
        }

        void CaseNotes()
        {
            var heights = new float[Case.suspectCount]; float total = 0;
            for (int i = 0; i < heights.Length; i++) { heights[i] = TextStyle(22, Ink, TextAnchor.UpperLeft).CalcHeight(new GUIContent(Case.suspects[i].testimony.Get(Preferences.english)), 420) + 76; total += heights[i]; }
            float hintHeight = Case.tutorial ? TextStyle(20, Muted, TextAnchor.UpperLeft).CalcHeight(new GUIContent(TutorialNotebookHint), 420) + 66 : 0;
            total += hintHeight;
            BeginScroll(new Rect(286, 224, 452, 492), ref testimonyScroll, total);
            float y = 0;
            for (int i = 0; i < Case.suspectCount; i++)
            {
                Label(new Rect(4, y, 418, 34), Case.suspects[i].name.Get(Preferences.english), 24, Blue);
                Label(new Rect(4, y + 40, 420, heights[i] - 54), Case.suspects[i].testimony.Get(Preferences.english), 22);
                y += heights[i];
            }
            if (Case.tutorial)
            {
                Label(new Rect(4, y + 8, 65, 32), L("提示", "Hint"), 24, Blue);
                Draw(tutorialHintIcon, new Rect(72, y + 11, 25, 25));
                Label(new Rect(4, y + 48, 420, hintHeight - 48), TutorialNotebookHint, 20, Muted);
            }
            EndScroll();
            Label(new Rect(854, 165, 415, 45), notesTable ? L("假设检验", "Test a hypothesis") : L("推演草稿", "Reasoning sketch"), 27);
            if (notesTable) NotesTablePage(); else NotesDrawingPage();
            if (Button(new Rect(854, 727, 245, 43), notesTable ? L("切换草稿", "Switch to sketch") : L("切换表格", "Switch to table"))) ToggleNotes();
            if (Button(new Rect(1159, 727, 157, 43), notesTable ? L("清除表格", "Clear table") : L("清除画布", "Clear sketch")))
            {
                if (notesTable) { Array.Clear(TruthMarks, 0, TruthMarks.Length); Array.Clear(CulpritMarks, 0, CulpritMarks.Length); }
                else ClearDrawing();
            }
        }
        void NotesDrawingPage()
        {
            if (Button(new Rect(855, 215, 109, 40), L("笔", "Pen"), !eraser)) { eraser = false; penOptions = !penOptions; }
            if (Button(new Rect(977, 215, 125, 40), L("橡皮", "Eraser"), eraser)) { eraser = true; penOptions = false; }
            Rect paper = new Rect(855, 277, 460, 440);
            Box(paper, Color.white, 0); Draw(drawing, paper); HandleDrawing(paper);
            if (!penOptions) return;
            Box(new Rect(856, 263, 460, 112), Pale);
            Label(new Rect(871, 271, 86, 30), L("粗细", "Width"), 18);
            penWidth = Slider(new Rect(960, 278, 251, 20), penWidth, 1, 14);
            Label(new Rect(1230, 272, 60, 30), Mathf.RoundToInt(penWidth).ToString(), 18);
            Color[] colors = { Ink, new Color32(197, 65, 69, 255), new Color32(40, 141, 115, 255), new Color32(231, 159, 42, 255), new Color32(141, 92, 165, 255) };
            for (int i = 0; i < colors.Length; i++)
            {
                if (Button(new Rect(877 + i * 77, 317, 64, 40), "")) penColor = colors[i];
                Box(new Rect(896 + i * 77, 325, 26, 24), colors[i]);
            }
        }
        void NotesTablePage()
        {
            Label(new Rect(854, 216, 463, 55), L("勾选仅是草稿，不会提交答案。", "These marks are notes, not your verdict."), 19, Muted);
            string[] headers = { L("姓名", "Name"), L("真", "True"), L("假", "False"), L("犯人", "Guilty") };
            float[] xs = { 0, 166, 260, 354 }; float[] ws = { 166, 94, 94, 108 };
            for (int c = 0; c < 4; c++) { Box(new Rect(855 + xs[c], 283, ws[c] - 2, 51), Blue, 0); Label(new Rect(855 + xs[c], 284, ws[c] - 2, 49), headers[c], 21, Color.white, TextAnchor.MiddleCenter); }
            BeginScroll(new Rect(855, 339, 480, 365), ref tableScroll, Case.suspectCount * 67);
            for (int row = 0; row < Case.suspectCount; row++)
            {
                Box(new Rect(0, row * 67, 164, 63), Pale, 0);
                Label(new Rect(12, row * 67, 145, 63), Case.suspects[row].name.Get(Preferences.english), 21, Ink, TextAnchor.MiddleLeft);
                for (int col = 1; col <= 3; col++)
                {
                    bool marked = col == 3 ? CulpritMarks[row] : TruthMarks[row] == col;
                    if (Button(new Rect(xs[col], row * 67, ws[col] - 2, 63), marked ? "✓" : "", marked, 29)) { if (col == 3) MarkCulprit(row); else MarkTruth(row, col); }
                }
            }
            EndScroll();
        }

        void SetupAudio()
        {
            music = gameObject.AddComponent<AudioSource>(); music.loop = true; music.playOnAwake = false;
            outgoingMusic = gameObject.AddComponent<AudioSource>(); outgoingMusic.loop = true; outgoingMusic.playOnAwake = false;
            effects = gameObject.AddComponent<AudioSource>(); effects.playOnAwake = false;
        }
        void SetMusic(AudioClip clip)
        {
            if (!music || !clip || music.clip == clip) return;
            var swap = outgoingMusic; outgoingMusic = music; music = swap;
            music.Stop(); music.clip = clip; music.volume = 0; music.loop = true; music.Play(); musicBlend = 0;
        }
        void PlayEffect(AudioClip clip)
        {
            if (!clip || !effects) return;
            LastEffect = clip.name; EffectSerial++; effects.volume = Preferences.sfxVolume; effects.PlayOneShot(clip);
        }
        void DisposePortraitVisuals() { foreach (var visual in portraitVisuals.Values) if (visual.outline) Destroy(visual.outline); portraitVisuals.Clear(); }
    }
}

