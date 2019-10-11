/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System;

namespace IFix.Core
{
    //该接口由注入器自动实现
    public interface WrappersManager
    {
        //创建一个delegate，如果anon非空就是闭包
        Delegate CreateDelegate(Type type, int id, object anon);
        //创建一个interface桥接器
        AnonymousStorey CreateBridge(int fieldNum, int[] slots, VirtualMachine virtualMachine);
        //创建一个wrapper对象（会由补丁加载逻辑调用，创建后放入wrapper数组）
        object CreateWrapper(int id);
        //初始化wrapper数组
        object InitWrapperArray(int len);
    }
}
