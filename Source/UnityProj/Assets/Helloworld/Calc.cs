/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

namespace IFix.Test
{
    //HelloworldCfg.cs里配置了这个类型
    public class Calculator
    {
        //修改成正确的逻辑后，打开如下注释，生成的补丁将修正该函数
        //[Patch]
        public int Add(int a, int b)
        {
            return a * b;
        }

        public int Sub(int a, int b)
        {
            return a / b;
        }
    }
}
