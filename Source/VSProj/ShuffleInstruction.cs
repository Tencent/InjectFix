/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Collections;
using System.Text;

namespace IFix
{
    public static class Program
    {
        private static string ConfuseKey = string.Empty;

        static string[] Shuffle(string[] codes)
        {
            Random r = string.IsNullOrEmpty(ConfuseKey) ? 
                        new Random(DateTime.Now.Millisecond) : new Random(ConfuseKey.GetHashCode() + 1);
            
            for (int i = codes.Length - 1; i >= 0; i--)
            {
                int cardIndex = r.Next(i);
                string temp = codes[cardIndex];
                codes[cardIndex] = codes[i];
                codes[i] = temp;

            }

            return codes;
        }

        static void Main(string[] args)
        {
            var des = args[1];

            if (args.Length >= 3)
                ConfuseKey = args[2];

            //已经生成了就不重新生成了
            if (File.Exists(des))
            {
                Console.WriteLine(des + " existed");
                return;
            }

            using (var output = new StreamWriter(
                new FileStream(args[1], FileMode.Create, FileAccess.Write), Encoding.UTF8))
            {
                string fileContent = File.ReadAllText(args[0], Encoding.Default);
                //假定指令定义是这样的
                //public enum Code
                //{
                //    Code1,
                //    Code2,
                //    ...
                //    CodeN,
                //}
                Regex regex = new Regex(
                    "(?<head>.*public\\s+enum\\s+Code\\s+{[^\\n]*\\n)(?<code>[^,}]+,[^\\n]*\\n)+(?<tail>\\s+}.+)",
                    RegexOptions.ExplicitCapture | RegexOptions.Singleline);
                Match match = regex.Match(fileContent);

                var head = (match.Groups["head"].Captures[0] as Capture).Value;
                output.Write(head);

                //打乱指令定义
                var codes = Shuffle(match.Groups["code"].Captures.Cast<Capture>()
                    .Select(capture => capture.Value).ToArray());

                foreach (var code in codes)
                {
                    //output.WriteLine("// -----------------code---------------");
                    output.Write(code);
                }

                //生成随机的magic code
                Random r = string.IsNullOrEmpty(ConfuseKey) ?
                            new Random(DateTime.Now.Millisecond) : new Random(ConfuseKey.GetHashCode());
                ulong magic = (uint)r.Next();
                magic = (magic << 32);
                magic = magic | ((uint)r.Next());

                var tail = (match.Groups["tail"].Captures[0] as Capture).Value;
                output.Write(Regex.Replace(tail,
                    @"(public\s+const\s+ulong\s+INSTRUCTION_FORMAT_MAGIC\s*=)(\s*\d+)(\s*;)",
                        "$1 " + magic + "$3"));
            }

            Console.WriteLine(des + " gen");
        }
    }
}