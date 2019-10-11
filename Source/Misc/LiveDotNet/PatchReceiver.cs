/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

//!!仅仅简单的开tcp，接收到数据没做校验就直接执行
//!!所以切记这个仅仅用于平时开发调试使用
//!!最终游戏发布包务必删除
#warning "Please remove PatchReceiver.cs from release package."

using UnityEngine;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;
using IFix.Core;

namespace IFix
{
    public class PatchReceiver : MonoBehaviour
    {
        //默认的补丁文件保存名
        const string PERSISTENT_FILE_NAME = "__LAST_RUN_SAVED_PATCH";

        //接收缓冲区的大小
        const int BUFFER_SIZE = 1024;

        Stream patch = null;

        //需要跟LiveDotNet.cs的端口配套
        public int Port = 8080;

        //这个设置为true的话，补丁文件会保存到文件，重启应用后仍然生效
        public bool Persistent = false;

        Socket listener = null;

        string lastRunSavePath;

        //1、监听
        //2、accpet到链接后，接收该链接所有数据
        //3、把数据作为补丁包加载
        void ReceivePatch()
        {
            byte[] bytes = new byte[BUFFER_SIZE];

            //监听所有地址
            IPAddress ipAddress = IPAddress.Parse("0.0.0.0");
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, Port);

            listener = new Socket(ipAddress.AddressFamily,
                SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(10);

                while (true)
                {
                    Socket handler = listener.Accept();

                    MemoryStream ms = new MemoryStream();

                    while (true)
                    {
                        int bytesRec = handler.Receive(bytes);
                        if (bytesRec == 0)
                        {
                            break;
                        }
                        ms.Write(bytes, 0, bytesRec);
                    }

                    if (Persistent)
                    {
                        File.WriteAllBytes(lastRunSavePath, ms.GetBuffer());
                    }

                    ms.Position = 0;

                    Debug.Log("Patch Size: " + ms.Length);

                    lock (this)
                    {
                        patch = ms;
                    }

                    try
                    {
                        handler.Shutdown(SocketShutdown.Both);
                    }
                    catch { }
                    finally
                    {
                        handler.Close();
                    }
                }

            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
            }
        }

        //如果设置了持久化，在Awake时加载上次保存的补丁文件
        void Awake()
        {
            lastRunSavePath = Application.persistentDataPath + Path.DirectorySeparatorChar + PERSISTENT_FILE_NAME;
            if (Persistent && File.Exists(lastRunSavePath))
            {
                using (var fs = File.Open(lastRunSavePath, FileMode.Open))
                {
                    PatchManager.Load(fs);
                }
            }
            DontDestroyOnLoad(gameObject);
            //启动线程来接收补丁，不卡主线程，两者通过patch变量来交接数据
            new Thread(ReceivePatch).Start();
        }

        void Update()
        {
            Stream ms = null;
            lock (this)
            {
                ms = patch;
                patch = null;
            }
            if (ms != null)
            {

                PatchManager.Load(ms);
            }
        }

        void OnDestroy()
        {
            listener.Close();
        }
    }
}
