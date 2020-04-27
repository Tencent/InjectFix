using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using IFix;
using System;

[Configure]
public class NewClassTestCfg {

	[IFix]
    static IEnumerable<Type> hotfix
    {
        get
        {
            return new List<Type>()
            {
                typeof(NewClassTest)
            };
        }
    }
}
