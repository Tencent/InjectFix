/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System.Collections.Generic;
using IFix;
using System;

//1������������[Configure]��ǩ
//2�������EditorĿ¼
[Configure]
public class HelloworldCfg
{
    [IFix]
    static IEnumerable<Type> hotfix
    {
        get
        {
            return typeof(testdll.Test).Assembly.GetTypes();

            //return new List<Type>()
            //{
            //    // typeof(Helloworld),
            //    typeof(testdll.Test),
            //    typeof(IFix.Test.Calculator),
            //    //AnotherClass��Pro Standard Assets�£�����뵽Assembly-CSharp-firstpass.dll�£�������ʾ��dll���޸�
            //    typeof(AnotherClass),
            //};
        }
    }
}
