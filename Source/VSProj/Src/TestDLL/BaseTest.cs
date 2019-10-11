/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace IFix.Test
{
    public delegate int TestDelegate(int a, out float b, ref long c, Action cb);

    public interface TestInterface
    {
        int Foo();
        void Bar(double a);
    }

    [CustomBridge]
    public static class AdditionalBridge
    {
        static List<Type> bridge = new List<Type>()
        {
            typeof(TestDelegate),
            typeof(IEnumerator<long>),
            typeof(IEnumerable<double>),
            typeof(TestInterface),
            typeof(Action<int, float, double, char>),
        };
    }

    public class RefTypeCounter
    {
        int i = 0;

        public void Inc()
        {
            i++;
        }

        public int Val
        {
            get
            {
                return i;
            }
        }
    }

    public struct ValueTypeCounter
    {
        public int i;

        public ValueTypeCounter(int v)
        {
            i = v;
        }

        public void Inc()
        {
            i++;
        }

        public int Val
        {
            get
            {
                return i;
            }
        }

        public override string ToString()
        {
            return "ValueTypeCounter { " + i + " }";
        }

        public int CompareTo(object o)
        {
            if (!(o is ValueTypeCounter))
            {
                throw new ArgumentException();
            }

            int v = ((ValueTypeCounter)o).i;
            if (i > v)
            {
                return 1;
            }
            if (i < v)
            {
                return -1;
            }
            return 0;
        }
    }

    public struct W1
    {
        public ValueTypeCounter F;
    }

    public struct W2
    {
        public W1 F;
    }

    public struct W3
    {
        public W2 F;
    }

    public class ValueTypeCounterContainer
    {
        public ValueTypeCounter c;

        public void Init(int a)
        {
            c = new ValueTypeCounter();
            c.i = a;
        }
    }

    public class GenericClass<T>
    {
        public string F<T1, T2>(T1 a)
        {
            return "1";
        }

        public string F<T1, T2>(T1 a, T b)
        {
            return "2";
        }

        public string F<T>(T a)
        {
            return "3";
        }

        public string F<T1>(T a)
        {
            return "4";
        }

        public string F<T1>(List<T1> a)
        {
            return "5";
        }

        public string F(List<T> a)
        {
            return "6";
        }

        //TODO: 泛型+引用及数组

        //TODO: 泛型实参同时含函数泛型参数及类泛型参数

        //TODO: 由于目前泛型函数不解析执行，所以泛型实参在泛型函数间传递不用考虑，但后续如果要支持泛型函数的解析执行的话，要加入这点的考虑

        //public void F<T4>(List<T4> a)
        //{
        //
        //}

        public string F(T a)
        {
            return "7";
        }

        public string F()
        {
            return "8";
        }

        public class InnerClass
        {
            public string F(T a)
            {
                return "9";
            }
        }

        public string F(T[] a)
        {
            return "0";
        }

        public string F<T1>(T1[] a)
        {
            return "a";
        }

        public string F(T[,] a)
        {
            return "b";
        }

        public string F<T1>(T1[,] a)
        {
            return "c";
        }

        public string F(ref T a)
        {
            return "d";
        }

        public string F<T1>(ref T1 a)
        {
            return "e";
        }

        public string F(ref T[] a)
        {
            return "f";
        }

        public string F<T1>(ref T1[] a)
        {
            return "g";
        }
    }

    public class AnonymousClass
    {
        public void Repeat(Action action, int n)
        {
            for (int i = 0; i < n; i++)
            {
                action();
            }
        }

        protected int f = 0;

        static int sf = 0;

        public void CallRepeat(int n, out int local, out int field, out int staticField)
        {
            int i = 0;
            Repeat(() =>
            {
                i++;
                f++;
                sf++;
            }, n);
            local = i;
            field = f;
            staticField = sf;
        }

        void fadd2()
        {
            f += 2;
            sf += 2;
        }

        public void CallRepeat(int n, out int field, out int staticField)
        {
            Repeat(fadd2, n);
            field = f;
            staticField = sf;
        }

        public void Lessthan(List<int> list, int upper)
        {
            list.RemoveAll(x => x > upper);
        }

        public void Lessthan5(List<int> list)
        {
            list.RemoveAll(x => x > 5);
        }

        public void LessthanField(List<int> list)
        {
            list.RemoveAll(x => x > f);
        }

        public virtual void FAdd()
        {
            f += 3;
        }

        public void CallRepeat(int n, out int field)
        {
            Repeat(FAdd, n);
            field = f;
        }

        //public Expression<Func<int, bool>> GenExpression()
        //{
        //    Expression<Func<int, bool>> exprTree = num => num < 5;
        //    return exprTree;
        //}

        public System.Collections.IEnumerator Generator()
        {
            yield return 1;
            for (int i = 1; i < 10; i++)
            {
                yield return i;
            }
            yield return f;
        }

        public IEnumerator<int> Generator(int n)
        {
            yield return 1;
            for (int i = 1; i < n; i++)
            {
                yield return i;
            }
            yield return f;
        }

        public System.Collections.IEnumerable GetEnumerable()
        {
            yield return 1;
            for (int i = 1; i < 10; i++)
            {
                yield return i;
            }
            yield return f;
        }
    }

    public class BaseClass
    {
        public virtual int Foo()
        {
            Console.WriteLine("BaseClass.Foo");
            return 0;
        }
    }

    public interface ItfWithRefParam
    {
        int WithRefParam(ref int a, out int b);
    }

    public class DrivenClass : BaseClass, ItfWithRefParam
    {
        public override int Foo()
        {
            Console.WriteLine("DrivenClass.Foo");
            return 1;
        }

        public int WithRefParam(ref int a, out int b)
        {
            a *= 2;
            b = a + 1;
            return a;
        }
    }

    public interface Calc
    {
        int Add(int a, int b);
        int Scale { get; set; }
    }

    public class SimpleCalc : Calc
    {
        public SimpleCalc()
        {
            Scale = 1;
        }

        public int Add(int a, int b)
        {
            return (a + b) * Scale;
        }

        public int Scale { get; set; }
    }

    public static class BaseTest
    {
        static int add(int a, int b)
        {
            return a + b;
        }

        public static long Base(int loop)
        {
            //System.Console.WriteLine(loop);
            long sum = 0;
            for (int i = 0; i < 3; i++)
            {
                sum += add(i, i + 1);
                switch(loop)
                {
                    case 0:
                        sum += 1;
                        break;
                    case 1:
                        sum += 2;
                        break;
                    case 2:
                    case 3:
                    case 4:
                    case 5:
                    case 6:
                    //case 7:
                        sum -= 1;
                        break;
                    default:
                        sum -= 2;
                        break;
                }
            }
            return sum;
        }

        public static long Ref(ref int a, int b, ref long c, long d, out int e)
        {
            a += b;
            c += d;
            e = a + b;
            return a + b + c + d + e;
        }

        static void Swap(ref object l, ref object r)
        {
            object t = l;
            l = r;
            r = t;
        }

        public static void Ref(ref object l, ref object r)
        {
            Swap(ref l, ref r);
        }

        public static void Ref(ref ValueTypeCounter l, ref ValueTypeCounter r)
        {
            ValueTypeCounter t = l;
            l = r;
            r = t;
        }

        //1、leave的目标不一定紧跟finally block，可以是任何地方
        //2、leave要找到最内层的finally跳转
        //3、endfinally有两种情况，如果是leave跳过来，则跳到leave的目标，如果不是，则重新抛异常
        //4、为了减少注入代码注入侧不try-catch，由解析器清栈，包括引用参数，所以要传入引用参数的个数
        //5、正常leave不应该有查找finally的操作，否则非常慢
        //6、一个finally block里头的leave指令，可以有不同的目标地址，比如：try{}catch{goto}
        //7、leave如果在一个finally block内，而目标地址在finally的try block之外，那么这个finally block在跳转前执行，
        //   如果有多个这样的finally，则从内到外执行（抛异常其实也可以理解为跳出了finally）
        public static void ExceptionBase(ref int p)
        {
            while (p != 100)
            {
                try
                {
                    //Console.WriteLine("in while");
                    if (p < 0)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        p--;
                        break;
                    }
                }
                finally
                {
                    p--;
                }
            }
            try
            {
                try
                {
                    if (p < 0)
                    {
                        throw new InvalidOperationException();
                    }
                    p++;
                }
                finally
                {
                    p += 2;
                }
            }
            finally
            {
                p += 2;
            }
        }

        public static void Rethrow()
        {
            try
            {
                throw new InvalidOperationException();
            }
            catch(InvalidOperationException e)
            {
                throw;
            }
        }

        public static void LeavePoint(int p, ref int n1, ref int f1, ref int f2)
        {
            L1:
            if (p == -1)
            {
                goto L3;
            }
            try
            {
                try
                {
                    if (p == 0)
                    {
                        p = -1;
                        goto L1;
                    }
                    else if(p == 1)
                    {
                        goto L2;
                    }
                    else if (p == 2)
                    {
                        goto L3;
                    }
                }
                finally
                {
                    f1++;
                }
                L2:
                n1++;
            }
            finally
            {
                f2++;
            }
            L3:
            return;
        }

        public static void TryCatchFinally(bool bThrow, ref bool t, ref bool c, ref bool f, ref bool e)
        {
            try
            {
                if (bThrow)
                {
                    throw new Exception();
                }
                Console.WriteLine("t");
                t = true;
            }
            catch
            {
                Console.WriteLine("c");
                c = true;
            }
            finally
            {
                Console.WriteLine("f");
                f = true;
            }
            Console.WriteLine("e");
            e = true;
        }

        public static void CatchByNextLevel(out bool f1, out bool f2, out bool f3)
        {
            f1 = f2 = f3 = false;
            try
            {
                try
                {
                    throw new Exception();
                }
                finally
                {
                    f1 = true;
                }
            }
            catch
            {
                f2 = true;
            }
            finally
            {
                f3 = true;
            }
        }

        static void CallInc(ValueTypeCounter counter)
        {
            counter.Inc();
        }

        static void CallInc(RefTypeCounter counter)
        {
            counter.Inc();
        }

        public static void PassByValue(ref ValueTypeCounter c1, RefTypeCounter c2)
        {
            c1.Inc();
            c2.Inc();
            CallInc(c1);
            CallInc(c2);
        }

        public static void VirtualFunc(out int r1, out int r2)
        {
            BaseClass o1 = new BaseClass();
            BaseClass o2 = new DrivenClass();
            r1 = o1.Foo();
            r2 = o2.Foo();
        }

        public static int InterfaceTest(int a, int b, int scale)
        {
            Calc calc = new SimpleCalc();
            calc.Scale = scale;
            return calc.Add(a, b);
        }

        public static int ItfWithRefParam(ref int a, out int b)
        {
            ItfWithRefParam o = new DrivenClass();
            return o.WithRefParam(ref a, out b);
        }

        public static string VirtualFuncOfStruct(ValueTypeCounter vtc)
        {
            return vtc.ToString() + ",hashcode:" + vtc.GetHashCode();
        }

        public static Type GetIntType()
        {
            return typeof(int);
        }

        public static string GenericOverload()
        {
            GenericClass<int> a = new GenericClass<int>();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(a.F());
            sb.Append(a.F(1));
            sb.Append(a.F<float>(1));
            sb.Append(a.F<float>(new List<float>()));
            sb.Append(a.F<int, double>(1));
            sb.Append(a.F(new List<int>()));
            sb.Append(a.F<int, double>(1, 1));
            GenericClass<int>.InnerClass b = new GenericClass<int>.InnerClass();
            sb.Append(b.F(1));
            sb.Append(a.F<byte>((byte)1));
            int[] p1 = null;
            sb.Append(a.F(p1));
            float[] p2 = null;
            sb.Append(a.F(p2));
            int[,] p3 = null;
            sb.Append(a.F(p3));
            float[,] p4 = null;
            sb.Append(a.F(p4));
            int p5 = 1;
            byte p6 = 1;
            sb.Append(a.F(ref p5));
            sb.Append(a.F(ref p6));
            sb.Append(a.F(ref p1));
            sb.Append(a.F(ref p2));
            return sb.ToString();
        }

        public static int SField = 1;

        public static int StaticFieldBase()
        {
            return SField++;
        }

        public static byte[] Conv_Ovf_I(long l)
        {
            return new byte[l];
        }

        public static int Conv_I4(float i)
        {
            return (int)i;
        }

        public static int Conv_I4(double i)
        {
            return (int)i;
        }

        public static int Conv_I4(long i)
        {
            return (int)i;
        }

        public static int Conv_Ovf_I4_Un(uint i)
        {
            return checked((int)i);
        }

        public static int Conv_Ovf_I4(long i)
        {
            return checked((int)i);
        }

        public static int Ldlen(int[] arr)
        {
            return arr.Length;
        }

        public static int[] Newarr(int len)
        {
            return new int[len];
        }

        public static RefTypeCounter Castclass(object o)
        {
            return (RefTypeCounter)o;
        }

        public static RefTypeCounter Isinst(object o)
        {
            return o as RefTypeCounter;
        }

        public static object ArrayGet(object[] arr, int idx)
        {
            return arr[idx];
        }

        public static void ArraySet(object[] arr, int idx, object val)
        {
            arr[idx] = val;
        }

        public static void ArraySet(object[] arr, int idx)
        {
            arr[idx] = 1;
        }

        public static bool ArrayGet(bool[] arr, int idx)
        {
            return arr[idx];
        }

        public static void ArraySet(bool[] arr, int idx, bool val)
        {
            arr[idx] = val;
        }

        public static byte ArrayGet(byte[] arr, int idx)
        {
            return arr[idx];
        }

        public static void ArraySet(byte[] arr, int idx, byte val)
        {
            arr[idx] = val;
        }

        public static sbyte ArrayGet(sbyte[] arr, int idx)
        {
            return arr[idx];
        }

        public static void ArraySet(sbyte[] arr, int idx, sbyte val)
        {
            arr[idx] = val;
        }

        public static int ArrayGet(int[] arr, int idx)
        {
            return arr[idx];
        }

        public static void ArraySet(int[] arr, int idx, int val)
        {
            arr[idx] = val;
        }

        public static uint ArrayGet(uint[] arr, int idx)
        {
            return arr[idx];
        }

        public static void ArraySet(uint[] arr, int idx, uint val)
        {
            arr[idx] = val;
        }

        public static float ArrayGet(float[] arr, int idx)
        {
            return arr[idx];
        }

        public static void ArraySet(float[] arr, int idx, float val)
        {
            arr[idx] = val;
        }

        public static double ArrayGet(double[] arr, int idx)
        {
            return arr[idx];
        }

        public static void ArraySet(double[] arr, int idx, double val)
        {
            arr[idx] = val;
        }

        public static char ArrayGet(char[] arr, int idx)
        {
            return arr[idx];
        }

        public static void ArraySet(char[] arr, int idx, char val)
        {
            arr[idx] = val;
        }

        public static short ArrayGet(short[] arr, int idx)
        {
            return arr[idx];
        }

        public static void ArraySet(short[] arr, int idx, short val)
        {
            arr[idx] = val;
        }

        public static ushort ArrayGet(ushort[] arr, int idx)
        {
            return arr[idx];
        }

        public static void ArraySet(ushort[] arr, int idx, ushort val)
        {
            arr[idx] = val;
        }

        public static IntPtr ArrayGet(IntPtr[] arr, int idx)
        {
            return arr[idx];
        }

        public static void ArraySet(IntPtr[] arr, int idx, IntPtr val)
        {
            arr[idx] = val;
        }

        public static UIntPtr ArrayGet(UIntPtr[] arr, int idx)
        {
            return arr[idx];
        }

        public static void ArraySet(UIntPtr[] arr, int idx, UIntPtr val)
        {
            arr[idx] = val;
        }

        public static int ArrayGet(int[] arr, uint idx)
        {
            return arr[idx];
        }

        public static void ArraySet(int[] arr, uint idx, int val)
        {
            arr[idx] = val;
        }

        public static int And(int a, int b)
        {
            return a & b;
        }

        public static long And(long a, long b)
        {
            return a & b;
        }

        public static int Or(int a, int b)
        {
            return a | b;
        }

        public static long Or(long a, long b)
        {
            return a | b;
        }

        static void intref(ref int a)
        {
            a += 10;
        }

        public static int Ldflda(ref ValueTypeCounter c)
        {
            intref(ref c.i);
            return c.i;
        }

        public static int Ldflda(ref ValueTypeCounterContainer cc)
        {
            return Ldflda(ref cc.c);
        }

        public static int Ldflda(ref W1 p)
        {
            return Ldflda(ref p.F);
        }

        public static int Ldflda(ref W2 p)
        {
            return Ldflda(ref p.F);
        }

        public static int Ldflda(ref W3 p)
        {
            return Ldflda(ref p.F);
        }

        public static int Ldflda_m(ref W3 p)
        {
            return p.F.F.F.i;
        }

        public static bool Ceq(int a, int b)
        {
            return a == b;
        }

        public static bool Ceq(double a, double b)
        {
            return a == b;
        }

        public static int Shl(int a, int bits)
        {
            return a << bits;
        }

        public static long Shl(long a, int bits)
        {
            return a << bits;
        }

        public static int Shr(int a, int bits)
        {
            return a >> bits;
        }

        public static long Shr(long a, int bits)
        {
            return a >> bits;
        }

        public static uint Shr_Un(uint a, int bits)
        {
            return a >> bits;
        }

        public static ulong Shr_Un(ulong a, int bits)
        {
            return a >> bits;
        }

        public static byte Conv_U1(int a)
        {
            return (byte)a;
        }

        public static byte Conv_Ovf_U1(int a)
        {
            return checked((byte)a);
        }

        public static byte Conv_Ovf_U1_Un(uint a)
        {
            return checked((byte)a);
        }

        public static void Ldelema(int[] arr, int idx)
        {
            intref(ref arr[idx]);
        }

        public static int Bgt(int a, int b)
        {
            if (a > b)
            {
                goto R1;
            }
            else if (a < b)
            {
                goto RM1;
            }
            else
            {
                goto R0;
            }
            R1:
            return 1;
            RM1:
            return -1;
            R0:
            return 0;
        }

        public static int Initobj(int a)
        {
            ValueTypeCounter c1 = new ValueTypeCounter();
            c1.Inc();
            ValueTypeCounter c2 = new ValueTypeCounter(a);
            ValueTypeCounterContainer cc = new ValueTypeCounterContainer();
            cc.Init(a);
            return c1.Val + c2.Val + cc.c.Val;
        }

        public static int si = 0;

        public static int Ldsflda()
        {
            intref(ref si);
            return si;
        }

        public static int Div(int a, int b)
        {
            return a / b;
        }

        public static float Div(float a, float b)
        {
            return a / b;
        }

        public static double Div(double a, double b)
        {
            return a / b;
        }

        public static long Div(long a, long b)
        {
            return a / b;
        }

        public static bool Cgt_Un(uint a, uint b)
        {
            return a > b;
        }

        public static bool NaNFloat(int op, float a, float b)
        {
            switch(op)
            {
                case 0:
                    return a < b;//clt
                case 1:
                    return a > b;//cgt
                case 2:
                    return a <= b;//cgt.un + ceq 0
                case 3:
                    return a >= b;//clt.un + ceq 0
                //case 5:
                //    return a == b;//ceq
                //case 6:
                //    return a != b;//ceq + ceq
            }
            throw new ArgumentException();
        }

        public static int Rem(int a, int b)
        {
            return a % b;
        }

        public static float Rem(float a, float b)
        {
            return a % b;
        }

        public static uint Rem(uint a, uint b)
        {
            return a % b;
        }

        public static double Ldc_R8()
        {
            return 3.1415;
        }

        public static double Ldc_I8()
        {
            return 9223372036854775808;
        }

        public static ulong Conv_U8(float a)
        {
            return (ulong)a;
        }

        public static long Conv_I8(float a)
        {
            return (long)a;
        }

        public static ulong Conv_Ovf_U8(long a)
        {
            return checked((ulong)a);
        }
        public static ulong Conv_Ovf_U8(float a)
        {
            return checked((ulong)a);
        }

        public static long Conv_Ovf_I8(ulong a)
        {
            return checked((long)a);
        }
        public static long Conv_Ovf_I8(float a)
        {
            return checked((long)a);
        }
        public static int Xor(int a, int b)
        {
            return a ^ b;
        }
        public static long Xor(long a, long b)
        {
            return a ^ b;
        }

        public static double Conv_R_Un(uint a)
        {
            return a;
        }

        public static float Conv_R_Un(ulong a)
        {
            return a;
        }

        public static int Mul_Ovf(int a, int b)
        {
            return checked(a * b);
        }

        public static uint Mul_Ovf_Un(uint a, uint b)
        {
            return checked(a * b);
        }

        public static int Add_Ovf(int a, int b)
        {
            return checked(a + b);
        }

        public static uint Add_Ovf_Un(uint a, uint b)
        {
            return checked(a + b);
        }

        public static uint Div_Un(uint a, uint b)
        {
            return a / b;
        }

        public static int Neg(int a)
        {
            return -a;
        }

        public static long Neg(long a)
        {
            return -a;
        }

        public static float Neg(float a)
        {
            return -a;
        }

        public static double Neg(double a)
        {
            return -a;
        }

        public static int Not(int a)
        {
            return ~a;
        }

        public static long Not(long a)
        {
            return ~a;
        }

        public static int Blt_Un(float a, float b)
        {
            if (a >= b)
            {
                return 11;
            }
            else
            {
                return 22;
            }
        }

        public static int Bgt_Un(float a, float b)
        {
            if (a <= b)
            {
                return 11;
            }
            else
            {
                return 22;
            }
        }

        /*public interface Itf
        {
            void Foo();
        }

        public struct MyStrunct : Itf
        {
            public int A;

            public void Foo()
            {
                Console.WriteLine("MyStrunct.Foo:" + A--);
            }

            public override string ToString() { return ""; }
        }

        class MyClass : Itf
        {
            public void Foo()
            {
                Console.WriteLine("MyClass.Foo");
            }
        }

        static void CallFoo(Itf itf)
        {
            itf.Foo();
        }

        public static void ConstrainedInstruction(bool a, int b)
        {
            MyClass e = new MyClass();
            e.ToString();
            DateTime dt = new DateTime();
            dt.ToString();
            MyStrunct ms = new MyStrunct() { A = 10 };
            ms.ToString();
            Console.WriteLine("a:" + a);
            Console.WriteLine("b:" + b);
            a.ToString();
            b.ToString();
            object[] args = new object[0];
            var m = typeof(Itf).GetMethod("Foo");
            m.Invoke(e, args);

            ms.Foo();
            Console.WriteLine("ms.A:" + ms.A);
            m.Invoke(ms, args);
            Console.WriteLine("ms.A:" + ms.A);
            CallFoo(ms);
            Console.WriteLine("ms.A:" + ms.A);

            object o = ms;
            Console.WriteLine("before o.A:" + ((MyStrunct)o).A);
            m.Invoke(o, args);
            Console.WriteLine("after o.A:" + ((MyStrunct)o).A);

            Itf itf = ms;
            itf.Foo();
        }*/

        /*public static void ExceptionBase(int p)
        {
            BEGIN:

            try
            {
                try
                {
                    if (p > 5)
                    {
                        Console.WriteLine("p=" + p);
                        p--;
                        throw new InvalidOperationException();
                    }
                    Console.WriteLine("leave1...");
                }
                catch (InvalidOperationException)
                {
                    goto BEGIN;
                }
                catch (InvalidProgramException)
                {

                }
                finally
                {
                    Console.WriteLine("finally1");
                    p--;
                }
                Console.WriteLine("leave2...");
            }
            finally
            {
                Console.WriteLine("finally2");
            }
        }*/

        /*public static void ExceptionBase(int p)
        {
            try
            {
                if (p == 1)
                {
                    throw new InvalidCastException();
                }
                else if (p == 2)
                {
                    throw new InvalidOperationException();
                }
                else if (p == 3)
                {
                    throw new InvalidProgramException();
                }
                else
                {
                    throw new Exception();
                }
            }
            catch(InvalidCastException e)
            {
                Console.WriteLine(e.StackTrace);
            }
            catch(InvalidOperationException e)
            {
                Console.WriteLine(e.StackTrace);
            }
            catch(InvalidProgramException e)
            {

            }
            finally
            {
                Console.WriteLine(1);
            }
        }*/
    }
}