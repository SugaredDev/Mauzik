using UnityEngine;
using System;
using System.Collections.Generic;
using FMODUnity;
using FMOD.Studio;
using FMOD;

namespace Mauzik
{

public class Audio
{
    
    public void Play()
    {
        instance.start();
    }

    public void Stop(bool hardStop = false) =>
        instance.stop(hardStop ? FMOD.Studio.STOP_MODE.IMMEDIATE : FMOD.Studio.STOP_MODE.ALLOWFADEOUT);

    public void Parameter(string name, float value) =>
        instance.setParameterByName(name, value);

    public void Remove(bool hardStop = false)
    {
        Library.Unregister(this);
        instance.stop(hardStop ? FMOD.Studio.STOP_MODE.IMMEDIATE : FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
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

    public static Audio Attach(Transform target, Package package)
    {
        if (package == null)
        {
            UnityEngine.Debug.LogError($"Mauzik => Attach called with null package.");
            return null;
        }
        
        var src = new Audio { package = package, gameObject = target.gameObject };
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

    const string LibraryName = "Library";
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

    static readonly HashSet<Audio> sources = new();
    static readonly Dictionary<string, HashSet<string>> bankEventPaths = new();

    static Package Get(string name)
    {
        var pkg = Data?.Get(name);
        if (pkg == null) UnityEngine.Debug.LogWarning($"Mauzik => Package \"{name}\" not found.");
        return pkg;
    }

    public static Audio Create(Transform target, string event_name, string parameter_name = null, float parameter_value = 0f)
    {
        var audio = Audio.Attach(target, Get(event_name));
        if (audio != null && !string.IsNullOrEmpty(parameter_name))
            audio.Parameter(parameter_name, parameter_value);
        return audio;
    }

    public static void OneShot(Transform target, string event_name, string parameter_name = null, float parameter_value = 0f)
    {
        var pkg = Get(event_name);
        if (pkg == null) return;

        var inst = RuntimeManager.CreateInstance(pkg.Event);
        if (!inst.isValid()) return;

        if (!string.IsNullOrEmpty(parameter_name))
            inst.setParameterByName(parameter_name, parameter_value);

        RuntimeManager.AttachInstanceToGameObject(inst, target.gameObject);
        inst.start();
        inst.release();
    }

    internal static void Register(Audio s) { if (s != null) sources.Add(s); }
    internal static void Unregister(Audio s) { if (s != null) sources.Remove(s); }

    public static bool SetBankVolume(string bank, float volume)
    {
        if (!TryGetBankEventPaths(NormalizeBankPath(bank), out var events)) return false;
        ApplyBankVolume(events, Mathf.Clamp01(volume));
        return true;
    }

    static void ApplyBankVolume(HashSet<string> events, float volume)
    {
        foreach (var s in new List<Audio>(sources))
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