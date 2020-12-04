/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

namespace IFix.Core
{
    using System;
    using Unity.IL2CPP.CompilerServices;
    using System.Collections.Generic;
    using System.Reflection;
    using System.IO;

    class RuntimeException : Exception
    {
        public Exception Real { get;set;}
    }

    public delegate void ExternInvoker(VirtualMachine vm, ref Call call, bool isInstantiate);

    internal class FieldAddr
    {
        public object Object;
        public int[] FieldIdList;
    }

    unsafe public class VirtualMachine
    {
        Instruction tellUnity4IncludeInstructionFisrt;

        public const int MAX_EVALUATION_STACK_SIZE = 1024 * 10;

        internal ObjectClone objectClone = new ObjectClone();

        Instruction** unmanagedCodes;

        ExceptionHandler[][] exceptionHandlers;

        Action onDispose;

        ExternInvoker[] externInvokers;

        MethodBase[] externMethods;

        Type[] externTypes;

        string[] internStrings;

        internal FieldInfo[] fieldInfos;

        AnonymousStoreyInfo[] anonymousStoreyInfos;

        Dictionary<Type, Dictionary<MethodInfo, MethodInfo>> overrideCache
            = new Dictionary<Type, Dictionary<MethodInfo, MethodInfo>>();

        internal Type[] staticFieldTypes;

        internal object[] staticFields;

        int[] cctors;

        WrappersManager wrappersManager;

        public ExceptionHandler[][] ExceptionHandlers
        {
            get
            {
                return exceptionHandlers;
            }
            set
            {
                exceptionHandlers = value;
            }
        }

        public Type[] ExternTypes
        {
            get
            {
                return externTypes;
            }
            set
            {
                externTypes = value;
            }
        }

        public MethodBase[] ExternMethods
        {
            get
            {
                return externMethods;
            }
            set
            {
                externMethods = value;
                externInvokers = new ExternInvoker[externMethods.Length]; 
            }
        }

        public string[] InternStrings
        {
            get
            {
                return internStrings;
            }
            set
            {
                internStrings = value;
            }
        }

        public FieldInfo[] FieldInfos
        {
            get
            {
                return fieldInfos;
            }
            set
            {
                fieldInfos = value;
            }
        }

        public AnonymousStoreyInfo[] AnonymousStoreyInfos
        {
            get
            {
                return anonymousStoreyInfos;
            }
            set
            {
                anonymousStoreyInfos = value;
            }
        }

        public Type[] StaticFieldTypes
        {
            get
            {
                return staticFieldTypes;
            }
            set
            {
                staticFields = value != null ? new object[value.Length] : null;
                staticFieldTypes = value;
            }
        }

        public int[] Cctors
        {
            get
            {
                return cctors;
            }
            set
            {
                cctors = value;
            }
        }

        public WrappersManager WrappersManager
        {
            get
            {
                return wrappersManager;
            }
            set
            {
                wrappersManager = value;
            }
        }

        internal VirtualMachine(Instruction** unmanaged_codes, Action on_dispose)
        {
            unmanagedCodes = unmanaged_codes;
            onDispose = on_dispose;
        }

        ~VirtualMachine()
        {
            onDispose();
            unmanagedCodes = null;
        }

        void checkCctorExecute(int fieldId, Value* argumentBase, object[] managedStack, Value* evaluationStackBase)
        {
            var cctorId = cctors[fieldId];
            //_Info("check " + fieldId + ", cctorId = " + cctorId);
            if (cctorId >= 0)
            {
                for(int i = 0; i < cctors.Length; i++)
                {
                    if (cctors[i] == cctorId)
                    {
                        //_Info("set " + i + " to -1");
                        cctors[i] = -1;
                    }
                }
                Execute(cctorId, argumentBase, managedStack, evaluationStackBase, 0);
            }
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        void store(Value* stackBase, Value* dst, Value* src, object[] managedStack)
        {
            *dst = *src;
            if (dst->Type >= ValueType.Object)
            {
                var obj = (dst->Type == ValueType.ValueType && managedStack[src->Value1] != null) //Nullable box后可能为空
                    ? objectClone.Clone(managedStack[src->Value1])
                    : managedStack[src->Value1];
                var dstPos = dst->Value1 = (int)(dst - stackBase);
                managedStack[dstPos] = obj;
            }
            else if (dst->Type == ValueType.ChainFieldReference)
            {
                managedStack[dst - stackBase] = managedStack[src - stackBase];
            }
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [Il2CppSetOption(Option.DivideByZeroChecks, false)]
        void copy(Value* stackBase, Value* dst, Value* src, object[] managedStack)
        {
            *dst = *src;
            if (dst->Type == ValueType.ValueType)
            {
                object obj = null;
                if (managedStack[src->Value1] != null) //Nullable box后可能为空
                    obj = objectClone.Clone(managedStack[src->Value1]);
                var dstPos = dst->Value1 = (int)(dst - stackBase);
                managedStack[dstPos] = obj;
            }
            else if (dst->Type == ValueType.ChainFieldReference)
            {
                managedStack[dst - stackBase] = managedStack[src - stackBase];
            }
        }

        //[Il2CppSetOption(Option.NullChecks, false)]
        //[Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        //[Il2CppSetOption(Option.DivideByZeroChecks, false)]
        public void Execute(int methodIndex, ref Call call, int argsCount, int refCount = 0)
        {
            Execute(unmanagedCodes[methodIndex], call.argumentBase + refCount, call.managedStack,
                call.evaluationStackBase, argsCount, methodIndex, refCount, call.topWriteBack);
        }


        //[Il2CppSetOption(Option.NullChecks, false)]
        //[Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        //[Il2CppSetOption(Option.DivideByZeroChecks, false)]
        public Value* Execute(int methodIndex, Value* argumentBase, object[] managedStack,
            Value* evaluationStackBase, int argsCount)
        {
            return Execute(unmanagedCodes[methodIndex], argumentBase, managedStack, evaluationStackBase,
                argsCount, methodIndex);
        }

        void printStack(string title, Value* val)
        {
#if UNITY_5
            UnityEngine.Debug.Log(title + ", t=" + val->Type + ", v=" + val->Value1);
#else
            Console.WriteLine(title + ", t=" + val->Type + ", v=" + val->Value1);
#endif
        }

        //运行时本身的错误，业务的错误应该直接throw，坏处是未处理的系统异常会被业务逻辑catch，
        //但这个可以通过逐步完善解决如果是业务异常封装，那么类似checked()，反射调用业务代码，
        //都要先try-catch，然后封装。而且也无法区分反射调用带来的异常以及系统异常（都是unwrap）
        //因为反射有可能从新进入到解析器
        void throwRuntimeException(Exception e, bool bWrap)
        {
            if (bWrap)
            {
                var t = new RuntimeException();
                t.Real = e;
                throw t;
            }
            else
            {
                throw e;
            }
        }

        ExceptionHandler getExceptionHandler(int methodIndex, Type exceptionType, int pc)
        {
            var exceptionHandlersOfMethod = exceptionHandlers[methodIndex];
            for (int i = 0; i < exceptionHandlersOfMethod.Length; i++)
            {
                var exceptionHandler = exceptionHandlersOfMethod[i];
                //1 catch只能从先catch子类，再catch父类
                //2 排列顺序按Handle block来，外层处理的handle block肯定在内层的后面
                if (
                        (
                            exceptionHandler.HandlerType == ExceptionHandlerType.Finally 
                            || (exceptionHandler.HandlerType == ExceptionHandlerType.Catch
                            && exceptionHandler.CatchType.IsAssignableFrom(exceptionType))
                        ) //type match
                        && pc >= exceptionHandler.TryStart && pc < exceptionHandler.TryEnd
                    )
                {
                    return exceptionHandler;
                }
            }
            return null;
        }

        //Value* traceValue = null;

        void arrayGet(object obj, int idx, Value* val, object[] managedStack, Value* evaluationStackBase)
        {
            int[] intArr = obj as int[];
            if (intArr != null)
            {
                val->Type = ValueType.Integer;
                val->Value1 = intArr[idx];
                return;
            }
            float[] floatArr = obj as float[];
            if (floatArr != null)
            {
                val->Type = ValueType.Float;
                *(float*)&val->Value1 = floatArr[idx];
                return;
            }
            double[] doubleArr = obj as double[];
            if (doubleArr != null)
            {
                val->Type = ValueType.Double;
                *(double*)&val->Value1 = doubleArr[idx];
                return;
            }
            byte[] byteArr = obj as byte[];
            if (byteArr != null)
            {
                val->Type = ValueType.Integer;
                val->Value1 = byteArr[idx];
                return;
            }
            bool[] boolArr = obj as bool[];
            if (boolArr != null)
            {
                val->Type = ValueType.Integer;
                val->Value1 = boolArr[idx] ? 1 : 0;
                return;
            }
            long[] longArr = obj as long[];
            if (longArr != null)
            {
                val->Type = ValueType.Long;
                *(long*)&val->Value1 = longArr[idx];
                return;
            }
            ulong[] ulongArr = obj as ulong[];
            if (ulongArr != null)
            {
                val->Type = ValueType.Long;
                *(ulong*)&val->Value1 = ulongArr[idx];
                return;
            }
            sbyte[] sbyteArr = obj as sbyte[];
            if (sbyteArr != null)
            {
                val->Type = ValueType.Integer;
                val->Value1 = sbyteArr[idx];
                return;
            }
            short[] shortArr = obj as short[];
            if (shortArr != null)
            {
                val->Type = ValueType.Integer;
                val->Value1 = shortArr[idx];
                return;
            }
            ushort[] ushortArr = obj as ushort[];
            if (ushortArr != null)
            {
                val->Type = ValueType.Integer;
                val->Value1 = ushortArr[idx];
                return;
            }
            char[] charArr = obj as char[];
            if (charArr != null)
            {
                val->Type = ValueType.Integer;
                val->Value1 = charArr[idx];
                return;
            }
            uint[] uintArr = obj as uint[];
            if (uintArr != null)
            {
                val->Type = ValueType.Integer;
                val->Value1 = (int)uintArr[idx];
                return;
            }

            EvaluationStackOperation.PushObject(evaluationStackBase, val, managedStack,
                (obj as Array).GetValue(idx), obj.GetType().GetElementType());
        }

        void arraySet(object obj, int idx, Value* val, object[] managedStack, Value* evaluationStackBase)
        {
            int[] intArr = obj as int[];
            if (intArr != null)
            {
                intArr[idx] = val->Value1;
                return;
            }
            float[] floatArr = obj as float[];
            if (floatArr != null)
            {
                floatArr[idx] = *(float*)&val->Value1;
                return;
            }
            double[] doubleArr = obj as double[];
            if (doubleArr != null)
            {
                doubleArr[idx] = *(double*)&val->Value1;
                return;
            }
            byte[] byteArr = obj as byte[];
            if (byteArr != null)
            {
                byteArr[idx] = (byte)val->Value1;
                return;
            }
            bool[] boolArr = obj as bool[];
            if (boolArr != null)
            {
                boolArr[idx] = val->Value1 != 0;
                return;
            }
            long[] longArr = obj as long[];
            if (longArr != null)
            {
                longArr[idx] = *(long*)&val->Value1;
                return;
            }
            ulong[] ulongArr = obj as ulong[];
            if (ulongArr != null)
            {
                ulongArr[idx] = *(ulong*)&val->Value1;
                return;
            }
            sbyte[] sbyteArr = obj as sbyte[];
            if (sbyteArr != null)
            {
                sbyteArr[idx] = (sbyte)val->Value1;
                return;
            }
            short[] shortArr = obj as short[];
            if (shortArr != null)
            {
                shortArr[idx] = (short)val->Value1;
                return;
            }
            ushort[] ushortArr = obj as ushort[];
            if (ushortArr != null)
            {
                ushortArr[idx] = (ushort)val->Value1;
                return;
            }
            char[] charArr = obj as char[];
            if (charArr != null)
            {
                charArr[idx] = (char)val->Value1;
                return;
            }
            uint[] uintArr = obj as uint[];
            if (uintArr != null)
            {
                uintArr[idx] = (uint)val->Value1;
                return;
            }

            (obj as Array).SetValue(EvaluationStackOperation.ToObject(evaluationStackBase, val, managedStack,
                obj.GetType().GetElementType(), this), idx);
        }

        public static Action<string> Info = Console.WriteLine;

        public static void _Info(string a)
        {
            if (Info != null) Info(a);
        }

        //[Il2CppSetOption(Option.NullChecks, false)]
        //[Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        //[Il2CppSetOption(Option.DivideByZeroChecks, false)]
        // #lizard forgives
        public Value* Execute(Instruction* pc, Value* argumentBase, object[] managedStack,
            Value* evaluationStackBase, int argsCount, int methodIndex,
            int refCount = 0, Value** topWriteBack = null)
        {
            if (pc->Code != Code.StackSpace) //TODO:删了pc会慢，但手机可能会快
            {
                throwRuntimeException(new InvalidProgramException("invalid code!"), topWriteBack == null);
            }

            int leavePoint = 0; //由于首指令是插入的StackSpace，所以leavePoint不可能等于0
            Exception throwExcepton = null; //use by rethrow
            Instruction* pcb = pc;

            int localsCount = (pc->Operand >> 16);
            int maxStack = (pc->Operand & 0xFFFF);
            //Console.WriteLine("localsCount:" + localsCount + ",maxStack:" + maxStack + ", argumentBase:"
            //    + (long)argumentBase);

            int argumentPos = (int)(argumentBase - evaluationStackBase);
            if (argumentPos + argsCount + localsCount + maxStack > MAX_EVALUATION_STACK_SIZE)
            {
                throwRuntimeException(new StackOverflowException(), topWriteBack == null);
            }

            Value* localBase = argumentBase + argsCount;
            Value* evaluationStackPointer = localBase + localsCount;
            //Debug.Log("loc:" + ((int)(code[0].TokenLong >> 32)));
            //Debug.Log("stackSize:" + stackSize + ", arg.length:" + (locs - args) + ", locs.length:"
            //    + (esp - locs) + ",es.length:" + (stackSize - (esp - stack)));

            pc++;

            //if (methodIndex == 7)
            //{
            //    traceValue = argumentBase + 1;
            //}

            while (true) //TODO: 常用指令应该放前面
            {
                try
                {
                    var code = pc->Code;
                    //if (methodIndex == 527 || methodIndex == 528)
                    //{
                    //    _Info("** Method Id = " + methodIndex + ", Start Code = " + code + ", Oprand = "
                    //        + pc->Operand + ", ESP = " + (evaluationStackPointer - localBase - localsCount)
                    //        + ", ABS = " + (evaluationStackPointer - evaluationStackBase));
                    //if (methodIndex == 84 && code == Code.Ldfld) throw new Exception("stop");
                    //}
                    //if (traceValue != null)
                    //{
                    //    _Info("before:" + traceValue->Type + "," + traceValue->Value1 + (traceValue->Type
                    //        == ValueType.Object ? ("," + managedStack[traceValue->Value1]) : ""));
                    //}
                    switch (code)
                    {
                        //Ldarg_0: 10.728% Ldarg_1: 4.4% Ldarg_2: 1.87% Ldarg_S:0.954%  Ldarg_3:0.93%
                        case Code.Ldarg:
                            {
                                //if (methodIndex == 197)
                                //{
                                //    var a = argumentBase + pc->Operand;
                                //    if (a->Type == ValueType.FieldReference)
                                //    {
                                //        var fieldInfo = fieldInfos[a->Value2];
                                //        _Info("field: " + fieldInfo.DeclaringType + "." + fieldInfo.Name);
                                //        _Info("a->Value1:" + a->Value1);
                                //        var obj = managedStack[a->Value1];
                                //        _Info("a.obj = " + (obj == null ? "null" : obj.ToString()));
                                //    }
                                //}
                                copy(evaluationStackBase, evaluationStackPointer, argumentBase + pc->Operand,
                                    managedStack);
                                //if (methodIndex == 197)
                                //{
                                //    var a = evaluationStackPointer;
                                //    if (a->Type == ValueType.FieldReference)
                                //    {
                                //        var fieldInfo = fieldInfos[a->Value2];
                                //        _Info("ep field: " + fieldInfo.DeclaringType + "." + fieldInfo.Name);
                                //        _Info("ep a->Value1:" + a->Value1);
                                //        var obj = managedStack[a->Value1];
                                //        _Info("ep a.obj = " + (obj == null ? "null" : obj.ToString()));
                                //    }
                                //}
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Call:// Call: 8.1233%
                            {
                                int narg = pc->Operand >> 16;
                                //Console.WriteLine("narg:" + narg);
                                //Console.WriteLine("before call ESP = " + (evaluationStackPointer - localBase
                                //    - localsCount) + ", ESPV=" + (long)evaluationStackPointer);
                                //printStack("a 1", evaluationStackPointer - 1 - 1);
                                //printStack("a 2", evaluationStackPointer - 1);
                                int methodIndexToCall = pc->Operand & 0xFFFF;
                                //if (methodIndex == 196 || methodIndex == 197 || methodIndex == 13)
                                //{
                                //    _Info("methodIndexToCall:" + methodIndexToCall);
                                //}
                                evaluationStackPointer = Execute(unmanagedCodes[methodIndexToCall],
                                    evaluationStackPointer - narg, managedStack, evaluationStackBase, narg,
                                    methodIndexToCall);
                                //Console.WriteLine("after call ESP = " + (evaluationStackPointer - localBase
                                //    - localsCount));
                                //printStack("ret", evaluationStackPointer - 1);
                            }
                            break;
                        case Code.Callvirt: //Callvirt: 5.156
                            {
                                int narg = pc->Operand >> 16;
                                var arg0 = evaluationStackPointer - narg;
                                if (arg0->Type != ValueType.Object)
                                {
                                    throwRuntimeException(new InvalidProgramException(arg0->Type.ToString()
                                        + " for Callvirt"), true);
                                }
                                if (managedStack[arg0->Value1] == null)
                                {
                                    throw new NullReferenceException("this is null");
                                }
                                //Console.WriteLine("narg:" + narg);
                                //Console.WriteLine("before call ESP = " + (evaluationStackPointer - localBase
                                //    - localsCount) + ", ESPV=" + (long)evaluationStackPointer);
                                //printStack("a 1", evaluationStackPointer - 1 - 1);
                                //printStack("a 2", evaluationStackPointer - 1);
                                int methodIndexToCall = pc->Operand & 0xFFFF;
                                //if (methodIndex == 203)
                                //{
                                //    _Info("methodIndexToCall:" + methodIndexToCall);
                                //}
                                evaluationStackPointer = Execute(unmanagedCodes[methodIndexToCall],
                                    evaluationStackPointer - narg, managedStack, evaluationStackBase,
                                    narg, methodIndexToCall);
                                //Console.WriteLine("after call ESP = " + (evaluationStackPointer - localBase
                                //    - localsCount));
                                //printStack("ret", evaluationStackPointer - 1);
                            }
                            break;

                        case Code.Callvirtvirt:
                            {
                                int narg = pc->Operand >> 16;
                                var arg0 = evaluationStackPointer - narg;
                                if (arg0->Type != ValueType.Object)
                                {
                                    throwRuntimeException(new InvalidProgramException(arg0->Type.ToString()
                                        + " for Callvirtvirt"), true);
                                }
                                if (managedStack[arg0->Value1] == null)
                                {
                                    throw new NullReferenceException("this is null");
                                }
                                var anonObj =  managedStack[arg0->Value1] as AnonymousStorey;
                                int[] vTable = anonymousStoreyInfos[anonObj.typeId].VTable;
                                int methodIndexToCall = vTable[pc->Operand & 0xFFFF];
                                evaluationStackPointer = Execute(unmanagedCodes[methodIndexToCall],
                                    evaluationStackPointer - narg, managedStack, evaluationStackBase,
                                    narg, methodIndexToCall);
                            }
                            break;

                        case Code.Ldvirtftn2:
                            {
                                int slot = pc->Operand & 0xFFFF;
                                var pm = evaluationStackPointer - 1;
                                var po = pm - 1;
                                var anonObj = managedStack[po->Value1] as AnonymousStorey;
                                pm->Value1 = anonymousStoreyInfos[anonObj.typeId].VTable[slot];
                                pm->Type = ValueType.Integer;
                            }
                            break;

                        case Code.CallExtern://部分来自Call部分来自Callvirt
                        case Code.Newobj: // 2.334642%
                            int methodId = pc->Operand & 0xFFFF;
                            if (code == Code.Newobj)
                            {
                                var method = externMethods[methodId];
                                if (method.DeclaringType.BaseType == typeof(MulticastDelegate)) // create delegate
                                {
                                    var pm = evaluationStackPointer - 1;
                                    var po = pm - 1;
                                    var o = managedStack[po->Value1];
                                    managedStack[po - evaluationStackBase] = null;
                                    Delegate del = null;
                                    if (pm->Type == ValueType.Integer)
                                    {
                                        //_Info("new closure!");
                                        del = wrappersManager.CreateDelegate(method.DeclaringType, pm->Value1, o);
                                        if (del == null)
                                        {
                                            del = GenericDelegateFactory.Create(method.DeclaringType, this,
                                                pm->Value1, o);
                                        }
                                        if (del == null)
                                        {
                                            throwRuntimeException(
                                                new InvalidProgramException("no closure wrapper for "
                                                + method.DeclaringType), true);
                                        }
                                    }
                                    //else if (pm->Type == ValueType.Float) // 
                                    //{
                                    //    del = GetGlobalWrappersManager().CreateDelegate(method.DeclaringType,
                                    //        pm->Value1, null);
                                    //    if (del == null)
                                    //    {
                                    //        throwRuntimeException(new InvalidProgramException(
                                    //            "no closure wrapper for " + method.DeclaringType), true);
                                    //    }
                                    //}
                                    else
                                    {
                                        var mi = managedStack[pm->Value1] as MethodInfo;
                                        managedStack[pm - evaluationStackBase] = null;
                                        del = Delegate.CreateDelegate(method.DeclaringType, o, mi);
                                    }
                                    po->Value1 = (int)(po - evaluationStackBase);
                                    managedStack[po->Value1] = del;
                                    evaluationStackPointer = pm;
                                    break;
                                }
                            }
                            int paramCount = pc->Operand >> 16;
                            var externInvokeFunc = externInvokers[methodId];
                            if (externInvokeFunc == null)
                            {
                                externInvokers[methodId] = externInvokeFunc
                                    = (new ReflectionMethodInvoker(externMethods[methodId])).Invoke;
                            }
                            //Info("call extern: " + externMethods[methodId]);
                            var top = evaluationStackPointer - paramCount;
                            //for(int kk = 0; kk < paramCount; kk++)
                            //{
                            //    string info = "arg " + kk + " " + (top + kk)->Type.ToString() + ": ";
                            //    if ((top + kk)->Type >= ValueType.Object)
                            //    {
                            //        var o = managedStack[(top + kk)->Value1];
                            //        info += "obj(" + (o == null ? "null" : o.GetHashCode().ToString()) + ")";
                            //    }
                            //    else
                            //    {
                            //        info += (top + kk)->Value1;
                            //    }
                            //    Info(info);
                            //}
                            Call call = new Call()
                            {
                                argumentBase = top,
                                currentTop = top,
                                managedStack = managedStack,
                                evaluationStackBase = evaluationStackBase
                            };
                            //调用外部前，需要保存当前top，以免外部从新进入内部时覆盖栈
                            ThreadStackInfo.Stack.UnmanagedStack->Top = evaluationStackPointer;
                            externInvokeFunc(this, ref call, code == Code.Newobj);
                            evaluationStackPointer = call.currentTop;
                            break;
                        //Ldloc_0:3.35279% Ldloc_S:2.624982% Ldloc_1:1.958552% Ldloc_2:1.278956% Ldloc_3:0.829925%
                        case Code.Ldloc:
                            {
                                //print("+++ldloc", locs + ins.Operand);
                                //if (methodIndex == 326)
                                //{
                                //    var a = localBase + pc->Operand;
                                //    _Info("l->Type:" + a->Type + ", l->Value1:" + a->Value1);
                                //    if (a->Type == ValueType.Object)
                                //    {
                                //        var obj = managedStack[a->Value1];
                                //        _Info("l.obj = " + (obj == null ? "null" : obj.GetType() + "(" 
                                //        + obj.GetHashCode() + ")"));
                                //    }
                                //}
                                copy(evaluationStackBase, evaluationStackPointer, localBase + pc->Operand, 
                                    managedStack);
                                //if (methodIndex == 326)
                                //{
                                //    var a = evaluationStackPointer;
                                //    _Info("e->Type:" + a->Type + ", e->Value1:" + a->Value1);
                                //    if (a->Type == ValueType.Object)
                                //    {
                                //        var obj = managedStack[a->Value1];
                                //        _Info("e.obj = " + (obj == null ? "null" : obj.GetType() + "(" 
                                //        + obj.GetHashCode() + ")"));
                                //    }
                                //}
                                evaluationStackPointer++;
                            }
                            break;
                        // Ldc_I4_0:3% Ldc_I4_1:2.254763% Ldc_I4_S:1.16403% Ldc_I4:0.8208519% Ldc_I4_2:0.58922%
                        // Ldc_I4_4:0.3086645% Ldc_I4_M1:0.2323434% Ldc_I4_8:0.1894684% Ldc_I4_3:0.1850208%
                        // Ldc_I4_5:0.1158159% Ldc_I4_7:0.08076869% Ldc_I4_6:0.07045022%
                        case Code.Ldc_I4:
                            {
                                //*((long*)(&evaluationStackPointer->Value1)) = pc->Operand;
                                evaluationStackPointer->Value1 = pc->Operand; //高位不清除
                                evaluationStackPointer->Type = ValueType.Integer;
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Ret:// 5.5% TODO: 分为带返回值和不带返回值
                            {
                                //TODO: 可优化? 检查到没都是基本类型后改指令
                                
                                if (topWriteBack != null)
                                {
                                    *topWriteBack = argumentBase - refCount;
                                }
                                throwExcepton = null;
                                if (pc->Operand != 0)
                                {
                                    *argumentBase = *(evaluationStackPointer - 1);
                                    if (argumentBase->Type == ValueType.Object
                                        || argumentBase->Type == ValueType.ValueType)
                                    {
                                        int resultPos = argumentBase->Value1;
                                        if (resultPos != argumentPos)
                                        {
                                            managedStack[argumentPos] = managedStack[resultPos];
                                            //managedStack[resultPos] = null;
                                        }
                                        argumentBase->Value1 = argumentPos;
                                    }
                                    for (int i = 0; i < evaluationStackPointer - evaluationStackBase - 1; i++)
                                    {
                                        managedStack[i + argumentPos + 1] = null;
                                    }

                                    return argumentBase + 1;
                                }
                                else
                                {
                                    for (int i = 0; i < evaluationStackPointer - evaluationStackBase; i++)
                                    {
                                        managedStack[i + argumentPos] = null;
                                    }
                                    return argumentBase;
                                }
                            }
                        //Stloc_0:1.717491% Stloc_S:1.316672% Stloc_1:1.020105% Stloc_2:0.6683876% Stloc_3:0.4547242%
                        case Code.Stloc:
                            {
                                evaluationStackPointer--;
                                //print("+++before stloc", locs + ins.Operand);
                                store(evaluationStackBase, localBase + pc->Operand, evaluationStackPointer,
                                    managedStack);
                                //print("+++after stloc", locs + ins.Operand);
                                managedStack[evaluationStackPointer - evaluationStackBase] = null;
                            }
                            break;
                        case Code.Ldfld: //5.017799%
                            {
                                var ptr = evaluationStackPointer - 1;
                                var fieldIndex = pc->Operand;
                                //_Info("Ldfld fieldIndex:" + fieldIndex);
                                if (fieldIndex >= 0)
                                {
                                    var fieldInfo = fieldInfos[fieldIndex];
                                    //_Info("Ldfld fieldInfo:" + fieldInfo);
                                    
                                    object obj = EvaluationStackOperation.ToObject(evaluationStackBase, ptr,
                                        managedStack, fieldInfo.DeclaringType, this, false);
                                    
                                    if (obj == null)
                                    {
                                        throw new NullReferenceException();
                                    }
                                    //_Info("Ldfld:" + fieldInfo + ",obj=" + obj.GetType());
                                    
                                    var fieldValue = fieldInfo.GetValue(obj);
                                    //_Info("fieldValue:" + fieldValue);
                                    //throw new Exception("fieldValue=" + fieldValue);
                                    EvaluationStackOperation.PushObject(evaluationStackBase, ptr, managedStack,
                                        fieldValue, fieldInfo.FieldType);
                                }
                                else
                                {
                                    fieldIndex = -(fieldIndex + 1);
                                    AnonymousStorey anonyObj = managedStack[ptr->Value1] as AnonymousStorey;
                                    anonyObj.Ldfld(fieldIndex, evaluationStackBase, ptr, managedStack);
                                }
                            }
                            break;
                        case Code.Ldstr://2.656827%
                            {
                                EvaluationStackOperation.PushObject(evaluationStackBase, evaluationStackPointer,
                                    managedStack, internStrings[pc->Operand], typeof(string));
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Brfalse://Brfalse_S:2.418613% Brfalse:0.106387%
                            {
                                bool transfer = false;
                                evaluationStackPointer--;
                                switch (evaluationStackPointer->Type)
                                {
                                    case ValueType.Integer:
                                        transfer = evaluationStackPointer->Value1 == 0;
                                        break;
                                    case ValueType.Long:
                                        transfer = *(long*)&evaluationStackPointer->Value1 == 0;
                                        break;
                                    case ValueType.Object:
                                    case ValueType.ValueType:
                                        transfer = managedStack[evaluationStackPointer->Value1] == null;
                                        break;
                                }
                                managedStack[evaluationStackPointer - evaluationStackBase] = null;
                                if (transfer)
                                {
                                    pc += pc->Operand;
                                    continue;
                                }
                            }
                            break;
                        case Code.Stfld://2.13361%
                            {
                                var ptr = evaluationStackPointer - 1 - 1;
                                var fieldIndex = pc->Operand;
                                if (fieldIndex >= 0)
                                {
                                    var fieldInfo = fieldInfos[pc->Operand];

                                    object obj = EvaluationStackOperation.ToObject(evaluationStackBase, ptr,
                                        managedStack, fieldInfo.DeclaringType, this, false);

                                    if (obj == null)
                                    {
                                        throw new NullReferenceException();
                                    }

                                    fieldInfo.SetValue(obj, EvaluationStackOperation.ToObject(evaluationStackBase,
                                        evaluationStackPointer - 1, managedStack, fieldInfo.FieldType, this));
                                    //如果field，array元素是值类型，需要重新update回去
                                    if ((ptr->Type == ValueType.FieldReference
                                        || ptr->Type == ValueType.ChainFieldReference
                                        || ptr->Type == ValueType.StaticFieldReference
                                        || ptr->Type == ValueType.ArrayReference) 
                                        && fieldInfo.DeclaringType.IsValueType)
                                    {
                                        EvaluationStackOperation.UpdateReference(evaluationStackBase, ptr,
                                            managedStack, obj, this, fieldInfo.DeclaringType);
                                    }
                                    managedStack[ptr - evaluationStackBase] = null;
                                    managedStack[evaluationStackPointer - 1 - evaluationStackBase] = null;
                                    evaluationStackPointer = ptr;
                                }
                                else
                                {
                                    fieldIndex = -(fieldIndex + 1);
                                    object obj = EvaluationStackOperation.ToObject(evaluationStackBase, ptr,
                                       managedStack, ptr->Type.GetType(), this, false);
                                    AnonymousStorey anonyObj = obj as AnonymousStorey;
                                    anonyObj.Stfld(fieldIndex, evaluationStackBase, evaluationStackPointer - 1, 
                                        managedStack);
                                    evaluationStackPointer = ptr;
                                }
                            }
                            break;
                        case Code.Brtrue://Brtrue_S:1.944675% Brtrue:0.07400832%
                            {
                                bool transfer = false;
                                evaluationStackPointer--;
                                switch (evaluationStackPointer->Type)
                                {
                                    case ValueType.Integer:
                                        transfer = evaluationStackPointer->Value1 != 0;
                                        break;
                                    case ValueType.Long:
                                        transfer = *(long*)&evaluationStackPointer->Value1 != 0;
                                        break;
                                    case ValueType.Object:
                                    case ValueType.ValueType:
                                        transfer = managedStack[evaluationStackPointer->Value1] != null;
                                        break;
                                }
                                managedStack[evaluationStackPointer - evaluationStackBase] = null;
                                if (transfer)
                                {
                                    pc += pc->Operand;
                                    continue;
                                }
                            }
                            break;
                        case Code.Add://1.356345%
                            {
                                Value* b = evaluationStackPointer - 1;
                                //大于1的立即数和指针运算在il2cpp（unity 5.4）有bug，都会按1算
                                Value* a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                switch (a->Type)//TODO: 通过修改指令优化掉
                                {
                                    case ValueType.Long:
                                        *((long*)&evaluationStackPointer->Value1)
                                            = *((long*)&a->Value1) + *((long*)&b->Value1);
                                        break;
                                    case ValueType.Integer:
                                        evaluationStackPointer->Value1 = a->Value1 + b->Value1;
                                        break;
                                    case ValueType.Float:
                                        *((float*)&evaluationStackPointer->Value1)
                                            = *((float*)&a->Value1) + *((float*)&b->Value1);
                                        break;
                                    case ValueType.Double:
                                        *((double*)&evaluationStackPointer->Value1)
                                            = *((double*)&a->Value1) + *((double*)&b->Value1);
                                        break;
                                    default:
                                        throwRuntimeException(new NotImplementedException(), true);
                                        break;
                                }
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Br://Br_S:1.162784% Br:0.2334108%
                            {
                                pc += pc->Operand;
                            }
                            continue;
                        case Code.Ldnull://1.203347%
                            {
                                var pos = (int)(evaluationStackPointer - evaluationStackBase);
                                managedStack[pos] = null;
                                evaluationStackPointer->Value1 = pos;
                                evaluationStackPointer->Type = ValueType.Object;
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Ldloca: //Ldloca_S:1.023663%
                            {
                                *(Value**)&evaluationStackPointer->Value1 = localBase + pc->Operand;
                                evaluationStackPointer->Type = ValueType.StackReference;
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Dup://0.9831008%
                            copy(evaluationStackBase, evaluationStackPointer, evaluationStackPointer - 1,
                                managedStack);
                            evaluationStackPointer++;
                            break;
                        case Code.Ldsfld: //0.7173114%
                            {
                                var fieldIndex = pc->Operand;
                                if (fieldIndex >= 0)
                                {
                                    var fieldInfo = fieldInfos[fieldIndex];
                                    if (fieldInfo == null)
                                    {
                                        throwRuntimeException(new InvalidProgramException(), true);
                                    }
                                    var fieldValue = fieldInfo.GetValue(null);
                                    EvaluationStackOperation.PushObject(evaluationStackBase, evaluationStackPointer,
                                        managedStack, fieldValue, fieldInfo.FieldType);
                                }
                                else
                                {
                                    fieldIndex = -(fieldIndex + 1);
                                    checkCctorExecute(fieldIndex, evaluationStackPointer, managedStack,
                                        evaluationStackBase);
                                    //_Info("load static field " + fieldIndex + " : " + staticFields[fieldIndex]);
                                    EvaluationStackOperation.PushObject(evaluationStackBase,
                                        evaluationStackPointer, managedStack,
                                        staticFields[fieldIndex], staticFieldTypes[fieldIndex]);
                                }
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Conv_I4: //0.5349591%
                            {
                                var ptr = evaluationStackPointer - 1;
                                int val = 0;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        val = (int)*(long*)&ptr->Value1;
                                        break;
                                    case ValueType.Float:
                                        val = (int)*(float*)&ptr->Value1;
                                        break;
                                    case ValueType.Double:
                                        val = (int)*(double*)&ptr->Value1;
                                        break;
                                    case ValueType.Integer:
                                        val = ptr->Value1;
                                        break;
                                }
                                ptr->Type = ValueType.Integer;
                                ptr->Value1 = val;
                            }
                            break;
                        case Code.Sub: //0.5299778%
                            {
                                Value* b = evaluationStackPointer - 1;
                                Value* a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                switch (a->Type)
                                {
                                    case ValueType.Long:
                                        *((long*)&evaluationStackPointer->Value1)
                                            = *((long*)&a->Value1) - *((long*)&b->Value1);
                                        break;
                                    case ValueType.Integer:
                                        evaluationStackPointer->Value1 = a->Value1 - b->Value1;
                                        break;
                                    case ValueType.Float:
                                        *((float*)&evaluationStackPointer->Value1)
                                            = *((float*)&a->Value1) - *((float*)&b->Value1);
                                        break;
                                    case ValueType.Double:
                                        *((double*)&evaluationStackPointer->Value1)
                                            = *((double*)&a->Value1) - *((double*)&b->Value1);
                                        break;
                                    default:
                                        throwRuntimeException(new NotImplementedException(), true);
                                        break;
                                }
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Ldlen: //0.5175245%
                            {
                                var ptr = evaluationStackPointer - 1;
                                Array arr = managedStack[ptr->Value1] as Array;
                                managedStack[ptr - evaluationStackBase] = null;
                                ptr->Type = ValueType.Integer;
                                ptr->Value1 = arr.Length;
                            }
                            break;
                        //TODO: 指令是否可以精简？比如if (a > b) 可以等同于if (b < a)
                        case Code.Blt: //Blt_S:0.4835447% Blt:0.04465406% 
                            {
                                var b = evaluationStackPointer - 1;
                                var a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                bool transfer = false;
                                switch (evaluationStackPointer->Type)
                                {
                                    case ValueType.Integer:
                                        transfer = a->Value1 < b->Value1;
                                        break;
                                    case ValueType.Long:
                                        transfer = *(long*)&a->Value1 < *(long*)&b->Value1;
                                        break;
                                    case ValueType.Float:
                                        transfer = *(float*)&a->Value1 < *(float*)&b->Value1;
                                        break;
                                    case ValueType.Double:
                                        transfer = *(double*)&a->Value1 < *(double*)&b->Value1;
                                        break;
                                }

                                if (transfer)
                                {
                                    pc += pc->Operand;
                                    continue;
                                }
                            }
                            break;
                        case Code.Stelem_Ref: //0.4734042%
                            {
                                var arrPtr = evaluationStackPointer - 1 - 1 - 1;
                                int idx = (evaluationStackPointer - 1 - 1)->Value1;
                                var valPtr = evaluationStackPointer - 1;
                                var arr = managedStack[arrPtr->Value1] as object[];
                                if (valPtr->Type != ValueType.Object)
                                {
                                    throw new ArrayTypeMismatchException();
                                }
                                arr[idx] = managedStack[valPtr->Value1];
                                managedStack[arrPtr - evaluationStackBase] = null; //清理，如果有的话
                                managedStack[valPtr - evaluationStackBase] = null;
                                evaluationStackPointer = arrPtr;
                            }
                            break;
                        case Code.Pop://0.4614846%
                            {
                                evaluationStackPointer--;
                                managedStack[evaluationStackPointer - evaluationStackBase] = null; ;
                            }
                            break;
                        case Code.Bne_Un://Bne_Un_S:0.4565032% Bne_Un:0.02793102%
                            {
                                var b = evaluationStackPointer - 1;
                                var a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                if (a->Type != b->Type
                                    || ((a->Type == ValueType.Object || a->Type == ValueType.ValueType) ?
                                    (managedStack[a->Value1] != managedStack[b->Value1]) :
                                    ((a->Value1 != b->Value1) || (a->Type != ValueType.Integer
                                    && a->Type != ValueType.Float && a->Value2 != b->Value2))))
                                {
                                    pc += pc->Operand;
                                    continue;
                                }
                            }
                            break;
                        case Code.Leave://Leave_S:0.4552579% Leave:0.03220074%
                            {
                                leavePoint = pc->Operand;
                            }
                            break;
                        case Code.Newarr: //0.4408476%
                            {
                                var type = externTypes[pc->Operand];
                                var ptr = evaluationStackPointer - 1;
                                int pos = (int)(ptr - evaluationStackBase);
                                managedStack[pos] = Array.CreateInstance(type, ptr->Value1);
                                ptr->Type = ValueType.Object;
                                ptr->Value1 = pos;
                            }
                            break;
                        case Code.And: //0.3967273%
                            {
                                var rhs = evaluationStackPointer - 1;
                                var lhs = evaluationStackPointer - 1 - 1;
                                switch (lhs->Type)
                                {
                                    case ValueType.Long:
                                        *((long*)&lhs->Value1) = *((long*)&lhs->Value1) & *((long*)&rhs->Value1);
                                        break;
                                    case ValueType.Integer:
                                        lhs->Value1 = lhs->Value1 & rhs->Value1;
                                        break;
                                    default:
                                        throw new InvalidProgramException("& for " + lhs->Type);
                                }
                                evaluationStackPointer = rhs;
                            }
                            break;
                        case Code.Volatile: //0.3912123%
                            break;
                        case Code.Castclass: //0.358122%
                            {
                                var ptr = evaluationStackPointer - 1;
                                var type = externTypes[pc->Operand];
                                var obj = managedStack[ptr->Value1];
                                if (obj != null && !type.IsAssignableFrom(obj.GetType()))
                                {
                                    throw new InvalidCastException(type + " is not assignable from "
                                        + obj.GetType());
                                }
                            }
                            break;
                        case Code.Beq://Beq_S:0.3517174% Beq: 0.03700416%
                            {
                                var b = evaluationStackPointer - 1;
                                var a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                if (a->Type == b->Type && ((a->Type == ValueType.Object
                                    || a->Type == ValueType.ValueType) ?
                                    (managedStack[a->Value1] == managedStack[b->Value1]) :
                                    ((a->Value1 == b->Value1) && (a->Type == ValueType.Integer
                                    || a->Type == ValueType.Float || a->Value2 == b->Value2))))
                                {
                                    pc += pc->Operand;
                                    continue;
                                }
                            }
                            break;
                        case Code.Ldelem_Ref: //0.3449571%
                            {
                                var arrPtr = evaluationStackPointer - 1 - 1;
                                int idx = (evaluationStackPointer - 1)->Value1;
                                var arrPos = arrPtr - evaluationStackBase;
                                var arr = managedStack[arrPtr->Value1] as object[];
                                managedStack[arrPos] = arr[idx];
                                arrPtr->Value1 = (int)arrPos;
                                evaluationStackPointer = evaluationStackPointer - 1;
                            }
                            break;
                        case Code.Ldtype:// Ldtoken:0.3325037%
                            EvaluationStackOperation.PushObject(evaluationStackBase, evaluationStackPointer,
                                managedStack, externTypes[pc->Operand], typeof(Type));
                            evaluationStackPointer++;
                            break;
                        case Code.Box://0.3100877%
                            {
                                var ptr = evaluationStackPointer - 1;
                                var type = externTypes[pc->Operand];
                                var pos = (int)(ptr - evaluationStackBase);
                                switch (ptr->Type)
                                {
                                    case ValueType.ValueType:
                                    case ValueType.Object:
                                        break;
                                    case ValueType.Integer:
                                        if (type.IsEnum)
                                        {
                                            managedStack[pos] = Enum.ToObject(type, ptr->Value1);
                                        }
                                        else if (type == typeof(int))
                                        {
                                            managedStack[pos] = ptr->Value1;
                                        }
                                        else if (type == typeof(uint))
                                        {
                                            managedStack[pos] = (uint)ptr->Value1;
                                        }
                                        else
                                        {
                                            managedStack[pos] = Convert.ChangeType(ptr->Value1, type);
                                        }
                                        ptr->Value1 = pos;
                                        break;
                                    case ValueType.Long:
                                        if (type == typeof(long))
                                        {
                                            managedStack[pos] = *(long*)&ptr->Value1;
                                        }
                                        else if (type == typeof(ulong))
                                        {
                                            managedStack[pos] = *(ulong*)&ptr->Value1;
                                        }
                                        else if (type.IsEnum)
                                        {
                                            managedStack[pos] = Enum.ToObject(type, *(long*)&ptr->Value1);
                                        }
                                        else if (type == typeof(IntPtr))
                                        {
                                            managedStack[pos] = new IntPtr(*(long*)&ptr->Value1);
                                        }
                                        else if (type == typeof(UIntPtr))
                                        {
                                            managedStack[pos] = new UIntPtr(*(ulong*)&ptr->Value1);
                                        }
                                        else
                                        {
                                            managedStack[pos] = Convert.ChangeType(*(long*)&ptr->Value1, type);
                                        }
                                        ptr->Value1 = pos;
                                        break;
                                    case ValueType.Float:
                                        managedStack[pos] = *(float*)&ptr->Value1;
                                        ptr->Value1 = pos;
                                        break;
                                    case ValueType.Double:
                                        managedStack[pos] = *(double*)&ptr->Value1;
                                        ptr->Value1 = pos;
                                        break;
                                    default:
                                        throwRuntimeException(new InvalidProgramException("to box a " + ptr->Type),
                                            true);
                                        break;
                                }
                                ptr->Type = ValueType.Object;
                            }
                            break;
                        case Code.Isinst://0.3074192%
                            {
                                var ptr = evaluationStackPointer - 1;
                                var type = externTypes[pc->Operand];
                                var pos = (int)(ptr - evaluationStackBase);
                                var obj = managedStack[ptr->Value1];
                                ptr->Type = ValueType.Object;
                                ptr->Value1 = pos;
                                managedStack[pos] = (obj != null && type.IsAssignableFrom(obj.GetType()))
                                    ? obj : null;
                            }
                            break;
                        case Code.Bge: //Bge_S:0.2954996% Bge:0.005870852%
                            {
                                var b = evaluationStackPointer - 1;
                                var a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                bool transfer = false;
                                switch (evaluationStackPointer->Type)
                                {
                                    case ValueType.Integer:
                                        transfer = a->Value1 >= b->Value1;
                                        break;
                                    case ValueType.Long:
                                        transfer = *(long*)&a->Value1 >= *(long*)&b->Value1;
                                        break;
                                    case ValueType.Float:
                                        transfer = *(float*)&a->Value1 >= *(float*)&b->Value1;
                                        break;
                                    case ValueType.Double:
                                        transfer = *(double*)&a->Value1 >= *(double*)&b->Value1;
                                        break;
                                }

                                if (transfer)
                                {
                                    pc += pc->Operand;
                                    continue;
                                }

                            }
                            break;
                        case Code.Conv_I8: //0.2652557%
                            {
                                var obj = evaluationStackPointer - 1;
                                long val;
                                switch (obj->Type)
                                {
                                    case ValueType.Integer:
                                        val = obj->Value1;
                                        break;
                                    case ValueType.Long:
                                        pc++;
                                        continue;
                                    case ValueType.Float:
                                        val = (long)*(float*)&obj->Value1;
                                        break;
                                    case ValueType.Double:
                                        val = (long)*(double*)&obj->Value1;
                                        break;
                                    default:
                                        val = 0;
                                        throwRuntimeException(new NotImplementedException(), true);
                                        break;
                                }
                                obj->Type = ValueType.Long;
                                *(long*)(&obj->Value1) = val;
                            }
                            break;
                        case Code.Ble://Ble_S:0.2581396%  Ble:0.0152998%
                            {
                                var b = evaluationStackPointer - 1;
                                var a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                bool transfer = false;
                                switch (evaluationStackPointer->Type)
                                {
                                    case ValueType.Integer:
                                        transfer = a->Value1 <= b->Value1;
                                        break;
                                    case ValueType.Long:
                                        transfer = *(long*)&a->Value1 <= *(long*)&b->Value1;
                                        break;
                                    case ValueType.Float:
                                        transfer = *(float*)&a->Value1 <= *(float*)&b->Value1;
                                        break;
                                    case ValueType.Double:
                                        transfer = *(double*)&a->Value1 <= *(double*)&b->Value1;
                                        break;
                                    default:
                                        throwRuntimeException(new NotImplementedException("Blt for "
                                            + evaluationStackPointer->Type), true);
                                        break;
                                }

                                if (transfer)
                                {
                                    pc += pc->Operand;
                                    continue;
                                }
                            }
                            break;
                        case Code.Endfinally: //0.2513792%
                            {
                                if (leavePoint == 0)//有异常
                                {
                                    int exceptionPos = (int)(evaluationStackPointer - evaluationStackBase - 1);
                                    var exception = managedStack[(evaluationStackPointer - 1)->Value1]
                                        as Exception;
                                    managedStack[exceptionPos] = null;
                                    evaluationStackPointer--;
                                    throw exception;
                                }
                                else
                                {
                                    if (pc->Operand == -1) //最外层
                                    {
                                        pc = pcb + leavePoint;
                                        leavePoint = 0;
                                        continue;
                                    }
                                    else //不是最外层
                                    {
                                        var nextFinally = exceptionHandlers[methodIndex][pc->Operand];
                                        if (leavePoint >= nextFinally.TryStart && leavePoint < nextFinally.TryEnd)
                                        {
                                            pc = pcb + leavePoint;
                                            leavePoint = 0;
                                            continue;
                                        }
                                        else
                                        {
                                            pc = pcb + nextFinally.HandlerStart;
                                            continue;
                                        }
                                    }
                                }
                            }
                        case Code.Or: //0.2490664%
                            {
                                var rhs = evaluationStackPointer - 1;
                                var lhs = evaluationStackPointer - 1 - 1;
                                switch (lhs->Type)
                                {
                                    case ValueType.Long:
                                        *((long*)&lhs->Value1) = *((long*)&lhs->Value1) | *((long*)&rhs->Value1);
                                        break;
                                    case ValueType.Integer:
                                        lhs->Value1 = lhs->Value1 | rhs->Value1;
                                        break;
                                    default:
                                        throw new InvalidProgramException("| for " + lhs->Type);
                                }
                                evaluationStackPointer = rhs;
                            }
                            break;
                        case Code.Stsfld: //0.2488885%
                            {
                                var fieldIndex = pc->Operand;
                                if (fieldIndex >= 0)
                                {
                                    var fieldInfo = fieldInfos[fieldIndex];
                                    if (fieldInfo == null)
                                    {
                                        throwRuntimeException(new InvalidProgramException(), true);
                                    }

                                    fieldInfo.SetValue(null, EvaluationStackOperation.ToObject(evaluationStackBase,
                                        evaluationStackPointer - 1, managedStack, fieldInfo.FieldType, this));
                                }
                                else
                                {
                                    fieldIndex = -(fieldIndex + 1);
                                    checkCctorExecute(fieldIndex, evaluationStackPointer, managedStack,
                                        evaluationStackBase);
                                    staticFields[fieldIndex]
                                        = EvaluationStackOperation.ToObject(evaluationStackBase,
                                        evaluationStackPointer - 1, managedStack, staticFieldTypes[fieldIndex], this);
                                    //_Info("store static field " + fieldIndex + " : " + staticFields[fieldIndex]);
                                }
                                managedStack[evaluationStackPointer - 1 - evaluationStackBase] = null;
                                evaluationStackPointer--;
                            }
                            break;
                        case Code.Ldflda: //0.240527%
                            {
                                var fieldInfo = pc->Operand >= 0 ? fieldInfos[pc->Operand] : null;
                                var ptr = evaluationStackPointer - 1;
                                //栈顶也是字段引用，而且该字段是值类型，需要update上层对象
                                if ((ptr->Type == ValueType.FieldReference
                                    || ptr->Type == ValueType.ChainFieldReference
                                    || ptr->Type == ValueType.ArrayReference) && fieldInfo != null
                                    && fieldInfo.FieldType.IsValueType)
                                {
                                    if (pc->Operand < 0)
                                    {
                                        throwRuntimeException(new NotSupportedException(
                                            "chain ref for compiler generated object!"), true);
                                    }
                                    //_Info("mult ref");
                                    //ptr->Value1：指向实际对象
                                    //ptr->Value2：-1表示第一个是FieldReference， -2表示是ArrayReference
                                    //managedStack[offset]：int[]，表示

                                    //多层引用managedStack[ptr - evaluationStackBase] == fieldIdList
                                    //多层引用,append
                                    if (ptr->Type == ValueType.ChainFieldReference)
                                    {
                                        var offset = ptr - evaluationStackBase;
                                        var fieldAddr = managedStack[offset] as FieldAddr;
                                        var fieldIdList = fieldAddr.FieldIdList;
                                        var newFieldIdList = new int[fieldIdList.Length + 1];
                                        Array.Copy(fieldIdList, newFieldIdList, fieldIdList.Length);
                                        newFieldIdList[fieldIdList.Length] = pc->Operand;
                                        managedStack[offset] = new FieldAddr()
                                        {
                                            Object = fieldAddr.Object,
                                            FieldIdList = newFieldIdList
                                        };
                                    }
                                    else
                                    {
                                        if (ptr->Value2 < 0)
                                        {
                                            throwRuntimeException(new NotSupportedException(
                                                "ref of compiler generated object field ref!"), true);
                                        }
                                        var offset = ptr - evaluationStackBase;
                                        var fieldAddr = new FieldAddr()
                                        {
                                            Object = managedStack[ptr->Value1],
                                            FieldIdList = new int[] { ptr->Value2, pc->Operand }
                                        };
                                        managedStack[offset] = fieldAddr;
                                        ptr->Value2 = ptr->Type == ValueType.FieldReference ? -1 : -2;
                                    }

                                    ptr->Type = ValueType.ChainFieldReference;
                                }
                                else
                                {
                                    object obj = EvaluationStackOperation.ToObject(evaluationStackBase, ptr,
                                        managedStack, fieldInfo == null ? typeof(AnonymousStorey)
                                        : fieldInfo.DeclaringType, this, false);
                                    ptr->Type = ValueType.FieldReference;
                                    ptr->Value1 = (int)(ptr - evaluationStackBase);
                                    managedStack[ptr->Value1] = obj;
                                    ptr->Value2 = pc->Operand;
                                    //_Info("sigle ref type = " + obj.GetType() + ",hc=" + obj.GetHashCode()
                                    //    + ",v1=" + ptr->Value1 + ",v2=" + ptr->Value2);
                                }
                                //_Info("Ldflda fieldInfo:" + fieldInfo + ", dt:" + fieldInfo.DeclaringType 
                                //    + ", ptr->Value1:" + ptr->Value1 + ", obj info:" + (obj == null ? "null"
                                //    : obj.GetHashCode().ToString() + "/" + managedStack[ptr->Value1]
                                //        .GetHashCode().ToString())
                                //     + ", ref eq? " + ReferenceEquals(obj, managedStack[ptr->Value1]));
                            }
                            break;
                        case Code.Mul://0.2389259%
                            {
                                Value* b = evaluationStackPointer - 1;
                                Value* a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                switch (a->Type)
                                {
                                    case ValueType.Long:
                                        *((long*)&evaluationStackPointer->Value1)
                                            = (*((long*)&a->Value1)) * (*((long*)&b->Value1));
                                        break;
                                    case ValueType.Integer:
                                        evaluationStackPointer->Value1 = a->Value1 * b->Value1;
                                        break;
                                    case ValueType.Float:
                                        *((float*)&evaluationStackPointer->Value1)
                                            = (*((float*)&a->Value1)) * (*((float*)&b->Value1));
                                        break;
                                    case ValueType.Double:
                                        *((double*)&evaluationStackPointer->Value1)
                                            = (*((double*)&a->Value1)) * (*((double*)&b->Value1));
                                        break;
                                }
                                evaluationStackPointer++;
                            }
                            break;
                        //case Code.Conv_I: //0.2136634% Convert to native int, pushing native int on stack.
                        case Code.Ldarga: // Ldarga_S:0.2035229%
                            {
                                *(Value**)&evaluationStackPointer->Value1 = argumentBase + pc->Operand;
                                evaluationStackPointer->Type = ValueType.StackReference;
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Conv_U: //0.1764814% Convert to unsigned native int, pushing native int on stack.
                            {
                                var ptr = evaluationStackPointer - 1;
                                void* val;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        val = (void *)*(ulong*)&ptr->Value1;
                                        break;
                                    case ValueType.Integer:
                                        val = (void*)(uint)ptr->Value1;
                                        break;
                                    default:
                                        val = null;
                                        throwRuntimeException(new NotImplementedException("Conv_U for" + ptr->Type), true);
                                        break;
                                }
                                ptr->Type = ValueType.Long;
                                *(void **)&ptr->Value1 = val;
                            }
                            break;
                        case Code.Starg://Starg_S:0.1551328 %
                            {
                                evaluationStackPointer--;
                                store(evaluationStackBase, argumentBase + pc->Operand, evaluationStackPointer,
                                    managedStack);
                                managedStack[evaluationStackPointer - evaluationStackBase] = null; ;
                            }
                            break;
                        case Code.Ceq: //0.1549549%
                            {
                                var rhs = evaluationStackPointer - 1;
                                var lhs = rhs - 1;
                                bool eq = false;

                                if (lhs->Type == rhs->Type)
                                {
                                    if (lhs->Type == ValueType.Object || lhs->Type == ValueType.ValueType)
                                    {
                                        var lpos = (int)(lhs - evaluationStackBase);
                                        var rpos = (int)(rhs - evaluationStackBase);
                                        eq = ReferenceEquals(managedStack[lhs->Value1], managedStack[rhs->Value1]);
                                        managedStack[lpos] = null;
                                        managedStack[rpos] = null;
                                    }
                                    else
                                    {
                                        eq = lhs->Value1 == rhs->Value1;
                                        if (lhs->Type != ValueType.Integer && lhs->Type != ValueType.Float)
                                        {
                                            eq = eq && (lhs->Value2 == rhs->Value2);
                                        }
                                    }
                                }
                                else
                                {
                                    throwRuntimeException(new InvalidProgramException("Ceq for diff type"), true);
                                }
                                lhs->Type = ValueType.Integer;
                                lhs->Value1 = eq ? 1 : 0;
                                evaluationStackPointer = rhs;
                            }
                            break;
                        case Code.Shl: //0.1362749%
                            {
                                var ptr = evaluationStackPointer - 1 - 1;
                                int bits = (evaluationStackPointer - 1)->Value1;
                                switch(ptr->Type)
                                {
                                    case ValueType.Integer:
                                        ptr->Value1 = ptr->Value1 << bits;
                                        break;
                                    case ValueType.Long:
                                        *((long*)&ptr->Value1) = (*((long*)&ptr->Value1)) << bits;
                                        break;
                                    default:
                                        throw new InvalidProgramException("<< for " + ptr->Type);
                                }
                                evaluationStackPointer--;
                            }
                            break;
                        case Code.Conv_U1: //0.1239995%
                            {
                                var ptr = evaluationStackPointer - 1;
                                int val;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                    case ValueType.Integer:
                                        val = (byte)ptr->Value1;
                                        break;
                                    case ValueType.Float:
                                        val = (byte)*(float*)&ptr->Value1;
                                        break;
                                    case ValueType.Double:
                                        val = (byte)*(double*)&ptr->Value1;
                                        break;
                                    default:
                                        throw new NotImplementedException();
                                }
                                ptr->Type = ValueType.Integer;
                                ptr->Value1 = val;
                            }
                            break;
                        case Code.Ldelema: //0.1124357%
                            {
                                var ptr = evaluationStackPointer - 1 - 1;
                                ptr->Type = ValueType.ArrayReference;
                                ptr->Value2 = (evaluationStackPointer - 1)->Value1;
                                evaluationStackPointer--;
                            }
                            break;
                        case Code.Stind_Ref://0.09482315%
                        case Code.Stind_I1://0.06333404%
                        case Code.Stind_I2://0.02793102%
                        case Code.Stind_I4://0.1106567%
                        case Code.Stind_I8://0.01352075%
                        case Code.Stind_R4: //0.001956951%
                        case Code.Stind_R8: //0.002668569%
                        case Code.Stind_I://0.01031847%
                        case Code.Stobj: // 0.02846474%
                            {
                                var ptr = evaluationStackPointer - 1 - 1;
                                var src = evaluationStackPointer - 1;
                                switch (ptr->Type)
                                {
                                    case ValueType.FieldReference:
                                    case ValueType.ChainFieldReference:
                                        {
                                            Type fieldType = null;
                                            if (ptr->Type == ValueType.ChainFieldReference)
                                            {
                                                var fieldAddr = managedStack[ptr - evaluationStackBase] as FieldAddr;
                                                var fieldIdList = fieldAddr.FieldIdList;
                                                fieldType
                                                    = fieldInfos[fieldIdList[fieldIdList.Length - 1]].FieldType;
                                            }
                                            else
                                            {
                                                fieldType = fieldInfos[ptr->Value2].FieldType;
                                            }
                                            EvaluationStackOperation.UpdateReference(evaluationStackBase, ptr,
                                                managedStack, EvaluationStackOperation.ToObject(evaluationStackBase,
                                                src, managedStack, fieldType, this), this, fieldType);
                                            //managedStack[ptr->Value1] = null;
                                            if (src->Type >= ValueType.Object)
                                            {
                                                managedStack[src - evaluationStackBase] = null;
                                            }
                                            evaluationStackPointer = ptr;
                                        }
                                        break;
                                    case ValueType.ArrayReference:
                                        {
                                            var obj = managedStack[ptr->Value1];
                                            managedStack[ptr - evaluationStackBase] = null;
                                            int idx = ptr->Value2;
                                            arraySet(obj, idx, src, managedStack, evaluationStackBase);
                                            if (src->Type >= ValueType.Object)
                                            {
                                                managedStack[src - evaluationStackBase] = null;
                                            }
                                            evaluationStackPointer = ptr;
                                        }
                                        break;
                                    case ValueType.StaticFieldReference:
                                        {
                                            var fieldIndex = ptr->Value1;
                                            if (fieldIndex >= 0)
                                            {
                                                var fieldInfo = fieldInfos[fieldIndex];
                                                fieldInfo.SetValue(null,
                                                    EvaluationStackOperation.ToObject(evaluationStackBase, src,
                                                    managedStack, fieldInfo.FieldType, this));
                                            }
                                            else
                                            {
                                                fieldIndex = -(fieldIndex + 1);
                                                staticFields[fieldIndex]
                                                    = EvaluationStackOperation.ToObject(evaluationStackBase, src,
                                                    managedStack, staticFieldTypes[fieldIndex], this);
                                            }
                                            if (src->Type >= ValueType.Object)
                                            {
                                                managedStack[src - evaluationStackBase] = null;
                                            }
                                            evaluationStackPointer = ptr;
                                        }
                                        break;
                                    case ValueType.StackReference:
                                        {
                                            Value* des = *(Value**)&ptr->Value1;
                                            *des = *src;
                                            if (src->Type == ValueType.Object)
                                            {
                                                int offset = (int)(des - evaluationStackBase);
                                                des->Value1 = offset;
                                                managedStack[offset] = managedStack[src->Value1];
                                                managedStack[src - evaluationStackBase] = null;
                                            }
                                            else if (src->Type == ValueType.ValueType)
                                            {
                                                int offset = (int)(des - evaluationStackBase);
                                                des->Value1 = offset;
                                                managedStack[offset] = objectClone.Clone(managedStack[src->Value1]);
                                                managedStack[src - evaluationStackBase] = null;
                                            }
                                            //Console.WriteLine("store to stack address:" + new IntPtr(des) 
                                            //    + ",val type:" + src->Type + ",val1:" + src->Value1);
                                            evaluationStackPointer = ptr;
                                        }
                                        break;
                                    default:
                                        throwRuntimeException(new InvalidProgramException(code
                                            + " expect ref, but got " + ptr->Type), true);
                                        break;
                                }
                            }
                            break;
                        case Code.Ldind_I1: //0.006226661%
                        case Code.Ldind_U1: //0.05870852%
                        case Code.Ldind_I2: //0.008717326%
                        case Code.Ldind_U2: //0.04536567%
                        case Code.Ldind_I4:  //0.106387%
                        case Code.Ldind_U4:  //0.04981329%
                        case Code.Ldind_I8:  //0.03042169%
                        case Code.Ldind_I: //0.02081484%
                        case Code.Ldind_R4: //0.007294089%
                        case Code.Ldind_R8:  //wrapper调用应先push值，然后参数取该值的地址
                        case Code.Ldind_Ref: //0.1070986% 操作符可以作为类型依据，不用走反射
                        case Code.Ldobj: //0.02348341%
                            {
                                Value* ptr = evaluationStackPointer - 1;
                                switch (ptr->Type)
                                {
                                    case ValueType.FieldReference:
                                        {
                                            //_Info("ptr->Value2:" + ptr->Value2);
                                            var fieldIndex = ptr->Value2;
                                            if (fieldIndex >= 0)
                                            {
                                                Type fieldType = null;
                                                fieldType = fieldInfos[fieldIndex].FieldType;

                                                //var fieldInfo = fieldInfos[ptr->Value2];
                                                var val = EvaluationStackOperation.ToObject(evaluationStackBase, ptr,
                                                    managedStack, fieldType, this, false);
                                                //_Info("val = " + val);
                                                EvaluationStackOperation.PushObject(evaluationStackBase, ptr,
                                                    managedStack, val, fieldType);
                                            }
                                            else
                                            {
                                                fieldIndex = -(fieldIndex + 1);
                                                AnonymousStorey anonyObj
                                                    = managedStack[ptr->Value1] as AnonymousStorey;
                                                anonyObj.Ldfld(fieldIndex, evaluationStackBase, ptr, managedStack);
                                            }
                                        }
                                        break;
                                    case ValueType.ChainFieldReference:
                                        {
                                            //_Info("ptr->Value2:" + ptr->Value2);
                                            Type fieldType = null;

                                            var fieldAddr = managedStack[ptr - evaluationStackBase] as FieldAddr;
                                            var fieldIdList = fieldAddr.FieldIdList;
                                            //_Info("fieldIdList:" + fieldIdList);
                                            fieldType = fieldInfos[fieldIdList[fieldIdList.Length - 1]].FieldType;

                                            //var fieldInfo = fieldInfos[ptr->Value2];
                                            var val = EvaluationStackOperation.ToObject(evaluationStackBase, ptr,
                                                managedStack, fieldType, this, false);
                                            //_Info("val = " + val);
                                            EvaluationStackOperation.PushObject(evaluationStackBase, ptr,
                                                managedStack, val, fieldType);
                                        }
                                        break;
                                    case ValueType.ArrayReference:
                                        {
                                            var obj = managedStack[ptr->Value1];
                                            managedStack[ptr - evaluationStackBase] = null;
                                            int idx = ptr->Value2;
                                            arrayGet(obj, idx, ptr, managedStack, evaluationStackBase);
                                        }
                                        break;
                                    case ValueType.StaticFieldReference:
                                        {
                                            var fieldIndex = ptr->Value1;
                                            if (fieldIndex >= 0)
                                            {
                                                var fieldInfo = fieldInfos[ptr->Value1];
                                                EvaluationStackOperation.PushObject(evaluationStackBase, ptr,
                                                    managedStack, fieldInfo.GetValue(null), fieldInfo.FieldType);
                                            }
                                            else
                                            {
                                                fieldIndex = -(fieldIndex + 1);
                                                EvaluationStackOperation.PushObject(evaluationStackBase, ptr,
                                                    managedStack, staticFields[fieldIndex],
                                                    staticFieldTypes[fieldIndex]);
                                            }
                                        }
                                        break;
                                    case ValueType.StackReference:
                                        {
                                            Value* src = *(Value**)&ptr->Value1;
                                            *ptr = *src;
                                            if (src->Type == ValueType.Object)
                                            {
                                                managedStack[ptr - evaluationStackBase] = managedStack[src->Value1];
                                                ptr->Value1 = (int)(ptr - evaluationStackBase);
                                            }
                                            else if (src->Type == ValueType.ValueType)
                                            {
                                                managedStack[ptr - evaluationStackBase]
                                                    = objectClone.Clone(managedStack[src->Value1]);
                                                ptr->Value1 = (int)(ptr - evaluationStackBase);
                                            }
                                        }
                                        break;
                                    default:
                                        throwRuntimeException(new InvalidProgramException(code
                                            + " expect ref, but got " + ptr->Type), true);
                                        break;
                                }
                            }
                            break;
                        case Code.Bgt: //Bgt_S:0.1104788% Bgt:0.01103009%
                            {
                                var b = evaluationStackPointer - 1;
                                var a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                bool transfer = false;
                                switch (evaluationStackPointer->Type)
                                {
                                    case ValueType.Integer:
                                        transfer = a->Value1 > b->Value1;
                                        break;
                                    case ValueType.Long:
                                        transfer = *(long*)&a->Value1 > *(long*)&b->Value1;
                                        break;
                                    case ValueType.Float:
                                        transfer = *(float*)&a->Value1 > *(float*)&b->Value1;
                                        break;
                                    case ValueType.Double:
                                        transfer = *(double*)&a->Value1 > *(double*)&b->Value1;
                                        break;
                                    default:
                                        throw new InvalidProgramException("Bgt for " + evaluationStackPointer->Type);
                                }

                                if (transfer)
                                {
                                    pc += pc->Operand;
                                    continue;
                                }
                            }
                            break;
                        case Code.Initobj: //0.1085218%
                            {
                                var ptr = evaluationStackPointer - 1;
                                var type = externTypes[pc->Operand];
                                EvaluationStackOperation.UpdateReference(evaluationStackBase, ptr, managedStack,
                                    Activator.CreateInstance(type), this, type);
                                if (ptr->Type >= ValueType.Object)
                                {
                                    managedStack[ptr - evaluationStackBase] = null;
                                }
                                evaluationStackPointer = ptr;
                            }
                            break;
                        case Code.Shr_Un: //0.09589058%
                            {
                                var ptr = evaluationStackPointer - 1 - 1;
                                int bits = (evaluationStackPointer - 1)->Value1;
                                switch (ptr->Type)
                                {
                                    case ValueType.Integer:
                                        ptr->Value1 = (int)(((uint)ptr->Value1) >> bits);
                                        break;
                                    case ValueType.Long:
                                        *((ulong*)&ptr->Value1) = (*((ulong*)&ptr->Value1)) >> bits;
                                        break;
                                    default:
                                        throw new InvalidProgramException(">> for " + ptr->Type);
                                }
                                evaluationStackPointer--;
                            }
                            break;
                        case Code.Stelem_I1: //0.09019764%
                            {
                                var val = (evaluationStackPointer - 1)->Value1;
                                var idx = (evaluationStackPointer - 1 - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1 - 1;
                                var obj = managedStack[ptr->Value1];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer = ptr;
                                byte[] byteArr = obj as byte[];
                                if (byteArr != null)
                                {
                                    byteArr[idx] = (byte)val;
                                    break;
                                }
                                bool[] boolArr = obj as bool[];
                                if (boolArr != null)
                                {
                                    boolArr[idx] = val != 0;
                                    break;
                                }
                                sbyte[] sbyteArr = obj as sbyte[];
                                if (sbyteArr != null)
                                {
                                    sbyteArr[idx] = (sbyte)val;
                                }
                            }
                            break;
                        //为什么有Ldelem_U1而没有Stelem_U1？因为load后push到es后，会扩展到int32，同样是0xff，
                        //作为有符号或无符号扩展是不一样的，一句话，高位扩展要考虑符号位，截取低位不需要
                        case Code.Ldelem_U1: //0.08557212% 
                            {
                                var idx = (evaluationStackPointer - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1;
                                var obj = managedStack[ptr->Value1];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer--;
                                ptr->Type = ValueType.Integer;
                                byte[] byteArr = obj as byte[];
                                if (byteArr != null)
                                {
                                    ptr->Value1 = byteArr[idx];
                                    break;
                                }
                                bool[] boolArr = obj as bool[];
                                if (boolArr != null)
                                {
                                    ptr->Value1 = boolArr[idx] ? 1 : 0;
                                }
                            }
                            break;
                        //hacker: mscorlib，Unbox指令只有6条,均是这样形式的语句产生的：((SomeValueType)obj).field，
                        //所以Unbox按Unbox_Any处理，只要ldfld能处理即可
                        case Code.Unbox:
                        case Code.Unbox_Any://0.0848605%
                            {
                                var ptr = evaluationStackPointer - 1;
                                var type = externTypes[pc->Operand];
                                //var pos = (int)(ptr - evaluationStackBase);
                                var obj = managedStack[ptr->Value1];
                                if (ptr->Type == ValueType.Object)
                                {
                                    if (type.IsValueType)
                                    {
                                        if (obj == null)
                                        {
                                            throw new NullReferenceException();
                                        }
                                        else if(type.IsPrimitive)
                                        {
                                            EvaluationStackOperation.UnboxPrimitive(ptr, obj, type);
                                        }
                                        else if(type.IsEnum)
                                        {
                                            EvaluationStackOperation.UnboxPrimitive(ptr, obj, Enum.GetUnderlyingType(type));
                                        }
                                        else
                                        {
                                            ptr->Type = ValueType.ValueType;
                                        }
                                    }
                                    //泛型函数是有可能Unbox_Any一个非值类型的
                                    else if (obj != null && !type.IsAssignableFrom(obj.GetType())) 
                                    {
                                        throw new InvalidCastException();
                                    }
                                }
                            }
                            break;
                        case Code.Div: //0.0693828%
                            {
                                Value* b = evaluationStackPointer - 1;
                                Value* a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                switch (a->Type)
                                {
                                    case ValueType.Long:
                                        *((long*)&evaluationStackPointer->Value1)
                                            = (*((long*)&a->Value1)) / (*((long*)&b->Value1));
                                        break;
                                    case ValueType.Integer:
                                        evaluationStackPointer->Value1 = a->Value1 / b->Value1;
                                        break;
                                    case ValueType.Float:
                                        *((float*)&evaluationStackPointer->Value1)
                                            = (*((float*)&a->Value1)) / (*((float*)&b->Value1));
                                        break;
                                    case ValueType.Double:
                                        *((double*)&evaluationStackPointer->Value1)
                                            = (*((double*)&a->Value1)) / (*((double*)&b->Value1));
                                        break;
                                }
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Cgt_Un: //0.06724794%
                            {
                                Value* rhs = evaluationStackPointer - 1;
                                Value* lhs = rhs - 1;

                                bool res = false;
                                switch (lhs->Type)
                                {
                                    case ValueType.Integer:
                                        res = (uint)lhs->Value1 > (uint)rhs->Value1;
                                        break;
                                    case ValueType.Long:
                                        res = *(ulong*)&lhs->Value1 > *(ulong*)&rhs->Value1;
                                        break;
                                    case ValueType.Float:
                                        res = !(*(float*)&lhs->Value1 <= *(float*)&rhs->Value1);
                                        break;
                                    case ValueType.Double:
                                        res = !(*(double*)&lhs->Value1 <= *(double*)&rhs->Value1);
                                        break;
                                    case ValueType.Object:
                                    case ValueType.ValueType:
                                        res = managedStack[lhs->Value1] != null && managedStack[rhs->Value1] == null;
                                        break;
                                }
                                lhs->Type = ValueType.Integer;
                                lhs->Value1 = res ? 1 : 0;
                                evaluationStackPointer = rhs;
                            }
                            break;
                        case Code.Ldelem_I4: //0.05853061%
                            {
                                var idx = (evaluationStackPointer - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1;
                                var arr = managedStack[ptr->Value1] as int[];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer--;
                                ptr->Type = ValueType.Integer;
                                ptr->Value1 = arr[idx];
                            }
                            break;
                        case Code.Ldelem_Any: //0.05657366% 
                            {
                                var arrPtr = evaluationStackPointer - 1 - 1;
                                int idx = (evaluationStackPointer - 1)->Value1;
                                var arr = managedStack[arrPtr->Value1] as Array;
                                EvaluationStackOperation.PushObject(evaluationStackBase, arrPtr, managedStack,
                                        arr.GetValue(idx), arr.GetType().GetElementType());
                                evaluationStackPointer = evaluationStackPointer - 1;
                            }
                            break;
                        case Code.Ldc_R8: //0.05088072%
                            {
                                *(double*)&evaluationStackPointer->Value1 = *(double*)(pc + 1); 
                                evaluationStackPointer->Type = ValueType.Double;
                                evaluationStackPointer++;
                                pc++;
                            }
                            break;
                        case Code.Stelem_I4: //0.05052491%
                            {
                                var val = (evaluationStackPointer - 1)->Value1;
                                var idx = (evaluationStackPointer - 1 - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1 - 1;
                                var obj = managedStack[ptr->Value1];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer = ptr;
                                int[] intArr = obj as int[];
                                if (intArr != null)
                                {
                                    intArr[idx] = val;
                                    break;
                                }
                                uint[] uintArr = obj as uint[];
                                if (uintArr != null)
                                {
                                    uintArr[idx] = (uint)val;
                                }
                            }
                            break;
                        case Code.Conv_U8: //0.04839005%
                            {
                                var obj = evaluationStackPointer - 1;
                                ulong val;
                                switch (obj->Type)
                                {
                                    case ValueType.Integer:
                                        val = *(uint*)&obj->Value1;//Conv_U8的操作数肯定是uint
                                        break;
                                    case ValueType.Long:
                                        pc++;
                                        continue;
                                    case ValueType.Float:
                                        val = (ulong)*(float*)&obj->Value1;
                                        break;
                                    case ValueType.Double:
                                        val = (ulong)*(double*)&obj->Value1;
                                        break;
                                    default:
                                        val = 0;
                                        throwRuntimeException(new NotImplementedException(), true);
                                        break;
                                }
                                obj->Type = ValueType.Long;
                                *(ulong*)(&obj->Value1) = val;
                            }
                            break;
                        case Code.Rem: //0.04714472%
                            {
                                Value* rhs = evaluationStackPointer - 1;
                                Value* lhs = rhs - 1;

                                switch (lhs->Type)
                                {
                                    case ValueType.Integer:
                                        lhs->Value1 = lhs->Value1 % rhs->Value1;
                                        break;
                                    case ValueType.Long:
                                        *(long*)&lhs->Value1 = *(long*)&lhs->Value1 % *(long*)&rhs->Value1;
                                        break;
                                    case ValueType.Float:
                                        *(float*)&lhs->Value1 = *(float*)&lhs->Value1 % *(float*)&rhs->Value1;
                                        break;
                                    case ValueType.Double:
                                        *(double*)&lhs->Value1 = *(double*)&lhs->Value1 % *(double*)&rhs->Value1;
                                        break;
                                }

                                evaluationStackPointer = rhs;
                            }
                            break;
                        case Code.Constrained://0.04714472% 
                            {
                                var lastInstruction = pc - 1;
                                var type = externTypes[pc->Operand];
                                var ptr = evaluationStackPointer - 1 - lastInstruction->Operand;
                                var obj = EvaluationStackOperation.ToObject(evaluationStackBase, ptr, managedStack, type, this);
                                var pos = (int)(ptr - evaluationStackBase);
                                managedStack[pos] = obj;
                                ptr->Value1 = pos;
                                ptr->Type = ValueType.Object;
                            }
                            break;
                        case Code.Switch://0.04518777%
                            {
                                int val = (evaluationStackPointer - 1)->Value1;
                                if (val >= 0 && val < pc->Operand)
                                {
                                    int* jmpTable = (int*)(pc + 1);
                                    //Console.WriteLine("val:" + val + ", jmp to:" + jmpTable[val]);
                                    pc += jmpTable[val];
                                }
                                else
                                {
                                    pc += ((pc->Operand + 1) >> 1) + 1;
                                }
                                continue;
                            }
                        case Code.Ldc_I8: //0.04429825%
                            {
                                *(long*)&evaluationStackPointer->Value1 = *(long*)(pc + 1); 
                                evaluationStackPointer->Type = ValueType.Long;
                                evaluationStackPointer++;
                                pc++;
                            }
                            break;
                        //case Code.Sizeof: //0.04412034% 用于指针，不支持
                        case Code.Xor: //0.03842739%
                            {
                                var rhs = evaluationStackPointer - 1;
                                var lhs = rhs - 1;
                                switch (lhs->Type)
                                {
                                    case ValueType.Long:
                                        *((long*)&lhs->Value1) = *((long*)&lhs->Value1) ^ *((long*)&rhs->Value1);
                                        break;
                                    case ValueType.Integer:
                                        lhs->Value1 = lhs->Value1 ^ rhs->Value1;
                                        break;
                                }
                                evaluationStackPointer = rhs;
                            }
                            break;
                        case Code.Ldftn: //0.03682625%
                            {
                                evaluationStackPointer->Type = ValueType.Object;
                                evaluationStackPointer->Value1 = (int)(evaluationStackPointer - evaluationStackBase);
                                managedStack[evaluationStackPointer->Value1] = externMethods[pc->Operand];
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Newanon:
                            {

                                var anonymousStoreyInfo = anonymousStoreyInfos[pc->Operand];
                                //_Info(anonymousStoreyInfo.Slots == null ? "raw" : "object with interfaces");
                                var pn = anonymousStoreyInfo.CtorParamNum;
                                //_Info("param count:" + pn + ", ctor id:" + anonymousStoreyInfo.CtorId);
                                AnonymousStorey anonymousStorey = (anonymousStoreyInfo.Slots == null)
                                    ? new AnonymousStorey(anonymousStoreyInfo.FieldNum, anonymousStoreyInfo.FieldTypes, pc->Operand, anonymousStoreyInfo.VTable, this)
                                    : wrappersManager.CreateBridge(anonymousStoreyInfo.FieldNum, anonymousStoreyInfo.FieldTypes, pc->Operand, anonymousStoreyInfo.VTable,
                                    anonymousStoreyInfo.Slots, this);

                                var pos = evaluationStackPointer;
                                for (int p = 0; p < pn; p++)
                                {
                                    //var src = pos - 1;
                                    //_Info("src t:" + src->Type + ",v:" + src->Value1);
                                    copy(evaluationStackBase, pos, pos - 1, managedStack);
                                    //_Info("des t:" + pos->Type + ",v:" + pos->Value1);
                                    pos = pos - 1;
                                }
                                pos->Type = ValueType.Object;
                                pos->Value1 = (int)(pos - evaluationStackBase);
                                //for (int p = 0; p < pn + 1; p++)
                                //{
                                //    var dsp = pos + p;
                                    //_Info("p " + p + ":" + dsp->Type + ",v:" + dsp->Value1);
                                //}
                                managedStack[pos->Value1] = anonymousStorey;
                                Execute(unmanagedCodes[anonymousStoreyInfo.CtorId], pos, managedStack,
                                    evaluationStackBase, pn + 1, anonymousStoreyInfo.CtorId);
                                pos->Type = ValueType.Object;
                                pos->Value1 = (int)(pos - evaluationStackBase);
                                managedStack[pos->Value1] = anonymousStorey;
                                evaluationStackPointer = pos + 1;
                            }
                            break;
                        case Code.Stelem_Any: //0.03166702% 
                            {
                                var arrPtr = evaluationStackPointer - 1 - 1 - 1;
                                int idx = (evaluationStackPointer - 1 - 1)->Value1;
                                var valPtr = evaluationStackPointer - 1;
                                var arr = managedStack[arrPtr->Value1] as Array;
                                var val = EvaluationStackOperation.ToObject(evaluationStackBase, valPtr,
                                        managedStack, arr.GetType().GetElementType(), this, false);
                                arr.SetValue(val, idx);
                                managedStack[arrPtr - evaluationStackBase] = null; //清理，如果有的话
                                managedStack[valPtr - evaluationStackBase] = null;
                                evaluationStackPointer = arrPtr;
                            }
                            break;
                        case Code.Conv_U2: //0.02917635%
                            {
                                var obj = evaluationStackPointer - 1;
                                ushort val = 0;
                                switch (obj->Type)
                                {
                                    case ValueType.Long:
                                    case ValueType.Integer:
                                        val = (ushort)obj->Value1;
                                        break;
                                    case ValueType.Float:
                                        val = (ushort)*(float*)&obj->Value1;
                                        break;
                                    case ValueType.Double:
                                        val = (ushort)*(double*)&obj->Value1;
                                        break;
                                }
                                obj->Type = ValueType.Integer;
                                obj->Value1 = val;
                            }
                            break;
                        case Code.Ldsflda: //0.02632988%
                            {
                                if (pc->Operand < 0)
                                {
                                    var fieldIndex = -(pc->Operand + 1);
                                    checkCctorExecute(fieldIndex, evaluationStackPointer, managedStack,
                                        evaluationStackBase);
                                }
                                evaluationStackPointer->Type = ValueType.StaticFieldReference;
                                evaluationStackPointer->Value1 = pc->Operand;
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Stelem_I2: //0.02117065%
                            {
                                var val = (evaluationStackPointer - 1)->Value1;
                                var idx = (evaluationStackPointer - 1 - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1 - 1;
                                var obj = managedStack[ptr->Value1];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer = ptr;
                                short[] shortArr = obj as short[];
                                if (shortArr != null)
                                {
                                    shortArr[idx] = (short)val;
                                    break;
                                }
                                ushort[] ushortArr = obj as ushort[];
                                if (ushortArr != null)
                                {
                                    ushortArr[idx] = (ushort)val;
                                    break;
                                }
                                char[] charArr = obj as char[];
                                if (charArr != null)
                                {
                                    charArr[idx] = (char)val;
                                }
                            }
                            break;
                        case Code.Blt_Un: //0.02010322%
                            {
                                var b = evaluationStackPointer - 1;
                                var a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                bool transfer = false;
                                switch (evaluationStackPointer->Type)
                                {
                                    case ValueType.Integer:
                                        transfer = (uint)a->Value1 < (uint)b->Value1;
                                        break;
                                    case ValueType.Long:
                                        transfer = *(ulong*)&a->Value1 < *(ulong*)&b->Value1;
                                        break;
                                    case ValueType.Float:
                                        transfer = !(*(float*)&a->Value1 >= *(float*)&b->Value1);
                                        break;
                                    case ValueType.Double:
                                        transfer = !(*(double*)&a->Value1 >= *(double*)&b->Value1);
                                        break;
                                }

                                if (transfer)
                                {
                                    pc += pc->Operand;
                                    continue;
                                }
                            }
                            break;
                        case Code.Ble_Un: //Ble_Un_S:0.01725675% Ble_Un:0.0003558092%
                            {
                                var b = evaluationStackPointer - 1;
                                var a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                bool transfer = false;
                                switch (evaluationStackPointer->Type)
                                {
                                    case ValueType.Integer:
                                        transfer = (uint)a->Value1 <= (uint)b->Value1;
                                        break;
                                    case ValueType.Long:
                                        transfer = *(ulong*)&a->Value1 <= *(ulong*)&b->Value1;
                                        break;
                                    case ValueType.Float:
                                        transfer = !(*(float*)&a->Value1 > *(float*)&b->Value1);
                                        break;
                                    case ValueType.Double:
                                        transfer = !(*(double*)&a->Value1 > *(double*)&b->Value1);
                                        break;
                                }

                                if (transfer)
                                {
                                    pc += pc->Operand;
                                    continue;
                                }
                            }
                            break;
                        case Code.Ldelem_U2: //0.01690094%
                            {
                                var idx = (evaluationStackPointer - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1;
                                var obj = managedStack[ptr->Value1];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer--;
                                ptr->Type = ValueType.Integer;
                                ushort[] ushortArr = obj as ushort[];
                                if (ushortArr != null)
                                {
                                    ptr->Value1 = ushortArr[idx];
                                    break;
                                }
                                char[] charArr = obj as char[];
                                if (charArr != null)
                                {
                                    ptr->Value1 = charArr[idx];
                                }
                            }
                            break;
                        case Code.Conv_R8: //0.01654513%
                            {
                                var ptr = evaluationStackPointer - 1;
                                double val = 0;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        val = *(long*)&ptr->Value1;
                                        break;
                                    case ValueType.Float:
                                        val = *(float*)&ptr->Value1;
                                        break;
                                    case ValueType.Integer:
                                        val = ptr->Value1;
                                        break;
                                    case ValueType.Double:
                                        pc++;
                                        continue;
                                }
                                ptr->Type = ValueType.Double;
                                *(double*)&ptr->Value1 = val;
                            }
                            break;
                        case Code.Conv_I2: //0.0154777%
                            {
                                var ptr = evaluationStackPointer - 1;
                                int val = 0;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        val = (short)*(long*)&ptr->Value1;
                                        break;
                                    case ValueType.Float:
                                        val = (short)*(float*)&ptr->Value1;
                                        break;
                                    case ValueType.Double:
                                        val = (short)*(double*)&ptr->Value1;
                                        break;
                                    case ValueType.Integer:
                                        val = (short)ptr->Value1;
                                        break;
                                }
                                ptr->Type = ValueType.Integer;
                                ptr->Value1 = val;
                            }
                            break;
                        case Code.Bgt_Un: //0.01476608%
                            {
                                var b = evaluationStackPointer - 1;
                                var a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                bool transfer = false;
                                switch (evaluationStackPointer->Type)
                                {
                                    case ValueType.Integer:
                                        transfer = (uint)a->Value1 > (uint)b->Value1;
                                        break;
                                    case ValueType.Long:
                                        transfer = *(ulong*)&a->Value1 > *(ulong*)&b->Value1;
                                        break;
                                    case ValueType.Float:
                                        transfer = !(*(float*)&a->Value1 <= *(float*)&b->Value1);
                                        break;
                                    case ValueType.Double:
                                        transfer = !(*(double*)&a->Value1 <= *(double*)&b->Value1);
                                        break;
                                }

                                if (transfer)
                                {
                                    pc += pc->Operand;
                                    continue;
                                }
                            }
                            break;
                        case Code.Mul_Ovf_Un: //0.01458818%
                            {
                                Value* b = evaluationStackPointer - 1;
                                Value* a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                switch (a->Type)
                                {
                                    case ValueType.Long:
                                        *((ulong*)&evaluationStackPointer->Value1)
                                            = checked((*((ulong*)&a->Value1)) * (*((ulong*)&b->Value1)));
                                        break;
                                    case ValueType.Integer:
                                        evaluationStackPointer->Value1
                                            = (int)checked((uint)a->Value1 * (uint)b->Value1);
                                        break;
                                }
                                evaluationStackPointer++;
                            }
                            break;
                        //Localloc 0.01441027% no support
                        case Code.Bge_Un: //0.01405446%
                            {
                                var b = evaluationStackPointer - 1;
                                var a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                bool transfer = false;
                                switch (evaluationStackPointer->Type)
                                {
                                    case ValueType.Integer:
                                        transfer = (uint)a->Value1 >= (uint)b->Value1;
                                        break;
                                    case ValueType.Long:
                                        transfer = *(ulong*)&a->Value1 >= *(ulong*)&b->Value1;
                                        break;
                                    case ValueType.Float:
                                        transfer = !(*(float*)&a->Value1 < *(float*)&b->Value1);
                                        break;
                                    case ValueType.Double:
                                        transfer = !(*(double*)&a->Value1 < *(double*)&b->Value1);
                                        break;
                                }

                                if (transfer)
                                {
                                    pc += pc->Operand;
                                    continue;
                                }

                            }
                            break;
                        case Code.Clt: //0.01405446%
                            {
                                Value* rhs = evaluationStackPointer - 1;
                                Value* lhs = rhs - 1;
                                
                                bool res = false;
                                switch (lhs->Type)
                                {
                                    case ValueType.Integer:
                                        res = lhs->Value1 < rhs->Value1;
                                        break;
                                    case ValueType.Long:
                                        res = *(long*)&lhs->Value1 < *(long*)&rhs->Value1;
                                        break;
                                    case ValueType.Float:
                                        res = *(float*)&lhs->Value1 < *(float*)&rhs->Value1;
                                        break;
                                    case ValueType.Double:
                                        res = *(double*)&lhs->Value1 < *(double*)&rhs->Value1;
                                        break;
                                }
                                lhs->Type = ValueType.Integer;
                                lhs->Value1 = res ? 1 : 0;
                                evaluationStackPointer = rhs;
                            }
                            break;
                        case Code.Cgt: //0.01387656%
                            {
                                Value* rhs = evaluationStackPointer - 1;
                                Value* lhs = rhs - 1;

                                bool res = false;
                                switch (lhs->Type)
                                {
                                    case ValueType.Integer:
                                        res = lhs->Value1 > rhs->Value1;
                                        break;
                                    case ValueType.Long:
                                        res = *(long*)&lhs->Value1 > *(long*)&rhs->Value1;
                                        break;
                                    case ValueType.Float:
                                        res = *(float*)&lhs->Value1 > *(float*)&rhs->Value1;
                                        break;
                                    case ValueType.Double:
                                        res = *(double*)&lhs->Value1 > *(double*)&rhs->Value1;
                                        break;
                                }
                                lhs->Type = ValueType.Integer;
                                lhs->Value1 = res ? 1 : 0;
                                evaluationStackPointer = rhs;
                            }
                            break;
                        case Code.Neg: //0.009606848%
                            {
                                var ptr = evaluationStackPointer - 1;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        *((long*)&ptr->Value1) = -*((long*)&ptr->Value1);
                                        break;
                                    case ValueType.Integer:
                                        ptr->Value1 = -ptr->Value1;
                                        break;
                                    case ValueType.Float:
                                        *((float*)&ptr->Value1) = -*((float*)&ptr->Value1);
                                        break;
                                    case ValueType.Double:
                                        *((double*)&ptr->Value1) = -*((double*)&ptr->Value1);
                                        break;
                                }
                            }
                            break;
                        case Code.Not: //0.008717326%
                            {
                                var ptr = evaluationStackPointer - 1;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        *((long*)&ptr->Value1) = ~*((long*)&ptr->Value1);
                                        break;
                                    case ValueType.Integer:
                                        ptr->Value1 = ~ptr->Value1;
                                        break;
                                }
                            }
                            break;
                        case Code.Ldelem_I8: //0.008005707%
                            {
                                var idx = (evaluationStackPointer - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1;
                                var obj = managedStack[ptr->Value1];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer--;
                                ptr->Type = ValueType.Long;
                                long[] longArr = obj as long[];
                                if (longArr != null)
                                {
                                    *(long*)&ptr->Value1 = longArr[idx];
                                    break;
                                }
                                ulong[] ulongArr = obj as ulong[];
                                if (ulongArr != null)
                                {
                                    *(ulong*)&ptr->Value1 = ulongArr[idx];
                                }
                            }
                            break;
                        case Code.Ldc_R4: //0.006404566%
                            {
                                //*((long*)(&evaluationStackPointer->Value1)) = pc->Operand;
                                *(float*)&evaluationStackPointer->Value1 = *(float*)&pc->Operand; //高位不清除
                                evaluationStackPointer->Type = ValueType.Float;
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Add_Ovf: //0.006404566%
                        case Code.Add_Ovf_Un:
                            {
                                Value* b = evaluationStackPointer - 1;
                                //大于1的立即数和指针运算在il2cpp（unity 5.4）有bug，都会按1算
                                Value* a = evaluationStackPointer - 1 - 1; 
                                evaluationStackPointer = a;
                                switch (a->Type)
                                {
                                    case ValueType.Long:
                                        if (code == Code.Add_Ovf)
                                        {
                                            *((long*)&evaluationStackPointer->Value1)
                                                = checked(*((long*)&a->Value1) + *((long*)&b->Value1));
                                        }
                                        else
                                        {
                                            *((ulong*)&evaluationStackPointer->Value1)
                                                = checked(*((ulong*)&a->Value1) + *((ulong*)&b->Value1));
                                        }
                                        break;
                                    case ValueType.Integer:
                                        if (code == Code.Add_Ovf)
                                        {
                                            evaluationStackPointer->Value1 = checked(a->Value1 + b->Value1);
                                        }
                                        else
                                        {
                                            evaluationStackPointer->Value1
                                                = (int)checked((uint)a->Value1 + (uint)b->Value1);
                                        }
                                        break;
                                    case ValueType.Float:
                                        *((float*)&evaluationStackPointer->Value1)
                                            = checked(*((float*)&a->Value1) + *((float*)&b->Value1));
                                        break;
                                    case ValueType.Double:
                                        *((double*)&evaluationStackPointer->Value1)
                                            = checked(*((double*)&a->Value1) + *((double*)&b->Value1));
                                        break;
                                }
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Ldelem_U4: //0.006226661%
                            {
                                var idx = (evaluationStackPointer - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1;
                                var arr = managedStack[ptr->Value1] as uint[];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer--;
                                ptr->Type = ValueType.Integer;
                                ptr->Value1 = (int)arr[idx];
                            }
                            break;
                        case Code.Conv_U4: //0.006048756%
                            {
                                var ptr = evaluationStackPointer - 1;
                                uint val = 0;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        val = (uint)*(long*)&ptr->Value1;
                                        break;
                                    case ValueType.Float:
                                        val = (uint)*(float*)&ptr->Value1;
                                        break;
                                    case ValueType.Double:
                                        val = (uint)*(double*)&ptr->Value1;
                                        break;
                                    case ValueType.Integer:
                                        val = (uint)ptr->Value1;
                                        break;
                                }
                                ptr->Type = ValueType.Integer;
                                ptr->Value1 = (int)val;
                            }
                            break;
                        case Code.Stelem_I8: //0.004269711%
                            {
                                var val = *(long*)&(evaluationStackPointer - 1)->Value1;
                                var idx = (evaluationStackPointer - 1 - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1 - 1;
                                var obj = managedStack[ptr->Value1];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer = ptr;
                                long[] longArr = obj as long[];
                                if (longArr != null)
                                {
                                    longArr[idx] = val;
                                    break;
                                }
                                ulong[] ulongArr = obj as ulong[];
                                if (ulongArr != null)
                                {
                                    ulongArr[idx] = (ulong)val;
                                }
                            }
                            break;
                        case Code.Ldelem_I2: //0.004269711%
                            {
                                var idx = (evaluationStackPointer - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1;
                                var obj = managedStack[ptr->Value1];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer--;
                                ptr->Type = ValueType.Integer;
                                short[] shortArr = obj as short[];
                                if (shortArr != null)
                                {
                                    ptr->Value1 = shortArr[idx];
                                    break;
                                }
                                char[] charArr = obj as char[];
                                if (charArr != null)
                                {
                                    ptr->Value1 = charArr[idx];
                                }
                            }
                            break;
                        case Code.Conv_I1: //0.003913901%
                            {
                                var ptr = evaluationStackPointer - 1;
                                int val = 0;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                    case ValueType.Integer:
                                        val = (sbyte)ptr->Value1;
                                        break;
                                    case ValueType.Float:
                                        val = (sbyte)*(float*)&ptr->Value1;
                                        break;
                                    case ValueType.Double:
                                        val = (sbyte)*(double*)&ptr->Value1;
                                        break;
                                }
                                ptr->Type = ValueType.Integer;
                                ptr->Value1 = val;
                            }
                            break;
                        case Code.Conv_R4: //0.003380188%
                            {
                                var ptr = evaluationStackPointer - 1;
                                float val = 0;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        val = *(long*)&ptr->Value1;
                                        break;
                                    case ValueType.Float:
                                        pc++;
                                        continue;
                                    case ValueType.Integer:
                                        val = ptr->Value1;
                                        break;
                                    case ValueType.Double:
                                        val = (float)*(double*)&ptr->Value1;
                                        break;
                                }
                                ptr->Type = ValueType.Float;
                                *(float*)&ptr->Value1 = val;
                            }
                            break;
                        case Code.Rem_Un: //0.003202283%
                            {
                                Value* rhs = evaluationStackPointer - 1;
                                Value* lhs = rhs - 1;

                                switch (lhs->Type)
                                {
                                    case ValueType.Integer:
                                        lhs->Value1 = (int)(((uint)lhs->Value1) % ((uint)rhs->Value1));
                                        break;
                                    case ValueType.Long:
                                        *(ulong*)&lhs->Value1 = *(ulong*)&lhs->Value1 % *(ulong*)&rhs->Value1;
                                        break;
                                }

                                evaluationStackPointer = rhs;
                            }
                            break;
                        case Code.Ldvirtftn: //0.003202283%
                            {
                                var ptr = evaluationStackPointer - 1;
                                var obj = managedStack[ptr->Value1];
                                var method = externMethods[pc->Operand];
                                if (obj.GetType() == method.DeclaringType)
                                {
                                    ptr->Type = ValueType.Object;
                                    ptr->Value1 = (int)(ptr - evaluationStackBase);
                                    managedStack[ptr->Value1] = externMethods[pc->Operand];
                                }
                                else
                                {
                                    //子类，.net实现Delegate.CreateDelegate会找到具体的override方法，
                                    //mono（至少unity5.2配套的mono）创建的delegate会指向父类方法
                                    var type = obj.GetType();
                                    var baseMethod = (method as MethodInfo).GetBaseDefinition();
                                    Dictionary<MethodInfo, MethodInfo> overrideMap;
                                    MethodInfo foundMethod = null;
                                    if (!overrideCache.TryGetValue(type, out overrideMap))
                                    {
                                        overrideMap = new Dictionary<MethodInfo, MethodInfo>();
                                        overrideCache[type] = overrideMap;
                                    }
                                    if (!overrideMap.TryGetValue(baseMethod, out foundMethod))
                                    {
                                        while (type != null)
                                        {
                                            var members = type.GetMember(baseMethod.Name, MemberTypes.Method,
                                                BindingFlags.Instance | BindingFlags.Public
                                                | BindingFlags.NonPublic);
                                            for (int i = 0; i < members.Length; i++)
                                            {
                                                var methodToCheck = members[i] as MethodInfo;
                                                if (methodToCheck.GetBaseDefinition() == baseMethod)
                                                {
                                                    foundMethod = methodToCheck;
                                                    break;
                                                }
                                            }
                                            if (foundMethod != null) break;
                                            type = type.BaseType;
                                        }
                                        overrideMap[baseMethod] = foundMethod;
                                    }

                                    ptr->Type = ValueType.Object;
                                    ptr->Value1 = (int)(ptr - evaluationStackBase);
                                    managedStack[ptr->Value1] = foundMethod;
                                }
                            }
                            break;
                        case Code.Div_Un: //0.00231276%
                            {
                                Value* b = evaluationStackPointer - 1;
                                Value* a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                switch (a->Type)
                                {
                                    case ValueType.Long:
                                        *((ulong*)&evaluationStackPointer->Value1)
                                            = (*((ulong*)&a->Value1)) / (*((ulong*)&b->Value1));
                                        break;
                                    case ValueType.Integer:
                                        evaluationStackPointer->Value1 = (int)((uint)a->Value1 / (uint)b->Value1);
                                        break;
                                }
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Mul_Ovf: //0.002134855%
                            {
                                Value* b = evaluationStackPointer - 1;
                                Value* a = evaluationStackPointer - 1 - 1;
                                evaluationStackPointer = a;
                                switch (a->Type)
                                {
                                    case ValueType.Long:
                                        *((long*)&evaluationStackPointer->Value1)
                                            = checked((*((long*)&a->Value1)) * (*((long*)&b->Value1)));
                                        break;
                                    case ValueType.Integer:
                                        evaluationStackPointer->Value1 = checked(a->Value1 * b->Value1);
                                        break;
                                    case ValueType.Float:
                                        *((float*)&evaluationStackPointer->Value1)
                                            = checked((*((float*)&a->Value1)) * (*((float*)&b->Value1)));
                                        break;
                                    case ValueType.Double:
                                        *((double*)&evaluationStackPointer->Value1)
                                            = checked((*((double*)&a->Value1)) * (*((double*)&b->Value1)));
                                        break;
                                }
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Clt_Un: //0.001956951%
                            {
                                Value* rhs = evaluationStackPointer - 1;
                                Value* lhs = rhs - 1;

                                bool res = false;
                                switch (lhs->Type)
                                {
                                    case ValueType.Integer:
                                        res = (uint)lhs->Value1 < (uint)rhs->Value1;
                                        break;
                                    case ValueType.Long:
                                        res = *(ulong*)&lhs->Value1 < *(ulong*)&rhs->Value1;
                                        break;
                                    case ValueType.Float:
                                        res = !(*(float*)&lhs->Value1 >= *(float*)&rhs->Value1);
                                        break;
                                    case ValueType.Double:
                                        res = !(*(double*)&lhs->Value1 >= *(double*)&rhs->Value1);
                                        break;
                                }
                                lhs->Type = ValueType.Integer;
                                lhs->Value1 = res ? 1 : 0;
                                evaluationStackPointer = rhs;
                            }
                            break;
                        case Code.Conv_R_Un: //0.001423237%
                            {
                                var ptr = evaluationStackPointer - 1;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        *(double*)&ptr->Value1 = *(ulong*)&ptr->Value1;
                                        break;
                                    case ValueType.Integer:
                                        *(double*)&ptr->Value1 = (uint)ptr->Value1;
                                        break;
                                }
                                ptr->Type = ValueType.Double;
                            }
                            break;
                        case Code.Stelem_I: //0.001245332% 
                            {
                                var val = *(long*)&(evaluationStackPointer - 1)->Value1;
                                var idx = (evaluationStackPointer - 1 - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1 - 1;
                                var obj = managedStack[ptr->Value1];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer = ptr;
                                IntPtr[] intPtrArr = obj as IntPtr[];
                                if (intPtrArr != null)
                                {
                                    intPtrArr[idx] = new IntPtr(val);
                                    break;
                                }
                                UIntPtr[] uintPtrArr = obj as UIntPtr[];
                                if (uintPtrArr != null)
                                {
                                    uintPtrArr[idx] = new UIntPtr((ulong)val);
                                }
                            }
                            break;
                        //case Code.Mkrefany: //0.001067428% __makeref关键字，先不支持
                        case Code.Stelem_R8: //0.000889523%
                            {
                                var val = *(double*)&(evaluationStackPointer - 1)->Value1;
                                var idx = (evaluationStackPointer - 1 - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1 - 1;
                                var arr = managedStack[ptr->Value1] as double[];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer = ptr;
                                arr[idx] = val;
                            }
                            break;
                        case Code.Ldelem_I: //0.000889523% 指针相关
                            {
                                var idx = (evaluationStackPointer - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1;
                                var obj = managedStack[ptr->Value1];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer--;
                                ptr->Type = ValueType.Long;
                                IntPtr[] intPtrArr = obj as IntPtr[];
                                if (intPtrArr != null)
                                {
                                    *(long*)(&ptr->Value1) = intPtrArr[idx].ToInt64();
                                    break;
                                }
                                UIntPtr[] uintPtrArr = obj as UIntPtr[];
                                if (uintPtrArr != null)
                                {
                                    *(ulong*)(&ptr->Value1) = uintPtrArr[idx].ToUInt64();
                                }
                            }
                            break;
                        case Code.Conv_Ovf_U8: //4
                        case Code.Conv_Ovf_U8_Un:
                            {
                                var ptr = evaluationStackPointer - 1;
                                ulong val;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        val = pc->Code == Code.Conv_Ovf_U8 ? checked((ulong)*(long*)&ptr->Value1) :
                                            checked(*(ulong*)&ptr->Value1);
                                        break;
                                    case ValueType.Integer:
                                        val = pc->Code == Code.Conv_Ovf_U8 ? checked((ulong)ptr->Value1) :
                                            checked((uint)ptr->Value1);
                                        break;
                                    case ValueType.Float:
                                        val = checked((ulong)*(float*)&ptr->Value1);
                                        break;
                                    case ValueType.Double:
                                        val = checked((ulong)*(double*)&ptr->Value1);
                                        break;
                                    default:
                                        throw new InvalidProgramException("Conv_Ovf_U8 for " + ptr->Type);
                                }
                                ptr->Type = ValueType.Long;
                                *(long*)&ptr->Value1 = (long)val;
                            }
                            break;
                        case Code.Conv_Ovf_I8:
                        case Code.Conv_Ovf_I8_Un:
                            {
                                var ptr = evaluationStackPointer - 1;
                                long val;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        val = pc->Code == Code.Conv_Ovf_I8 ? checked(*(long*)&ptr->Value1) :
                                            checked((long)*(ulong*)&ptr->Value1);
                                        break;
                                    case ValueType.Integer:
                                        val = pc->Code == Code.Conv_Ovf_I8 ? checked((long)ptr->Value1) :
                                            checked((uint)ptr->Value1);
                                        break;
                                    case ValueType.Float:
                                        val = checked((long)*(float*)&ptr->Value1);
                                        break;
                                    case ValueType.Double:
                                        val = checked((long)*(double*)&ptr->Value1);
                                        break;
                                    default:
                                        throw new InvalidProgramException("Conv_Ovf_I8 for " + ptr->Type);
                                }
                                ptr->Type = ValueType.Long;
                                *(long*)&ptr->Value1 = val;
                            }
                            break;
                        case Code.Conv_Ovf_I1: //3
                        case Code.Conv_Ovf_I1_Un:
                            {
                                var ptr = evaluationStackPointer - 1;
                                int val = 0;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                    case ValueType.Integer:
                                        val = pc->Code == Code.Conv_Ovf_I1 ? checked((sbyte)ptr->Value1) :
                                            checked((sbyte)(uint)ptr->Value1);
                                        break;
                                    case ValueType.Float:
                                        val = checked((sbyte)*(float*)&ptr->Value1);
                                        break;
                                    case ValueType.Double:
                                        val = checked((sbyte)*(double*)&ptr->Value1);
                                        break;
                                }
                                ptr->Type = ValueType.Integer;
                                ptr->Value1 = val;
                            }
                            break;
                        // case Code.Refanytype: //关键字__reftype(this)，暂时不支持
                        case Code.Ldelem_R8:
                            {
                                var idx = (evaluationStackPointer - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1;
                                var arr = managedStack[ptr->Value1] as double[];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer--;
                                ptr->Type = ValueType.Double;
                                *(double*)&ptr->Value1 = arr[idx];
                            }
                            break;
                        case Code.Ldelem_R4:
                            {
                                var idx = (evaluationStackPointer - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1;
                                var arr = managedStack[ptr->Value1] as float[];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer--;
                                ptr->Type = ValueType.Float;
                                *(float*)&ptr->Value1 = arr[idx];
                            }
                            break;
                        case Code.Conv_Ovf_I4:
                        case Code.Conv_Ovf_I: // TODO: Conv_Ovf_I
                            {
                                var ptr = evaluationStackPointer - 1;
                                int val;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        val = checked((int)*(long*)&ptr->Value1);
                                        break;
                                    case ValueType.Float:
                                        val = checked((int)*(float*)&ptr->Value1);
                                        break;
                                    case ValueType.Double:
                                        val = checked((int)*(double*)&ptr->Value1);
                                        break;
                                    case ValueType.Integer:
                                        val = ptr->Value1;
                                        break;
                                    default:
                                        val = 0;
                                        throwRuntimeException(new NotImplementedException(), true);
                                        break;
                                }
                                ptr->Type = ValueType.Integer;
                                ptr->Value1 = val;
                            }
                            break;
                        case Code.Conv_Ovf_U2:
                        case Code.Conv_Ovf_U2_Un:
                            {
                                var ptr = evaluationStackPointer - 1;
                                int val = 0;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        val = pc->Code == Code.Conv_Ovf_U2 ? checked((ushort)*(long*)&ptr->Value1) :
                                            checked((ushort)*(ulong*)&ptr->Value1);
                                        break;
                                    case ValueType.Integer:
                                        val = pc->Code == Code.Conv_Ovf_U2 ? checked((ushort)ptr->Value1) :
                                            checked((ushort)(uint)ptr->Value1);
                                        break;
                                    case ValueType.Float:
                                        val = checked((ushort)*(float*)&ptr->Value1);
                                        break;
                                    case ValueType.Double:
                                        val = checked((ushort)*(double*)&ptr->Value1);
                                        break;
                                }
                                ptr->Type = ValueType.Integer;
                                ptr->Value1 = val;
                            }
                            break;
                        case Code.Conv_Ovf_U1:
                        case Code.Conv_Ovf_U1_Un:
                            {
                                var ptr = evaluationStackPointer - 1;
                                int val;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        val = pc->Code == Code.Conv_Ovf_U1 ? checked((byte)*(long*)&ptr->Value1) :
                                            checked((byte)*(ulong*)&ptr->Value1);
                                        break;
                                    case ValueType.Integer:
                                        val = pc->Code == Code.Conv_Ovf_U1 ? checked((byte)ptr->Value1) :
                                            checked((byte)(uint)ptr->Value1);
                                        break;
                                    case ValueType.Float:
                                        val = checked((byte)*(float*)&ptr->Value1);
                                        break;
                                    case ValueType.Double:
                                        val = checked((byte)*(double*)&ptr->Value1);
                                        break;
                                    default:
                                        throw new InvalidProgramException("Conv_Ovf_U1 for " + ptr->Type);
                                }
                                ptr->Type = ValueType.Integer;
                                ptr->Value1 = val;
                            }
                            break;
                        case Code.Conv_Ovf_U4:
                        case Code.Conv_Ovf_U4_Un:
                            {
                                var ptr = evaluationStackPointer - 1;
                                uint val = 0;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        val = pc->Code == Code.Conv_Ovf_U4 ? checked((uint)*(long*)&ptr->Value1) :
                                            checked((uint)*(ulong*)&ptr->Value1);
                                        break;
                                    case ValueType.Integer:
                                        val = checked((uint)ptr->Value1);
                                        break;
                                    case ValueType.Float:
                                        val = checked((uint)*(float*)&ptr->Value1);
                                        break;
                                    case ValueType.Double:
                                        val = checked((uint)*(double*)&ptr->Value1);
                                        break;
                                }
                                ptr->Type = ValueType.Integer;
                                ptr->Value1 = (int)val;
                            }
                            break;
                        case Code.Ldelem_I1:
                            {
                                var idx = (evaluationStackPointer - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1;
                                var obj = managedStack[ptr->Value1];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer--;
                                ptr->Type = ValueType.Integer;
                                sbyte[] sbyteArr = obj as sbyte[];
                                if (sbyteArr != null)
                                {
                                    ptr->Value1 = sbyteArr[idx];
                                    break;
                                }
                                bool[] boolArr = obj as bool[];
                                if (boolArr != null)
                                {
                                    ptr->Value1 = boolArr[idx] ? 1 : 0;
                                }
                            }
                            break;
                        case Code.Conv_Ovf_I_Un: // TODO:
                        case Code.Conv_Ovf_I4_Un:
                            {
                                var ptr = evaluationStackPointer - 1;
                                int val;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                        val = checked((int)*(ulong*)&ptr->Value1);
                                        break;
                                    case ValueType.Float:
                                        val = checked((int)*(float*)&ptr->Value1);
                                        break;
                                    case ValueType.Double:
                                        val = checked((int)*(double*)&ptr->Value1);
                                        break;
                                    case ValueType.Integer:
                                        val = checked((int)*(uint*)&ptr->Value1);
                                        break;
                                    default:
                                        val = 0;
                                        throwRuntimeException(new NotImplementedException(), true);
                                        break;
                                }
                                ptr->Type = ValueType.Integer;
                                ptr->Value1 = val;
                            }
                            break;
                        case Code.Stelem_R4:
                            {
                                var val = *(float*)&(evaluationStackPointer - 1)->Value1;
                                var idx = (evaluationStackPointer - 1 - 1)->Value1;
                                var ptr = evaluationStackPointer - 1 - 1 - 1;
                                var arr = managedStack[ptr->Value1] as float[];
                                managedStack[ptr - evaluationStackBase] = null;
                                evaluationStackPointer = ptr;
                                arr[idx] = val;
                            }
                            break;
                        case Code.Conv_Ovf_I2:
                        case Code.Conv_Ovf_I2_Un:
                            {
                                var ptr = evaluationStackPointer - 1;
                                int val = 0;
                                switch (ptr->Type)
                                {
                                    case ValueType.Long:
                                    case ValueType.Integer:
                                        val = pc->Code == Code.Conv_Ovf_I2 ? checked((short)ptr->Value1) :
                                            checked((short)(uint)ptr->Value1);
                                        break;
                                    case ValueType.Float:
                                        val = checked((short)*(float*)&ptr->Value1);
                                        break;
                                    case ValueType.Double:
                                        val = checked((short)*(double*)&ptr->Value1);
                                        break;
                                }
                                ptr->Type = ValueType.Integer;
                                ptr->Value1 = val;
                            }
                            break;
                        //case Code.Conv_Ovf_U: //ptr->m_Size = (this.scratch - checked((UIntPtr)ptr->m_Ptr)) / 1;
                        case Code.Throw: //1.404557%，虽然静态占比大，但运行时执行的次数应该比较少
                            {
                                var exceptionPos = (evaluationStackPointer - evaluationStackBase - 1);
                                var exception = managedStack[(evaluationStackPointer - 1)->Value1] as Exception;
                                managedStack[exceptionPos] = null;
                                evaluationStackPointer--;
                                throw exception;
                            }
                        case Code.Shr: 
                            {
                                var ptr = evaluationStackPointer - 1 - 1;
                                int bits = (evaluationStackPointer - 1)->Value1;
                                switch (ptr->Type)
                                {
                                    case ValueType.Integer:
                                        ptr->Value1 = ptr->Value1 >> bits;
                                        break;
                                    case ValueType.Long:
                                        *((long*)&ptr->Value1) = (*((long*)&ptr->Value1)) >> bits;
                                        break;
                                    default:
                                        throw new InvalidProgramException(">> for " + ptr->Type);
                                }
                                evaluationStackPointer--;
                            }
                            break;
                        case Code.Ldtoken: //大多数都被ldtype替代了
                            {
                                var type = externTypes[pc->Operand];
                                EvaluationStackOperation.PushObject(evaluationStackBase, evaluationStackPointer,
                                    managedStack, type.TypeHandle, typeof(RuntimeTypeHandle));
                                evaluationStackPointer++;
                            }
                            break;
                        case Code.Rethrow:
                            throw throwExcepton;
                        case Code.Nop://0.0270415% but被过滤了
                            break;
                        default:
                            throwRuntimeException(new NotImplementedException(code.ToString() + " " + pc->Operand),
                                true);
                            break;

                    }

                    //if (methodIndex == 527 || methodIndex == 528)
                    //{
                    //    _Info("** End Code = " + pc->Code + ", Oprand = " + pc->Operand + ", ESP = " 
                    //        + (evaluationStackPointer - localBase - localsCount));
                    //}
                    //if (traceValue != null)
                    //{
                    //    _Info("after:" + traceValue->Type + "," + traceValue->Value1 + (traceValue->Type
                    //        == ValueType.Object ? ("," + managedStack[traceValue->Value1]) : ""));
                    //}
                    pc++;
                }
                catch(RuntimeException e)
                {
                    if (topWriteBack != null)
                    {
                        *topWriteBack = argumentBase - refCount;
                        throw e.Real;
                    }
                    else
                    {
                        throw e;
                    }
                }
                catch(Exception e)
                {
                    int ipc = (int)(pc - pcb);
                    ExceptionHandler eh = getExceptionHandler(methodIndex, e.GetType(), ipc);
                    if (eh != null)
                    {
                        //clear evaluation stack
                        Value* newEvaluationStackPointer = localBase + localsCount;
                        int topPos = (int)(evaluationStackPointer - evaluationStackBase);
                        int newPos = (int)(newEvaluationStackPointer - evaluationStackBase);
                        for (int i = newPos; i < topPos; i++)
                        {
                            managedStack[i] = null;
                        }

                        evaluationStackPointer = newEvaluationStackPointer;
                        evaluationStackPointer->Type = ValueType.Object;
                        evaluationStackPointer->Value1 = newPos;
                        managedStack[newPos] = e;
                        evaluationStackPointer++;

                        throwExcepton = e;

                        pc = pcb + eh.HandlerStart;
                    }
                    else
                    {
                        int topPos = (int)(evaluationStackPointer - evaluationStackBase);
                        int newPos = (int)(argumentBase - evaluationStackBase) - refCount;
                        for (int i = newPos; i < topPos; i++)
                        {
                            managedStack[i] = null;
                        }

                        if (topWriteBack != null)
                        {
                            *topWriteBack = argumentBase - refCount;
                        }

                        throwExcepton = null;
                        throw e;
                    }
                }
            }
        }

        public string Statistics()
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendFormat("exceptionHandlers: {0}\n", exceptionHandlers == null ? 0 : exceptionHandlers.Length);
            sb.AppendFormat("externInvokers: {0}\n", externInvokers == null ? 0 : externInvokers.Length);
            sb.AppendFormat("externMethods: {0}\n", externMethods == null ? 0 : externMethods.Length);
            sb.AppendFormat("externTypes: {0}\n", externTypes == null ? 0 : externTypes.Length);
            sb.AppendFormat("internStrings: {0}\n", internStrings == null ? 0 : internStrings.Length);
            sb.AppendFormat("fieldInfos: {0}\n", fieldInfos == null ? 0 : fieldInfos.Length);
            sb.AppendFormat("anonymousStoreyInfos: {0}\n", anonymousStoreyInfos == null ? 0
                : anonymousStoreyInfos.Length);
            sb.AppendFormat("overrideCache: {0}\n", overrideCache == null ? 0 : overrideCache.Count);
            sb.AppendFormat("staticFieldTypes: {0}\n", staticFieldTypes == null ? 0 : staticFieldTypes.Length);
            sb.AppendFormat("staticFields: {0}\n", staticFields == null ? 0 : staticFields.Length);
            sb.AppendFormat("cctors: {0}\n", cctors == null ? 0 : cctors.Length);

            return sb.ToString();
        }

        [Obsolete("not support now!", true)]
        public static void SetGlobal(VirtualMachine virtualMachine, bool throwWhileExisted = false)
        {
            throw new NotSupportedException();
        }

        [Obsolete("use PatchManager.Load instead!")]
        public static void InitializeGlobal(Stream stream)
        {
            PatchManager.Load(stream);
        }

        [Obsolete("use PatchManager.Load instead!")]
        public static void ReplaceGlobal(Stream stream)
        {
            PatchManager.Load(stream);
        }

        [Obsolete("use PatchManager.Unload instead!")]
        public static void RemoveGlobal()
        {
            PatchManager.Unload(Assembly.GetCallingAssembly());
        }

        [Obsolete("not support now!", true)]
        public static VirtualMachine GetGlobal()
        {
            throw new NotSupportedException();
        }
    }
}
