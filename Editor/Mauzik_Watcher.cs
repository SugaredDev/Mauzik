using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using FMODUnity;

namespace Mauzik.Editor
{

public static class Mauzik_Watcher
{

    internal const string AssetPath = "Assets/Plugins/Mauzik/Resources/Library.asset";
    static bool isSyncing;

    internal static void UpdateLibrary()
    {
        if (isSyncing) return;
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EventManager.IsLoaded) return;

        var bank = AssetDatabase.LoadAssetAtPath<Mauzik_Data>(AssetPath);
        if (bank == null) return;

        var allEvents = EventManager.Events?
            .Where(e => e != null && !string.IsNullOrEmpty(e.Path) && e.Path.StartsWith("event:/"))
            .ToList() ?? new List<EditorEventRef>();

        if (allEvents.Count == 0) return;

        isSyncing = true;
        try
        {
            var so = new SerializedObject(bank);
            so.Update();

            var arr = so.FindProperty("Packages");
            if (arr == null) return;

            bool changed = false;

            var livePaths = new HashSet<string>(allEvents.Select(e => e.Path));
            for (int i = arr.arraySize - 1; i >= 0; i--)
            {
                var e = arr.GetArrayElementAtIndex(i).FindPropertyRelative("Event");
                if (e != null && !livePaths.Contains(e.GetEventReferencePath()))
                {
                    arr.DeleteArrayElementAtIndex(i);
                    changed = true;
                }
            }

            foreach (var ev in allEvents)
            {
                string eventPath = ev.Path;
                string expectedName = EventFinalName(eventPath);
                var localParams = ev.LocalParameters ?? new List<EditorParamRef>();

                int index = FindPackageIndex(arr, eventPath);
                if (index < 0)
                {
                    arr.arraySize++;
                    var elem = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                    elem.FindPropertyRelative("Name").stringValue = expectedName;
                    elem.FindPropertyRelative("Event").SetEventReference(ev.Guid, eventPath);
                    var paramP = elem.FindPropertyRelative("parameters");
                    paramP.arraySize = localParams.Count;
                    for (int i = 0; i < localParams.Count; i++)
                        paramP.GetArrayElementAtIndex(i).stringValue = localParams[i].Name;
                    changed = true;
                    continue;
                }

                var pkg = arr.GetArrayElementAtIndex(index);
                var nameProp = pkg.FindPropertyRelative("Name");
                var paramsProp = pkg.FindPropertyRelative("parameters");

                if (nameProp.stringValue != expectedName) { nameProp.stringValue = expectedName; changed = true; }

                if (ParamsDirty(paramsProp, localParams))
                {
                    paramsProp.arraySize = localParams.Count;
                    for (int i = 0; i < localParams.Count; i++)
                        paramsProp.GetArrayElementAtIndex(i).stringValue = localParams[i].Name;
                    changed = true;
                }
            }

            if (!changed) return;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
        }
        finally { isSyncing = false; }
    }

    static int FindPackageIndex(SerializedProperty arr, string eventPath)
    {
        for (int i = 0; i < arr.arraySize; i++)
        {
            var e = arr.GetArrayElementAtIndex(i).FindPropertyRelative("Event");
            if (e != null && e.GetEventReferencePath() == eventPath) return i;
        }
        return -1;
    }

    static bool ParamsDirty(SerializedProperty paramsProp, List<EditorParamRef> lp)
    {
        if (paramsProp.arraySize != lp.Count) return true;
        for (int i = 0; i < lp.Count; i++)
            if (paramsProp.GetArrayElementAtIndex(i).stringValue != lp[i].Name) return true;
        return false;
    }

    internal static string EventFinalName(string path) =>
        path.Contains('/') ? path.Substring(path.LastIndexOf('/') + 1) : path;

}

class Mauzik_BankWatcher : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        bool hasBankChange = imported.Concat(deleted).Concat(moved)
            .Any(p => p.EndsWith(".bank", StringComparison.OrdinalIgnoreCase));
        if (hasBankChange)
            EditorApplication.delayCall += Mauzik_Watcher.UpdateLibrary;
    }
}

}
