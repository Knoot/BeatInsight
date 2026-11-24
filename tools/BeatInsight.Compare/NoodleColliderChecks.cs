using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using V=System.Numerics.Vector3;
using M=System.Numerics.Matrix4x4;
using EmitCodes=System.Reflection.Emit.OpCodes;

namespace BeatInsight.Compare;

public sealed class ColliderBox
{
    V size;
    public int Writes;
    public V Size {get=>size;set{size=value;Writes++;}}
}
public sealed class ColliderPose
{
    public M Matrix=M.Identity;
    V position;
    public int Writes,PointCalls;
    public V Position {get=>position;set{position=value;Writes++;}}
    public V TransformPoint(V point){PointCalls++;return V.Transform(point,Matrix);}
}
public abstract class ColliderComponent { public ColliderPose Transform {get;set;}=new(); }
public sealed class ColliderCuttable : ColliderComponent
{
    public static Func<ColliderCuttable,V>? NativeSize;
    public ColliderBox Box=new();
    bool canBeCut;
    public int EnableWrites;
    public bool CanBeCut {get=>canBeCut;set{canBeCut=value;EnableWrites++;}}
    public V ColliderSize=>NativeSize!(this);
}
public sealed class ColliderMovement
{
    public V PrevLocalPosition {get;set;}
    public V LocalPosition {get;set;}
    public event Action? Moved;
    public int ListenerCount=>Moved?.GetInvocationList().Length??0;
    public void Raise()=>Moved?.Invoke();
}
public sealed class ColliderTrailing : ColliderComponent
{
    public ColliderMovement Movement=new();
    public ColliderCuttable Main=new();
    public ColliderCuttable[] Trailers=Array.Empty<ColliderCuttable>();
    public ColliderPose CachedTransform=null!;
    public Action<ColliderTrailing> Handler=null!;
    public void Handle()=>Handler(this);
}
public sealed class ColliderNote { public int Direction {get;set;} }
public sealed class ColliderHub
{
    public readonly HashSet<object> Listeners=new();
    public void Add(object item)=>Listeners.Add(item);
    public void Remove(object item)=>Listeners.Remove(item);
}
public sealed class ColliderController
{
    public ColliderNote NoteData {get;}=new();
    public ColliderHub DidInitEvent {get;}=new();
}
public sealed class ColliderBig
{
    public ColliderBox Box=new();
    public V DefaultSize;
    public ColliderController Controller=new();
}
public static class ColliderMath { public static bool Different(object? a,object? b)=>!ReferenceEquals(a,b); }

static class NoodleColliderChecks
{
    public static object Run(string gameRoot)
    {
        string path=Path.Combine(gameRoot,"Beat Saber_Data/Managed/Main.dll");
        if(Comparison.HashFile(path)!="A276C8A50ACFAD69A3DD3A82CDE08571C3262344702B9B014C89756771B4495E")throw new InvalidDataException("Reinspect changed dynamic collider handlers");
        using var module=ModuleDefinition.ReadModule(path);
        var methods=new List<MethodDefinition>();
        MethodDefinition Method(string owner,string name)
        {var method=module.Types.Single(t=>t.Name==owner).Methods.Single(m=>m.Name==name);methods.Add(method);return method;}
        var awake=Compile<Action<ColliderBig>>(Method("NoteBigCuttableColliderSize","Awake"),typeof(void),typeof(ColliderBig));
        var big=Compile<Action<ColliderBig,ColliderController>>(Method("NoteBigCuttableColliderSize","HandleNoteControllerDidInit"),typeof(void),typeof(ColliderBig),typeof(ColliderController));
        var bigDestroy=Compile<Action<ColliderBig>>(Method("NoteBigCuttableColliderSize","OnDestroy"),typeof(void),typeof(ColliderBig));
        var trailing=Compile<Action<ColliderTrailing>>(Method("ProModeTrailingCollidersController","HandleNoteDidMoveInJumpPhase"),typeof(void),typeof(ColliderTrailing));
        var start=Compile<Action<ColliderTrailing>>(Method("ProModeTrailingCollidersController","Start"),typeof(void),typeof(ColliderTrailing));
        var destroy=Compile<Action<ColliderTrailing>>(Method("ProModeTrailingCollidersController","OnDestroy"),typeof(void),typeof(ColliderTrailing));
        ColliderCuttable.NativeSize=Compile<Func<ColliderCuttable,V>>(Method("BoxCuttableBySaber","get_colliderSize"),typeof(V),typeof(ColliderCuttable));
        int cases=0,failed=0,bigCases=0,trailingCases=0;var failures=new List<object>();
        void Check(bool pass,object scenario,bool isBig)
        {cases++;if(isBig)bigCases++;else trailingCases++;if(!pass){failed++;if(failures.Count<16)failures.Add(scenario);}}
        static bool Near(V a,V b)=>V.Distance(a,b)<.0001f;
        try
        {
            foreach(var initial in new[]{new V(.2f,.5f,.3f),new V(.8f,.4f,.6f),new V(.5f,.5f,.25f)})
            foreach(var current in new[]{new V(.1f,.9f,.4f),new V(.9f,.1f,.4f),new V(.3f,.3f,.4f)})
            {
                var context=new ColliderBig();context.Box.Size=initial;
                awake(context);
                Check(context.DefaultSize==initial&&context.Controller.DidInitEvent.Listeners.SetEquals(new[]{(object)context}),new{kind="big-awake",initial,current},true);
                void Fire(int direction)
                {
                    context.Controller.NoteData.Direction=direction;
                    foreach(var listener in context.Controller.DidInitEvent.Listeners)big((ColliderBig)listener,context.Controller);
                }
                context.Box.Size=current;context.Box.Writes=0;
                var square=new V(MathF.Max(current.X,current.Y),MathF.Max(current.X,current.Y),current.Z);
                Fire(8);
                Check(context.Box.Size==square&&context.Box.Writes==1&&context.DefaultSize==initial,new{kind="dot-current-size",initial,current},true);
                Fire(8);
                Check(context.Box.Size==square&&context.Box.Writes==2,new{kind="dot-repeat",initial,current},true);
                foreach(int direction in Enumerable.Range(0,8))
                {
                    Fire(direction);
                    Check(context.Box.Size==initial&&context.DefaultSize==initial,new{kind="arrow-restores-default",initial,current,direction},true);
                    Fire(8);
                }
                bigDestroy(context);
                Check(context.Controller.DidInitEvent.Listeners.Count==0,new{kind="big-unsubscribe",initial,current},true);
            }
            foreach(float distance in new[]{-.5f,0f,.25f,.5f,1f,2f})foreach(float length in new[]{.25f,.5f})
            foreach(int count in new[]{0,1,4})foreach(bool enabled in new[]{false,true})foreach(float scaleX in new[]{-1f,1f})
            foreach(float yaw in new[]{0f,30f})foreach(float scaleZ in new[]{.6f,1f})
            {
                float radians=yaw*MathF.PI/180;var translation=new V(5,-2,3);
                var context=new ColliderTrailing {Handler=trailing,Transform=new ColliderPose {Matrix=M.CreateScale(scaleX,1,scaleZ)*M.CreateRotationY(radians)*M.CreateTranslation(translation)}};
                context.Main.Box.Size=new V(.3f,.7f,length);context.Main.CanBeCut=enabled;
                context.Movement.LocalPosition=new V(-1,2,1);context.Movement.PrevLocalPosition=new V(9,7,1+distance);
                context.Trailers=Enumerable.Range(0,count).Select(i=>new ColliderCuttable {CanBeCut=true,Transform=new ColliderPose {Position=new V(100+i,200+i,300+i)}}).ToArray();
                var previous=context.Trailers.Select(t=>t.Transform.Position).ToArray();
                foreach(var item in context.Trailers){item.Transform.Writes=0;item.EnableWrites=0;}
                start(context);
                bool pass=ReferenceEquals(context.CachedTransform,context.Transform)&&context.Movement.ListenerCount==1;
                context.Movement.Raise();
                float remaining=distance-length;
                for(int i=0;i<count;i++)
                {
                    var item=context.Trailers[i];
                    if(remaining<0)pass&=!item.CanBeCut&&item.Transform.Writes==0&&item.Transform.Position==previous[i];
                    else
                    {
                        float z=remaining/count*(i+1)*scaleZ;
                        var expected=translation+new V(MathF.Sin(radians)*z,0,MathF.Cos(radians)*z);
                        pass&=item.CanBeCut==enabled&&item.Transform.Writes==1&&Near(item.Transform.Position,expected);
                    }
                    pass&=item.EnableWrites==1;
                }
                pass&=context.Transform.PointCalls==(remaining<0?0:count);
                if(remaining>=0)
                {
                    var placed=context.Trailers.Select(t=>t.Transform.Position).ToArray();
                    context.Movement.PrevLocalPosition=context.Movement.LocalPosition;context.Movement.Raise();
                    pass&=context.Trailers.Select((t,i)=>!t.CanBeCut&&t.Transform.Position==placed[i]&&t.Transform.Writes==1).All(p=>p);
                    context.Movement.PrevLocalPosition=new V(9,7,1+distance);context.Movement.Raise();
                    pass&=context.Trailers.All(t=>t.CanBeCut==enabled&&t.Transform.Writes==2);
                }
                var writes=context.Trailers.Select(t=>t.Transform.Writes).ToArray();
                destroy(context);context.Movement.Raise();
                pass&=context.Movement.ListenerCount==0&&context.Trailers.Select(t=>t.Transform.Writes).SequenceEqual(writes);
                Check(pass,new{kind="trailing-lifecycle",distance,length,count,enabled,scaleX,yaw,scaleZ},false);
            }
        }
        finally{ColliderCuttable.NativeSize=null;}
        return new{cases,failed,bigCases,trailingCases,copiedInstructions=methods.Sum(m=>m.Body.Instructions.Count),
            methods=methods.Select(m=>m.FullName).ToArray(),failures,
            scope="Complete installed dot-size and Pro Mode trailing handlers, Awake/Start/OnDestroy and raw colliderSize getter with controlled component/event/box receivers; managed affine TransformPoint plus independent scalar gold; pooled transitions and unsubscribe; native Unity collider/Transform/sweep/lifecycle not executed; no support admission"};
    }

    static T Compile<T>(MethodDefinition method,Type result,params Type[] arguments) where T:Delegate
    {
        if(!method.HasThis||method.Parameters.Count!=arguments.Length-1||method.Body.ExceptionHandlers.Count!=0)throw new InvalidDataException("Changed collider handler ABI");
        var dynamic=new DynamicMethod("InstalledCollider_"+method.DeclaringType.Name+"_"+method.Name,result,arguments,typeof(NoodleColliderChecks).Module,true);
        var il=dynamic.GetILGenerator();
        foreach(var local in method.Body.Variables)
            il.DeclareLocal(local.VariableType.FullName switch {"System.Single"=>typeof(float),"System.Int32"=>typeof(int),"UnityEngine.Vector3"=>typeof(V),"BoxCuttableBySaber[]"=>typeof(ColliderCuttable[]),_=>throw new InvalidDataException("Unexpected collider local: "+local.VariableType)});
        var labels=method.Body.Instructions.ToDictionary(i=>i,_=>il.DefineLabel());
        FieldInfo BindField(FieldReference field)
        {
            (Type Type,string Name) target=field.DeclaringType.Name switch
            {
                "Vector3"=>(typeof(V),field.Name.ToUpperInvariant()),
                "NoteBigCuttableColliderSize"=>(typeof(ColliderBig),field.Name switch {"_boxCollider"=>"Box","_noteController"=>"Controller","_defaultColliderSize"=>"DefaultSize",_=>throw new InvalidDataException("Unknown big-collider field")}),
                "ProModeTrailingCollidersController"=>(typeof(ColliderTrailing),field.Name switch {"_noteMovement"=>"Movement","_mainSmallCuttableBySaber"=>"Main","_trailingSmallCuttableBySaberList"=>"Trailers","_transform"=>"CachedTransform",_=>throw new InvalidDataException("Unknown trailing field")}),
                "BoxCuttableBySaber" when field.Name=="_collider"=>(typeof(ColliderCuttable),"Box"),
                _=>throw new InvalidDataException("Unknown collider field: "+field)
            };
            return target.Type.GetField(target.Name)!;
        }
        MethodBase BindCall(MethodReference call)=>call.DeclaringType.Name switch
        {
            "Vector3" when call.Name==".ctor"=>typeof(V).GetConstructor(new[]{typeof(float),typeof(float),typeof(float)})!,
            "BoxCollider"=>call.Name=="get_size"?typeof(ColliderBox).GetProperty("Size")!.GetMethod!:call.Name=="set_size"?typeof(ColliderBox).GetProperty("Size")!.SetMethod!:throw new InvalidDataException("Unknown box operation"),
            "NoteControllerBase"=>call.Name=="get_noteData"?typeof(ColliderController).GetProperty("NoteData")!.GetMethod!:call.Name=="get_didInitEvent"?typeof(ColliderController).GetProperty("DidInitEvent")!.GetMethod!:throw new InvalidDataException("Unknown controller operation"),
            "NoteData" when call.Name=="get_cutDirection"=>typeof(ColliderNote).GetProperty("Direction")!.GetMethod!,
            "Mathf" when call.Name=="Max"=>typeof(MathF).GetMethod("Max",new[]{typeof(float),typeof(float)})!,
            "ILazyCopyHashSet`1"=>call.Name is "Add" or "Remove"?typeof(ColliderHub).GetMethod(call.Name)!:throw new InvalidDataException("Unknown event operation"),
            "Object" when call.Name=="op_Inequality"=>typeof(ColliderMath).GetMethod("Different")!,
            "NoteMovement"=>call.Name switch {"get_prevLocalPosition"=>typeof(ColliderMovement).GetProperty("PrevLocalPosition")!.GetMethod!,"get_localPosition"=>typeof(ColliderMovement).GetProperty("LocalPosition")!.GetMethod!,"add_noteDidMoveInJumpPhaseEvent"=>typeof(ColliderMovement).GetEvent("Moved")!.AddMethod!,"remove_noteDidMoveInJumpPhaseEvent"=>typeof(ColliderMovement).GetEvent("Moved")!.RemoveMethod!,_=>throw new InvalidDataException("Unknown movement operation")},
            "BoxCuttableBySaber" when call.Name=="get_colliderSize"=>typeof(ColliderCuttable).GetProperty("ColliderSize")!.GetMethod!,
            "CuttableBySaber"=>call.Name=="get_canBeCut"?typeof(ColliderCuttable).GetProperty("CanBeCut")!.GetMethod!:call.Name=="set_canBeCut"?typeof(ColliderCuttable).GetProperty("CanBeCut")!.SetMethod!:throw new InvalidDataException("Unknown cuttable operation"),
            "Component" when call.Name=="get_transform"=>typeof(ColliderComponent).GetProperty("Transform")!.GetMethod!,
            "Transform"=>call.Name=="TransformPoint"?typeof(ColliderPose).GetMethod("TransformPoint")!:call.Name=="set_position"?typeof(ColliderPose).GetProperty("Position")!.SetMethod!:throw new InvalidDataException("Unknown collider pose operation"),
            "ProModeTrailingCollidersController" when call.Name=="HandleNoteDidMoveInJumpPhase"=>typeof(ColliderTrailing).GetMethod("Handle")!,
            "Action" when call.Name==".ctor"=>typeof(Action).GetConstructor(new[]{typeof(object),typeof(IntPtr)})!,
            _=>throw new InvalidDataException("Unknown collider call: "+call)
        };
        foreach(var instruction in method.Body.Instructions)
        {
            il.MarkLabel(labels[instruction]);
            var opcode=(System.Reflection.Emit.OpCode)typeof(EmitCodes).GetField(instruction.OpCode.Code.ToString())!.GetValue(null)!;
            switch(instruction.Operand)
            {
                case null:il.Emit(opcode);break;
                case float value:il.Emit(opcode,value);break;
                case ParameterDefinition parameter:il.Emit(opcode,(byte)(parameter.Index+1));break;
                case VariableDefinition variable:il.Emit(opcode,(byte)variable.Index);break;
                case Instruction target:il.Emit(opcode,labels[target]);break;
                case FieldReference field:il.Emit(opcode,BindField(field));break;
                case MethodReference call:
                    var bound=BindCall(call);
                    if(bound is ConstructorInfo ctor)il.Emit(opcode,ctor);
                    else{var targetMethod=(MethodInfo)bound;il.Emit(instruction.OpCode.Code==Code.Ldftn?EmitCodes.Ldftn:targetMethod.IsStatic?EmitCodes.Call:opcode,targetMethod);}
                    break;
                default:throw new InvalidDataException("Unknown collider instruction: "+instruction);
            }
        }
        return (T)dynamic.CreateDelegate(typeof(T));
    }
}
