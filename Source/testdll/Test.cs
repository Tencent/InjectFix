using IFix;
using System;

namespace testdll
{
    public class Test
    {
        public int Add(int a,int b)
        {
            return a + b;
        }

        [Patch]
        public static int Min(int a, int b)
        {
            return Math.Min(a, b);
        }
    }
}
