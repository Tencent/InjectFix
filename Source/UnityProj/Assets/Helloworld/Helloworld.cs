/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System;
using System.Collections.Generic;
using UnityEngine;
using IFix.Core;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

public static class StackClass<T> where T : unmanaged
{
    public static Stack<object> cache = new Stack<object>();
}

// 跑不同仔细看文档Doc/example.md
public unsafe class Helloworld : MonoBehaviour {

    public static unsafe object BoxValueToObject<T>(T value) where T : unmanaged
    {
        Stack<object> cache = StackClass<T>.cache;
        object result = null;
        lock (cache)
        {
            result = cache.Count <= 0 ? default(T) : cache.Pop();
        }
        
        GCHandle h = GCHandle.Alloc(result, GCHandleType.Pinned);
        IntPtr ptr = h.AddrOfPinnedObject();

        T* valuePtr = (T*)(ptr).ToPointer();
        *valuePtr = value;
                
        h.Free();

        return result;
    }

    public static unsafe void UnBoxObjectToValue<T>(object value, out T ret) where T : unmanaged
    {
        Stack<object> cache = StackClass<T>.cache;
        
        GCHandle h = GCHandle.Alloc(value, GCHandleType.Pinned);
        IntPtr ptr = h.AddrOfPinnedObject();
        T* valuePtr = (T*)(ptr).ToPointer();
        lock (cache)
        {
            cache.Push(value);
        }
                       
        h.Free();
        fixed (T* p = &ret)
        {
            *p = *valuePtr; 
        }
    }

    public class TestHandle
    {
        public int v1;
        public long v2;
        public Vector3 v4;
        public short v3;
    }

    private void StructHandle(byte* ptr, Value* evaluationStackBase, Value* evaluationStackPointer,
        object[] managedStack)
    {
        var v = *(AllValueStruct*)ptr;
        EvaluationStackOperation.PushValueUnmanaged(evaluationStackBase, evaluationStackPointer, managedStack, v); 
    }

    public TestEnumValue _enumValue = TestEnumValue.t1;
    // check and load patchs
    void Start ()
    {
        print( UnsafeUtility.IsUnmanaged(typeof(TestEnumValue)));
        
        KeyValuePair<int, int> l = default;
        List<int> li = default;
        MyStruct ms = default;
        Vector3 z = default;
        
        VirtualMachine.Info = (s) => UnityEngine.Debug.Log(s);
        //try to load patch for Assembly-CSharp.dll
        var patch = Resources.Load<TextAsset>("Assembly-CSharp.patch");
        if (patch != null)
        {
            UnityEngine.Debug.Log("loading Assembly-CSharp.patch ...");
            var sw = Stopwatch.StartNew();
            var vm = PatchManager.Load(new MemoryStream(patch.bytes));
            vm.externInvokersHandle = (MethodBase  mb, out ExternInvoker ei) =>
            {
                bool ret = false; 
                var fb = new IFix.Binding.IFixBindingCaller(mb, out ret);
                ei = fb.Invoke;
                return ret;
            };

            EvaluationStackOperation.RegistPushFieldAction(typeof(AllValueStruct), StructHandle);



            UnityEngine.Debug.Log("patch Assembly-CSharp.patch, using " + sw.ElapsedMilliseconds + " ms");
        }
        //try to load patch for Assembly-CSharp-firstpass.dll
        patch = Resources.Load<TextAsset>("Assembly-CSharp-firstpass.patch");
        if (patch != null)
        {
            UnityEngine.Debug.Log("loading Assembly-CSharp-firstpass ...");
            var sw = Stopwatch.StartNew();
            PatchManager.Load(new MemoryStream(patch.bytes));
            UnityEngine.Debug.Log("patch Assembly-CSharp-firstpass, using " + sw.ElapsedMilliseconds + " ms");
        }

        test();
        
    }

    public struct MyStruct
    {
        public int x;
        public int y;

        public MyStruct(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
        //public List<int> list;
    }

    private void CallObject(object o)
    {
        Vector3 newPos = new Vector3();// = (Vector3)o;
        UnBoxObjectToValue(o, out newPos);
    }

    private void CallMyObject(object o)
    {
        MyStruct newPos;// = (MyStruct)o;
        UnBoxObjectToValue(o, out newPos);
    }
    
    Vector3 pos = new Vector3(1, 2, 3);
    MyStruct s = new MyStruct(1, 1);
    
    Calculator calc;
    private void Update()
    {
        if(calc == null) calc = new Calculator();
        calc.Add(10, 9);
    }

    //[IFix.Patch]
    void test()
    {
        if(calc == null) calc = new Calculator();
        //test calc.Add
        UnityEngine.Debug.Log("10 + 9 = " +calc.Add(10, 9)); 
        //test calc.Sub
        UnityEngine.Debug.Log("10 - 2 = " + calc.Sub(10, 2));

        var anotherClass = new AnotherClass(1);
        //AnotherClass in Assembly-CSharp-firstpass.dll
        var ret = anotherClass.Call(i => i + 1);
        UnityEngine.Debug.Log("anotherClass.Call, ret = " + ret);

        //test for InjectFix/Fix(Android) InjectFix/Fix(IOS) Menu for unity 2018.3 or newer
#if UNITY_2018_3_OR_NEWER
#if UNITY_IOS
        UnityEngine.Debug.Log("UNITY_IOS");
#endif
#if UNITY_EDITOR
        UnityEngine.Debug.Log("UNITY_EDITOR");
#endif
#if UNITY_ANDROID
        UnityEngine.Debug.Log("UNITY_ANDROID");
#endif
#endif
    }
}
