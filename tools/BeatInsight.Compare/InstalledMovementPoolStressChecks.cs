using System.Reflection;
using UnityEngine;

namespace BeatInsight.Compare;

static class InstalledMovementPoolStressChecks
{
    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static FieldInfo Field(Type type,string name)
    {
        for(Type? t=type;t!=null;t=t.BaseType)
            if(t.GetField(name,flags|BindingFlags.DeclaredOnly) is FieldInfo field)return field;
        throw new MissingFieldException(type.FullName,name);
    }
    internal static object Run(object pool,BeatmapObjectData source)
    {
        var poolType=pool.GetType();var spawn=poolType.GetMethod("Spawn",flags)!;
        var despawn=poolType.BaseType!.BaseType!.GetMethod("Despawn",flags)!;
        int Active()=>(int)Field(poolType,"_activeCount").GetValue(pool)!;
        int Inactive()
        {
            var items=Field(poolType,"_inactiveItems").GetValue(pool)!;
            return (int)items.GetType().GetProperty("Count")!.GetValue(items)!;
        }
        if(Active()!=0||Inactive()!=40)throw new InvalidDataException("Installed pool warmup state changed");
        int capacity=0,spawns=0,resets=0;
        var seen=new HashSet<object>(ReferenceEqualityComparer.Instance);
        for(int round=0;round<8;round++)
        {
            var active=new HashSet<object>(ReferenceEqualityComparer.Instance);
            try
            {
                for(int i=0;i<64;i++)
                {
                    // Unregistered copies must clear all prior per-object overrides.
                    var item=spawn.Invoke(pool,new object[]{source.GetCopy()})!;
                    if(!active.Add(item))throw new InvalidDataException("Pool returned a duplicate active provider");
                    seen.Add(item);spawns++;
                    var overrides=item.GetType().GetFields(flags).Where(f=>f.Name.EndsWith("Override",StringComparison.Ordinal)).ToArray();
                    if(overrides.Length!=8||overrides.Any(f=>f.GetValue(item)!=null)||Active()!=active.Count)
                        throw new InvalidDataException("Installed provider pool retained per-object state");
                    foreach(var field in overrides)
                    {
                        var type=Nullable.GetUnderlyingType(field.FieldType);
                        field.SetValue(item,type==typeof(float)?(object)31f:type==typeof(Vector3)?new Vector3(1,2,3):throw new InvalidDataException("Unknown movement override type"));
                        if(field.GetValue(item)==null)throw new InvalidDataException("Override control was not populated");
                        resets++;
                    }
                    if(round>0&&Active()+Inactive()!=capacity)throw new InvalidDataException("Installed pool grew after warmup");
                }
                if(round==0){capacity=Active()+Inactive();if(capacity<64)throw new InvalidDataException("Installed pool did not grow");}
            }
            finally
            {
                foreach(var item in round%2==0?active.Reverse():active)despawn.Invoke(pool,new[]{item});
            }
            if(Active()!=0||Inactive()!=capacity||seen.Count>capacity)throw new InvalidDataException("Installed pool counts did not recover");
        }
        // Leave fixture providers clean before the remaining callback checks.
        var clean=new HashSet<object>(ReferenceEqualityComparer.Instance);
        try
        {
            for(int i=0;i<capacity;i++)
            {
                var item=spawn.Invoke(pool,new object[]{source.GetCopy()})!;
                if(!clean.Add(item)||item.GetType().GetFields(flags).Any(f=>f.Name.EndsWith("Override",StringComparison.Ordinal)&&f.GetValue(item)!=null))
                    throw new InvalidDataException("Final provider reset failed");
            }
        }
        finally{foreach(var item in clean)despawn.Invoke(pool,new[]{item});}
        if(Active()!=0||Inactive()!=capacity)throw new InvalidDataException("Final pool sweep changed capacity");
        return new{rounds=8,concurrent=64,spawns,overrideControls=resets,capacity,cleanSweep=clean.Count,observedProviders=seen.Count,active=Active(),inactive=Inactive(),
            scope="Actual pool resolved from NoodlePlayerInstaller WithInitialSize(40), native Spawn/Reinitialize/Despawn and growth past initial size. Eight batches of unregistered note copies; controlled nullable overrides populated between reuse to verify InitObject clears them. Capacity stays fixed after warmup; no Unity component pool, frame timing or weak collection claim."};
    }
}
