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
    t1,
    t2
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

    private AllValueStruct astruct = new AllValueStruct();
    //修改成正确的逻辑后，打开如下注释，生成的补丁将修正该函数
    [Patch]
    public int Add(int a, int b)
    {
        return
            TestAllValueStruct(default(AllValueStruct)) + TestAllValueStruct(default(AllValueStruct)) + TestAllValueStruct(astruct)
            + TestVector3(default(Vector3)) + TestVector3(default(Vector3)) + TestVector3(v) + TestVector3(Vector3.one) 
            + TestEnum(default(TestEnumValue)) + TestEnum(default(TestEnumValue)) + TestEnum(thisEnum) + TestEnum(TestEnumValue.t1);
        //return DoAdd(a, b);
    }

    public int TestEnum(TestEnumValue v)
    {
        return (int)v;
    }

    public int TestAllValueStruct(AllValueStruct v)
    {
        return 10000;
    }
    
    public int TestAllValueStruct(ChildClassStruct v)
    {
        return 996;
    }
    
    public int TestAllValueStruct(ClassStruct v)
    {
        return 996;
    }

    public int DoAdd(int a, int b)
    {
        return a * b; 
    }

    public int Sub(int a, int b)
    {
        return a / b;
    }
    
    public int TestVector3(Vector3 v)
    {
        return 0;
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