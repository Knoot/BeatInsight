using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using BeatInsight.Formats;
using Mono.Cecil;
using UnityEngine;

namespace BeatInsight.Compare;

static class NoodleWallContactChecks
{
    public static object Run(string gameRoot)
    {
        string path=Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll");
        if(Comparison.HashFile(path)!="A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")throw new InvalidDataException("Reinspect changed collision/trajectory");
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        using var module=ModuleDefinition.ReadModule(path);
        var collision=module.Types.Single(t=>t.Name=="PlayerHeadAndObstacleInteraction").Methods.Single(m=>m.Name=="RefreshIntersectingObstacles");
        bool Calls(MethodDefinition m,string type,string name)=>m.Body.Instructions.Any(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName==type&&r.Name==name);
        if(!Calls(collision,"UnityEngine.Transform","InverseTransformPoint")||!Calls(collision,"UnityEngine.Bounds","Contains"))throw new InvalidDataException("Head collision space changed");
        if(collision.Body.Instructions.Any(i=>i.Operand is MethodReference r&&(r.Name.Contains("Sphere")||r.Name.Contains("Capsule"))))throw new InvalidDataException("Unexpected volume collision path");
        var length=module.Types.Single(t=>t.Name=="ObstacleController").Methods.Single(m=>m.Name=="GetObstacleLength");
        if(!Calls(length,"IVariableMovementDataProvider","get_jumpDuration")||!Calls(length,"ObstacleData","get_duration"))throw new InvalidDataException("Wall length derivation changed");
        var main=AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        var providerType=main.GetType("VariableMovementDataProvider",true)!;
        var controllerType=main.GetType("ObstacleController",true)!;
        var position=controllerType.GetMethod("GetPosForTime",flags)!;
        var providerField=controllerType.BaseType!.GetField("_variableMovementDataProvider",flags)!;
        int cases=0,samples=0,failed=0;float maxPositionError=0;var failures=new List<object>();
        foreach(float speed in new[]{10f,20f,30f})foreach(float half in new[]{.25f,.5f,1f})
        foreach(float duration in new[]{.05f,.5f,2f,8f})foreach(float headZ in new[]{-.3f,0f,.3f})foreach(float minZ in new[]{0f,.1f})
        {
            float wallTime=10,lengthZ=speed*duration,maxZ=minZ+lengthZ;
            var movement=WallJumpMovement.Resolve(new JumpSettingsSnapshot(1,0,half),120,speed,0);
            var interval=WallContactInterval.AtPointPlane(movement,wallTime,headZ,minZ,maxZ);
            var provider=RuntimeHelpers.GetUninitializedObject(providerType);
            providerType.GetField("_jumpDuration",flags)!.SetValue(provider,2*half);
            var controller=RuntimeHelpers.GetUninitializedObject(controllerType);providerField.SetValue(controller,provider);
            controllerType.GetField("_startPos",flags)!.SetValue(controller,new Vector3(0,0,100+speed*half));
            controllerType.GetField("_midPos",flags)!.SetValue(controller,new Vector3(0,0,speed*half));
            controllerType.GetField("_endPos",flags)!.SetValue(controller,new Vector3(0,0,-speed*half));
            float move=(float)providerType.GetProperty("moveDuration",flags)!.GetValue(provider)!;
            float startOffset=wallTime-move-half;
            bool pass=true;
            foreach(float time in new[]{interval.Start-.01f,interval.Start,interval.Start+.01f,(interval.Start+interval.End)*.5f,interval.End-.01f,interval.End,interval.End+.01f})
            {
                var native=(Vector3)position.Invoke(controller,new object[]{time-startOffset})!;
                float expected=-speed*(time-wallTime),error=Math.Abs(native.z-expected);
                maxPositionError=Math.Max(maxPositionError,error);
                float local=headZ-native.z;
                bool inside=local>=minZ&&local<=maxZ,expectedInside=time>=interval.Start&&time<=interval.End;
                bool atBoundary=Math.Abs(local-minZ)<.0001f||Math.Abs(local-maxZ)<.0001f;
                bool samplePass=error<.0001f&&(atBoundary||inside==expectedInside);
                pass&=samplePass;samples++;
                if(!samplePass&&failures.Count<10)failures.Add(new{speed,half,duration,headZ,minZ,time,local,inside,expectedInside,error});
            }
            cases++;if(!pass)failed++;
        }
        return new{schemaVersion=1,cases,samples,failed,maxPositionError,failures,gameStarted=false,referenceUsed=false,
            scope="actual ObstacleController.GetPosForTime with controlled constant provider and pre-avoided state; explicit point-plane Z interval versus independent slab membership, boundary tolerance; collision InverseTransformPoint/Contains and wall length IL audited; no Unity Transform/native Bounds.Contains, live head pose, rotated/scaled/time-overridden walls or analyzer admission"};
    }
}
