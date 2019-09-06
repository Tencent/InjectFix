/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System;
using UnityEngine;

//只是用来演示Assembly-CSharp-firstpass.dll的修复
//所以，该文件要放在Pro Standard Assets或者Plugins目录下
//逻辑很简单，没什么好说的
public class AnotherClass
{
    int pass;

    public AnotherClass(int init)
    {
        this.pass = init;
    }

    [IFix.Patch]
    public int Call(Func<int, int> func)
    {
        int sum = 0;
        for(int i =0; i < 2; i++)
        {
            sum += func(pass);
            Debug.Log(string.Format("i = {0}, sum = {1}", i, sum));
        }
        return sum;
    }

}
