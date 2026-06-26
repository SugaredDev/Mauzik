using UnityEngine;

namespace Mauzik
{

[CreateAssetMenu(fileName = "Audio_Bank", menuName = "Audio/Bank")]
public class Audio_Library : ScriptableObject
{

    public Audio_Package[] Packages;

    public Audio_Package Get(string name)
    {
        foreach (var p in Packages)
            if (p.Name == name) return p;
        return null;
    }

}

}