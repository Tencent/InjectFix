using IFix;
using System;

namespace testdll
{
    public class Temp<T>where T:new()
    {
        public T obj;

        public T yy
        {
            get => obj;
        }

        public virtual T HH() {
            return obj;
        }
    }

    public class BB : Temp<int>
    {

        public override int HH()
        {
            return base.HH();
        }
        public int xx
        {
            get { return obj; }
        }
        public BB()
        {
            obj = 2;
        }

        public void FF()
        {
            Console.WriteLine(obj);
        }
    }

    public class Test
    {
        public  Test()
        {

        }

        private void hhh()
        {

        }

        //[Patch]
        public int Add(int a, int b)
        {
            return a + b + 1000;
        }

        //[Patch]
        public static int Min(int a, int b)
        {
            return Math.Min(a, b) + 1000;
        }

        public static T Def<T>()where T:new()
        {
            return new Temp<T>().obj;
        }

        public static void haha()
        {
            {
                var obj = new Temp<DateTime>();
            }
            {
                var obj = new Temp<Test>();
            }

        }
    }
}
