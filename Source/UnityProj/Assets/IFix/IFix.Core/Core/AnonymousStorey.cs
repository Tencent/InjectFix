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
        internal int typeId;
        protected VirtualMachine virtualMachine;
        int equalMethodId;
        int finalizeMethodId;
        int getHashCodeMethodId;
        int toStringMethodId;

        public AnonymousStorey(int fieldNum, int[] fieldTypes,int typeID, int[] vTable, VirtualMachine virtualMachine)
        {
            unmanagedFields = new Value[fieldNum];
            managedFields = new object[fieldNum];
            for (int i = 0; i < fieldTypes.Length; ++i)
            {
                if (fieldTypes[i] > 0)
                {
                    unmanagedFields[i].Type = ValueType.ValueType;
                    unmanagedFields[i].Value1 = i;
                    int id = fieldTypes[i] - 1;
                    managedFields[i] = Activator.CreateInstance(virtualMachine.ExternTypes[id]);
                }
                else if (fieldTypes[i] == -2)
                {
                    unmanagedFields[i].Type = ValueType.Object;
                    unmanagedFields[i].Value1 = i;
                }
            }
            typeId = typeID;
            this.virtualMachine = virtualMachine;
            equalMethodId = vTable[0];
            finalizeMethodId = vTable[1];
            getHashCodeMethodId = vTable[2];
            toStringMethodId = vTable[3];
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

        public bool ObjectEquals(object obj)
        {
            return base.Equals(obj);
        }

        public override bool Equals(object obj)
        {
            if(equalMethodId == -1)
                return ObjectEquals(obj);
            Call call = Call.Begin();
            call.PushObject(this);
            call.PushObject(obj);
            virtualMachine.Execute(equalMethodId, ref call, 2, 0);
            return call.GetBoolean(0);
        }

        public int ObjectGetHashCode()
        {
            return base.GetHashCode();
        }

        public override int GetHashCode()
        {
            if(getHashCodeMethodId == -1)
                return ObjectGetHashCode();
            Call call = Call.Begin();
            call.PushObject(this);
            virtualMachine.Execute(getHashCodeMethodId, ref call, 1, 0);
            return call.GetInt32(0);
        }

        public string ObjectToString()
        {
            return base.ToString();
        }

        public override string ToString()
        {
            if (toStringMethodId == -1)
                return ObjectToString();
            Call call = Call.Begin();
            call.PushObject(this);
            virtualMachine.Execute(toStringMethodId, ref call, 1, 0);
            return call.GetAsType<string>(0);
        }

        ~AnonymousStorey()
        {
            if (finalizeMethodId != -1)
            {
                Call call = Call.Begin();
                call.PushObject(this);
                virtualMachine.Execute(finalizeMethodId, ref call, 1, 0);
            }
        }
    }

    public class AnonymousStoreyInfo
    {
        public int FieldNum = 0;
        public int[] FieldTypes = null;
        public int CtorId = 0;
        public int CtorParamNum = 0;
        public int[] Slots = null;
        public int[] VTable = null;
    }
}