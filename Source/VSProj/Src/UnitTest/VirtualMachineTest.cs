/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using NUnit.Framework;
using IFix.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace IFix.Test
{
    [TestFixture]
    unsafe public class VirtualMachineTest
    {
        [OneTimeSetUpAttribute]
        public static void Init()
        {
            /*if (!Directory.Exists("../Data"))
            {
                Directory.CreateDirectory("../Data");
            }
            Process ilfix = new Process();
            ilfix.StartInfo.FileName = "../Bin/IFix.exe";
            ilfix.StartInfo.Arguments = "../Lib/IFix.TestDLL.dll ../Data/ ../Lib/IFix.Core.dll";
            ilfix.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            ilfix.StartInfo.RedirectStandardOutput = true;
            ilfix.StartInfo.UseShellExecute = false;
            ilfix.StartInfo.CreateNoWindow = true;
            ilfix.Start();
            ilfix.WaitForExit();*/
            //Console.WriteLine(typeof(int[]).MakeByRefType().AssemblyQualifiedName);
            using (FileStream fs = File.Open("../Data/IFix.TestDLL.Redirect.dif", FileMode.Open))
            {
                PatchManager.Load(fs);
            }
        }

        [Test]
        public void SimpleTest()
        {
            var virtualMachine = SimpleVirtualMachineBuilder.CreateVirtualMachine(1);
            Call call = Call.Begin();
            call.PushInt32(4);
            call.PushInt32(6);
            virtualMachine.Execute(0, ref call, 2);
            Call.End(ref call);
            Assert.AreEqual(10, call.GetInt32());
        }

        [Test]
        public void FileVMBuildBaseTest()
        {
            /*var virtualMachine = FileVirtualMachineBuilder.CreateVirtualMachine("../Data/IFix.TestDLL.dif");
            for (int i = 0; i < 10; i++)
            {
                Call call = Call.Begin();
                call.PushInteger(i);
                virtualMachine.Execute(1, ref call, 1);
                Call.End(ref call);
                Assert.AreEqual(BaseTest.Base(i), call.GetResultAsLong());
            }*/
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(BaseTest.Base(i), Redirect.BaseTest.Base(i));
            }
        }

        [Test]
        public void RefBase()
        {
            //几个典型基础值类型的引用类型测试
            int a1 = 2;
            long b1 = 5;
            int c1 = 1;
            long r1 = BaseTest.Ref(ref a1, a1, ref b1, b1, out c1);

            int a2 = 2;
            long b2 = 5;
            int c2 = 1;
            long r2 = Redirect.BaseTest.Ref(ref a2, a2, ref b2, b2, out c2);

            Assert.AreEqual(a1, a2);
            Assert.AreEqual(b1, b2);
            Assert.AreEqual(c1, c2);
            Assert.AreEqual(r1, r2);

            //对象的引用测试
            object o1 = new object();
            object o2 = new object();

            object p1 = o1;
            object p2 = o2;
            Redirect.BaseTest.Ref(ref p1, ref p2);
            Assert.True(ReferenceEquals(o1, p2));
            Assert.True(ReferenceEquals(o2, p1));

            //结构体的引用测试
            Redirect.ValueTypeCounter v1 = new Redirect.ValueTypeCounter(1);
            Redirect.ValueTypeCounter v2 = new Redirect.ValueTypeCounter(2);

            Redirect.BaseTest.Ref(ref v1, ref v2);
            Assert.AreEqual(1, v2.Val);
            Assert.AreEqual(2, v1.Val);

            for (int i = 0; i < VirtualMachine.MAX_EVALUATION_STACK_SIZE; i++)
            {
                int a = 2;
                long b = 5;
                int c = 1;
                Redirect.BaseTest.Ref(ref a, a, ref b, b, out c);
            }
        }

        [Test]
        public void ExceptionBase()
        {
            //BaseTest.ExceptionBase(1);
            //BaseTest.ExceptionBase(-1);
            //之所以要进行MAX_EVALUATION_STACK_SIZE次测试，是测试有漏清理栈对象
            for (int j = 2; j < VirtualMachine.MAX_EVALUATION_STACK_SIZE + 2; j++)
            {
                int tmp1 = j;
                BaseTest.ExceptionBase(ref tmp1);
                int tmp2 = j;
                //Console.WriteLine("before:" + tmp2);
                Redirect.BaseTest.ExceptionBase(ref tmp2);
                //Console.WriteLine("after:" + tmp2);
                Assert.AreEqual(tmp1, tmp2);
            }

            //基础异常流程测试
            int i1 = -1, i2 = -1;
            Assert.That(() => BaseTest.ExceptionBase(ref i1), Throws.ArgumentException);
            Assert.That(() => Redirect.BaseTest.ExceptionBase(ref i2), Throws.ArgumentException);
            //Console.WriteLine(i2);

            i1 = 0;
            i2 = 0;
            Assert.That(() => BaseTest.ExceptionBase(ref i1), Throws.InvalidOperationException);
            Assert.That(() => Redirect.BaseTest.ExceptionBase(ref i2), Throws.InvalidOperationException);
            //Console.WriteLine(i);

            //BaseTest.ExceptionBase(10);
            Assert.Throws<InvalidOperationException>(() => BaseTest.Rethrow());
            Assert.Throws<InvalidOperationException>(() => Redirect.BaseTest.Rethrow());
        }

        //各种异常跳出点的测试
        [Test]
        public void LeavePoint()
        {
            int a1 = 0, b1 = 0, c1 = 0, a2 = 0, b2 = 0, c2 = 0;

            BaseTest.LeavePoint(0, ref a1, ref b1, ref c1);
            Redirect.BaseTest.LeavePoint(0, ref a2, ref b2, ref c2);
            Assert.AreEqual(a1, a2);
            Assert.AreEqual(b1, b2);
            Assert.AreEqual(c1, c2);

            a1 = 0; b1 = 0; c1 = 0; a2 = 0; b2 = 0; c2 = 0;
            BaseTest.LeavePoint(1, ref a1, ref b1, ref c1);
            Redirect.BaseTest.LeavePoint(1, ref a2, ref b2, ref c2);
            Assert.AreEqual(a1, a2);
            Assert.AreEqual(b1, b2);
            Assert.AreEqual(c1, c2);

            a1 = 0; b1 = 0; c1 = 0; a2 = 0; b2 = 0; c2 = 0;
            BaseTest.LeavePoint(2, ref a1, ref b1, ref c1);
            Redirect.BaseTest.LeavePoint(2, ref a2, ref b2, ref c2);
            Assert.AreEqual(a1, a2);
            Assert.AreEqual(b1, b2);
            Assert.AreEqual(c1, c2);

            a1 = 0; b1 = 0; c1 = 0; a2 = 0; b2 = 0; c2 = 0;
            BaseTest.LeavePoint(3, ref a1, ref b1, ref c1);
            Redirect.BaseTest.LeavePoint(3, ref a2, ref b2, ref c2);
            Assert.AreEqual(a1, a2);
            Assert.AreEqual(b1, b2);
            Assert.AreEqual(c1, c2);
        }

        //finally逻辑的测试
        [Test]
        public void TryCatchFinally()
        {
            bool t1, t2;
            bool c1, c2;
            bool f1, f2;
            bool e1, e2;

            t1 = c1 = f1 = e1 = false;
            t2 = c2 = f2 = e2 = false;
            BaseTest.TryCatchFinally(false, ref t1, ref c1, ref f1, ref e1);
            Redirect.BaseTest.TryCatchFinally(false, ref t2, ref c2, ref f2, ref e2);
            Assert.AreEqual(t1, t2);
            Assert.AreEqual(c1, c2);
            Assert.AreEqual(f1, f2);
            Assert.AreEqual(e1, e2);

            t1 = c1 = f1 = e1 = false;
            t2 = c2 = f2 = e2 = false;
            BaseTest.TryCatchFinally(true, ref t1, ref c1, ref f1, ref e1);
            Redirect.BaseTest.TryCatchFinally(true, ref t2, ref c2, ref f2, ref e2);
            Assert.AreEqual(t1, t2);
            Assert.AreEqual(c1, c2);
            Assert.AreEqual(f1, f2);
            Assert.AreEqual(e1, e2);

            //BaseTest.ConstrainedInstruction(true, 1);
        }

        //try-catch嵌套测试
        [Test]
        public void CatchByNextLevel()
        {
            bool a1, a2, a3;
            bool b1, b2, b3;
            BaseTest.CatchByNextLevel(out a1, out a2, out a3);
            Redirect.BaseTest.CatchByNextLevel(out b1, out b2, out b3);
            Assert.AreEqual(a1, b1);
            Assert.AreEqual(a2, b2);
            Assert.AreEqual(a3, b3);
        }

        //public class ShallowCloneTest
        //{
        //    public int Foo;
        //    public long Bar;

            //    public ShallowCloneTest Clone()
            //    {
            //        return (ShallowCloneTest)base.MemberwiseClone();
            //    }
            //}

        //class基础测试
        [Test]
        public void ClassBase()
        {
            Redirect.RefTypeCounter rtc = new Redirect.RefTypeCounter();
            int c = rtc.Val;
            rtc.Inc();//TODO: 反射访问字段非常慢
            Assert.AreEqual(rtc.Val, c + 1);
            //var MemberwiseClone = typeof(object).GetMethod("MemberwiseClone",
            //    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            //object vt = new ValueTypeCounter();
            ////ShallowCloneTest t1 = new ShallowCloneTest() { Bar = 1, Foo = 2 };
            //Stopwatch sw = Stopwatch.StartNew();
            ////Console.Write("m:" + MemberwiseClone);
            //var objClone = new ObjectClone();
            //for (int i = 0; i < 1000000; ++i)
            //{
            //    //var cloned = t1.Clone();
            //    //MemberwiseClone.Invoke(t1, null);
            //    //MemberwiseClone.Invoke(vt, null);
            //    //var cloned = ObjectCloner.Clone(t1);
            //    var cloned = objClone.Clone(vt);
            //}
            //Console.WriteLine("Took {0:0.00}s", sw.Elapsed.TotalSeconds);
        }

        //结构体基础测试
        [Test]
        public void StructBase()
        {
            Redirect.ValueTypeCounter vtc = new Redirect.ValueTypeCounter();
            int c = vtc.Val;
            vtc.Inc();
            Assert.AreEqual(vtc.Val, c + 1);
        }

        //参数值传递测试
        [Test]
        public void PassByValue()
        {
            Redirect.ValueTypeCounter c1 = new Redirect.ValueTypeCounter();
            Redirect.RefTypeCounter c2 = new Redirect.RefTypeCounter();
            Redirect.BaseTest.PassByValue(ref c1, c2);
            //Console.WriteLine("c1.v:" + c1.Val + ",c2.v:" + c2.Val);
            Assert.AreEqual(2, c2.Val);
            Assert.AreEqual(1, c1.Val);
        }

        //虚函数测试
        [Test]
        public void VirtualFunc()
        {
            int r1, r2;
            Redirect.BaseTest.VirtualFunc(out r1, out r2);
            Assert.AreEqual(0, r1);
            Assert.AreEqual(1, r2);
            Redirect.BaseClass o1 = new Redirect.BaseClass();
            Redirect.BaseClass o2 = new Redirect.DrivenClass();
            Assert.AreEqual(0, o1.Foo());
            Assert.AreEqual(1, o2.Foo());
        }

        //接口测试
        [Test]
        public void InterfaceTest()
        {
            Assert.AreEqual(30, Redirect.BaseTest.InterfaceTest(1, 2, 10));
        }

        //结构体虚函数测试
        [Test]
        public void VirtualFuncOfStruct()
        {
            Redirect.ValueTypeCounter c1 = new Redirect.ValueTypeCounter();
            c1.Inc();
            c1.Inc();
            Assert.AreEqual("ValueTypeCounter { 2 }", c1.ToString());

            Assert.AreEqual(c1.ToString() + ",hashcode:" + c1.GetHashCode(), Redirect.BaseTest.VirtualFuncOfStruct(c1));
        }

        //带ref参数的interface测试
        [Test]
        public void ItfWithRefParam()
        {
            int a = 10;
            int b;
            int ret = Redirect.BaseTest.ItfWithRefParam(ref a, out b);
            //Console.WriteLine("a:" + a + ",b:" + b + ",ret:" + ret);
            Assert.AreEqual(20, a);
            Assert.AreEqual(21, b);
            Assert.AreEqual(20, ret);
        }

        //ldtoken指令的测试
        [Test]
        public void LdTokenBase()
        {
            Assert.AreEqual(typeof(int), Redirect.BaseTest.GetIntType());
        }

        //unbox指令测试
        [Test]
        public void UnboxBase()
        {
            Redirect.ValueTypeCounter c1 = new Redirect.ValueTypeCounter();
            Redirect.ValueTypeCounter c2 = new Redirect.ValueTypeCounter();
            c1.Inc();
            Assert.AreEqual(1, c1.CompareTo(c2));
            c2.Inc();
            Assert.AreEqual(0, c1.CompareTo(c2));
            c2.Inc();
            Assert.AreEqual(-1, c1.CompareTo(c2));

            Assert.That(() => c1.CompareTo(1), Throws.ArgumentException);
        }

        //泛型签名测试
        [Test]
        public void GenericOverload()
        {
            Assert.AreEqual(BaseTest.GenericOverload(), Redirect.BaseTest.GenericOverload());
            //Console.WriteLine(Redirect.BaseTest.GenericOverload());
        }

        //静态字段测试
        [Test]
        public void StaticFieldBase()
        {
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(BaseTest.StaticFieldBase(), Redirect.BaseTest.StaticFieldBase());
            }
        }

        //ConvI4指令
        [Test]
        public void ConvI4Base()
        {
            Assert.AreEqual(BaseTest.Conv_I4((float)uint.MaxValue),
                Redirect.BaseTest.Conv_I4((float)uint.MaxValue));
            Assert.AreEqual(BaseTest.Conv_I4((double)uint.MaxValue),
                Redirect.BaseTest.Conv_I4((double)uint.MaxValue));
            Assert.AreEqual(BaseTest.Conv_I4(long.MaxValue),
                Redirect.BaseTest.Conv_I4(long.MaxValue));
            Assert.Throws<OverflowException>(() => Redirect.BaseTest.Conv_Ovf_I4_Un(uint.MaxValue));
            Assert.Throws<OverflowException>(() => Redirect.BaseTest.Conv_Ovf_I4(long.MaxValue));
        }

        //LdLen指令
        [Test]
        public void LdLen()
        {
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(i, Redirect.BaseTest.Ldlen(new int[i]));
            }
        }

        //Newarr指令
        [Test]
        public void Newarr()
        {
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(i, Redirect.BaseTest.Newarr(i).Length);
            }
        }

        //Isinst，Castclass指令
        [Test]
        public void Cast()
        {
            Redirect.BaseClass bc = new Redirect.BaseClass();
            Assert.AreEqual(null, Redirect.BaseTest.Isinst(bc));
            Assert.Throws<InvalidCastException>(() => Redirect.BaseTest.Castclass(bc));
        }

        //数组测试
        [Test]
        public void Array()
        {
            object[] objArr = new object[2];
            Redirect.BaseTest.ArraySet(objArr, 0);
            var now = DateTime.Now;
            Redirect.BaseTest.ArraySet(objArr, 1, now);
            Assert.AreEqual(1, objArr[0]);
            Assert.AreEqual(now, objArr[1]);
            Assert.AreEqual(1, Redirect.BaseTest.ArrayGet(objArr, 0));
            Assert.AreEqual(now, Redirect.BaseTest.ArrayGet(objArr, 1));
            Assert.Throws<NullReferenceException>(() => Redirect.BaseTest.ArraySet(null, 1));
            Assert.Throws<IndexOutOfRangeException>(() => Redirect.BaseTest.ArraySet(objArr, -1));
            Assert.Throws<IndexOutOfRangeException>(() => Redirect.BaseTest.ArraySet(objArr, 2));
            byte[] byteArr = new byte[2] { 1, 2 };
            Assert.AreEqual(1, Redirect.BaseTest.ArrayGet(byteArr, 0));
            Assert.AreEqual(2, Redirect.BaseTest.ArrayGet(byteArr, 1));
            Redirect.BaseTest.ArraySet(byteArr, 0, 10);
            Assert.AreEqual(10, byteArr[0]);
            Assert.AreEqual(2, byteArr[1]);
            Redirect.BaseTest.ArraySet(byteArr, 1, 20);
            Assert.AreEqual(10, byteArr[0]);
            Assert.AreEqual(20, byteArr[1]);

            int[] intArr = new int[2] { 1, 2 };
            Assert.AreEqual(1, Redirect.BaseTest.ArrayGet(intArr, 0));
            Assert.AreEqual(2, Redirect.BaseTest.ArrayGet(intArr, 1));
            Redirect.BaseTest.ArraySet(intArr, 0, 10);
            Assert.AreEqual(10, intArr[0]);
            Assert.AreEqual(2, intArr[1]);
            Redirect.BaseTest.ArraySet(intArr, 1, 20);
            Assert.AreEqual(10, intArr[0]);
            Assert.AreEqual(20, intArr[1]);

            intArr = new int[2] { 1, 2 };
            Assert.AreEqual(1, Redirect.BaseTest.ArrayGet(intArr, (uint)0));
            Assert.AreEqual(2, Redirect.BaseTest.ArrayGet(intArr, (uint)1));
            Redirect.BaseTest.ArraySet(intArr, (uint)0, 10);
            Assert.AreEqual(10, intArr[0]);
            Assert.AreEqual(2, intArr[1]);

            uint[] uintArr = new uint[2] { 1, 2 };
            Assert.AreEqual(1, Redirect.BaseTest.ArrayGet(uintArr, 0));
            Assert.AreEqual(2, Redirect.BaseTest.ArrayGet(uintArr, 1));
            Redirect.BaseTest.ArraySet(uintArr, 0, 10);
            Assert.AreEqual(10, uintArr[0]);
            Assert.AreEqual(2, uintArr[1]);
            Redirect.BaseTest.ArraySet(uintArr, 1, 20);
            Assert.AreEqual(10, uintArr[0]);
            Assert.AreEqual(20, uintArr[1]);

            float[] floatArr = new float[2] { 1, 2 };
            Assert.AreEqual(1, Redirect.BaseTest.ArrayGet(floatArr, 0));
            Assert.AreEqual(2, Redirect.BaseTest.ArrayGet(floatArr, 1));
            Redirect.BaseTest.ArraySet(floatArr, 0, 10);
            Assert.AreEqual(10, floatArr[0]);
            Assert.AreEqual(2, floatArr[1]);
            Redirect.BaseTest.ArraySet(floatArr, 1, 20);
            Assert.AreEqual(10, floatArr[0]);
            Assert.AreEqual(20, floatArr[1]);

            double[] doubleArr = new double[2] { 1, 2 };
            Assert.AreEqual(1, Redirect.BaseTest.ArrayGet(doubleArr, 0));
            Assert.AreEqual(2, Redirect.BaseTest.ArrayGet(doubleArr, 1));
            Redirect.BaseTest.ArraySet(doubleArr, 0, 10);
            Assert.AreEqual(10, doubleArr[0]);
            Assert.AreEqual(2, doubleArr[1]);
            Redirect.BaseTest.ArraySet(doubleArr, 1, 20);
            Assert.AreEqual(10, doubleArr[0]);
            Assert.AreEqual(20, doubleArr[1]);

            short[] shortArr = new short[2] { 1, 2 };
            Assert.AreEqual(1, Redirect.BaseTest.ArrayGet(shortArr, 0));
            Assert.AreEqual(2, Redirect.BaseTest.ArrayGet(shortArr, 1));
            Redirect.BaseTest.ArraySet(shortArr, 0, 10);
            Assert.AreEqual(10, shortArr[0]);
            Assert.AreEqual(2, shortArr[1]);
            Redirect.BaseTest.ArraySet(shortArr, 1, 20);
            Assert.AreEqual(10, shortArr[0]);
            Assert.AreEqual(20, shortArr[1]);

            ushort[] ushortArr = new ushort[2] { 1, 2 };
            Assert.AreEqual(1, Redirect.BaseTest.ArrayGet(ushortArr, 0));
            Assert.AreEqual(2, Redirect.BaseTest.ArrayGet(ushortArr, 1));
            Redirect.BaseTest.ArraySet(ushortArr, 0, 10);
            Assert.AreEqual(10, ushortArr[0]);
            Assert.AreEqual(2, ushortArr[1]);
            Redirect.BaseTest.ArraySet(ushortArr, 1, 20);
            Assert.AreEqual(10, ushortArr[0]);
            Assert.AreEqual(20, ushortArr[1]);

            char[] charArr = new char[2] { 'a', 'b' };
            Assert.AreEqual('a', Redirect.BaseTest.ArrayGet(charArr, 0));
            Assert.AreEqual('b', Redirect.BaseTest.ArrayGet(charArr, 1));
            Redirect.BaseTest.ArraySet(charArr, 0, 'c');
            Assert.AreEqual('c', charArr[0]);
            Assert.AreEqual('b', charArr[1]);
            Redirect.BaseTest.ArraySet(charArr, 1, 'd');
            Assert.AreEqual('c', charArr[0]);
            Assert.AreEqual('d', charArr[1]);

            IntPtr[] intPtrArr = new IntPtr[] { new IntPtr(int.MaxValue), new IntPtr(int.MinValue) };
            Assert.AreEqual((long)int.MaxValue, Redirect.BaseTest.ArrayGet(intPtrArr, 0).ToInt64());
            Assert.AreEqual((long)int.MinValue, Redirect.BaseTest.ArrayGet(intPtrArr, 1).ToInt64());
            Redirect.BaseTest.ArraySet(intPtrArr, 0, new IntPtr(1));
            Assert.AreEqual((long)1, Redirect.BaseTest.ArrayGet(intPtrArr, 0).ToInt64());
            Assert.AreEqual((long)int.MinValue, Redirect.BaseTest.ArrayGet(intPtrArr, 1).ToInt64());
            Redirect.BaseTest.ArraySet(intPtrArr, 1, new IntPtr(2));
            Assert.AreEqual((long)1, Redirect.BaseTest.ArrayGet(intPtrArr, 0).ToInt64());
            Assert.AreEqual((long)2, Redirect.BaseTest.ArrayGet(intPtrArr, 1).ToInt64());

            UIntPtr[] uintPtrArr = new UIntPtr[] { new UIntPtr(int.MaxValue), new UIntPtr(0) };
            Assert.AreEqual((ulong)int.MaxValue, Redirect.BaseTest.ArrayGet(uintPtrArr, 0).ToUInt64());
            Assert.AreEqual((ulong)0, Redirect.BaseTest.ArrayGet(uintPtrArr, 1).ToUInt64());
            Redirect.BaseTest.ArraySet(uintPtrArr, 0, new UIntPtr(1));
            Assert.AreEqual((ulong)1, Redirect.BaseTest.ArrayGet(uintPtrArr, 0).ToUInt64());
            Assert.AreEqual((ulong)0, Redirect.BaseTest.ArrayGet(uintPtrArr, 1).ToUInt64());
            Redirect.BaseTest.ArraySet(uintPtrArr, 1, new UIntPtr(2));
            Assert.AreEqual((ulong)1, Redirect.BaseTest.ArrayGet(uintPtrArr, 0).ToUInt64());
            Assert.AreEqual((ulong)2, Redirect.BaseTest.ArrayGet(uintPtrArr, 1).ToUInt64());
        }

        //逻辑操作符
        [Test]
        public void LogicalOperator()
        {
            int a = 321312, b = 954932;
            Assert.AreEqual(a & b, Redirect.BaseTest.And(a, b));
            Assert.AreEqual(a | b, Redirect.BaseTest.Or(a, b));
            long c = 415661, d = 5415513;
            Assert.AreEqual(c & d, Redirect.BaseTest.And(c, d));
            Assert.AreEqual(c | d, Redirect.BaseTest.Or(c, d));
        }

        //Ldflda指令
        [Test]
        public void Ldflda()
        {
            Redirect.ValueTypeCounter c = new Redirect.ValueTypeCounter();
            Redirect.BaseTest.Ldflda(ref c);
            Assert.AreEqual(10, c.Val);
            Redirect.BaseTest.Ldflda(ref c);
            c.Inc();
            Assert.AreEqual(21, c.Val);

            c = new Redirect.ValueTypeCounter();
            Redirect.ValueTypeCounterContainer cc = new Redirect.ValueTypeCounterContainer();
            cc.c = c;
            Redirect.BaseTest.Ldflda(ref cc);
            Assert.AreEqual(10, cc.c.i);
            Redirect.BaseTest.Ldflda(ref cc);
            cc.c.Inc();
            Assert.AreEqual(21, cc.c.Val);

            Redirect.W1 w1 = new Redirect.W1()
            {
                F = new Redirect.ValueTypeCounter()
            };

            Redirect.W2 w2 = new Redirect.W2()
            {
                F = w1
            };

            Redirect.W3 w3 = new Redirect.W3()
            {
                F = w2
            };

            Redirect.BaseTest.Ldflda(ref w1);
            Assert.AreEqual(10, w1.F.i);

            Redirect.BaseTest.Ldflda(ref w2);
            Assert.AreEqual(10, w2.F.F.i);

            Redirect.BaseTest.Ldflda(ref w3);
            Assert.AreEqual(10, w3.F.F.F.i);

            Assert.AreEqual(10, Redirect.BaseTest.Ldflda_m(ref w3));
        }

        //Conv_Ovf_I指令
        [Test]
        public void Conv_Ovf_I()
        {
            int i = 10;
            Assert.AreEqual(i, Redirect.BaseTest.Conv_Ovf_I(i).Length);
        }

        //Ceq指令
        [Test]
        public void Ceq()
        {
            Assert.True(Redirect.BaseTest.Ceq(1, 1));
            Assert.False(Redirect.BaseTest.Ceq(321, 1));
            Assert.True(Redirect.BaseTest.Ceq((double)1, 1));
            Assert.False(Redirect.BaseTest.Ceq((double)321, 1));
        }

        //位操作符
        [Test]
        public void BitsOp()
        {
            int a = 321312;
            int bits = 5;
            long b = a;
            uint ua = uint.MaxValue;
            ulong ub = ulong.MaxValue;
            Assert.AreEqual(a << bits, Redirect.BaseTest.Shl(a, bits));
            Assert.AreEqual(b << bits, Redirect.BaseTest.Shl(b, bits));
            Assert.AreEqual(a >> bits, Redirect.BaseTest.Shr(a, bits));
            Assert.AreEqual(b >> bits, Redirect.BaseTest.Shr(b, bits));
            Assert.AreEqual(ua >> bits, Redirect.BaseTest.Shr_Un(ua, bits));
            Assert.AreEqual(ub >> bits, Redirect.BaseTest.Shr_Un(ub, bits));
            long c = 321421;
            Assert.AreEqual(a ^ bits, Redirect.BaseTest.Xor(a, bits));
            Assert.AreEqual(b ^ c, Redirect.BaseTest.Xor(b, c));

            Assert.AreEqual(~a, Redirect.BaseTest.Not(a));
            Assert.AreEqual(~b, Redirect.BaseTest.Not(b));
        }

        //Conv_U1指令
        [Test]
        public void Conv_U1()
        {
            int a = 1024;
            Assert.AreEqual(0, Redirect.BaseTest.Conv_U1(a));
            Assert.Throws<OverflowException>(() => Redirect.BaseTest.Conv_Ovf_U1(a));
            Assert.Throws<OverflowException>(() => Redirect.BaseTest.Conv_Ovf_U1_Un((uint)a));
        }

        [Test]
        public void Ldelema()
        {
            int[] arr = new int[] { 1, 2 };
            Redirect.BaseTest.Ldelema(arr, 0);
            Assert.AreEqual(11, arr[0]);
            Assert.AreEqual(2, arr[1]);
            Redirect.BaseTest.Ldelema(arr, 1);
            Assert.AreEqual(11, arr[0]);
            Assert.AreEqual(12, arr[1]);
        }

        [Test]
        public void Bgt()
        {
            Assert.AreEqual(1, Redirect.BaseTest.Bgt(3, 2));
            Assert.AreEqual(-1, Redirect.BaseTest.Bgt(2, 3));
            Assert.AreEqual(0, Redirect.BaseTest.Bgt(3, 3));
        }

        [Test]
        public void Ldsflda()
        {
            Assert.AreEqual(10, Redirect.BaseTest.Ldsflda());
            Assert.AreEqual(20, Redirect.BaseTest.Ldsflda());
        }

        [Test]
        public void Initobj()
        {
            Assert.AreEqual(BaseTest.Initobj(42), Redirect.BaseTest.Initobj(42));
        }

        //数学运算测试，checked关键字测试
        [Test]
        public void Arithmetic()
        {
            int a0 = 1, b0 = 2;
            long a1 = 324, b1 = 4314;
            float a2 = 321.41f, b2 = 31254.99f;
            double a3 = 321321.314312f, b3 = 3214321.31255;
            Assert.AreEqual(a0 / b0, Redirect.BaseTest.Div(a0, b0));
            Assert.AreEqual(a1 / b1, Redirect.BaseTest.Div(a1, b1));
            Assert.AreEqual(a2 / b2, Redirect.BaseTest.Div(a2, b2));
            Assert.AreEqual(a3 / b3, Redirect.BaseTest.Div(a3, b3));

            Assert.Throws<OverflowException>(() => Redirect.BaseTest.Mul_Ovf(int.MaxValue, 2));
            Assert.Throws<OverflowException>(() => Redirect.BaseTest.Mul_Ovf_Un(uint.MaxValue, 2));
            Assert.Throws<OverflowException>(() => Redirect.BaseTest.Add_Ovf(int.MaxValue, 1));
            Assert.Throws<OverflowException>(() => Redirect.BaseTest.Add_Ovf_Un(uint.MaxValue, 1));

            Assert.AreEqual(1 / uint.MaxValue, Redirect.BaseTest.Div_Un(1, uint.MaxValue));

            Assert.AreEqual(-a0, Redirect.BaseTest.Neg(a0));
            Assert.AreEqual(-a1, Redirect.BaseTest.Neg(a1));
            Assert.AreEqual(-a2, Redirect.BaseTest.Neg(a2));
            Assert.AreEqual(-a3, Redirect.BaseTest.Neg(a3));
        }

        //Nan运算
        [Test]
        public void NaNFloat()
        {
            for (int i = 0; i < 4; i++)
            {
                Console.WriteLine("nan nan " + i);
                Assert.AreEqual(BaseTest.NaNFloat(i, float.NaN, float.NaN),
                    Redirect.BaseTest.NaNFloat(i, float.NaN, float.NaN));
            }

            for (int i = 0; i < 4; i++)
            {
                Console.WriteLine("1 nan " + i);
                Assert.AreEqual(BaseTest.NaNFloat(i, 1, float.NaN), Redirect.BaseTest.NaNFloat(i, 1, float.NaN));
            }

            for (int i = 0; i < 4; i++)
            {
                Console.WriteLine("nan 1 " + i);
                Assert.AreEqual(BaseTest.NaNFloat(i, float.NaN, 1), Redirect.BaseTest.NaNFloat(i, float.NaN, 1));
            }
        }

        [Test]
        public void Rem()
        {
            Assert.AreEqual(BaseTest.Rem(32, 7), Redirect.BaseTest.Rem(32, 7));
            Assert.AreEqual(BaseTest.Rem(32.1f, 7), Redirect.BaseTest.Rem(32.1f, 7));

            Assert.AreEqual(BaseTest.Rem(uint.MaxValue, 7), Redirect.BaseTest.Rem(uint.MaxValue, 7));
        }

        [Test]
        public void Ldc_R8()
        {
            Assert.AreEqual(BaseTest.Ldc_R8(), Redirect.BaseTest.Ldc_R8());
        }

        [Test]
        public void Ldc_I8()
        {
            Assert.AreEqual(BaseTest.Ldc_I8(), Redirect.BaseTest.Ldc_I8());
        }

        //64位测试
        [Test]
        public void Int64()
        {
            float a = ulong.MaxValue;
            Assert.AreEqual(BaseTest.Conv_I8(a), Redirect.BaseTest.Conv_I8(a));
            Assert.AreEqual(BaseTest.Conv_U8(a), Redirect.BaseTest.Conv_U8(a));
            Assert.Throws<OverflowException>(()=>Redirect.BaseTest.Conv_Ovf_U8(-1));
            Assert.Throws<OverflowException>(() => Redirect.BaseTest.Conv_Ovf_U8(a * 2));
            Assert.Throws<OverflowException>(() => Redirect.BaseTest.Conv_Ovf_I8(ulong.MaxValue));
            Assert.Throws<OverflowException>(() => Redirect.BaseTest.Conv_Ovf_I8(a * 2));
        }

        class AnonymousClass2 : Redirect.AnonymousClass
        {
            public override void FAdd()
            {
                f += 5;
            }
        }

        int sum_of_enumerator(System.Collections.IEnumerator enumerator)
        {
            int sum = 0;
            while (enumerator.MoveNext())
            {
                object c = enumerator.Current;
                if (c is int)
                {
                    sum += (int)c;
                }
            }
            return sum;
        }

        int sum_of_enumerator(IEnumerator<int> enumerator)
        {
            int sum = 0;
            while (enumerator.MoveNext())
            {
                sum += enumerator.Current;
            }
            return sum;
        }

        [Test]
        public void Closure()
        {
            Redirect.AnonymousClass anony = new Redirect.AnonymousClass();
            int local = 0, field = 0, staticField = 0;
            anony.CallRepeat(10, out local, out field, out staticField);
            //Console.WriteLine("local:" + local + ",field:" + field + ",static field:" + staticField);
            Assert.AreEqual(10, local);
            Assert.AreEqual(10, field);
            Assert.AreEqual(10, staticField);
            anony.CallRepeat(6, out local, out field, out staticField);
            Assert.AreEqual(6, local);
            Assert.AreEqual(16, field);
            Assert.AreEqual(16, staticField);

            anony.CallRepeat(2, out field, out staticField);
            Assert.AreEqual(20, field);
            Assert.AreEqual(20, staticField);

            anony.CallRepeat(1, out field);
            Assert.AreEqual(23, field);

            List<int> list = new List<int> { 43, 5, 7, 8, 9, 2, 200 };
            anony.Lessthan(list, 40);
            Assert.AreEqual(5, list.Count);
            anony.Lessthan(list, 5);
            Assert.AreEqual(2, list.Count);
            anony.Lessthan(list, 1);
            Assert.AreEqual(0, list.Count);

            List<int> list2 = new List<int> { 43, 5, 7, 8, 9, 2, 200 };
            anony.LessthanField(list2);
            Assert.AreEqual(5, list2.Count);
            anony.Lessthan5(list2);
            Assert.AreEqual(2, list2.Count);

            AnonymousClass2 anony2 = new AnonymousClass2();
            anony2.CallRepeat(3, out field);
            Assert.AreEqual(15, field);

            Redirect.AnonymousClass a = new Redirect.AnonymousClass();
            AnonymousClass b = new AnonymousClass();
            Assert.AreEqual(sum_of_enumerator(a.Generator()), sum_of_enumerator(b.Generator()));
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual(sum_of_enumerator(a.Generator(i)), sum_of_enumerator(b.Generator(i)));
            }

            Assert.AreEqual(sum_of_enumerator(a.GetEnumerable().GetEnumerator()),
                sum_of_enumerator(b.GetEnumerable().GetEnumerator()));
        }

        [Test]
        public static void Conv_R_Un()
        {
            Assert.AreEqual(BaseTest.Conv_R_Un(uint.MaxValue), Redirect.BaseTest.Conv_R_Un(uint.MaxValue));
            Assert.AreEqual(BaseTest.Conv_R_Un(ulong.MaxValue), Redirect.BaseTest.Conv_R_Un(ulong.MaxValue));
        }

        [Test]
        public static void NaNFloatBranch()
        {
            Assert.AreEqual(BaseTest.Blt_Un(float.NaN, float.NaN), Redirect.BaseTest.Blt_Un(float.NaN, float.NaN));
            Assert.AreEqual(BaseTest.Blt_Un(1, float.NaN), Redirect.BaseTest.Blt_Un(1, float.NaN));
            Assert.AreEqual(BaseTest.Blt_Un(float.NaN, 1), Redirect.BaseTest.Blt_Un(float.NaN, 1));
            Assert.AreEqual(BaseTest.Bgt_Un(float.NaN, float.NaN), Redirect.BaseTest.Bgt_Un(float.NaN, float.NaN));
            Assert.AreEqual(BaseTest.Bgt_Un(1, float.NaN), Redirect.BaseTest.Bgt_Un(1, float.NaN));
            Assert.AreEqual(BaseTest.Bgt_Un(float.NaN, 1), Redirect.BaseTest.Bgt_Un(float.NaN, 1));
        }

        //TODO: Conv_U2 Ble_Un Conv_R8 Conv_R4 Bge_Un Conv_I2 Conv_Ovf_I2 Conv_Ovf_I2_Un
        //Conv_U4 Conv_Ovf_U4 Conv_Ovf_U4_Un
        //Conv_I1 Conv_Ovf_I1 Conv_Ovf_I1_Un
    }
}