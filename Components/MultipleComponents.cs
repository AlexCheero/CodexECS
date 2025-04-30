using System.Collections.Generic;
using CodexECS;

public struct MultipleComponents<T> : IComponent
{
    public SimpleList<T> components;
    
    public static MultipleComponents<T> Default => new() { components = new() };
    public static void Init(ref MultipleComponents<T> instance) => instance.components ??= new();
    public static void Cleanup(ref MultipleComponents<T> instance)
    {
        if (instance.components == null)
            return;
        for (int i = 0; i < instance.components.Length; i++)
            ComponentMeta<T>.Cleanup(ref instance.components[i]);
        instance.components.Clear();
    }
}