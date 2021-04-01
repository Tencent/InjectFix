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
        Nop,
        Break,
        Ldarg,
        Ldloc,
        Stloc,
        Ldarga,
        Starg,
        Ldloca,
        Ldnull,
        Ldc_I4,
        Ldc_I8,
        Ldc_R4,
        Ldc_R8,
        Dup,
        Pop,
        Jmp,
        Call,
        CallExtern,
        //Calli,
        Ret,
        Br,
        Brfalse,
        Brtrue,
        Beq,
        Bge,
        Bgt,
        Ble,
        Blt,
        Bne_Un,
        Bge_Un,
        Bgt_Un,
        Ble_Un,
        Blt_Un,
        Switch,
        Ldind_I1,
        Ldind_U1,
        Ldind_I2,
        Ldind_U2,
        Ldind_I4,
        Ldind_U4,
        Ldind_I8,
        Ldind_I,
        Ldind_R4,
        Ldind_R8,
        Ldind_Ref,
        Stind_Ref,
        Stind_I1,
        Stind_I2,
        Stind_I4,
        Stind_I8,
        Stind_R4,
        Stind_R8,
        Add,
        Sub,
        Mul,
        Div,
        Div_Un,
        Rem,
        Rem_Un,
        And,
        Or,
        Xor,
        Shl,
        Shr,
        Shr_Un,
        Neg,
        Not,
        Conv_I1,
        Conv_I2,
        Conv_I4,
        Conv_I8,
        Conv_R4,
        Conv_R8,
        Conv_U4,
        Conv_U8,
        Callvirt,
        Callvirtvirt,
        Ldvirtftn2,
        Cpobj,
        Ldobj,
        Ldstr,
        Newobj,
        Castclass,
        Isinst,
        Conv_R_Un,
        Unbox,
        Throw,
        Ldfld,
        Ldflda,
        Stfld,
        Ldsfld,
        Ldsflda,
        Stsfld,
        Stobj,
        Conv_Ovf_I1_Un,
        Conv_Ovf_I2_Un,
        Conv_Ovf_I4_Un,
        Conv_Ovf_I8_Un,
        Conv_Ovf_U1_Un,
        Conv_Ovf_U2_Un,
        Conv_Ovf_U4_Un,
        Conv_Ovf_U8_Un,
        Conv_Ovf_I_Un,
        Conv_Ovf_U_Un,
        Box,
        Newarr,
        Ldlen,
        Ldelema,
        Ldelem_I1,
        Ldelem_U1,
        Ldelem_I2,
        Ldelem_U2,
        Ldelem_I4,
        Ldelem_U4,
        Ldelem_I8,
        Ldelem_I,
        Ldelem_R4,
        Ldelem_R8,
        Ldelem_Ref,
        Stelem_I,
        Stelem_I1,
        Stelem_I2,
        Stelem_I4,
        Stelem_I8,
        Stelem_R4,
        Stelem_R8,
        Stelem_Ref,
        Ldelem_Any,
        Stelem_Any,
        Unbox_Any,
        Conv_Ovf_I1,
        Conv_Ovf_U1,
        Conv_Ovf_I2,
        Conv_Ovf_U2,
        Conv_Ovf_I4,
        Conv_Ovf_U4,
        Conv_Ovf_I8,
        Conv_Ovf_U8,
        Refanyval,
        Ckfinite,
        Mkrefany,
        Ldtoken,
        Ldtype, // custom
        Conv_U2,
        Conv_U1,
        Conv_I,
        Conv_Ovf_I,
        Conv_Ovf_U,
        Add_Ovf,
        Add_Ovf_Un,
        Mul_Ovf,
        Mul_Ovf_Un,
        Sub_Ovf,
        Sub_Ovf_Un,
        Endfinally,
        Leave,
        Stind_I,
        Conv_U,
        Arglist,
        Ceq,
        Cgt,
        Cgt_Un,
        Clt,
        Clt_Un,
        Ldftn,
        Newanon,
        Ldvirtftn,
        Localloc,
        Endfilter,
        Unaligned,
        Volatile,
        Tail,
        Initobj,
        Constrained,
        Cpblk,
        Initblk,
        No,
        Rethrow,
        Sizeof,
        Refanytype,
        Readonly,

        //Pseudo instruction
        StackSpace,
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct Instruction
    {
        /// <summary>
        /// 指令MAGIC
        /// </summary>
        public const ulong INSTRUCTION_FORMAT_MAGIC = 317431043901;

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