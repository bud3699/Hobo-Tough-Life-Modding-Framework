using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using BepInEx.Logging;

namespace HoboModPlugin.Framework
{
    public static class FrameworkUtils
    {
        private static ManualLogSource _log;

        public static void Initialize(ManualLogSource log)
        {
            _log = log;
        }

        public static unsafe void SetIl2CppField<T>(T instance, string fieldName, object value) where T : Il2CppObjectBase
        {
            if (instance == null) return;

            var classPtr = Il2CppClassPointerStore<T>.NativeClassPtr;
            var fieldPtr = IL2CPP.GetIl2CppField(classPtr, fieldName);

            if (fieldPtr == IntPtr.Zero)
            {
                _log?.LogError($"Field '{fieldName}' not found on type {typeof(T).Name}");
                return;
            }

            var instancePtr = IL2CPP.Il2CppObjectBaseToPtrNotNull(instance);
            
            // For primitive types and string
            if (value is int i)
            {
                *(int*)((nint)instancePtr + (int)IL2CPP.il2cpp_field_get_offset(fieldPtr)) = i;
            }
            else if (value is bool b)
            {
                *(bool*)((nint)instancePtr + (int)IL2CPP.il2cpp_field_get_offset(fieldPtr)) = b;
            }
            else if (value is float f)
            {
                *(float*)((nint)instancePtr + (int)IL2CPP.il2cpp_field_get_offset(fieldPtr)) = f;
            }
            else if (value is string s)
            {
                // String needs WriteBarrier? IL2CPP.il2cpp_gc_wbarrier_set_field handles object references?
                // For strings we usually convert to Il2CppString ptr.
                var strPtr = IL2CPP.ManagedStringToIl2Cpp(s);
                 IL2CPP.il2cpp_gc_wbarrier_set_field(instancePtr, (nint)instancePtr + (int)IL2CPP.il2cpp_field_get_offset(fieldPtr), strPtr);
            }
            else if (value is Il2CppObjectBase obj)
            {
                var objPtr = IL2CPP.Il2CppObjectBaseToPtr(obj);
                IL2CPP.il2cpp_gc_wbarrier_set_field(instancePtr, (nint)instancePtr + (int)IL2CPP.il2cpp_field_get_offset(fieldPtr), objPtr);
            }
            else if (value is System.Enum)
            {
                // Handle enums by casting to int (most enums are int32)
                // If enum is byte or long, this might fail, but standard enums are int.
                int enumVal = (int)(object)value;
                *(int*)((nint)instancePtr + (int)IL2CPP.il2cpp_field_get_offset(fieldPtr)) = enumVal;
            }
            else
            {
                _log?.LogWarning($"Unsupported type {value?.GetType()} for field {fieldName}");
            }
        }
    }
}
