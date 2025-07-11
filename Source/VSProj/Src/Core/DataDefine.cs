/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 Tencent.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

namespace IFix.Core
{
    public enum ValueType
    {
        Integer,
        Long,
        Float,
        Double,
        StackReference,//Value = pointer, 
        StaticFieldReference,
        FieldReference,//Value1 = objIdx, Value2 = fieldIdx
        ChainFieldReference,
        Object,        //Value1 = objIdx
        ValueType,     //Value1 = objIdx
        ArrayReference,//Value1 = objIdx, Value2 = elemIdx
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct Value
    {
        public ValueType Type;
        public int Value1;
        public int Value2;
    }
}