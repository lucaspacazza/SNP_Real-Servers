// Test only: the production comparer is compiled unchanged against this small
// managed pointer boundary. No Unity classes or native Il2Cpp calls execute.
namespace Il2CppInterop.Runtime.InteropTypes
{
    public class Il2CppObjectBase
    {
        private readonly IntPtr _pointer;
        private readonly bool _collected;
        public Il2CppObjectBase(IntPtr pointer, bool collected = false) { _pointer = pointer; _collected = collected; }
        public IntPtr Pointer => _collected ? throw new Il2CppInterop.Runtime.ObjectCollectedException("Collected fake native object") : _pointer;
    }
}
namespace Il2CppInterop.Runtime
{
    public sealed class ObjectCollectedException : Exception
    {
        public ObjectCollectedException(string message) : base(message) { }
    }
}
