using IFix;
using System;

namespace testdll
{
    public class Test
    {
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
    }
}
