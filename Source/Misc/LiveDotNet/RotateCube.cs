/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using UnityEngine;
using IFix;

//用来演示修改代码后，立即刷新到真机
public class RotateCube : MonoBehaviour
{
    public Light theLight;

    [Patch]
    void Update()
    {
        //旋转
        transform.Rotate(Vector3.up * Time.deltaTime * 20);
        //改变颜色
        theLight.color = new Color(Mathf.Sin(Time.time) / 2 + 0.5f, 0, 0, 1);
    }
}
