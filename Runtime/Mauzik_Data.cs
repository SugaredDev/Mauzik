using UnityEngine;

namespace Mauzik
{

public class Mauzik_Data : ScriptableObject
{

    public Mauzik_Package[] Packages;

    public Mauzik_Package Get(string name)
    {
        if (Packages == null) return null;
        foreach (var p in Packages)
            if (p != null && p.Name == name) return p;
        return null;
    }
    
}

}