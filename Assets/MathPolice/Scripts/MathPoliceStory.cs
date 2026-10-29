using System;
using System.Text;
using UnityEngine;

namespace MathPolice
{
    public sealed partial class MathPoliceDemo
    {
        static readonly Color StatementOrange = new Color(.91f, .57f, .24f);
        const string StatementOpen = "<b><color=#E8913D>";
        const string StatementClose = "</color></b>";
        float narrationBlend;

        void BuildDialogue()
        {
            dialogue.Clear();
            if (Case.script != null && Case.script.Length > 0) dialogue.AddRange(Case.script);
            else
            {
                if (Case.introduction != null)
                    foreach (var line in Case.introduction) { line.stage = "station"; dialogue.Add(line); }
                for (int i = 0; i < Case.suspectCount; i++) dialogue.Add(new DialogueDefinition {
                    speaker = Case.suspects[i].name, text = Case.suspects[i].testimony,
                    role = "suspect", stage = "interrogation", suspectIndex = i });
                dialogue.Add(new DialogueDefinition {
                    role = "officer", stage = "debrief", expression = "smile", playerExpression = "thinking",
                    speaker = new LocalizedText { zh = "警长", en = "Chief" }, text = new LocalizedText {
                        zh = "供词已经记录完毕。你可以打开本子整理思路，再选择嫌疑人，提交你的判断。",
                        en = "All statements are recorded. Work through your notebook, then select a suspect and submit your verdict." } });
            }
            foreach (var line in dialogue)
                if (line.role == "officer") line.speaker = new LocalizedText { zh = "警长", en = "Chief" };
        }

        bool IsNarration => !IsMainMenu && !Investigating && dialogue.Count > 0 && dialogue[DialogueIndex].stage == "narration";
        bool InterviewAt(int index) => dialogue[index].stage == "interrogation";
        float RoomAt(int index) => dialogue[index].stage == "interrogation" || dialogue[index].stage == "debrief" ? 1 : 0;

        string FormatSpeech(DialogueDefinition line, int offset, int length)
        {
            bool english = Preferences.english && !string.IsNullOrEmpty(line.text.en);
            return HighlightRange(line.text.Get(Preferences.english), offset, length, english);
        }

        string FormatHistory(DialogueDefinition line, int offset, int length)
        {
            string full = line.speaker.Get(Preferences.english) + "\n" + line.text.Get(Preferences.english) + "\n\n";
            return HighlightRange(full, offset, length, Preferences.english && !string.IsNullOrEmpty(line.text.en));
        }

        string HighlightRange(string full, int offset, int length, bool english)
        {
            offset = Mathf.Clamp(offset, 0, full.Length);
            int end = Mathf.Clamp(offset + length, offset, full.Length);
            var marked = new bool[full.Length];
            foreach (var suspect in Case.suspects)
            {
                string key = suspect.testimony.Get(english);
                if (string.IsNullOrEmpty(key)) continue;
                // A key claim remains the same when followed by a comma instead of a period.
                if (full.IndexOf(key, StringComparison.Ordinal) < 0) key = key.TrimEnd('。', '.');
                for (int found = full.IndexOf(key, StringComparison.Ordinal); found >= 0;
                    found = full.IndexOf(key, found + key.Length, StringComparison.Ordinal))
                    for (int i = found; i < found + key.Length; i++) marked[i] = true;
            }
            // Apply tags after slicing visible characters; partial typewriter lines never expose markup.
            var result = new StringBuilder(); bool open = false;
            for (int i = offset; i < end; i++)
            {
                if (marked[i] != open) { result.Append(marked[i] ? StatementOpen : StatementClose); open = marked[i]; }
                result.Append(full[i] == '<' ? '＜' : full[i] == '>' ? '＞' : full[i]);
            }
            if (open) result.Append(StatementClose);
            return result.ToString();
        }

        GUIStyle RichStyle(int size) => new GUIStyle(TextStyle(size, Ink, TextAnchor.UpperLeft)) { richText = true };
        void RichLabel(Rect r, string text, int size) => GUI.Label(r, text, RichStyle(size));
    }
}
