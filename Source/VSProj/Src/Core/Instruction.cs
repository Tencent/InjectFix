/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

namespace IFix.Core
{
    public enum Code : int
    {
		Cgt,
				Stelem_R8,
				Volatile,
				Conv_Ovf_I4,
				Add_Ovf,
				Conv_Ovf_U4_Un,
				Conv_Ovf_U1_Un,
				Unaligned,
				Ldc_R4,
				Conv_Ovf_I_Un,
				And,
				Neg,
				Ldind_I8,
				Ldelem_Ref,
				Conv_Ovf_I2_Un,
				Add_Ovf_Un,
				Cpblk,
				Conv_Ovf_I,
				Shr_Un,
				Conv_Ovf_U4,
				Ret,
				Xor,
				Clt,
				Conv_I,
				Ldind_I4,
				Dup,
				Ldelem_R4,
				Clt_Un,
				Ldarga,
				Sizeof,
				Mul,
				Localloc,
				Ckfinite,
				Conv_U8,
				Stelem_I,
				Or,
				Blt,
				Ble,
				Ble_Un,
				Blt_Un,
				Bgt,
				Bge,
				Bge_Un,
				Bgt_Un,
				Bne_Un,
				Ldelema,
				Ldftn,
				No,
				Ldfld,
				Conv_Ovf_I4_Un,
				Tail,
				Initblk,
				Readonly,
				Stelem_I1,
				Sub_Ovf_Un,
				Ldtoken,
				Ldelem_I1,
				Refanyval,
				Stind_I1,
				Div_Un,
				Ldarg,
				Newanon,
				Ldvirtftn2,
				Conv_Ovf_U2,
				Conv_Ovf_I1_Un,
				Br,
				Mkrefany,
				Ldstr,
				Newarr,
				Ldtype,
				Conv_Ovf_I1,
				Conv_R4,
				Ldc_I4,
				Stelem_I4,
				Not,
				Conv_U2,
				Rem_Un,
				Ldelem_U2,
				Conv_I4,
				Stobj,
				Brtrue,
				Conv_U4,
				Call,
				Rem,
				Castclass,
				Conv_Ovf_I8,
				Conv_I8,
				Add,
				Ldloc,
				Conv_Ovf_U8_Un,
				CallExtern,
				Conv_U1,
				Conv_Ovf_U1,
				StackSpace,
				Stsfld,
				Shl,
				Ldelem_R8,
				Ldelem_I8,
				Ldind_U4,
				Break,
				Sub_Ovf,
				Conv_R8,
				Mul_Ovf,
				Brfalse,
				Conv_Ovf_I2,
				Jmp,
				Conv_U,
				Ldelem_U1,
				Div,
				Ldsfld,
				Initobj,
				Stelem_I8,
				Newobj,
				Stelem_Any,
				Conv_I2,
				Pop,
				Unbox_Any,
				Cpobj,
				Stind_R8,
				Ldind_I,
				Conv_I1,
				Cgt_Un,
				Stelem_Ref,
				Stind_I2,
				Mul_Ovf_Un,
				Ldind_R4,
				Conv_Ovf_U,
				Ldelem_I,
				Stind_I4,
				Nop,
				Throw,
				Ldobj,
				Endfinally,
				Ldvirtftn,
				Ldsflda,
				Callvirtvirt,
				Stind_Ref,
				Ldnull,
				Stind_I8,
				Ldelem_U4,
				Conv_Ovf_U8,
				Ldc_I8,
				Stind_I,
				Unbox,
				Beq,
				Switch,
				Callvirt,
				Constrained,
				Refanytype,
				Ldind_U1,
				Starg,
				Sub,
				Ldflda,
				Ldind_I1,
				Rethrow,
				Ldind_U2,
				Ldind_R8,
				Ldelem_Any,
				Leave,
				Endfilter,
				Conv_Ovf_U_Un,
				Conv_Ovf_I8_Un,
				Stelem_R4,
				Stloc,
				Stfld,
				Ceq,
				Box,
				Ldind_I2,
				Shr,
				Arglist,
				Conv_R_Un,
				Stelem_I2,

				Ldind_Ref,
				Ldc_R8,
				Ldelem_I4,
				Conv_Ovf_U2_Un,
				Stind_R4,
				Ldlen,
				Ldloca,
				Ldelem_I2,
				
				//Pseudo instruction
				Isinst,
				
				CallStaticR_I4_I4_I4_Extern,
				Add1_Loc,
				Add_I4,
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct Instruction
    {
        /// <summary>
        /// 指令MAGIC
        /// </summary>
        public const ulong INSTRUCTION_FORMAT_MAGIC = 1719456845587952638UL;

        /// <summary>
        /// 当前指令
        /// </summary>
        public Code Code;

        /// <summary>
        /// 操作数
        /// </summary>
        public int Operand;
    }

    public delegate int CallR_I4_I4_I4(int arg1, int arg2);
    
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