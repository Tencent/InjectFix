using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            foreach(var cls in typeof(testdll.BB).Assembly.GetTypes())
            {
                if(cls.Namespace == "testdll")
                {
                    Print(cls);
                }
                
            }
        }

        static void Print(Type t)
        {
            Console.WriteLine($"====> {t.Name}");
            foreach (var m in t.GetMethods())
            {
                Console.WriteLine($"{m.Name} {m.IsGenericMethod} {m.IsGenericMethodDefinition}");
            }
        }
    }
}
