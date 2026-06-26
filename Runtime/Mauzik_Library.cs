using UnityEngine;

namespace Mauzik
{

[CreateAssetMenu(fileName = "Mauzik_Library", menuName = "Audio/Library")]
public class Mauzik_Library : ScriptableObject
{

    public Audio_Package[] Packages;

    public Audio_Package Get(string name)
    {
        if (Packages == null) return null;
        foreach (var p in Packages)
            if (p != null && p.Name == name) return p;
        return null;
    }
    
}

}