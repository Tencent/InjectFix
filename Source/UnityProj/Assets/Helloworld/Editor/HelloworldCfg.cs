/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System.Collections.Generic;
using IFix;
using System;

//1、配置类必须打[Configure]标签
//2、必须放Editor目录
[Configure]
public class HelloworldCfg
{
    [IFix]
    static IEnumerable<Type> hotfix
    {
        get
        {
            return new List<Type>()
            {
                typeof(Helloworld),
                typeof(IFix.Test.Calculator),
                //AnotherClass在Pro Standard Assets下，会编译到Assembly-CSharp-firstpass.dll下，用来演示多dll的修复
                typeof(AnotherClass),
            };
        }
    }
}
