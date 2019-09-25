/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

namespace IFix.Core
{
    public enum Code
    {
        Constrained,
        Ldobj,
        Conv_Ovf_U1,
        Ldelem_Ref,
        Stfld,
        Unbox,
        Leave,
        Conv_Ovf_I2_Un,
        Starg,
        Stsfld,
        Newanon,
        Conv_Ovf_I8_Un,
        Ldvirtftn,
        Stind_I2,
        Conv_Ovf_I1_Un,
        Break,
        Stelem_Any,
        No,
        Ldind_I8,
        Bge,
        Ldloca,
        Ldelem_I8,
        Stelem_Ref,
        Cgt,
        Conv_Ovf_I_Un,
        Newobj,
        Xor,
        Ret,
        Localloc,
        Conv_Ovf_I,
        Jmp,
        Refanyval,
        Arglist,
        Ldelem_R4,
        Unaligned,
        Clt_Un,
        Initblk,
        Ldelem_U2,
        And,
        Cpblk,
        Cgt_Un,
        Ldind_I2,
        Callvirt,
        Bne_Un,
        Sizeof,
        Bge_Un,
        Ldftn,
        Endfilter,
        Stloc,
        Stelem_R4,
        Div_Un,
        Volatile,
        Ldelem_Any,
        Sub_Ovf_Un,
        Isinst,
        Sub,
        Box,
        Ldelem_U4,
        Conv_Ovf_U_Un,
        Add_Ovf_Un,
        Ldsflda,
        Ldind_U4,
        Stind_I4,
        Bgt,
        Ldelem_U1,
        Ldtype, // custom
        Conv_U,
        Conv_Ovf_I4_Un,
        Ldind_U2,
        Ldsfld,
        Conv_R8,
        Conv_U1,
        Ldc_I8,
        Rem,
        Ldelema,
        Neg,
        Add_Ovf,
        Conv_Ovf_U4,
        Initobj,
        Conv_Ovf_U8,
        Ldarga,
        Mul_Ovf,
        Unbox_Any,
        Br,
        Conv_Ovf_U4_Un,
        Stelem_I,
        Conv_Ovf_U,
        Stind_I1,
        Ldstr,
        Cpobj,
        Stind_Ref,
        Conv_Ovf_U1_Un,
        Ldind_I4,
        Ldflda,
        Brfalse,
        Tail,
        Conv_U4,
        Refanytype,
        Shl,
        Rem_Un,
        Mul,
        Ldc_R8,
        Ldtoken,
        Conv_I,
        Sub_Ovf,
        Conv_Ovf_I1,
        Ldc_R4,
        Pop,
        Shr_Un,
        Ldind_I,
        Stobj,
        Blt_Un,
        Ldind_R8,
        Conv_I8,
        Stind_R4,
        Ldelem_I1,
        Nop,
        Shr,
        Conv_U2,
        Newarr,
        Conv_I1,
        Conv_R_Un,
        Div,
        Readonly,
        Throw,
        Or,
        Ldnull,
        Ble_Un,
        Stelem_I1,
        Not,
        Switch,
        Stind_R8,
        Ldind_U1,
        Conv_U8,
        Conv_I2,
        Conv_I4,
        Ldind_I1,
        Add,
        Ceq,
        Ldind_Ref,
        Endfinally,
        Rethrow,
        Bgt_Un,
        Stind_I8,
        Stelem_I8,
        Blt,
        Ldind_R4,
        Conv_R4,
        Ble,
        Clt,
        Conv_Ovf_I2,
        Ldloc,
        Ldfld,
        Mkrefany,
        Brtrue,
        Ldc_I4,
        Conv_Ovf_U8_Un,
        Ldelem_I2,
        Ckfinite,
        CallExtern,
        Stind_I,
        Ldelem_R8,
        Ldarg,
        Conv_Ovf_I4,
        Stelem_I2,
        Ldelem_I4,
        Castclass,
        Ldelem_I,
        //Calli,
        Ldlen,
        Dup,
        Mul_Ovf_Un,

        //Pseudo instruction
        StackSpace,
        Beq,
        Conv_Ovf_I8,
        Stelem_R8,
        Conv_Ovf_U2,
        Call,
        Stelem_I4,
        Conv_Ovf_U2_Un,
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct Instruction
    {
        /// <summary>
        /// 指令MAGIC
        /// </summary>
        public const ulong INSTRUCTION_FORMAT_MAGIC = 6277304513193022118;

        /// <summary>
        /// 当前指令
        /// </summary>
        public Code Code;

        /// <summary>
        /// 操作数
        /// </summary>
        public int Operand;
    }

    public enum ExceptionHandlerType
    {
        Catch = 0,
        Filter = 1,
        Finally = 2,
        Fault = 4
    }

    public sealed class ExceptionHandler
    {
        public System.Type CatchType;
        public int CatchTypeId;
        public int HandlerEnd;
        public int HandlerStart;
        public ExceptionHandlerType HandlerType;
        public int TryEnd;
        public int TryStart;
    }
}