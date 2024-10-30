/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms.
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */


//HelloworldCfg.cs里配置了这个类型

using System;
using IFix;
using UnityEngine;

public enum TestEnumValue
{
    t1 = 1,
    t2 = 2,
}

public struct AllValueStruct
{
    public int x;
}

public struct ChildClassStruct
{
    public ClassStruct cs;
}

public struct ClassStruct
{
    public GameObject go;
}

public class Calculator
{
    private TestEnumValue thisEnum = TestEnumValue.t2;
    private Vector3 v = Vector3.right;
    private Vector3? nullableV1 = null;
    private Vector3? nullableV2 = Vector3.left;

    private static Vector3? nullableV3 = Vector3.left;

    private AllValueStruct astruct = new AllValueStruct{x=1};

    //修改成正确的逻辑后，打开如下注释，生成的补丁将修正该函数
    public int Add(int a, int b)
    {
        return 
        TestAllValueStruct(default(AllValueStruct)) + TestAllValueStruct(default(AllValueStruct)) +
        TestAllValueStruct(astruct) +
        TestVector3(default(Vector3)) + TestVector3(default(Vector3)) + TestVector3(v) + TestVector3(Vector3.one) +
        TestEnum(TestEnumValue.t2) + TestEnum(default(TestEnumValue)) + TestEnum(thisEnum) + TestEnum(TestEnumValue.t1) + 
        TestRefInt(ref a) + a +
        TestNullable(null) + TestNullable(Vector3.left) + TestNullable(nullableV1) + TestNullable(nullableV2)
        + TestNullable(nullableV3); 
        //return DoAdd(a, b); 
    }

    public int TestNullable(ref Vector3? v)
    {
        if (v == null)
        {
            return 9 * 1000000;
        }

        return (int)(v.Value.x + v.Value.y + v.Value.z) * 1000000;
    }
    
    public int TestNullable(Vector3? v)
    {
        if (v == null)
        {
            return 9 * 1000000;
        }

        return (int)(v.Value.x + v.Value.y + v.Value.z) * 1000000;
    }

    public int TestRefInt(ref int v)
    {
        v = 100000;
        return v;
    }

    public int TestEnum(TestEnumValue v)
    {
        return (int)v * 1000;
    }

    public int TestAllValueStruct(AllValueStruct v)
    {
        return v.x * 10000;
    }

    public int TestVector3(Vector3 v)
    {
        return 10 * (int)(v.x + v.y + v.z);
    }

    public int Sub(int a, int b)
    {
        return a / b;
    }

    public int Mult(int a, int b)
    {
        return a * b;
    }

    public int Div(int a, int b)
    {
        return a / b;
    }
}