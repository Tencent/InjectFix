/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

namespace IFix.Core
{
    using System;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Threading;

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct UnmanagedStack
    {
        public Value* Base;
        public Value* Top;
    }

    unsafe class ThreadStackInfo
    {
        public UnmanagedStack* UnmanagedStack;
        public object[] ManagedStack;

        IntPtr evaluationStackHandler;
        IntPtr unmanagedStackHandler;

        //int index;

        public ThreadStackInfo()
        {
            //index = idx;
            evaluationStackHandler = Marshal.AllocHGlobal(sizeof(Value) * VirtualMachine.MAX_EVALUATION_STACK_SIZE);
            unmanagedStackHandler = Marshal.AllocHGlobal(sizeof(UnmanagedStack));

            UnmanagedStack = (UnmanagedStack*)unmanagedStackHandler.ToPointer();
            UnmanagedStack->Base = UnmanagedStack->Top = (Value*)evaluationStackHandler.ToPointer();
            ManagedStack = new object[VirtualMachine.MAX_EVALUATION_STACK_SIZE];
        }

        //去掉析构，正常而言，静态变量不会析构，如果整个虚拟机释放的话，通过Marshal.AllocHGlobal分配的非托管
        //内存应该也会自动释放吧？
        //~ThreadStackInfo()
        //{
        //    //VirtualMachine._Info("~ThreadStackInfo");
        //    lock(stackListGuard)
        //    {
        //        stackList[index] = null;
        //    }
        //    UnmanagedStack = null;
        //    ManagedStack = null;
        //    Marshal.FreeHGlobal(evaluationStackHandler);
        //    Marshal.FreeHGlobal(unmanagedStackHandler);
        //}

        //本来ThreadStatic是很合适的方案，但据说Unity下的ThreadStatic会Crash，
        //Unity文档：https://docs.unity3d.com/Manual/Attributes.html
        //相关issue链接：https://issuetracker.unity3d.com/issues/
        //                 e-document-threadstatic-attribute-must-not-be-used-i-will-cause-crashes
        //issue内容：
        //This is a known limitation of the liveness check, as the we don't handle thread static or
        //context static variables as roots when performing the collection. 
        //The crash will happen in mono_unity_liveness_calculation_from_statics
        //[ThreadStatic]
        //internal static ThreadStackInfo Stack = null;

        static LocalDataStoreSlot localSlot = Thread.AllocateDataSlot();

        internal static ThreadStackInfo Stack
        {
            get
            {
                var stack = Thread.GetData(localSlot) as ThreadStackInfo;
                if (stack == null)
                {
                    VirtualMachine._Info("create thread stack");
                    stack = new ThreadStackInfo();
                    Thread.SetData(localSlot, stack);
                }
                return stack;
            }
        }
    }

    unsafe internal static class EvaluationStackOperation
    {
        internal static void UnboxPrimitive(Value* evaluationStackPointer, object obj, Type type)
        {
            if (obj is int)
            {
                evaluationStackPointer->Type = ValueType.Integer;
                evaluationStackPointer->Value1 = (int)obj;
            }
            else if (obj is float)
            {
                evaluationStackPointer->Type = ValueType.Float;
                *(float*)(&evaluationStackPointer->Value1) = (float)obj;
            }
            else if (obj is bool)
            {
                evaluationStackPointer->Type = ValueType.Integer;
                evaluationStackPointer->Value1 = (bool)(obj) ? 1 : 0;
            }
            else if (obj is double)
            {
                evaluationStackPointer->Type = ValueType.Double;
                *(double*)(&evaluationStackPointer->Value1) = (double)obj;
            }
            else if (obj is long)
            {
                evaluationStackPointer->Type = ValueType.Long;
                *(long*)(&evaluationStackPointer->Value1) = (long)obj;
            }
            else if (obj is byte)
            {
                evaluationStackPointer->Type = ValueType.Integer;
                evaluationStackPointer->Value1 = (byte)obj;
            }
            else if (obj is uint)
            {
                evaluationStackPointer->Type = ValueType.Integer;
                evaluationStackPointer->Value1 = (int)(uint)obj;
            }
            else if (obj is ushort)
            {
                evaluationStackPointer->Type = ValueType.Integer;
                evaluationStackPointer->Value1 = (int)(ushort)obj;
            }
            else if (obj is short)
            {
                evaluationStackPointer->Type = ValueType.Integer;
                evaluationStackPointer->Value1 = (short)obj;
            }
            else if (obj is char)
            {
                evaluationStackPointer->Type = ValueType.Integer;
                evaluationStackPointer->Value1 = (int)(char)obj;
            }
            else if (obj is ulong)
            {
                evaluationStackPointer->Type = ValueType.Long;
                *(ulong*)(&evaluationStackPointer->Value1) = (ulong)obj;
            }
            else if (obj is sbyte)
            {
                evaluationStackPointer->Type = ValueType.Integer;
                evaluationStackPointer->Value1 = (sbyte)obj;
            }
            else if (obj is IntPtr)
            {
                evaluationStackPointer->Type = ValueType.Long;
                *(long*)(&evaluationStackPointer->Value1) = ((IntPtr)obj).ToInt64();
            }
            else if (obj is UIntPtr)
            {
                evaluationStackPointer->Type = ValueType.Long;
                *(ulong*)(&evaluationStackPointer->Value1) = ((UIntPtr)obj).ToUInt64();
            }
            else
                throw new NotImplementedException("Unbox a " + obj.GetType() + " to " + type);
        }

        internal static object mGet(bool isArray, object root, int layer, int[] fieldIdList, FieldInfo[] fieldInfos)
        {
            //Console.WriteLine("mGet " + root);
            var fieldId = fieldIdList[layer];
            if (layer == 0)
            {
                if (isArray)
                {
                    return (root as Array).GetValue(fieldId);
                }
                else
                {
                    var fieldInfo = fieldInfos[fieldId];
                    return fieldInfo.GetValue(root);
                }
            }
            else
            {
                var fieldInfo = fieldInfos[fieldId];
                //VirtualMachine._Info("before --- " + fieldInfo);
                var ret =  fieldInfo.GetValue(mGet(isArray, root, layer - 1, fieldIdList, fieldInfos));
                //VirtualMachine._Info("after --- " + fieldInfo);
                return ret;
            }
        }

        internal static void mSet(bool isArray, object root, object val, int layer, int[] fieldIdList,
            FieldInfo[] fieldInfos)
        {
            var fieldId = fieldIdList[layer];
            if (layer == 0)
            {
                if (isArray)
                {
                    (root as Array).SetValue(val, fieldId);
                }
                else
                {
                    var fieldInfo = fieldInfos[fieldId];
                    //VirtualMachine._Info("set1 " + val.GetType() + " to " + fieldInfo + " of " + root.GetType()
                    //    + ", root.hc = " + root.GetHashCode());
                    fieldInfo.SetValue(root, val);
                }
            }
            else
            {
                var fieldInfo = fieldInfos[fieldId];
                //VirtualMachine._Info("before get " + fieldInfo);
                var parent = mGet(isArray, root, layer - 1, fieldIdList, fieldInfos);
                //VirtualMachine._Info("after get " + fieldInfo);
                //VirtualMachine._Info("before set " + fieldInfo);
                fieldInfo.SetValue(parent, val);
                //VirtualMachine._Info("set2 " + val.GetType() + " to " + fieldInfo + " of " + parent.GetType());
                //VirtualMachine._Info("after set " + fieldInfo);
                mSet(isArray, root, parent, layer - 1, fieldIdList, fieldInfos);
            }
        }

        // #lizard forgives
        internal static unsafe object ToObject(Value* evaluationStackBase, Value* evaluationStackPointer,
            object[] managedStack, Type type, VirtualMachine virtualMachine, bool valueTypeClone = true)
        {
            //未初始化的local引用可能作为out参数反射调用
            //TODO: 验证值类型out参数，对应参数位置是否可以是null？
            switch (evaluationStackPointer->Type)
            {
                case ValueType.Integer:
                    {
                        int i = evaluationStackPointer->Value1;
                        if (type == typeof(int))
                            return i;
                        else if (type == typeof(bool))
                            return i == 1;
                        else if (type == typeof(sbyte))
                            return (sbyte)i;
                        else if (type == typeof(byte))
                            return (byte)i;
                        else if (type == typeof(char))
                            return (char)i;
                        else if (type == typeof(short))
                            return (short)i;
                        else if (type == typeof(ushort))
                            return (ushort)i;
                        else if (type == typeof(uint))
                            return (uint)i;
                        else if (type.IsEnum)
                        {
                            return Enum.ToObject(type, i);
                        }
                        else 
                            return null;
                    }
                case ValueType.Long:
                    {
                        long l = *(long*)&evaluationStackPointer->Value1;
                        if (type == typeof(long))
                        {
                            return l;
                        }
                        else if (type == typeof(ulong))
                        {
                            return (ulong)l;
                        }
                        else if (type == typeof(IntPtr))
                        {
                            return new IntPtr(l);
                        }
                        else if (type == typeof(UIntPtr))
                        {
                            return new UIntPtr((ulong)l);
                        }
                        else if (type.IsEnum)
                        {
                            return Enum.ToObject(type, l);
                        }
                        else
                        {
                            return null;
                        }
                    }
                case ValueType.Float:
                    {
                        if (type == typeof(float))
                        {
                            return *(float*)&evaluationStackPointer->Value1;
                        }
                        else
                        {
                            return null;
                        }
                    }
                case ValueType.Double:
                    {
                        if (type == typeof(double))
                        {
                            return *(double*)&evaluationStackPointer->Value1;
                        }
                        else
                        {
                            return null;
                        }
                    }
                case ValueType.Object:
                    return managedStack[evaluationStackPointer->Value1];
                case ValueType.ValueType:
                    if (valueTypeClone && managedStack[evaluationStackPointer->Value1] != null)
                    {
                        return virtualMachine.objectClone.Clone(managedStack[evaluationStackPointer->Value1]);
                    }
                    else
                    {
                        return managedStack[evaluationStackPointer->Value1];
                    }
                case ValueType.StackReference:
                    {
                        return ToObject(evaluationStackBase, (*(Value**)&evaluationStackPointer->Value1),
                            managedStack, type, virtualMachine, valueTypeClone);
                    }
                case ValueType.FieldReference:
                case ValueType.ChainFieldReference:
                    {
                        //VirtualMachine._Info("ToObject FieldReference:" + evaluationStackPointer->Value2
                        //    + "," + evaluationStackPointer->Value1);
                        if (evaluationStackPointer->Type == ValueType.ChainFieldReference)
                        {
                            var fieldAddr = managedStack[evaluationStackPointer - evaluationStackBase] as FieldAddr;
                            var fieldIdList = fieldAddr.FieldIdList;
                            return mGet(evaluationStackPointer->Value2 != -1,
                                fieldAddr.Object, fieldIdList.Length - 1,
                                fieldIdList, virtualMachine.fieldInfos);
                        }
                        else
                        {
                            if (evaluationStackPointer->Value2 >= 0)
                            {
                                var fieldInfo = virtualMachine.fieldInfos[evaluationStackPointer->Value2];
                                var obj = managedStack[evaluationStackPointer->Value1];
                                return fieldInfo.GetValue(obj);
                            }
                            else
                            {
                                var obj = managedStack[evaluationStackPointer->Value1] as AnonymousStorey;
                                return obj.Get(-(evaluationStackPointer->Value2 + 1), type,
                                    virtualMachine, valueTypeClone);
                            }
                        }
                    }
                case ValueType.ArrayReference:
                    var arr = managedStack[evaluationStackPointer->Value1] as Array;
                    return arr.GetValue(evaluationStackPointer->Value2);
                case ValueType.StaticFieldReference:
                    {
                        var fieldIndex = evaluationStackPointer->Value1;
                        if (fieldIndex >= 0)
                        {
                            var fieldInfo = virtualMachine.fieldInfos[fieldIndex];
                            return fieldInfo.GetValue(null);
                        }
                        else
                        {
                            fieldIndex = -(fieldIndex + 1);
                            return virtualMachine.staticFields[fieldIndex];
                        }
                    }
                default:
                    throw new NotImplementedException("get obj of " + evaluationStackPointer->Type);
            }
        }

        public static void PushObject(Value* evaluationStackBase, Value* evaluationStackPointer,
            object[] managedStack, object obj, Type type)
        {
            if (obj != null)
            {
                if (type.IsPrimitive)
                {
                    UnboxPrimitive(evaluationStackPointer, obj, type);
                    return;
                }
                else if (type.IsEnum)
                {
                    var underlyingType = Enum.GetUnderlyingType(type);
                    if (underlyingType == typeof(long) || underlyingType == typeof(ulong))
                    {
                        evaluationStackPointer->Type = ValueType.Long;
                        *(long*)(&evaluationStackPointer->Value1) = underlyingType == typeof(long) ? 
                            Convert.ToInt64(obj) : (long)Convert.ToUInt64(obj) ;
                    }
                    else
                    {
                        evaluationStackPointer->Type = ValueType.Integer;
                        evaluationStackPointer->Value1 = Convert.ToInt32(obj);
                    }
                    return;
                }
            }
            int pos = (int)(evaluationStackPointer - evaluationStackBase);
            evaluationStackPointer->Value1 = pos;
            managedStack[pos] = obj;

            evaluationStackPointer->Type = (obj != null && type.IsValueType) ?
                ValueType.ValueType : ValueType.Object;
        }

        public static void UpdateReference(Value* evaluationStackBase, Value* evaluationStackPointer,
            object[] managedStack, object obj, VirtualMachine virtualMachine, Type type) //反射专用
        {
            switch (evaluationStackPointer->Type)
            {
                case ValueType.StackReference:
                    var des = *(Value**)&evaluationStackPointer->Value1;
                    //VirtualMachine._Info("UpdateReference des->Type:" + des->Type + ", des->Value1:"
                    //    + des->Value1 + ", des:" + new IntPtr(des) + ", offset:" + (des - evaluationStackBase) );
                    PushObject(evaluationStackBase, des, managedStack, obj, type);
                    break;
                case ValueType.ArrayReference:
                    var arr = managedStack[evaluationStackPointer->Value1] as Array;
                    arr.SetValue(obj, evaluationStackPointer->Value2);
                    break;
                case ValueType.FieldReference:
                case ValueType.ChainFieldReference:
                    {
                        if (evaluationStackPointer->Type == ValueType.ChainFieldReference)
                        {
                            var fieldAddr = managedStack[evaluationStackPointer - evaluationStackBase] as FieldAddr;
                            var fieldIdList = fieldAddr.FieldIdList;
                            //for(int i = 0; i < fieldIdList.Length; i++)
                            //{
                            //    VirtualMachine._Info("fid " + i + ": " + fieldIdList[i] + ", "
                            //        + virtualMachine.fieldInfos[fieldIdList[i]]);
                            //}
                            mSet(evaluationStackPointer->Value2 != -1,
                                fieldAddr.Object, obj, fieldIdList.Length - 1,
                                fieldIdList, virtualMachine.fieldInfos);
                        }
                        else
                        {
                            if (evaluationStackPointer->Value2 >= 0)
                            {


                                var fieldInfo = virtualMachine.fieldInfos[evaluationStackPointer->Value2];
                                //VirtualMachine._Info("update field: " + fieldInfo);
                                //VirtualMachine._Info("update field of: " + fieldInfo.DeclaringType);
                                //VirtualMachine._Info("update ref obj: "
                                //    + managedStack[evaluationStackPointer->Value1]);
                                //VirtualMachine._Info("update ref obj idx: " + evaluationStackPointer->Value1);
                                fieldInfo.SetValue(managedStack[evaluationStackPointer->Value1], obj);
                            }
                            else
                            {
                                var anonymousStorey = managedStack[evaluationStackPointer->Value1]
                                    as AnonymousStorey;
                                anonymousStorey.Set(-(evaluationStackPointer->Value2 + 1), obj, type, virtualMachine);
                            }
                        }
                        break;
                    }
                case ValueType.StaticFieldReference://更新完毕，直接return
                    {
                        var fieldIndex = evaluationStackPointer->Value1;
                        if (fieldIndex >= 0)
                        {
                            var fieldInfo = virtualMachine.fieldInfos[evaluationStackPointer->Value1];
                            fieldInfo.SetValue(null, obj);
                        }
                        else
                        {
                            fieldIndex = -(fieldIndex + 1);
                            virtualMachine.staticFields[fieldIndex] = obj;
                        }
                        break;
                    }
            }
        }
    }

    unsafe public struct Call
    {
        internal Value* argumentBase;

        internal Value* evaluationStackBase;

        internal object[] managedStack;

        internal Value* currentTop;//用于push状态

        internal Value** topWriteBack;

        public static Call Begin()
        {
            var stack = ThreadStackInfo.Stack;
            return new Call()
            {
                managedStack = stack.ManagedStack,
                currentTop = stack.UnmanagedStack->Top,
                argumentBase = stack.UnmanagedStack->Top,
                evaluationStackBase = stack.UnmanagedStack->Base,
                topWriteBack = &(stack.UnmanagedStack->Top)
            };
        }

        public void PushBoolean(bool b)
        {
            currentTop->Value1 = b ? 1 : 0;
            currentTop->Type = ValueType.Integer;
            currentTop++;
        }

        public bool GetBoolean(int offset = 0)
        {
            return (argumentBase + offset)->Value1 == 0 ? false : true;
        }

        public void PushByte(byte b)
        {
            PushInt32(b);
        }

        public byte GetByte(int offset = 0)
        {
            return (byte)GetInt32(offset);
        }

        public void PushSByte(sbyte sb)
        {
            PushInt32(sb);
        }

        public sbyte GetSByte(int offset = 0)
        {
            return (sbyte)GetInt32(offset);
        }

        public void PushInt16(short s)
        {
            PushInt32(s);
        }

        public short GetInt16(int offset = 0)
        {
            return (short)GetInt32(offset);
        }

        public void PushChar(char c)
        {
            PushInt32(c);
        }

        public char GetChar(int offset = 0)
        {
            return (char)GetInt32(offset);
        }

        public void PushUInt16(ushort us)
        {
            PushInt32(us);
        }

        public ushort GetUInt16(int offset = 0)
        {
            return (ushort)GetInt32(offset);
        }

        public void PushInt32(int i)
        {
            currentTop->Value1 = i;
            currentTop->Type = ValueType.Integer;
            currentTop++;
        }

        public int GetInt32(int offset = 0)
        {
            return (argumentBase + offset)->Value1;
        }

        public void PushUInt32(uint ui)
        {
            PushInt32((int)ui);
        }

        public uint GetUInt32(int offset = 0)
        {
            return (uint)GetInt32(offset);
        }

        public void PushInt64(long i)
        {
            *(long*)&currentTop->Value1 = i;
            currentTop->Type = ValueType.Long;
            currentTop++;
        }

        public long GetInt64(int offset = 0)
        {
            return *((long*)&((argumentBase + offset)->Value1));
        }

        public void PushUInt64(ulong i)
        {
            PushInt64((long)i);
        }

        public ulong GetUInt64(int offset = 0)
        {
            return (ulong)GetInt64(offset);
        }

        public void PushSingle(float f)
        {
            *(float*)(&currentTop->Value1) = f;
            currentTop->Type = ValueType.Float;
            currentTop++;
        }

        public float GetSingle(int offset = 0)
        {
            return *((float*)&((argumentBase + offset)->Value1));
        }

        public void PushDouble(double d)
        {
            *(double*)(&currentTop->Value1) = d;
            currentTop->Type = ValueType.Double;
            currentTop++;
        }

        public double GetDouble(int offset = 0)
        {
            return *((double*)&((argumentBase + offset)->Value1));
        }

        public void PushIntPtr(IntPtr i)
        {
            PushInt64(i.ToInt64());
        }

        public IntPtr GetIntPtr(int offset = 0)
        {
            return new IntPtr(GetInt64(offset));
        }

        public void PushUIntPtr(UIntPtr i)
        {
            PushUInt64(i.ToUInt64());
        }

        public UIntPtr GetUIntPtr(int offset = 0)
        {
            return new UIntPtr(GetUInt64(offset));
        }

        public void PushObject(object o)
        {
            int pos = (int)(currentTop - evaluationStackBase);
            currentTop->Type = ValueType.Object;
            currentTop->Value1 = pos;
            managedStack[pos] = o;
            currentTop++;
        }

        public void PushValueType(object o)
        {
            int pos = (int)(currentTop - evaluationStackBase);
            currentTop->Type = ValueType.ValueType;
            currentTop->Value1 = pos;
            managedStack[pos] = o;
            currentTop++;
        }

        public object GetObject(int offset = 0)
        {
            var ptr = argumentBase + offset;
            object ret = managedStack[ptr->Value1];
            managedStack[ptr - evaluationStackBase] = null;
            return ret;
        }

        public T GetAsType<T>(int offset = 0)
        {
            //if (typeof(T).IsEnum)
            //{
            //    var obj = GetObject(offset);
            //    var ptr = argumentBase + offset;
            //    VirtualMachine._Info("ptr =" + new IntPtr(ptr) + ", offset=" + (ptr - evaluationStackBase)
            //        + ",ptr->Value1=" + ptr->Value1 + ",ptr->Type=" + ptr->Type);

            //    if (obj != null)
            //    {
            //        VirtualMachine._Info("obj = " + obj + ", type = " + obj.GetType());
            //    }
            //    else
            //    {
            //        VirtualMachine._Info("obj = null");
            //    }
            //    return (T)Enum.ToObject(typeof(T), obj);
            //}
            //else
            //{
            //    return (T)GetObject(offset);
            //}
            return (T)GetObject(offset);
        }

        public void PushObjectAsResult(object obj, Type type) //反射专用
        {
            EvaluationStackOperation.PushObject(evaluationStackBase, argumentBase, managedStack, obj, type);
            currentTop = argumentBase + 1;
        }

        public void PushRef(int offset)
        {
            //Console.WriteLine("PushRef:" + offset + " address:" + new IntPtr(argumentBase + offset));
            *(Value**)&currentTop->Value1 = argumentBase + offset;
            currentTop->Type = ValueType.StackReference;
            currentTop++;
        }

        public void UpdateReference(int offset, object obj, VirtualMachine virtualMachine, Type type) //反射专用
        {
            EvaluationStackOperation.UpdateReference(ThreadStackInfo.Stack.UnmanagedStack->Base,
                argumentBase + offset, managedStack, obj, virtualMachine, type);
        }

        public static void End(ref Call call)
        {
            //Top的维护
            //ThreadStackInfo.Stack.UnmanagedStack->Top = call.argumentBase;
        }
    }

}