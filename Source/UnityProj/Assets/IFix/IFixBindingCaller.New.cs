
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unsafe.As;

namespace IFix.Core
{
    using System;
    using System.Reflection;
    using IFix.Core;
    using UnityEngine;
    using System.IO;
#if UNITY_5_5_OR_NEWER
    using UnityEngine.Profiling;
#endif
    using UnityEngine;
    public unsafe partial class IFixBindingCaller
    {
        
        static FieldInfo methodCodeField = typeof(Delegate).GetField("method_code",BindingFlags.Instance | BindingFlags.NonPublic);
        static int methodCodeOffset = UnsafeUtility.GetFieldOffset(methodCodeField);
        static FieldInfo targetField = typeof(Delegate).GetField("m_target",BindingFlags.Instance | BindingFlags.NonPublic);
        static int targetOffset = UnsafeUtility.GetFieldOffset(targetField);

        private static FileDictionary<string, int> delDict;

        public static void Init()
        {
            string dictPath = Application.persistentDataPath + "/BindingDelDict.bytes";
            var datas = Resources.Load<TextAsset>("BindingDelDict").bytes;
            File.WriteAllBytes(dictPath, datas);
            delDict = new FileDictionary<string, int>(dictPath, 1024, false, true);
        }

        public static void UnInit()
        {
            if (delDict != null)
            {
                delDict.Dispose();
            }
        }

        public ExternInvoker InvokeCall = null;
        private MethodInfo method;
        Delegate caller = null;
        byte* callerPtr = null;
        bool hasThis;
        bool pushResult;
        int paramStart = 0;
        int paramCount = 0; 

        public IFixBindingCaller(MethodBase method, out bool isSuccess)
        {
            this.method = (MethodInfo)method;
            isSuccess = false;
            string methodUniqueStr = TypeNameUtils.GetMethodDelegateKey(method);
            
            if (methodUniqueStr == "")
            {
                isSuccess = false;
                return;
            }

            if (delDict == null)
            {
                Init();
            }

            if (delDict.TryGetValue(methodUniqueStr, out int info))
            {
                InitParams();
                var invokeMethod = typeof(IFixBindingCaller).GetMethod($"Invoke{info}");
                InvokeCall =  (ExternInvoker)Delegate.CreateDelegate(typeof(ExternInvoker), this, invokeMethod);
    
                var delType = Type.GetType($"IFix.Core.IFixBindingCaller+IFixCallDel{info}");
                if (hasThis)
                {
                    //object obj = Activator.CreateInstance(method.ReflectedType);
                    caller = Delegate.CreateDelegate(delType, null, (MethodInfo)method);
                }
                else
                    caller = Delegate.CreateDelegate(delType, (MethodInfo)method);

                callerPtr = (byte*)UnsafeAsUtility.AsPoint(ref caller);
                
                isSuccess = true;
            }
            
        }

        private void InitParams()
        {
            hasThis = !method.IsStatic;
            paramStart = method.IsStatic ? 0 : 1;
            pushResult = method.ReturnType != typeof(void);
            paramCount = method.GetParameters().Length;
        }
        
        public void Invoke(VirtualMachine virtualMachine, ref Call call, bool isInstantiate)
        {
            if (hasThis)
            {
                object instance = call.managedStack[call.argumentBase->Value1];
                //targetField.SetValue(caller, instance);
                void* p = UnsafeAsUtility.AsPoint(ref instance);
                
#if ENABLE_IL2CPP
                *(void**)(callerPtr + methodCodeOffset) = p;
#else
                *(void**)(callerPtr + targetOffset) = p;
#endif
            }
            
            // 这里走delegate创建
            InvokeCall(virtualMachine, ref call, isInstantiate);

            // finally
            // {
            //     Value* pArg = call.argumentBase;
            //     if (pushResult)
            //     {
            //         pArg++;
            //     }
            //     
            //     for (int i = (pushResult ? 1 : 0); i < paramCount + (hasThis ? 1 : 0); i++)
            //     {
            //         BoxUtils.RecycleObject(call.managedStack[pArg - call.evaluationStackBase]);
            //         call.managedStack[pArg - call.evaluationStackBase] = null;
            //         pArg++;
            //     }
            // }
        }

    }
}
