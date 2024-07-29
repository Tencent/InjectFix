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
using System.Threading.Tasks;
using IFix;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Assertions.Must;
using UnityEngine.Profiling;
using Random = UnityEngine.Random;


// 跑不同仔细看文档Doc/example.md
public unsafe class Helloworld : MonoBehaviour
{
    public TestEnumValue _enumValue = TestEnumValue.t1;

    private Dictionary<Type, bool> dict = new Dictionary<Type, bool>();
    

    // check and load patchs
    void Start()
    {
        //test();
        if(calc == null) calc = new Calculator();
    }


    Vector3 pos = new Vector3(1, 2, 3);

    Calculator calc;

    private void Update()
    {
        for(int i = 0;i<10;i++)
            calc.Add(10, 9);
    }

    public void LoadPatch()
    {
        var patch = Resources.Load<TextAsset>("Assembly-CSharp.patch");
        if (patch != null)
        { 
            UnityEngine.Debug.Log("loading Assembly-CSharp.patch ...");
            var sw = Stopwatch.StartNew(); 
            var vm = PatchManager.Load(new MemoryStream(patch.bytes));
            UnityEngine.Debug.Log("patch Assembly-CSharp.patch, using " + sw.ElapsedMilliseconds + " ms");
        }
        
        test();
    }
    
    public void TestRand()
    {
        var sw = Stopwatch.StartNew();
        //DoTestRand();
        for(int i = 0;i<1000;i++)
            calc.Add(10, 9);

        UnityEngine.Debug.Log("Test call 1000 Struct, using " + (float)sw.ElapsedTicks / 10000 + " ms");
    }

    [Patch]
    public void DoTestRand()
    {
        for (int i = 0; i < 10000000; i++)
        {
            Random.Range(-500, 500);
        }
    }

    //[IFix.Patch]
    void test()
    {
        if (calc == null) calc = new Calculator();
        //test calc.Add 

        // 测试多线程fix会不会出问题
        Parallel.For(0, 100, (v) =>
        {
            UnityEngine.Debug.Log("10 + 9 = " + calc.Add(10, 9));
            UnityEngine.Debug.Log("10 + 9 = " + calc.Add(10, 9));
            UnityEngine.Debug.Log("10 + 9 = " + calc.Add(10, 9));
        });
        
        
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