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
    //匿名类模拟
    public class AnonymousStorey
    {
        Value[] unmanagedFields;
        object[] managedFields;
        public AnonymousStorey(int fieldNum)
        {
            unmanagedFields = new Value[fieldNum];
            managedFields = new object[fieldNum];
        }

        unsafe internal void Ldfld(int fieldIndex, Value* evaluationStackBase, Value* evaluationStackPointer,
            object[] managedStack)
        {
            //VirtualMachine._Info("AnonymousStorey Ldfld fieldIndex:" + fieldIndex + ","
            //    + unmanagedFields[fieldIndex].Type + "," +  unmanagedFields[fieldIndex].Value1);
            *evaluationStackPointer = unmanagedFields[fieldIndex];
            if (unmanagedFields[fieldIndex].Type >= ValueType.Object)
            {
                evaluationStackPointer->Value1 = (int)(evaluationStackPointer - evaluationStackBase);
                managedStack[evaluationStackPointer->Value1] = managedFields[fieldIndex];
            }
        }

        unsafe internal void Stfld(int fieldIndex, Value* evaluationStackBase, Value* evaluationStackPointer,
            object[] managedStack)
        {
            //VirtualMachine._Info("AnonymousStorey Stfld fieldIndex:" + fieldIndex + ","
            //    + evaluationStackPointer->Type + "," + evaluationStackPointer->Value1);
            unmanagedFields[fieldIndex] = *evaluationStackPointer;
            if (evaluationStackPointer->Type >= ValueType.Object)
            {
                unmanagedFields[fieldIndex].Value1 = fieldIndex;
                managedFields[fieldIndex] = managedStack[evaluationStackPointer->Value1];
            }
        }

        unsafe internal object Get(int fieldIndex, Type type, VirtualMachine virtualMachine,
            bool valueTypeClone)
        {
            fixed(Value* b = &unmanagedFields[0])
            {
                var ret = EvaluationStackOperation.ToObject(b, b + fieldIndex, managedFields, type,
                    virtualMachine, valueTypeClone);
                //VirtualMachine._Info("AnonymousStorey.Get, field=" + fieldIndex + ", val=" + ret);
                return ret;
            }
        }

        unsafe internal void Set(int fieldIndex, object obj, Type type, VirtualMachine virtualMachine)
        {
            fixed (Value* b = &unmanagedFields[0])
            {
                //VirtualMachine._Info("AnonymousStorey.Set, field=" + fieldIndex + ", val=" + obj);
                EvaluationStackOperation.PushObject(b, b + fieldIndex, managedFields, obj, type);
            }
        }
    }

    public class AnonymousStoreyInfo
    {
        public int FieldNum = 0;
        public int CtorId = 0;
        public int CtorParamNum = 0;
        public int[] Slots = null;
    }
}