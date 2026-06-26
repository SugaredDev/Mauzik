using UnityEngine;
using System;
using System.Collections.Generic;
using FMODUnity;
using FMOD.Studio;
using FMOD;

namespace Mauzik
{

public class Source
{
    
    public void Play()
    {
        RuntimeManager.AttachInstanceToGameObject(instance, gameObject);
        instance.start();
    }

    public void Stop(bool fadeout = true) =>
        instance.stop(fadeout ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);

    public void Parameter(string name, float value) =>
        instance.setParameterByName(name, value);

    public void Remove()
    {
        Library.Unregister(this);
        instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        instance.release();
    }

    public bool SetVolume(float volume) =>
        instance.isValid() && instance.setVolume(Mathf.Clamp01(volume)) == RESULT.OK;

    public bool GetVolume(out float volume)
    {
        volume = 1f;
        return instance.isValid() && instance.getVolume(out volume) == RESULT.OK;
    }

    public void Sync(int ms)
    {
        if (!instance.isValid()) return;
        if (instance.getTimelinePosition(out int pos) == RESULT.OK && Mathf.Abs(ms - pos) > 50)
            instance.setTimelinePosition(ms);
    }

    // =========================

    public Package package;
    EventInstance instance;
    GameObject gameObject;
    public string EventPath { get; private set; }

    public static Source Attach(Package package, Transform target)
    {
        if (package == null)
        {
            UnityEngine.Debug.LogError($"Mauzik => No package for \"{target.name}\".");
            return null;
        }
        
        var src = new Source { package = package, gameObject = target.gameObject };
        src.instance = RuntimeManager.CreateInstance(package.Event);
        RuntimeManager.AttachInstanceToGameObject(src.instance, src.gameObject);
        
        if (src.instance.isValid() &&
            src.instance.getDescription(out EventDescription desc) == RESULT.OK &&
            desc.isValid() && desc.getPath(out string path) == RESULT.OK)
            src.EventPath = path;
        
        Library.Register(src);
        return src;
    }

    public bool IsValid() => instance.isValid();
    
}

// ==============================================================================================
    
public static class Library
{

    const string LibraryName = "Mauzik_Library";
    static Data data;
    
    static Data Data
    {
        get
        {
            if (data != null) return data;
            data = Resources.Load<Data>(LibraryName);
            if (data == null) UnityEngine.Debug.LogError($"Mauzik => No {LibraryName} found in Resources. Create one via Tools > Audio Tool.");
            return data;
        }
    }

    static readonly HashSet<Source> sources = new();
    static readonly Dictionary<string, HashSet<string>> bankEventPaths = new();

    static Package Get(string name)
    {
        var pkg = Data?.Get(name);
        if (pkg == null) UnityEngine.Debug.LogWarning($"Mauzik => Package \"{name}\" not found.");
        return pkg;
    }

    public static Source Create(string name, Transform target) =>
        Source.Attach(Get(name), target);

    internal static void Register(Source s) { if (s != null) sources.Add(s); }
    internal static void Unregister(Source s) { if (s != null) sources.Remove(s); }

    public static bool SetBankVolume(string bankName, float volume)
    {
        if (!TryGetBankEventPaths(NormalizeBankPath(bankName), out var events)) return false;
        ApplyBankVolume(events, Mathf.Clamp01(volume));
        return true;
    }

    static void ApplyBankVolume(HashSet<string> events, float volume)
    {
        foreach (var s in new List<Source>(sources))
            if (s != null && s.IsValid() && !string.IsNullOrEmpty(s.EventPath) && events.Contains(s.EventPath))
                s.SetVolume(volume);
    }

    static bool TryGetBankEventPaths(string bankPath, out HashSet<string> events)
    {
        if (bankEventPaths.TryGetValue(bankPath, out events)) return events?.Count > 0;

        events = new HashSet<string>();
        if (RuntimeManager.StudioSystem.getBank(bankPath, out Bank fmodBank) != RESULT.OK || !fmodBank.isValid())
            return false;
        if (fmodBank.getEventList(out EventDescription[] descs) != RESULT.OK || descs == null)
            return false;

        foreach (var desc in descs)
            if (desc.isValid() && desc.getPath(out string p) == RESULT.OK && !string.IsNullOrEmpty(p))
                events.Add(p);

        bankEventPaths[bankPath] = events;
        return events.Count > 0;
    }

    static string NormalizeBankPath(string name) =>
        string.IsNullOrWhiteSpace(name) ? "bank:/Master" :
        name.StartsWith("bank:/", StringComparison.OrdinalIgnoreCase) ? name : $"bank:/{name}";

}

    [System.Serializable]
    public class Package
    {

        public string Name;
        public EventReference Event;
        public string[] parameters;

    }

}