using UnityEngine;
using System;
using System.Collections.Generic;
using FMODUnity;
using FMOD.Studio;
using FMOD;

using Mauzik;

namespace Mauzik
{
    
public static class Mauzik_Master
{

    const string LibraryName = "Mauzik_Library";
    static Mauzik_Library data;
    
    static Mauzik_Library Data
    {
        get
        {
            if (data != null) return data;
            data = Resources.Load<Mauzik_Library>(LibraryName);
            if (data == null) UnityEngine.Debug.LogError($"Mauzik => No {LibraryName} found in Resources. Create one via Tools > Audio Tool.");
            return data;
        }
    }

    static readonly HashSet<Audio_Source> sources = new();
    static readonly Dictionary<string, HashSet<string>> bankEventPaths = new();

    public static Audio_Package Get(string name)
    {
        var pkg = Data?.Get(name);
        if (pkg == null) UnityEngine.Debug.LogWarning($"Mauzik => Package \"{name}\" not found.");
        return pkg;
    }

    public static Audio_Source Attach(string name, Transform target) =>
        Audio_Source.Create(Get(name), target);

    internal static void Register(Audio_Source s) { if (s != null) sources.Add(s); }
    internal static void Unregister(Audio_Source s) { if (s != null) sources.Remove(s); }

    public static bool SetBankVolume(string bankName, float volume)
    {
        if (!TryGetBankEventPaths(NormalizeBankPath(bankName), out var events)) return false;
        ApplyBankVolume(events, Mathf.Clamp01(volume));
        return true;
    }

    static void ApplyBankVolume(HashSet<string> events, float volume)
    {
        foreach (var s in new List<Audio_Source>(sources))
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
public class Audio_Package
{

    public string Name;
    public EventReference Event;
    public string[] parameters;

}

public class Audio_Source
{
    public Audio_Package package;
    EventInstance instance;
    GameObject gameObject;
    
    public string EventPath { get; private set; }

    public static Audio_Source Create(Audio_Package package, Transform target)
    {
        if (package == null)
        {
            UnityEngine.Debug.LogError($"Mauzik => No package for \"{target.name}\".");
            return null;
        }
        
        var src = new Audio_Source { package = package, gameObject = target.gameObject };
        src.instance = RuntimeManager.CreateInstance(package.Event);
        RuntimeManager.AttachInstanceToGameObject(src.instance, src.gameObject);
        
        if (src.instance.isValid() &&
            src.instance.getDescription(out EventDescription desc) == RESULT.OK &&
            desc.isValid() && desc.getPath(out string path) == RESULT.OK)
            src.EventPath = path;
        
        Mauzik_Master.Register(src);
        return src;
    }

    public void Play()
    {
        RuntimeManager.AttachInstanceToGameObject(instance, gameObject);
        instance.start();
    }

    public void Stop(bool fadeout = true) =>
        instance.stop(fadeout ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);

    public void SetParameter(int index, float value) =>
        instance.setParameterByName(package.parameters[index], value);

    public void SetParameter(string name, float value) =>
        instance.setParameterByName(name, value);

    public void Remove()
    {
        Mauzik_Master.Unregister(this);
        instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        instance.release();
    }

    public bool IsValid() => instance.isValid();

    public bool SetVolume(float volume) =>
        instance.isValid() && instance.setVolume(Mathf.Clamp01(volume)) == RESULT.OK;

    public bool TryGetVolume(out float volume)
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
    
}

}