/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

namespace IFix.Core
{
    using System.Reflection;
    using System;
    using System.Linq.Expressions;

    public class ObjectClone
    {
        MethodInfo memberwiseClone;
        //Func<object> ptrToMemberwiseClone;
        //FieldInfo target;
        //Func<object, object> cloneFunc;
        public ObjectClone()
        {
            memberwiseClone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance
                | BindingFlags.NonPublic);
            //ptrToMemberwiseClone = new Func<object>(MemberwiseClone);
            //target = ptrToMemberwiseClone.GetType().GetField("_target", BindingFlags.Instance
            //    | BindingFlags.NonPublic);
            //var methodInfo = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance
            //    | BindingFlags.NonPublic);
            //var p = Expression.Parameter(typeof(object), "obj");
            //var mce = Expression.Call(p, methodInfo);
            //cloneFunc = Expression.Lambda<Func<object, object>>(mce, p).Compile();//TODO: 需要用到jit么？
        }

        public object Clone(object obj)
        {
            return memberwiseClone.Invoke(obj, null);//1.79s
            //target.SetValue(ptrToMemberwiseClone, obj);
            //return ptrToMemberwiseClone();//1.17s
            //return ((Func<object>)Delegate.CreateDelegate(typeof(Func<object>), obj, memberwiseClone))();//3.05s
            //return cloneFunc(obj);//0.06s
        }
    }
}