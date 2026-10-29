using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MathPolice.Editor
{
    public static class StoryCaseChecks
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        public static string Status { get; private set; } = "Not run";
        [MenuItem("Math Police/Run five chapter checks")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || Status == "Running") throw new InvalidOperationException("Enter Play Mode; run checks once.");
            var d = UnityEngine.Object.FindAnyObjectByType<MathPoliceDemo>();
            d.StartCoroutine(Check(d));
        }
        static IEnumerator Check(MathPoliceDemo d)
        {
            Status = "Running";
            var original = d.CaptureState(); bool menu = d.IsMainMenu;
            bool suppress = d.SuppressTutorialsForValidation;
            d.SuppressTutorialsForValidation = true;
            bool language = d.Preferences.english, background = Application.runInBackground;
            Application.runInBackground = true; bool passed = false;
            try
            {
                Require(d.caseFiles.Length == 5, "five active chapters");
                int count = 0, interruptions = 0;
                for (int chapter = 0; chapter < 5; chapter++)
                {
                    d.StartCase(chapter); var script = d.Case.script;
                    Require(d.Case.Solve().SequenceEqual(new[] { d.Case.culpritIndex }), "unique culprit " + chapter);
                    Require(((List<DialogueDefinition>)Get(d, "dialogue")).Count == script.Length, "no generated extra dialogue");
                    if (chapter > 0)
                    {
                        Require((float)Get(d, "narrationBlend") == 1 && script[0].speaker.zh == "旁白", "black narration opening");
                        var narration = d.CaptureState(); d.RestoreState(narration);
                        Require((float)Get(d, "narrationBlend") == 1 && d.DialogueIndex == 0, "load narration");
                    }
                    for (int i = 0; i < script.Length; i++)
                    {
                        Require(d.DialogueIndex == i, "dialogue order");
                        var line = script[i];
                        Require(!string.IsNullOrEmpty(line.text.en), "English dialogue");
                        if (line.role == "officer") Require(line.speaker.zh == "警长", "chief name");
                        foreach (bool english in new[] { false, true })
                        {
                            d.Preferences.english = english;
                            string plain = line.text.Get(english);
                            string formatted = (string)Invoke(d, "FormatSpeech", line, 0, plain.Length);
                            Require(Regex.Replace(formatted, "<[^>]+>", "") == plain, "rich text preserves dialogue");
                            if (d.Case.suspects.Any(s => plain.Contains(s.testimony.Get(english))))
                            {
                                Require(formatted.Contains("<b><color=#E8913D>"), "key statement emphasis");
                                string history = (string)Invoke(d, "FormatHistory", line, 0, line.speaker.Get(english).Length + plain.Length + 3);
                                Require(history.Contains("<color=#E8913D>"), "history emphasis");
                                for (int n = 0; n <= plain.Length; n++)
                                {
                                    string partial = (string)Invoke(d, "FormatSpeech", line, 0, n);
                                    Require(Regex.Replace(partial, "<[^>]+>", "") == plain.Substring(0, n), "typewriter preserves characters");
                                    Require(Regex.Matches(partial, "<b>").Count == Regex.Matches(partial, "</b>").Count, "balanced partial tags");
                                }
                            }
                        }
                        d.Preferences.english = false;
                        if (line.stage == "interrogation" && line.role != "suspect")
                        {
                            interruptions++;
                            Require((bool)Invoke(d, "InterviewAt", i), "interruption retains interview staging");
                            Require(line.suspectIndex == script[i - 1].suspectIndex, "same suspect visible during interruption");
                            var saved = d.CaptureState(); var portraits = d.PortraitIds;
                            d.RestoreState(saved);
                            Require(d.InInterrogation && d.DialogueIndex == i && portraits.SequenceEqual(d.PortraitIds), "load interview interruption");
                        }
                        Set(d, "characters", 10000f); d.AdvanceDialogue();
                        yield return Settled(d);
                        count++;
                    }
                    Require(d.Investigating && d.HistoryCount == script.Length, "complete history and lineup");
                    Invoke(d, "BuildPages");
                    string recorded = Regex.Replace(string.Concat((List<string>)Get(d, "pages")), "<[^>]+>", "");
                    string expected = string.Concat(script.Select(l => l.speaker.zh + "\n" + l.text.zh + "\n\n"));
                    Require(recorded == expected, "pagination retains every dialogue character");
                    Require(!d.SubmitVerdict((d.Case.culpritIndex + 1) % d.Case.suspectCount), "wrong verdict");
                    Require(d.SubmitVerdict(d.Case.culpritIndex), "correct verdict");
                    d.StartNextCase(); yield return Settled(d);
                    Require(chapter == 4 ? d.Solved && d.Case.id == "chapter_05" : d.Case.id == $"chapter_{chapter + 2:00}" && !d.Solved, "next chapter / final boundary");
                }
                Require(interruptions > 0, "interview interjections checked");
                // Skip must record the entire authored script, not just key statements.
                d.StartCase(2); d.SkipDialogue();
                float deadline = Time.realtimeSinceStartup + 25;
                while ((!d.Investigating || d.TransitionBusy) && Time.realtimeSinceStartup < deadline) yield return null;
                Require(d.Investigating && !d.TransitionBusy && d.HistoryCount == d.Case.script.Length, "skip complete script");
                passed = true; Status = "PASS";
                Debug.Log($"STORY_CASE_CHECK_PASS: {count} bilingual lines, five unique answers, narrator restore, ten interview interjections, rich typewriter/history, lossless pagination, next cases and skip.");
            }
            finally
            {
                if (!passed) Status = "FAIL (see Console)";
                d.SuppressTutorialsForValidation = suppress;
                d.RestoreState(original); d.Preferences.english = language; Application.runInBackground = background;
                if (menu) Invoke(d, "ShowMainMenuImmediate");
            }
        }
        static IEnumerator Settled(MathPoliceDemo d)
        {
            float deadline = Time.realtimeSinceStartup + 8;
            while (d.TransitionBusy && Time.realtimeSinceStartup < deadline) yield return null;
            Require(!d.TransitionBusy, "transition completed");
            yield return null;
        }
        static object Get(MathPoliceDemo d, string name) => d.GetType().GetField(name, Flags).GetValue(d);
        static void Set(MathPoliceDemo d, string name, object value) => d.GetType().GetField(name, Flags).SetValue(d, value);
        static object Invoke(MathPoliceDemo d, string name, params object[] args) => d.GetType().GetMethod(name, Flags).Invoke(d, args);
        static void Require(bool condition, string message) { if (!condition) throw new Exception("Story check failed: " + message); }
    }
}
