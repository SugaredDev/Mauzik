using UnityEngine;
using System;
using FMODUnity;
using FMOD.Studio;
using FMOD;

namespace Mauzik
{

public class Audio
{

    // =========================
    
    public void Stop(bool hardStop = false)
    {
        instance.stop(hardStop ? FMOD.Studio.STOP_MODE.IMMEDIATE : FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        instance.release();
    }
        

    public void Parameter(string name, float value) =>
        instance.setParameterByName(name, value);

    public void Sync(int ms)
    {
        if (!instance.isValid()) return;
        if (instance.getTimelinePosition(out int pos) == RESULT.OK && Mathf.Abs(ms - pos) > 50)
            instance.setTimelinePosition(ms);
    }

    // =========================

    public Mauzik_Package package;
    EventInstance instance;

    public static Audio Attach(Transform target, Mauzik_Package package)
    {
        if (package == null)
        {
            UnityEngine.Debug.LogError($"Mauzik => Attach called with null package.");
            return null;
        }
        
        var src = new Audio { package = package };
        src.instance = RuntimeManager.CreateInstance(package.Event);
        RuntimeManager.AttachInstanceToGameObject(src.instance, target.gameObject);
        
        src.instance.start();
        src.instance.release();
        return src;
    }
    
}
    
public static class Library
{

    // =========================

    public static Audio Play(Transform target, string event_name, string parameter_name = null, float parameter_value = 0f)
    {
        var audio = Audio.Attach(target, Get_Event(event_name));
        if (audio != null && !string.IsNullOrEmpty(parameter_name))
            audio.Parameter(parameter_name, parameter_value);
        return audio;
    }

    public static bool Volume(string bank_name, float bank_volume)
    {
        string path = NormalizeBusPath(bank_name);
        Bus bus = Get_Bus(path);
        return bus.isValid() && bus.setVolume(Mathf.Clamp01(bank_volume)) == RESULT.OK;
    }

    // =========================

    const string LibraryName = "Mauzik_Library";
    static Mauzik_Data data;
    static System.Collections.Generic.Dictionary<string, Bus> busCache = new();
    
    static Mauzik_Data Data
    {
        get
        {
            if (data != null) return data;
            data = Resources.Load<Mauzik_Data>(LibraryName);
            if (data == null) UnityEngine.Debug.LogError($"Mauzik => No {LibraryName} found in Resources. Create one via Tools > Audio Tool.");
            return data;
        }
    }

    static Mauzik_Package Get_Event(string name)
    {
        var pkg = Data?.Get(name);
        if (pkg == null) UnityEngine.Debug.LogWarning($"Mauzik => Package \"{name}\" not found.");
        return pkg;
    }

    static Bus Get_Bus(string path)
    {
        if (busCache.TryGetValue(path, out var bus) && bus.isValid())
            return bus;
        
        bus = RuntimeManager.GetBus(path);
        if (bus.isValid())
            busCache[path] = bus;
        
        return bus;
    }

    static string NormalizeBusPath(string name = "Master") =>
        name.StartsWith("bus:/", StringComparison.OrdinalIgnoreCase) ? name : $"bus:/{name}";

}

    [System.Serializable]
    public class Mauzik_Package
    {

        public string Name;
        public EventReference Event;
        public string[] parameters;

    }

}