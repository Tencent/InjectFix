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
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using IFix;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine.Assertions.Must;
using UnityEngine.Profiling;
using Unsafe.As;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public class TestStruct
{
    public int v;
    
    public void TestCall()
    {
        Debug.Log("TestCall " + v);
    }
}

// 跑不同仔细看文档Doc/example.md
public unsafe class Helloworld : MonoBehaviour
{
    public static readonly ProfilerMarker  marker = new ProfilerMarker("marker");

    private Dictionary<Type, bool> dict = new Dictionary<Type, bool>();
    
    static FieldInfo methodCodeField = typeof(Delegate).GetField("method_code",BindingFlags.Instance | BindingFlags.NonPublic);
    static int methodCodeOffset = UnsafeUtility.GetFieldOffset(methodCodeField);
    static FieldInfo targetField = typeof(Delegate).GetField("m_target",BindingFlags.Instance | BindingFlags.NonPublic);
    static int targetOffset = UnsafeUtility.GetFieldOffset(targetField);

    public Transform parent;
    public int xxx;

    private GameObject prefab;
    // check and load patchs
    void Start()
    {
        TestStruct ts = new TestStruct();
        ts.v = 1;
        var method = typeof(TestStruct).GetMethod("TestCall");
        var action = (Action)Delegate.CreateDelegate(typeof(Action), ts, method);
        action();
        
        ts = new TestStruct();
        ts.v = 114514;
        void* p = UnsafeAsUtility.AsPoint(ref ts);
        byte* ap = (byte*)UnsafeAsUtility.AsPoint(ref action);

        int v = 1;
        ref int vv = ref v;
        ref int vvv = ref vv;
        
        *(void**)(ap + targetOffset) = p;
        *(void**)(ap + methodCodeOffset) = p;
        
        //field.SetValue(action,  (IntPtr));
        action();
        IFixBindingCaller.Init();
        //test();
        if(calc == null) calc = new Calculator();
        UnityEngine.Debug.Log("10 + 9 = " + calc.Add(10, 9));
        UnityEngine.Debug.Log("10 + 9 = " + calc.Add(10, 9));
        UnityEngine.Debug.Log("10 + 9 = " + calc.Add(10, 9));
        
        prefab = Resources.Load<GameObject>("Button");
    }


    Vector3 pos = new Vector3(1, 2, 3);

    Calculator calc;

    private void Update()
    {
        for(int i = 0;i<10;i++)
            calc.Add(10, 9);
    }

    static Vector3[] vecs = new Vector3[] { Vector3.zero, Vector3.one, 100 *Vector3.forward, 200 *Vector3.forward, 300 *Vector3.forward, 400 *Vector3.forward,};
    public void TestAsyncInstance()
    {
        Profiler.BeginSample("TestAsyncInstance");
        InstantiateAsync<GameObject>(prefab, 6, parent, vecs);
        Profiler.EndSample();
    }
    
    public void TestSyncInstance()
    {
        Profiler.BeginSample("TestSyncInstance");
        for(int i =0;i<6;i++)
            Instantiate(prefab, Vector3.zero,  Quaternion.identity, parent);
        Profiler.EndSample();
    }

    public void LoadPatch()
    {
        var patch = Resources.Load<TextAsset>("Assembly-CSharp.patch");
        if (patch != null)
        { 
            UnityEngine.Debug.Log("loading Assembly-CSharp.patch ...");
            var sw = Stopwatch.StartNew(); 
            var vm = PatchManager.Load(new MemoryStream(patch.bytes), false,
                (MethodBase mb, out ExternInvoker ei) =>
                {
                        bool ret = false;
                        if (mb is ConstructorInfo)
                        {
                            ei = null;
                            return false;
                        }
                    
                        var fb = new IFixBindingCaller(mb, out ret);
                        ei = fb.Invoke;
                        return ret;
                });

            UnityEngine.Debug.Log("patch Assembly-CSharp.patch, using " + sw.ElapsedMilliseconds + " ms");
        }
        
        test();
    }


    private void OnDestroy()
    {
        try
        {
            if (!enabled)
            {
                using (marker.Auto())
                {
                    IFixBindingCaller.UnInit();
                }
            }
            else
            {
                var m = new ProfilerMarker("marker2");
                using (m.Auto())
                {
                    IFixBindingCaller.UnInit();
                }
            }

            // using (new ProfilerMarker(enabled? "marker" : range + "marker " + range + Random.Range(0, 1)).Auto())
            // {
            //     IFixBindingCaller.UnInit();
            //     Vector3 v = default;
            //     using (new ProfilerMarker($"marker{range}").Auto())
            //     {
            //         IFixBindingCaller.UnInit();
            //     }
            // }
        }
        finally
        {
            IFixBindingCaller.UnInit();
        }
    }

    public void TestRand()
    {
        var sw = Stopwatch.StartNew();
        // for (int i = 0; i < 10000000; i++)
        // {
        //     DoTestRand();
        // }

        DoTestRand();
        // for(int i = 0;i<1000;i++)
        //     calc.Add(10, 9);

        UnityEngine.Debug.Log("Test call 1000 Struct, using " + (float)sw.ElapsedTicks / 10000 + " ms");
    }

    private static int range = 10;
    [Patch]
    public void DoTestRand()
    {
        int i = 0;
        for (i = 0; i < 10000000; ++i)
        {
            Random.Range(-500, 500);
        }
        
        print(i);
    }

    //[IFix.Patch]
    void test()
    {
        if (calc == null) calc = new Calculator();
        //test calc.Add 

        // 测试多线程fix会不会出问题
        Parallel.For(0, 10, (v) =>
        {
            UnityEngine.Debug.Log("10 + 9 = " + calc.Add(10, 9));
            UnityEngine.Debug.Log("10 + 9 = " + calc.Add(10, 9));
            UnityEngine.Debug.Log("10 + 9 = " + calc.Add(10, 9));
        });
        // UnityEngine.Debug.Log("10 + 9 = " + calc.Add(10, 9));
        // UnityEngine.Debug.Log("10 + 9 = " + calc.Add(10, 9));
        // UnityEngine.Debug.Log("10 + 9 = " + calc.Add(10, 9));
        
        
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