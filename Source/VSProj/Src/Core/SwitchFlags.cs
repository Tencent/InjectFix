/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System;

namespace IFix
{
    //切换到解析执行
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
    public class InterpretAttribute : Attribute
    {
    }

    //直接在要做成补丁的方法上打标签
    [AttributeUsage(AttributeTargets.Method)]
    public class PatchAttribute : Attribute
    {
    }

    //可以手动指定要生成delegate（主要用于闭包）、interface（比如迭代器语法糖）的桥接
    [AttributeUsage(AttributeTargets.Class)]
    public class CustomBridgeAttribute : Attribute
    {

    }
}