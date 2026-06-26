using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FMODUnity;

namespace Mauzik
{

public class Audio_Debugger : EditorWindow
{

    const string ResourcesPath = "Assets/Audio/Resources";
    const string AssetPath = ResourcesPath + "/Audio_Library.asset";

    class ScriptRef { public string path; public int line; public string token; }

    Audio_Library bank;
    SerializedObject so;
    HashSet<string> scriptPkgRefs = new();
    HashSet<string> scriptParamRefs = new();
    List<ScriptRef> orphans = new();
    List<ScriptRef> correctRefs = new();
    bool showCorrectRefs = false;
    Dictionary<string, bool> bankFolds = new();
    Vector2 scroll;

    GUIStyle sDotGreen, sDotRed, sDotGray, sSection, sOrphanSection, sMini, sRichMini;

    [MenuItem("Tools/Audio (FMOD)")]
    static void Open() => GetWindow<Audio_Debugger>("Audio (FMOD)");

    void OnEnable() => RefreshAll();

    void OnValidate() => RefreshAll(); // TODO: To be improved so it's when FMOd refreshes and not on all Validates.

    void RefreshAll()
    {
        bank = AssetDatabase.LoadAssetAtPath<Audio_Library>(AssetPath);
        so = bank != null ? new SerializedObject(bank) : null;
        ScanScripts();
        Repaint();
    }

    void ScanScripts()
    {
        scriptPkgRefs.Clear();
        scriptParamRefs.Clear();
        orphans.Clear();
        correctRefs.Clear();

        var strAssignRe = new Regex(@"(\w+)\s*=\s*""([^""]+)""",                                                    RegexOptions.Compiled);
        var pkgRe       = new Regex(@"Audio_Master\s*\.\s*(?:Get|Attach)\s*\(\s*(?:""([^""]+)""|(\w+))",           RegexOptions.Compiled);
        var paramRe     = new Regex(@"\.Parameter\s*\(\s*(?:""([^""]+)""|(\w+))",                                  RegexOptions.Compiled);
        var paramIdxRe  = new Regex(@"\.Parameter\s*\(\s*(\d+)\s*,",                                               RegexOptions.Compiled);

        var validParamNames = bank?.Packages?
            .Where(p => p?.parameters != null)
            .SelectMany(p => p.parameters)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet() ?? new HashSet<string>();

        int maxParamIdx = bank?.Packages?
            .Where(p => p?.parameters != null && p.parameters.Length > 0)
            .Select(p => p.parameters.Length - 1)
            .DefaultIfEmpty(-1)
            .Max() ?? -1;

        foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
        {
            string ap = AssetDatabase.GUIDToAssetPath(guid);
            if (!ap.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
            string src;
            try { src = File.ReadAllText(ap); } catch { continue; }

            var stringVars = new Dictionary<string, string>();
            foreach (Match m in strAssignRe.Matches(src))
                stringVars[m.Groups[1].Value] = m.Groups[2].Value;

            foreach (Match m in pkgRe.Matches(src))
            {
                string n = m.Groups[1].Success ? m.Groups[1].Value
                    : (m.Groups[2].Success && stringVars.TryGetValue(m.Groups[2].Value, out var v) ? v : null);
                if (n == null) continue;
                scriptPkgRefs.Add(n);
                string token = m.Groups[1].Success ? $"\"{n}\"" : $"{m.Groups[2].Value} (\"{n}\")";
                var sr = new ScriptRef { path = ap, line = LineOf(src, m.Index), token = token };
                if (bank?.Packages == null || !bank.Packages.Any(p => p?.Name == n))
                    orphans.Add(sr);
                else
                    correctRefs.Add(sr);
            }

            foreach (Match m in paramRe.Matches(src))
            {
                string n = m.Groups[1].Success ? m.Groups[1].Value
                    : (m.Groups[2].Success && stringVars.TryGetValue(m.Groups[2].Value, out var v) ? v : null);
                if (n == null) continue;
                scriptParamRefs.Add(n);
                string token = m.Groups[1].Success ? $"\"{n}\"" : $"{m.Groups[2].Value} (\"{n}\")";
                var sr = new ScriptRef { path = ap, line = LineOf(src, m.Index), token = token };
                if (!validParamNames.Contains(n))
                    orphans.Add(sr);
                else
                    correctRefs.Add(sr);
            }

            foreach (Match m in paramIdxRe.Matches(src))
                if (int.TryParse(m.Groups[1].Value, out int idx) && idx > maxParamIdx)
                    orphans.Add(new ScriptRef { path = ap, line = LineOf(src, m.Index), token = $"[{idx}]" });
        }
    }

    static int LineOf(string src, int idx)
    {
        int n = 1;
        for (int i = 0; i < idx && i < src.Length; i++) if (src[i] == '\n') n++;
        return n;
    }

    void EnsureStyles()
    {
        if (sSection != null) return;
        sDotGreen = new GUIStyle(EditorStyles.label) { fontSize = 13, fixedHeight = 18,
            normal = { textColor = new Color(0.30f, 0.85f, 0.30f) } };
        sDotRed = new GUIStyle(EditorStyles.label) { fontSize = 13, fixedHeight = 18,
            normal = { textColor = new Color(0.90f, 0.28f, 0.28f) } };
        sDotGray = new GUIStyle(EditorStyles.label) { fontSize = 13, fixedHeight = 18,
            normal = { textColor = new Color(0.55f, 0.55f, 0.55f) } };
        sSection = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        sOrphanSection = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12,
            normal = { textColor = new Color(0.90f, 0.28f, 0.28f) } };
        sMini = new GUIStyle(EditorStyles.miniLabel) { fixedHeight = 18 };
        sRichMini = new GUIStyle(EditorStyles.miniLabel) { fixedHeight = 18, richText = true };
    }

    void OnGUI()
    {
        EnsureStyles();
        DrawToolbar();

        if (bank == null) { DrawCreateBankPrompt(); return; }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.Space(6);

        DrawScripts();
        Divider();
        DrawEvents();
        EditorGUILayout.Space(8);
        EditorGUILayout.EndScrollView();
    }

    void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("Refresh All", EditorStyles.toolbarButton, GUILayout.Width(80)))
                RefreshAll();
            if (bank != null && GUILayout.Button("Select Asset", EditorStyles.toolbarButton, GUILayout.Width(80)))
            { Selection.activeObject = bank; EditorGUIUtility.PingObject(bank); }
            GUILayout.FlexibleSpace();
            bool ready = EventManager.IsLoaded;
            string status = ready
                ? $"FMOD: {EventManager.Banks.Count} banks · {EventManager.Events.Count} events"
                : "FMOD: cache not loaded";
            GUILayout.Label(status, EditorStyles.miniLabel);
        }
    }

    void DrawCreateBankPrompt()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox($"No Audio_Libray found at {AssetPath}.", MessageType.Warning);
        if (GUILayout.Button("Create Audio_Libray", GUILayout.Height(30)))
        {
            Directory.CreateDirectory(ResourcesPath);
            AssetDatabase.Refresh();
            var a = CreateInstance<Audio_Library>();
            AssetDatabase.CreateAsset(a, AssetPath);
            AssetDatabase.SaveAssets();
            RefreshAll();
        }
    }

    // Script References

    void DrawScripts()
    {
        EditorGUILayout.LabelField("Scripts", sSection);
        EditorGUILayout.Space(4);

        if (orphans.Count > 0)
        {
            EditorGUILayout.LabelField($"  Incorrect  ({orphans.Count})", sOrphanSection);
            EditorGUILayout.Space(2);
            foreach (var r in orphans) DrawRef(r, sDotRed);
            EditorGUILayout.Space(4);
        }

        showCorrectRefs = EditorGUILayout.Foldout(showCorrectRefs, $"  Correct  ({correctRefs.Count})", true);
        if (showCorrectRefs)
        {
            EditorGUILayout.Space(2);
            foreach (var r in correctRefs) DrawRef(r, sDotGreen);
        }
    }

    void DrawRef(ScriptRef r, GUIStyle dot)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("●", dot, GUILayout.Width(14), GUILayout.Height(18));
            if (GUILayout.Button("Open", GUILayout.Width(46), GUILayout.Height(17)))
            {
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(r.path);
                if (ms != null) AssetDatabase.OpenAsset(ms, r.line);
            }
            string rel = r.path.StartsWith("Assets/") ? r.path.Substring(7) : r.path;
            EditorGUILayout.LabelField($"{rel}:{r.line} <b>→</b> {r.token}", sRichMini,
                GUILayout.ExpandWidth(true), GUILayout.Height(18));
        }
        EditorGUILayout.Space(2);
    }

    // Events

    void DrawEvents()
    {
        EditorGUILayout.LabelField("Events", sSection);
        EditorGUILayout.Space(4);

        if (!EventManager.IsLoaded)
        {
            EditorGUILayout.HelpBox(
                "FMOD => Events cache not loaded.",
                MessageType.Info);
            if (bank.Packages != null)
            {
                foreach (var p in bank.Packages)
                {
                    if (p == null) continue;
                    bool used = scriptPkgRefs.Contains(p.Name);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("●", used ? sDotGreen : sDotRed, GUILayout.Width(14));
                        EditorGUILayout.LabelField(p.Event.Path, sMini);
                    }
                }
            }
            return;
        }

        EnsurePackages();

        var allEvents = EventManager.Events?
            .Where(e => e != null && !string.IsNullOrEmpty(e.Path) && e.Path.StartsWith("event:/"))
            .ToList() ?? new List<EditorEventRef>();

        var allBanks = allEvents
            .SelectMany(e => e.Banks ?? Enumerable.Empty<EditorBankRef>())
            .Where(b => b != null)
            .Distinct()
            .OrderBy(b => b.Name)
            .ToList();

        foreach (var bankRef in allBanks)
        {
            var evList = allEvents
                .Where(e => e.Banks != null && e.Banks.Contains(bankRef))
                .OrderBy(e => e.Path)
                .ToList();
            if (evList.Count == 0) continue;

            string key = bankRef.Name;
            if (!bankFolds.ContainsKey(key)) bankFolds[key] = true;
            bankFolds[key] = EditorGUILayout.Foldout(
                bankFolds[key], $"  bank:/{bankRef.Name}", true, EditorStyles.foldoutHeader);
            if (!bankFolds[key]) continue;

            foreach (var ev in evList)
            {
                Audio_Package pkg = FindPkg(ev.Path);
                bool used = pkg != null && scriptPkgRefs.Contains(pkg.Name);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(14);
                    GUILayout.Label("●", used ? sDotGreen : sDotRed, GUILayout.Width(14), GUILayout.Height(18));
                    string label = ev.Path.Length > 60 ? "…" + ev.Path.Substring(ev.Path.Length - 59) : ev.Path;
                    GUILayout.Label(label, sMini, GUILayout.ExpandWidth(true), GUILayout.Height(18));
                }
                EditorGUILayout.Space(2);

                if (pkg != null && ev.LocalParameters != null && ev.LocalParameters.Count > 0)
                {
                    for (int j = 0; j < ev.LocalParameters.Count; j++)
                    {
                        var fp = ev.LocalParameters[j];
                        string pName = (pkg.parameters != null && j < pkg.parameters.Length)
                            ? pkg.parameters[j] : fp.Name;
                        bool pUsed = scriptParamRefs.Contains(pName);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(14);
                            GUILayout.Label("↳", sDotGray, GUILayout.Width(16));
                            GUILayout.Label("●", pUsed ? sDotGreen : sDotRed,
                                GUILayout.Width(14), GUILayout.Height(18));
                            GUILayout.Label($"{pName} <b>({fp.Min}–{fp.Max})</b>", sRichMini,
                                GUILayout.ExpandWidth(true), GUILayout.Height(18));
                        }
                        EditorGUILayout.Space(2);
                    }
                }
            }
            EditorGUILayout.Space(4);
        }
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    Audio_Package FindPkg(string eventPath) =>
        bank?.Packages?.FirstOrDefault(p => p != null && p.Event.Path == eventPath);

    void EnsurePackages()
    {
        if (!EventManager.IsLoaded || bank == null || so == null) return;

        var allEvents = EventManager.Events?
            .Where(e => e != null && !string.IsNullOrEmpty(e.Path) && e.Path.StartsWith("event:/"))
            .ToList() ?? new List<EditorEventRef>();

        static string FinalName(string path) => path.Contains('/')
            ? path.Substring(path.LastIndexOf('/') + 1)
            : path;

        static bool ParamsDirty(Audio_Package pkg, List<EditorParamRef> lp)
        {
            if ((pkg.parameters?.Length ?? 0) != lp.Count) return true;
            for (int i = 0; i < lp.Count; i++)
                if ((pkg.parameters?[i] ?? "") != lp[i].Name) return true;
            return false;
        }

        bool needsWork = allEvents.Any(ev =>
        {
            var pkg = FindPkg(ev.Path);
            if (pkg == null) return true;
            var lp = ev.LocalParameters ?? new List<EditorParamRef>();
            return pkg.Name != FinalName(ev.Path) || ParamsDirty(pkg, lp);
        });
        if (!needsWork) return;

        so.Update();
        var arr = so.FindProperty("Packages");

        foreach (var ev in allEvents)
        {
            string name = FinalName(ev.Path);
            var lp = ev.LocalParameters ?? new List<EditorParamRef>();
            var pkg = FindPkg(ev.Path);

            if (pkg == null)
            {
                Undo.RecordObject(bank, "Auto-add Audio Package");
                arr.arraySize++;
                var elem = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                elem.FindPropertyRelative("Name").stringValue = name;
                elem.FindPropertyRelative("Event").SetEventReference(ev.Guid, ev.Path);
                var paramP = elem.FindPropertyRelative("parameters");
                paramP.arraySize = lp.Count;
                for (int i = 0; i < lp.Count; i++)
                    paramP.GetArrayElementAtIndex(i).stringValue = lp[i].Name;
            }
            else if (pkg.Name != name || ParamsDirty(pkg, lp))
            {
                for (int i = 0; i < arr.arraySize; i++)
                {
                    var elem = arr.GetArrayElementAtIndex(i);
                    if (elem.FindPropertyRelative("Event").GetEventReferencePath() == ev.Path)
                    {
                        Undo.RecordObject(bank, "Sync Audio Package");
                        elem.FindPropertyRelative("Name").stringValue = name;
                        var paramP = elem.FindPropertyRelative("parameters");
                        paramP.arraySize = lp.Count;
                        for (int j = 0; j < lp.Count; j++)
                            paramP.GetArrayElementAtIndex(j).stringValue = lp[j].Name;
                        break;
                    }
                }
            }
        }

        so.ApplyModifiedProperties();
        Commit();
    }

    void Commit()
    {
        EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssets();
        bank = AssetDatabase.LoadAssetAtPath<Audio_Library>(AssetPath);
        so = bank != null ? new SerializedObject(bank) : null;
        ScanScripts();
        Repaint();
    }

    static void Divider()
    {
        EditorGUILayout.Space(4);
        var r = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.35f));
        EditorGUILayout.Space(4);
    }

}

}