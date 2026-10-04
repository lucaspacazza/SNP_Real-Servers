using System.Reflection;
using System.Runtime.ExceptionServices;

internal static class Program
{
    private static int passed, failed;
    private static int Main()
    {
        Run("Native insertion assigns the current counter before advancing it", () =>
        {
            int next=284, slot=-1;
            var calls=new List<string>();
            var uid=Reserve(()=>next,v=>{calls.Add("counter");next=v;},_=>false,v=>{calls.Add("slot");slot=v;},()=>slot);
            Equal(284,uid); Equal(284,slot); Equal(285,next); Equal("slot,counter",string.Join(',',calls));
        });
        Run("A reserved UID is never assigned to another physical slot", () =>
        {
            int next=284,slot=-1;
            Equal(286,Reserve(()=>next,v=>next=v,id=>id==284||id==285,v=>slot=v,()=>slot));
            Equal(287,next);
        });
        Run("An invalid starting counter cannot create a zero or negative reservation", () =>
        {
            int next=-1,slot=-1;
            Equal(1,Reserve(()=>next,v=>next=v,_=>false,v=>slot=v,()=>slot)); Equal(2,next);
        });
        Run("A failed native registration does not advance the global counter", () =>
        {
            int next=284;
            Throws<IOException>(()=>Reserve(()=>next,v=>next=v,_=>false,_=>throw new IOException("registration"),()=>-1));
            Equal(284,next);
        });
        Run("An unconfirmed native setter is rejected before publishing a reservation", () =>
        {
            int next=284;
            Throws<InvalidOperationException>(()=>Reserve(()=>next,v=>next=v,_=>false,_=>{},()=>-1)); Equal(284,next);
        });
        Run("An exhausted integer counter cannot wrap into a loose sentinel", () =>
        {
            Throws<OverflowException>(()=>Reserve(()=>int.MaxValue,_=>throw new Exception("write"),_=>false,_=>throw new Exception("assign"),()=>-1));
        });
        Run("Collision search is bounded before any native mutation", () =>
        {
            int probes=0;
            Throws<InvalidOperationException>(()=>Reserve(()=>10,_=>throw new Exception("write"),_=>{probes++;return true;},_=>throw new Exception("assign"),()=>-1,3));
            Equal(3,probes);
        });
        Run("A nonpositive search budget is rejected", () => Throws<ArgumentOutOfRangeException>(()=>Reserve(()=>10,_=>{},_=>false,_=>{},()=>-1,0)));
        Run("A free minus-one native slot must be repaired", () => True(ShouldMark(284,new[]{-1,-1,-1},0,3)));
        Run("Zero native placeholders must also be repaired", () => True(ShouldMark(284,new[]{0,0,0},0,3)));
        Run("A partial reservation repairs only its own free remainder", () => True(ShouldMark(284,new[]{284,-1,0},0,3)));
        Run("An intact multi-U reservation needs no second native mutation", () => True(!ShouldMark(284,new[]{284,284,284},0,3)));
        Run("Another server's occupied units can never be overwritten", () => True(!ShouldMark(284,new[]{284,285,-1},0,3)));
        Run("An invalid UID cannot be written into rack occupancy", () => True(!ShouldMark(-1,new[]{-1,-1},0,2)));
        Run("An out-of-range reservation cannot be partially published", () => True(!ShouldMark(284,new[]{-1,-1},1,3)));
        Run("Negative positions cannot mutate native arrays", () => True(!ShouldMark(284,new[]{-1,-1},-1,1)));
        Run("The exact requested offset is used without claiming adjacent servers", () => True(ShouldMark(284,new[]{285,-1,-1,286},1,2)));
        Run("Legacy repair accepts one exactly matching saved pose", () => Equal(0,Match(new[]{Pose(1,2,3)},Pose(1,2,3))));
        Run("Legacy repair does not choose a nearby slot", () => Equal(-1,Match(new[]{Pose(1,2,3)},Pose(1.001f,2,3))));
        Run("Legacy repair rejects ambiguous identical slot poses", () => Equal(-1,Match(new[]{Pose(1,2,3),Pose(1,2,3)},Pose(1,2,3))));
        Run("Occupied slots cannot receive a legacy server", () => Equal(-1,Match(new[]{Pose(1,2,3)},Pose(1,2,3),_=>false)));
        Run("An opposite quaternion sign represents the same saved rotation", () => Equal(0,Match(new[]{Pose(1,2,3)},new[]{1f,2f,3f,0f,0f,0f,-1f})));
        Run("A different rack orientation cannot match a saved server", () => Equal(-1,Match(new[]{Pose(1,2,3)},new[]{1f,2f,3f,0f,1f,0f,0f})));
        Console.WriteLine($"Rack persistence: {passed} passed; {failed} failed.");
        return failed==0?0:1;
    }
    private static object? Call(string name, params object?[] arguments)
    {
        var type=Assembly.GetExecutingAssembly().GetType("DataCenterAutomatorServers.NativeRackUidAllocator") ?? throw new TypeLoadException("Native rack UID allocation policy is missing.");
        try { return type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,arguments); }
        catch(TargetInvocationException error) when(error.InnerException!=null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    private static int Reserve(Func<int> next,Action<int> write,Func<int,bool> occupied,Action<int> assign,Func<int> verify,int budget=10000)=>(int)Call("Reserve",next,write,occupied,assign,verify,budget)!;
    private static bool ShouldMark(int uid,int[] slots,int index,int units)=>(bool)Call("ShouldMark",uid,slots,index,units)!;
    private static float[] Pose(float x,float y,float z)=>new[]{x,y,z,0f,0f,0f,1f};
    private static int Match(float[][] poses,float[] target,Predicate<int>? available=null)
    {
        var type=Assembly.GetExecutingAssembly().GetType("DataCenterAutomatorServers.ExactRackPoseIndex") ?? throw new TypeLoadException("Exact legacy pose matching is missing.");
        var index=Activator.CreateInstance(type,new object[]{poses})!;
        return (int)type.GetMethod("FindUnique")!.Invoke(index,new object[]{target,available??(_=>true)})!;
    }
    private static void Run(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    private static void True(bool condition){if(!condition)throw new InvalidOperationException("Assertion failed.");}
    private static void Equal<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new InvalidOperationException($"Expected {expected}; got {actual}.");}
    private static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
}
