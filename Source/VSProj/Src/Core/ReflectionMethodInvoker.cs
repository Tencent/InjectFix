/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System.Reflection;
using System;

namespace IFix.Core
{
    internal class ReflectionMethodInvoker
    {
        int paramCount;

        bool hasThis;

        bool hasReturn;

        bool[] refFlags;

        bool[] outFlags;

        Type[] rawTypes;

        //object[] args;

        MethodBase method;

        ConstructorInfo ctor = null;

        Type returnType = null;

        bool isNullableHasValue = false;
        bool isNullableValue = false;

        public ReflectionMethodInvoker(MethodBase method)
        {
            var paramerInfos = method.GetParameters();
            paramCount = paramerInfos.Length;
            refFlags = new bool[paramCount];
            outFlags = new bool[paramCount];
            rawTypes = new Type[paramCount];
            //args = new object[paramCount];

            for (int i = 0; i < paramerInfos.Length; i++)
            {
                outFlags[i] = paramerInfos[i].IsOut;
                if (paramerInfos[i].ParameterType.IsByRef)
                {
                    refFlags[i] = true;
                    rawTypes[i] = paramerInfos[i].ParameterType.GetElementType();
                }
                else
                {
                    refFlags[i] = false;
                    rawTypes[i] = paramerInfos[i].ParameterType;
                }
            }
            this.method = method;
            if (method.IsConstructor)
            {
                ctor = method as ConstructorInfo;
                returnType = method.DeclaringType;
                hasReturn = true;
            }
            else
            {
                returnType = (method as MethodInfo).ReturnType;
                hasReturn = returnType != typeof(void);
            }
            hasThis = !method.IsStatic;
            bool isNullableMethod = method.DeclaringType.IsGenericType
                && method.DeclaringType.GetGenericTypeDefinition() == typeof(Nullable<>);
            isNullableHasValue = isNullableMethod && method.Name == "get_HasValue";
            isNullableValue = isNullableMethod && method.Name == "get_Value";
        }

        // #lizard forgives
        public unsafe void Invoke(VirtualMachine virtualMachine, ref Call call, bool isInstantiate)
        {
            var managedStack = call.managedStack;
            var pushResult = false;
            var args = new object[paramCount];
            try
            {
                //virtualMachine._Info("method: " + method);
                Value* pArg = call.argumentBase;

                int paramStart = 0;

                if (hasThis && !isInstantiate)
                {
                    paramStart = 1;
                    pArg++;
                }

                for (int i = 0; i < paramCount; i++)
                {
                    if (!outFlags[i])
                    {
                        args[i] = EvaluationStackOperation.ToObject(call.evaluationStackBase, pArg, managedStack,
                            rawTypes[i], virtualMachine);
                    }
                    //if (pArg->Type >= ValueType.Object)
                    //{
                    //    managedStack[pArg->Value1] = null;
                    //}
                    //if (method.Name == "Invoke" && method.DeclaringType.Name == "MethodBase")
                    //{
                    //    VirtualMachine._Info(i + " pArg->Type:" + pArg->Type);
                    //    VirtualMachine._Info(i + " args[i]:" + args[i]);
                    //    if (args[i] != null)
                    //    {
                    //        VirtualMachine._Info(i + " args[i]:" + args[i].GetHashCode());
                    //    }
                    //    VirtualMachine._Info(i + " args[i].GetType:" + (args[i] == null ? 
                    //        "null" : args[i].GetType().ToString()));
                    //    if (i == 1 && args[i] is object[])
                    //    {
                    //        var objs = args[i] as object[];
                    //        for (int j = 0; j < objs.Length;j++)
                    //        {
                    //            VirtualMachine._Info("obj " + j + ": " + (objs[j] == null ? 
                    //            "null" : objs[j].GetType().ToString()));
                    //        }
                    //    }
                    //}
                    pArg++;
                }

                object ret;

                if (isInstantiate || (method.IsConstructor && method.DeclaringType.IsValueType))
                {
                    ret = ctor.Invoke(args);//TODO: Delegate创建用Delegate.CreateDelegate
                }
                else
                {
                    object instance = null;
                    if (hasThis)
                    {
                        instance = EvaluationStackOperation.ToObject(call.evaluationStackBase, call.argumentBase,
                            managedStack, method.DeclaringType, virtualMachine, false);
                    }
                    //Nullable仍然是值类型，只是新增了是否为null的标志位，仍然通过传地址的方式进行方法调用，
                    //但这在反射调用行不通，参数是object类型，boxing到object就是null，所以会触发
                    //“Non-static method requires a target”异常
                    //所以这只能特殊处理一下
                    if (isNullableHasValue)
                    {
                        ret = (instance != null);
                    }
                    else if (isNullableValue)
                    {
                        ret = instance;
                    }
                    else
                    {
                        ret = method.Invoke(instance, args);
                    }
                }

                for (int i = 0; i < paramCount; i++)
                {
                    if (refFlags[i])
                    {
                        call.UpdateReference(i + paramStart, args[i], virtualMachine, rawTypes[i]);
                    }
                }

                if (hasReturn || isInstantiate)
                {
                    if (method.IsConstructor && method.DeclaringType.IsValueType && !isInstantiate)
                    {
                        call.UpdateReference(0, ret, virtualMachine, method.DeclaringType);
                    }
                    else
                    {
                        call.PushObjectAsResult(ret, returnType);
                        pushResult = true;
                    }
                }
            }
            //catch (TargetException  e)
            //{
            //    //VirtualMachine._Info("exception method: " + method + ", in " + method.DeclaringType + ", msg:"
            //        + e.InnerException);
            //    //for (int i = 0; i < paramCount; i++)
            //    //{
            //    //    VirtualMachine._Info("arg " + i + " type: " + (args[i] == null ? "null" : args[i].GetType()
            //    //        .ToString()) + " value: " + args[i]);
            //    //}
            //    if (e.InnerException is System.ArgumentException && args.Length == 2 && args[1] is object[])
            //    {
            //        //VirtualMachine._Info("exception method: " + method + ", in " + method.DeclaringType
            //        //    + ", msg:" + e.InnerException);
            //        if (instance is MethodBase)
            //        {
            //            MethodBase mb = instance as MethodBase;
            //            VirtualMachine._Info("exception method: " + mb + ", in " + mb.DeclaringType);
            //        }
            //        args = args[1] as object[];
            //        for (int i = 0; i < args.Length; i++)
            //        {
            //            VirtualMachine._Info("arg " + i + " type: " + (args[i] == null ? 
            //            "null" : args[i].GetType().ToString()) + " value: " + args[i]);
            //        }
            //    }
            //    throw e;
            //}
            finally
            {
                //for (int i = 0; i < paramCount; i++)
                //{
                //    args[i] = null;
                //}
                Value* pArg = call.argumentBase;
                if (pushResult)
                {
                    pArg++;
                }
                for (int i = (pushResult ? 1 : 0); i < paramCount + ((hasThis && !isInstantiate) ? 1 : 0); i++)
                {
                    managedStack[pArg - call.evaluationStackBase] = null;
                    pArg++;
                }
            }
        }
    }
}
