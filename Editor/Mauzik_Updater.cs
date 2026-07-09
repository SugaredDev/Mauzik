using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using FMODUnity;

namespace Mauzik.Editor
{

[InitializeOnLoad]
public static class Mauzik_Updater
{

    const string AssetPath = "Assets/Plugins/Mauzik/Resources/Library.asset";
    static bool isSyncing;

    static Mauzik_Updater()
    {
        EditorApplication.delayCall += Validate;
        EditorApplication.projectChanged += Validate;
    }

    static void Validate()
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

            foreach (var ev in allEvents)
            {
                string eventPath = ev.Path;
                string expectedName = FinalName(eventPath);
                var localParams = ev.LocalParameters ?? new List<EditorParamRef>();

                int index = FindPackageIndexByEventPath(arr, eventPath);
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

                var packageProp = arr.GetArrayElementAtIndex(index);
                var nameProp = packageProp.FindPropertyRelative("Name");
                var paramsProp = packageProp.FindPropertyRelative("parameters");

                if (nameProp.stringValue != expectedName)
                {
                    nameProp.stringValue = expectedName;
                    changed = true;
                }

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
        finally
        {
            isSyncing = false;
        }
    }

    static int FindPackageIndexByEventPath(SerializedProperty packagesArray, string eventPath)
    {
        for (int i = 0; i < packagesArray.arraySize; i++)
        {
            var elem = packagesArray.GetArrayElementAtIndex(i);
            var eventProp = elem.FindPropertyRelative("Event");
            if (eventProp != null && eventProp.GetEventReferencePath() == eventPath)
                return i;
        }
        return -1;
    }

    static bool ParamsDirty(SerializedProperty paramsProp, List<EditorParamRef> localParams)
    {
        if (paramsProp.arraySize != localParams.Count) return true;
        for (int i = 0; i < localParams.Count; i++)
            if (paramsProp.GetArrayElementAtIndex(i).stringValue != localParams[i].Name)
                return true;
        return false;
    }

    static string FinalName(string path) =>
        path.Contains('/') ? path.Substring(path.LastIndexOf('/') + 1) : path;

}

}
