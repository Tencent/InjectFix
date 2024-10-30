/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System.Reflection;
using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

namespace IFix.Core
{
    internal class ReflectionMethodInvoker
    {
        int paramCount;
        bool hasThis;
        bool valueInstance;

        bool hasReturn;

        bool[] refFlags;

        bool[] outFlags;

        bool[] isValueFlags;

        Type[] rawTypes;

        [ThreadStatic]
        static Stack<object[]>[] argsPool = null;

        MethodBase method;

        ConstructorInfo ctor = null;

        Type returnType = null;
        bool returnTypeIsValueType;
        Type declaringType = null;
        Type cacheReturnType = null;

        bool isNullableHasValue = false;
        bool isNullableValue = false;
		bool isNullableGetValueOrDefault = false;
        bool IsConstructor = false;
        bool declaringTypeIsValueType = false;

        private static object[] GetArgs(int paramCount)
        {
            if (argsPool == null)
            {
                argsPool = new Stack<object[]>[256];
                for (int i = 0; i < 256; i++)
                {
                    argsPool[i] = new Stack<object[]>();
                }
            }
            
            Stack<object[]> pool = argsPool[paramCount];
            if (pool.Count > 0)
            {
                return pool.Pop();
            }
            return new object[paramCount];
        }

        private static void RecycleArgsToPool(object[] args)
        {
            int paramCount = args.Length;
            Stack<object[]> pool = argsPool[paramCount];
            // if(pool == null)
            // {
            //     pool = new Stack<object[]>();
            //     argsPool[paramCount] = pool;
            // }
            pool.Push(args);
        }

        private void RecycleArgs(object[] args)
        {
            for (int i = 0; i < paramCount; i++)
            {
                if(isValueFlags[i])
                    BoxUtils.RecycleObject(args[i]);

                args[i] = null;
            }

            RecycleArgsToPool(args);
        }

        public ReflectionMethodInvoker(MethodBase method)
        {
            var paramerInfos = method.GetParameters();
            paramCount = paramerInfos.Length;
            refFlags = new bool[paramCount];
            outFlags = new bool[paramCount];
            isValueFlags = new bool[paramCount];
            rawTypes = new Type[paramCount];

            for (int i = 0; i < paramerInfos.Length; i++)
            {
                outFlags[i] = !paramerInfos[i].IsIn && paramerInfos[i].IsOut;
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

                isValueFlags[i] = paramerInfos[i].ParameterType.IsValueType;
            }
            this.method = method;
            returnTypeIsValueType = false;
            if (method.IsConstructor)
            {
                ctor = method as ConstructorInfo;
                returnType = method.DeclaringType;
                returnTypeIsValueType = returnType.IsValueType;
                cacheReturnType = Nullable.GetUnderlyingType(returnType);
                if (cacheReturnType == null) cacheReturnType = returnType;

                hasReturn = true;
            }
            else
            {
                returnType = (method as MethodInfo).ReturnType;
                hasReturn = returnType != typeof(void);
                if (hasReturn)
                {
                    returnTypeIsValueType = returnType.IsValueType;
                    cacheReturnType = Nullable.GetUnderlyingType(returnType);
                    if (cacheReturnType == null) cacheReturnType = returnType;
                }
            }
            hasThis = !method.IsStatic;
            valueInstance = hasThis && method.DeclaringType.IsValueType;

            bool isNullableMethod = method.DeclaringType.IsGenericType
                && method.DeclaringType.GetGenericTypeDefinition() == typeof(Nullable<>);
            isNullableHasValue = isNullableMethod && method.Name == "get_HasValue";
            isNullableValue = isNullableMethod && method.Name == "get_Value";
			isNullableGetValueOrDefault = isNullableMethod && method.Name == "GetValueOrDefault";

            IsConstructor = method.IsConstructor;
            declaringTypeIsValueType = method.DeclaringType.IsValueType;
            declaringType = method.DeclaringType;
        }

        // #lizard forgives
        public unsafe void Invoke(VirtualMachine virtualMachine, ref Call call, bool isInstantiate)
        {
            var managedStack = call.managedStack;
            var pushResult = false;
            object ret = null;
            var args = GetArgs(paramCount);
            // invoke中会自己调用自己

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
                            rawTypes[i], virtualMachine, false);
                    }
                    pArg++;
                }

                if (isInstantiate || (IsConstructor && declaringTypeIsValueType))
                {
                    if (returnTypeIsValueType)
                    {
                        ret = BoxUtils.CreateBoxValue(cacheReturnType, true);
                    }
                    else
                    {
                        ret = null;
                    }
                    
                    ret = UnsafeUtility.CallMethod(ctor, args, ret);
                    //ret = ctor.Invoke(args);//TODO: Delegate创建用Delegate.CreateDelegate
                }
                else
                {
                    object instance = null;
                    if (hasThis)
                    {
                        instance = EvaluationStackOperation.ToObject(call.evaluationStackBase, call.argumentBase,
                            managedStack, declaringType, virtualMachine, false);
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
					else if (isNullableGetValueOrDefault)
					{
						if(instance == null)
                        {
                            if (paramCount == 0)
                                ret = BoxUtils.CreateDefaultBoxValue(returnType);
							else
								ret = args[0];
						}
						else
						{
							ret = instance;
						}
					}
                    else
                    {
                        if (hasThis && instance == null)
                        {
                            throw new TargetException(string.Format("can not invoke method [{0}.{1}], Non-static method require instance but got null.", method.DeclaringType, method.Name));
                        }
                        else
                        {
                             if (returnTypeIsValueType)
                             {
                                 ret = BoxUtils.CreateBoxValue(cacheReturnType, true);
                             }
                             else
                             {
                                 ret = null;
                             }
                             
                             ret = UnsafeUtility.CallMethod(method, instance, args, ret);
                            //ret = method.Invoke(instance, args);
                        }
                    }

                    if (valueInstance)
                    {
                        BoxUtils.RecycleObject(instance);
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
                    if (IsConstructor && BoxUtils.GetTypeIsValueType(declaringType) && !isInstantiate)
                    {
                        call.UpdateReference(0, ret, virtualMachine, declaringType);
                    }
                    else
                    {
                        call.PushObjectAsResult(ret, returnType);
                        pushResult = true;
                    }
                }
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException;
            }
            finally
            {
                RecycleArgs(args);
                
                // Value* pArg = call.argumentBase;
                // if (pushResult)
                // {
                //     pArg++;
                // }
                //
                // for (int i = (pushResult ? 1 : 0),imax=paramCount + ((hasThis && !isInstantiate) ? 1 : 0); i < imax; i++)
                // {
                //     BoxUtils.RecycleObject(managedStack[pArg - call.evaluationStackBase]);
                //     managedStack[pArg - call.evaluationStackBase] = null;
                //     pArg++;
                // }
            }
        }
    }
}
