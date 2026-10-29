using System;
using System.Collections.Generic;
using UnityEngine;

namespace MathPolice
{
    [Serializable] public class LocalizedText
    {
        public string zh, en;
        public string Get(bool english) => english && !string.IsNullOrEmpty(en) ? en : zh;
    }
    [Serializable] public class StatementRule
    {
        public string kind;
        public int target;
        public bool factValue;
        public StatementRule[] terms;
    }
    [Serializable] public class SuspectDefinition
    {
        public LocalizedText name, testimony;
        public string gender = "male";
        public StatementRule rule;
    }
    [Serializable] public class DialogueDefinition
    {
        public LocalizedText speaker, text;
        public string role = "officer", expression = "normal", playerExpression, officerExpression;
        public int suspectIndex = -1;
        // Stage controls the cast independently of the speaker (e.g. questions during an interview).
        public string stage;
    }
    [Serializable] public class CaseDefinition
    {
        public string id;
        public bool tutorial;
        public LocalizedText title, summary, explanation;
        public int suspectCount, truthCount, culpritIndex;
        public SuspectDefinition[] suspects;
        public DialogueDefinition[] introduction;
        public DialogueDefinition[] script;

        public void Validate()
        {
            if (string.IsNullOrEmpty(id) || suspects == null || suspects.Length < 2 || suspects.Length != suspectCount)
                throw new ArgumentException("Case requires an id and suspectCount matching the suspect list (at least two).");
            if (culpritIndex < 0 || culpritIndex >= suspectCount || truthCount < 0 || truthCount > suspectCount)
                throw new ArgumentException("Invalid culpritIndex or truthCount.");
            foreach (var s in suspects)
                if (s.name == null || string.IsNullOrEmpty(s.name.zh) || s.testimony == null || string.IsNullOrEmpty(s.testimony.zh))
                    throw new ArgumentException("Every suspect needs a name and testimony.");
            if (script != null && script.Length > 0)
                foreach (var line in script)
                {
                    if (line.speaker == null || line.text == null || string.IsNullOrEmpty(line.text.zh))
                        throw new ArgumentException("Script lines require speaker and text.");
                    if (line.stage != "narration" && line.stage != "station" && line.stage != "interrogation" && line.stage != "debrief")
                        throw new ArgumentException("Unknown script stage.");
                    if (line.stage == "interrogation" && (line.suspectIndex < 0 || line.suspectIndex >= suspectCount))
                        throw new ArgumentException("Interview lines need the visible suspect index.");
                }
            // Rules are optional for author-written stories. Complete rules enable uniqueness verification.
            if (Array.TrueForAll(suspects, s => s.rule != null && !string.IsNullOrEmpty(s.rule.kind)))
            {
                var solutions = Solve();
                if (solutions.Count != 1 || solutions[0] != culpritIndex)
                    throw new ArgumentException("Case rules do not yield the configured unique culprit.");
            }
        }

        public List<int> Solve()
        {
            if (suspectCount > 16) throw new ArgumentException("Rule verification supports up to 16 suspects; omit rules for larger authored cases.");
            var solutions = new List<int>();
            for (int culprit = 0; culprit < suspectCount; culprit++)
            for (int mask = 0; mask < (1 << suspectCount); mask++)
            {
                int count = 0;
                for (int i = 0; i < suspectCount; i++) if ((mask & (1 << i)) != 0) count++;
                if (count != truthCount) continue;
                bool valid = true;
                for (int i = 0; i < suspectCount; i++)
                {
                    var r = suspects[i].rule;
                    bool value = Evaluate(r, culprit, mask);
                    if (value != ((mask & (1 << i)) != 0)) { valid = false; break; }
                }
                if (valid && !solutions.Contains(culprit)) solutions.Add(culprit);
            }
            return solutions;
        }

        bool Evaluate(StatementRule rule, int culprit, int mask)
        {
            if (rule == null) throw new ArgumentException("Missing rule.");
            if (rule.kind == "all" || rule.kind == "any" || rule.kind == "implies")
            {
                if (rule.terms == null || rule.terms.Length != 2) throw new ArgumentException("Compound rules require two terms.");
                bool a = Evaluate(rule.terms[0], culprit, mask), b = Evaluate(rule.terms[1], culprit, mask);
                return rule.kind == "all" ? a && b : rule.kind == "any" ? a || b : !a || b;
            }
            if (rule.kind != "fact" && (rule.target < 0 || rule.target >= suspectCount)) throw new ArgumentException("Invalid rule target.");
            switch (rule.kind)
            {
                case "culpritIs": return culprit == rule.target;
                case "culpritIsNot": return culprit != rule.target;
                case "statementTrue": return (mask & (1 << rule.target)) != 0;
                case "statementFalse": return (mask & (1 << rule.target)) == 0;
                case "fact": return rule.factValue;
                default: throw new ArgumentException("Unknown rule: " + rule.kind);
            }
        }
    }

    [Serializable] public class DemoPreferences
    {
        public bool english;
        public bool fullscreen = true;
        public float textSpeed = 36, autoDelay = 1.8f, musicVolume = .25f, sfxVolume = .5f, fontSize = 26, opacity = .96f;
        public int resolution = 2;
    }
    [Serializable] public class DemoSave
    {
        public int version = 3;
        public string caseId, savedAt, drawingPng, thumbnailPng;
        public int dialogueIndex, revealedCharacters, selectedSuspect = -1;
        public bool investigating, solved;
        public int[] history, truthMarks;
        public bool[] culpritMarks;
        public string[] portraitIds;
        public bool notesTable;
        public bool keyTutorialSeen, notebookTutorialSeen, introComicVisible;
        public int tutorialStep;
    }
}
