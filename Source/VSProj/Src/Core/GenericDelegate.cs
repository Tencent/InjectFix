/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

//ifix会分析闭包，针对闭包对应的delegate生成适配器
//但是有的情况是原来那个地方不是用闭包，修复时用了闭包，这时会报找不到适配器的错误。
//这种问题可以通过CustomBridage配置来避免，但是很多时候用户无法预知这种情况。
//这里就是为了减少这种情况的影响：参数个数不超过4个，且均为引用类型，
//无返回值或者返回值是引用类型，这里能够做到（通过泛型）自动生成适配器。

namespace IFix.Core
{
    internal class GenericDelegateFactory
    {
        //无返回值泛型方法
        static MethodInfo[] genericAction = null;
        //有返回值泛型方法
        static MethodInfo[] genericFunc = null;

        //泛型delegate适配器构造器的缓存
        static Dictionary<Type, Func<GenericDelegate, Delegate>> genericDelegateCreatorCache
            = new Dictionary<Type, Func<GenericDelegate, Delegate>>();

        //Prevent unity il2cpp code stripping
        static void PreventStripping(object obj)
        {
            if (obj != null)
            {
                var gd = new GenericDelegate(null, -1, null);
                gd.Action();
                gd.Action(obj);
                gd.Action(obj, obj);
                gd.Action(obj, obj, obj);
                gd.Action(obj, obj, obj, obj);

                gd.Func<object>();
                gd.Func<object, object>(obj);
                gd.Func<object, object, object>(obj, obj);
                gd.Func<object, object, object, object>(obj, obj, obj);
                gd.Func<object, object, object, object, object>(obj, obj, obj, obj);
            }
        }

        internal static Delegate Create(Type delegateType, VirtualMachine virtualMachine, int methodId, object anonObj)
        {
            Func<GenericDelegate, Delegate> genericDelegateCreator;
            if (!genericDelegateCreatorCache.TryGetValue(delegateType, out genericDelegateCreator))
            {
                //如果泛型方法数组未初始化
                if (genericAction == null)
                {
                    PreventStripping(null);
                    var methods = typeof(GenericDelegate).GetMethods(BindingFlags.Instance | BindingFlags.Public
                        | BindingFlags.DeclaredOnly);
                    genericAction = methods.Where(m => m.Name == "Action").OrderBy(m => m.GetParameters().Length)
                        .ToArray();
                    genericFunc = methods.Where(m => m.Name == "Func").OrderBy(m => m.GetParameters().Length).ToArray();
                }

                MethodInfo delegateMethod = delegateType.GetMethod("Invoke");

                var parameters = delegateMethod.GetParameters();
                if ((delegateMethod.ReturnType.IsValueType && delegateMethod.ReturnType != typeof(void)) 
                    || parameters.Length > 4
                    || parameters.Any(p => p.ParameterType.IsValueType || p.ParameterType.IsByRef)
                    )
                {
                    //如果不在支持的范围，则生成一个永远返回空的构造器
                    genericDelegateCreator = (x) => null;
                }
                else
                {
                    if (delegateMethod.ReturnType == typeof(void) && parameters.Length == 0)
                    {
                        //对无参无返回值特殊处理
                        var methodInfo = genericAction[0];
                        genericDelegateCreator = (o) => Delegate.CreateDelegate(delegateType, o, methodInfo);
                    }
                    else
                    {
                        //根据参数个数，返回值找到泛型实现
                        var typeArgs = parameters.Select(pinfo => pinfo.ParameterType);
                        MethodInfo genericMethodInfo = null;
                        if (delegateMethod.ReturnType == typeof(void))
                        {
                            genericMethodInfo = genericAction[parameters.Length];
                        }
                        else
                        {
                            genericMethodInfo = genericFunc[parameters.Length];
                            //如果是有返回值，需要加上返回值作为泛型实参
                            typeArgs = typeArgs.Concat(new Type[] { delegateMethod.ReturnType });
                        }
                        //实例化泛型方法
                        var methodInfo = genericMethodInfo.MakeGenericMethod(typeArgs.ToArray());
                        //构造器
                        genericDelegateCreator = (o) => Delegate.CreateDelegate(delegateType, o, methodInfo);
                    }
                }
                //缓存构造器，下次调用直接返回
                genericDelegateCreatorCache[delegateType] = genericDelegateCreator;
            }
            //创建delegate
            return genericDelegateCreator(new GenericDelegate(virtualMachine, methodId, anonObj));
        }
    }

    //泛型适配器
    internal class GenericDelegate
    {
        //指向的虚拟机对象
        VirtualMachine virtualMachine;

        //虚拟机方法id
        int methodId;

        //绑定的匿名对象
        object anonObj;

        //预计算，是否要把anonObj push的标志未
        bool pushSelf;

        //预计算，如果有anonObj参数个数则要+1
        int extraArgNum;

        internal GenericDelegate(VirtualMachine virtualMachine, int methodId, object anonObj)
        {
            this.virtualMachine = virtualMachine;
            this.methodId = methodId;
            this.anonObj = anonObj;
            pushSelf = anonObj != null;
            extraArgNum = pushSelf ? 1 : 0;
        }

        public void Action()
        {
            Call call = Call.Begin();
            if (pushSelf)
            {
                call.PushObject(anonObj);
            }
            virtualMachine.Execute(methodId, ref call, extraArgNum);
        }

        public void Action<T1>(T1 p1)
            where T1 : class
        {
            //创建call对象
            Call call = Call.Begin();
            if (pushSelf)
            {
                //如果有绑定的匿名对象，push
                call.PushObject(anonObj);
            }
            //push第一个参数
            call.PushObject(p1);
            //调用指定id的虚拟机方法
            virtualMachine.Execute(methodId, ref call, 1 + extraArgNum);
        }

        public void Action<T1, T2>(T1 p1, T2 p2) 
            where T1 : class
            where T2 : class
        {
            Call call = Call.Begin();
            if (pushSelf)
            {
                call.PushObject(anonObj);
            }
            call.PushObject(p1);
            call.PushObject(p2);
            virtualMachine.Execute(methodId, ref call, 2 + extraArgNum);
        }

        public void Action<T1, T2, T3>(T1 p1, T2 p2, T3 p3)
            where T1 : class
            where T2 : class
            where T3 : class
        {
            Call call = Call.Begin();
            if (pushSelf)
            {
                call.PushObject(anonObj);
            }
            call.PushObject(p1);
            call.PushObject(p2);
            call.PushObject(p3);
            virtualMachine.Execute(methodId, ref call, 3 + extraArgNum);
        }

        public void Action<T1, T2, T3, T4>(T1 p1, T2 p2, T3 p3, T4 p4)
            where T1 : class
            where T2 : class
            where T3 : class
            where T4 : class
        {
            Call call = Call.Begin();
            if (pushSelf)
            {
                call.PushObject(anonObj);
            }
            call.PushObject(p1);
            call.PushObject(p2);
            call.PushObject(p3);
            call.PushObject(p4);
            virtualMachine.Execute(methodId, ref call, 4 + extraArgNum);
        }

        public TResult Func<TResult>()
            where TResult : class
        {
            Call call = Call.Begin();
            if (pushSelf)
            {
                call.PushObject(anonObj);
            }
            virtualMachine.Execute(methodId, ref call, extraArgNum);
            return (TResult)call.GetObject();
        }

        public TResult Func<T1, TResult>(T1 p1)
            where T1 : class
            where TResult : class
        {
            Call call = Call.Begin();
            if (pushSelf)
            {
                call.PushObject(anonObj);
            }
            call.PushObject(p1);
            virtualMachine.Execute(methodId, ref call, 1 + extraArgNum);
            //从栈上获取结果
            return (TResult)call.GetObject();
        }

        public TResult Func<T1, T2, TResult>(T1 p1, T2 p2)
            where T1 : class
            where T2 : class
            where TResult : class
        {
            Call call = Call.Begin();
            if (pushSelf)
            {
                call.PushObject(anonObj);
            }
            call.PushObject(p1);
            call.PushObject(p2);
            virtualMachine.Execute(methodId, ref call, 2 + extraArgNum);
            return (TResult)call.GetObject();
        }

        public TResult Func<T1, T2, T3, TResult>(T1 p1, T2 p2, T3 p3)
            where T1 : class
            where T2 : class
            where T3 : class
            where TResult : class
        {
            Call call = Call.Begin();
            if (pushSelf)
            {
                call.PushObject(anonObj);
            }
            call.PushObject(p1);
            call.PushObject(p2);
            call.PushObject(p3);
            virtualMachine.Execute(methodId, ref call, 3 + extraArgNum);
            return (TResult)call.GetObject();
        }

        public TResult Func<T1, T2, T3, T4, TResult>(T1 p1, T2 p2, T3 p3, T4 p4)
            where T1 : class
            where T2 : class
            where T3 : class
            where T4 : class
            where TResult : class
        {
            Call call = Call.Begin();
            if (pushSelf)
            {
                call.PushObject(anonObj);
            }
            call.PushObject(p1);
            call.PushObject(p2);
            call.PushObject(p3);
            call.PushObject(p4);
            virtualMachine.Execute(methodId, ref call, 4 + extraArgNum);
            return (TResult)call.GetObject();
        }
    }
}
