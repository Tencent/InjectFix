/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System;
using System.Reflection;
using System.Collections.Generic;

namespace IFix.Core
{
    //虚拟机使用给工具类
    public static class Utils
    {
        /// <summary>
        /// 判断一个方法是否能赋值到一个delegate变量
        /// </summary>
        /// <param name="delegateMethod">delegate变量的类型里头的invoke方法</param>
        /// <param name="method">待赋值的方法</param>
        /// <returns>是否能赋值</returns>
        public static bool IsAssignable(MethodInfo delegateMethod, MethodInfo method)
        {
            if (delegateMethod == null || method == null)
            {
                return false;
            }
            if (delegateMethod.ReturnType != method.ReturnType)
            {
                return false;
            }
            ParameterInfo[] lhsParams = delegateMethod.GetParameters();
            ParameterInfo[] rhsParams = method.GetParameters();
            if (lhsParams.Length != rhsParams.Length)
            {
                return false;
            }

            for (int i = 0; i < lhsParams.Length; i++)
            {
                if (lhsParams[i].ParameterType != rhsParams[i].ParameterType
                    || lhsParams[i].IsOut != rhsParams[i].IsOut)
                {
                    return false;
                }
            }

            return true;
        }

        //适配器的缓存，如果不做缓存，每次都调用IsAssignable一个个的取匹配会非常慢
        static Dictionary<Type, MethodInfo> delegateAdptCache = new Dictionary<Type, MethodInfo>();

        /// <summary>
        /// 从一个wrapper对象里头，查找能够适配到特定delegate的方法
        /// </summary>
        /// <param name="obj">wrapper对象</param>
        /// <param name="delegateType">delegate类型</param>
        /// <param name="perfix">方法前缀，能够排除掉一些方法，比如构造函数</param>
        /// <returns></returns>
        public static Delegate TryAdapterToDelegate(object obj, Type delegateType, string perfix)
        {
            MethodInfo method;
            if (!delegateAdptCache.TryGetValue(delegateType, out method))
            {
                MethodInfo delegateMethod = delegateType.GetMethod("Invoke");
                var methods = obj.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly);
                for (int i = 0; i < methods.Length; i++)
                {
                    if (methods[i].Name.StartsWith(perfix) && IsAssignable(delegateMethod, methods[i]))
                    {
                        method = methods[i];
                        delegateAdptCache[delegateType] = method;
                    }
                }
            }
            if (method == null)
            {
                return null;
            }
            else
            {
                return Delegate.CreateDelegate(delegateType, obj, method);
            }
        }
    }
}