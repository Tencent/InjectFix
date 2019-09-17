/*
 * Tencent is pleased to support the open source community by making InjectFix available.
 * Copyright (C) 2019 THL A29 Limited, a Tencent company.  All rights reserved.
 * InjectFix is licensed under the MIT License, except for the third-party components listed in the file 'LICENSE' which may be subject to their corresponding license terms. 
 * This file is subject to the terms and conditions defined in file 'LICENSE', which is part of this source code package.
 */

using UnityEngine;
using UnityEditor;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System;

namespace IFix.Editor
{
    //输入三个信息：
    //  1、平台（ios、android）
    //  2、手机的ip地址
    //  3、端口信息，端口信息要和PatchReceiver配置对应上
    public class LiveDotNet : EditorWindow
    {
        private int platformIndex = 0;
        private string strIp = "";
        private int port = 8080;
        private string[] platforms = new string[] { "ios", "android" };

        [MenuItem("InjectFix/LiveDotNet")]
        private static void OpenWindow()
        {
            LiveDotNet window = GetWindow<LiveDotNet>();
            window.titleContent = new GUIContent("LiveDotNet");
        }

        void OnGUI()
        {
            platformIndex = EditorGUILayout.Popup("Platform: ", platformIndex, platforms);
            strIp = EditorGUILayout.TextField("IP: ", strIp);
            port = EditorGUILayout.IntField("Port: ", port);
            if (GUILayout.Button("patch"))
                doPatch();
        }

        //1、IFixEditor.GenPlatformPatch会生成生成补丁文件
        //2、发送给手机
        void doPatch()
        {
            IFixEditor.Platform platform = platformIndex == 0 ? IFixEditor.Platform.ios : IFixEditor.Platform.android;
            string patchPath = "Temp/Assembly-CSharp.patch.bytes";
            IFixEditor.GenPlatformPatch(platform, "Temp/");

            IPAddress ip;
            if (!IPAddress.TryParse(strIp, out ip))
            {
                throw new FormatException("Invalid ip-adress");
            }

            IPEndPoint remoteEndPoint = new IPEndPoint(ip, port);
            doSend(File.ReadAllBytes(patchPath), remoteEndPoint);
            File.Delete(patchPath);
        }

        //1、对手机建立TCP链接
        //2、发送整个包
        //3、关闭链接
        void doSend(byte[] bytes, IPEndPoint remoteEndPoint)
        {
            try
            {
                Socket sender = new Socket(remoteEndPoint.Address.AddressFamily,
                    SocketType.Stream, ProtocolType.Tcp);

                try
                {
                    sender.Connect(remoteEndPoint);

                    Debug.Log(string.Format("Socket connected to {0}",
                        sender.RemoteEndPoint.ToString()));

                    int bytesSent = sender.Send(bytes);

                    Debug.Log(string.Format("bytesSent = {0}", bytesSent));

                    sender.Shutdown(SocketShutdown.Both);
                    sender.Close();

                }
                catch (ArgumentNullException ane)
                {
                    Debug.LogError(string.Format("ArgumentNullException : {0}", ane.ToString()));
                }
                catch (SocketException se)
                {
                    Debug.LogError(string.Format("SocketException : {0}", se.ToString()));
                }
                catch (Exception e)
                {
                    Debug.LogError(string.Format("Unexpected exception : {0}", e.ToString()));
                }

            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
            }
        }
    }
}
