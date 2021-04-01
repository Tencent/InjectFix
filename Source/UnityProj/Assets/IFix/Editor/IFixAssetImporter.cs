
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Helper;
using IFix.Core;

#if UNITY_2020_1_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

[ScriptedImporter(2, new[]{"ifix"})]
public class IFixAssetImpot : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        var prefax = Path.GetExtension(ctx.assetPath).Substring(1);
        var asset = ScriptableObject.CreateInstance<IFixAsset>();
        asset.data = File.ReadAllBytes(ctx.assetPath);
       
        ctx.AddObjectToAsset("main obj", asset, LoadIconTexture(prefax));
        ctx.SetMainObject(asset);
    }
    
    private Texture2D LoadIconTexture(string prefax)
    {
        return AssetDatabase.LoadAssetAtPath("Assets/IFix/Editor/ifix.png", typeof(Texture2D)) as Texture2D;
    }
}

[CustomEditor(typeof(IFixAsset))]
public class IFixAssetEditor : Editor
{
    private IFixAsset mTarget;
    public void OnEnable()
    {
        mTarget = target as IFixAsset;
        
    }

    public void OnDestroy()
    {
    }

    private static bool mShowBase = false;
    public override void OnInspectorGUI()
    {
        mShowBase = EditorGUILayout.Toggle("ShowBase", mShowBase);
        // if (mShowBase)
        // {
        //     base.OnInspectorGUI();
        // }
        // else
        {
            DrawIFixPatch();
        }
    }

    private static bool foldoutExtern = false;
    private static bool foldoutMethod = false;
    private static bool foldoutExternMethod = false;
    private static bool foldoutStrings = false;
    private static bool foldoutFields = false;
    private static bool foldoutanonymousStorey = false;
    private static bool foldoutFixCount = false;
    private static bool foldoutStaticFieldType = false;
    private static bool foldoutNewClass = false;

    void DrawIFixPatch()
    {
        var fileStream = new MemoryStream(mTarget.data);
        var reader = new BinaryReader(fileStream);
        try
        // if(true)
        {

            var magic = reader.ReadInt64();
            magic = EditorGUILayout.LongField("Magic", magic);
            var interfaceBridgeTypeName = reader.ReadString();
            EditorGUILayout.LabelField("interfaceBridge", interfaceBridgeTypeName);

            int externTypeCount = reader.ReadInt32();
            foldoutExtern = EditorGUILayout.Foldout(foldoutExtern,$"extern:{externTypeCount}", true);
            var externTypes = new Type[externTypeCount];
            ++EditorGUI.indentLevel;
            for (int i = 0; i < externTypeCount; i++)
            {
                var assemblyQualifiedName = reader.ReadString();
                externTypes[i] = Type.GetType(assemblyQualifiedName);
                if(foldoutExtern)
                {
                    if (externTypes[i] == null)
                    {
                        EditorGUILayout.LabelField($"{i}: ERROR: [{assemblyQualifiedName}] not found");
                    }
                    else
                    {
                        EditorGUILayout.LabelField($"{i}:{externTypes[i].FullName}");
                    }
                }
            }

            --EditorGUI.indentLevel;

            int methodCount = reader.ReadInt32();
            foldoutMethod = EditorGUILayout.Foldout(foldoutMethod,$"methodBody:{methodCount}", true);
            ++EditorGUI.indentLevel;
            for (int i = 0; i < methodCount; i++)
            {
                int codeSize = reader.ReadInt32();
                var codes = new List<Tuple<IFix.Core.Code, int>>();
                for (int ii = 0; ii < codeSize; ii++)
                {
                    var op = (IFix.Core.Code)reader.ReadInt32();//operator
                    var opt= reader.ReadInt32();
                    codes.Add(new Tuple<Code, int>(op, opt));
                }

                var ehs = new List<Tuple<ExceptionHandlerType,int,int,int,int,int>>();
                var ehsOfMethodCount = reader.ReadInt32();
                for (int j = 0; j < ehsOfMethodCount; j++)
                {
                    var handlerType = (ExceptionHandlerType)reader.ReadInt32();
                    var catchTid = reader.ReadInt32();
                    var tryStart = reader.ReadInt32();
                    var tryEnd = reader.ReadInt32();
                    var handlerStart = reader.ReadInt32();
                    var handlerEnd = reader.ReadInt32();
                    ehs.Add(new Tuple<ExceptionHandlerType, int, int, int, int, int>(handlerType, catchTid, tryStart, tryEnd, handlerStart, handlerEnd));
                }
                if(foldoutMethod)
                    EditorGUILayout.LabelField($"{i}: cs:{codeSize} ehs:{ehsOfMethodCount} [{string.Join(",", codes.GetRange(0, Math.Min(5, codes.Count)))}{(codes.Count>5?", ..." : "")}]");
            }
            --EditorGUI.indentLevel;

            int externMethodCount = reader.ReadInt32();
            foldoutExternMethod = EditorGUILayout.Foldout(foldoutExternMethod,$"externMethod:{externMethodCount}", true);
            ++EditorGUI.indentLevel;
            for (int i = 0; i < externMethodCount; i++)
            {
                var externMethod = IFix.Core.PatchManager.readMethod(reader, externTypes);
                if(foldoutExternMethod)
                    EditorGUILayout.LabelField($"{i}:{externMethod}");
            }
            --EditorGUI.indentLevel;

            int internStringsCount = reader.ReadInt32();
            foldoutStrings = EditorGUILayout.Foldout(foldoutStrings,$"strings:{internStringsCount}", true);
            ++EditorGUI.indentLevel;
            for (int i = 0; i < internStringsCount; i++)
            {
                var internStrings = reader.ReadString();
                if(foldoutStrings)
                    EditorGUILayout.LabelField($"{i}:{internStrings}");
            }
            --EditorGUI.indentLevel;

            var fieldCount = reader.ReadInt32();
            foldoutFields = EditorGUILayout.Foldout(foldoutFields,$"field:{fieldCount}", true);
            ++EditorGUI.indentLevel;
            for (int i = 0; i < fieldCount; i++)
            {
                var isNewField = reader.ReadBoolean();
                var declaringType = externTypes[reader.ReadInt32()];
                var fieldName = reader.ReadString();
                if (!isNewField)
                {
                    if(foldoutFields)
                        EditorGUILayout.LabelField($"{i}:{fieldName}:{declaringType}");
                }
                else
                {
                    var fieldType = externTypes[reader.ReadInt32()];
                    var methodId = reader.ReadInt32();
                    if(foldoutFields)
                        EditorGUILayout.LabelField($"{i}:{fieldName}:{declaringType} {fieldType} {methodId}");
                }
            }
            --EditorGUI.indentLevel;

            var staticFieldTypeCount = reader.ReadInt32();
            foldoutStaticFieldType = EditorGUILayout.Foldout(foldoutStaticFieldType,$"staticFieldType:{staticFieldTypeCount}", true);
            ++EditorGUI.indentLevel;
            for (int i = 0; i < staticFieldTypeCount; i++)
            {
                var staticFieldTypes = externTypes[reader.ReadInt32()];
                var cctors = reader.ReadInt32();
                if(foldoutStaticFieldType)
                    EditorGUILayout.LabelField($"{i}:{staticFieldTypes} cctors:{cctors}");
            }
            --EditorGUI.indentLevel;

            var anonymousStoreyInfoCount = reader.ReadInt32();
            var anonymousStoreyInfos = new AnonymousStoreyInfo[anonymousStoreyInfoCount];
            foldoutanonymousStorey = EditorGUILayout.Foldout(foldoutanonymousStorey,$"anonymousStorey:{anonymousStoreyInfoCount}", true);
            ++EditorGUI.indentLevel;
            for (int i = 0; i < anonymousStoreyInfoCount; i++)
            {
                int fieldNum = reader.ReadInt32();
                // EditorGUILayout.LongField("field", fieldNum);
                ++EditorGUI.indentLevel;
                var fieldTypes = new List<int>();
                for (int fieldIdx = 0; fieldIdx < fieldNum; ++fieldIdx)
                {
                    fieldTypes.Add(reader.ReadInt32());
                }
                --EditorGUI.indentLevel;

                int ctorId = reader.ReadInt32();
                int ctorParamNum = reader.ReadInt32();
                // var slots = IFix.Core.PatchManager.readSlotInfo(reader, itfMethodToId, externTypes, maxId);
                int interfaceCount = reader.ReadInt32();
                var interfaces = new List<Tuple<int, List<int>>>();
                // EditorGUILayout.LongField("interface", interfaceCount);
                ++EditorGUI.indentLevel;
                for (int ii = 0; ii < interfaceCount; ii++)
                {
                    var itfId = reader.ReadInt32();
                    var itf = externTypes[itfId];
                    var methodIds = new List<int>();
                    foreach (var method in itf.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public
                        | BindingFlags.Instance))
                    {
                        int methodId = reader.ReadInt32();
                        methodIds.Add(methodId);
                    }
                    interfaces.Add(new Tuple<int, List<int>>(itfId,methodIds));
                }
                --EditorGUI.indentLevel;

                int virtualMethodNum = reader.ReadInt32();
                if (virtualMethodNum < 0 || virtualMethodNum > 256)
                {
                    throw new Exception($"virtualMethodNum:{virtualMethodNum}");
                }
                int[] vTable = new int[virtualMethodNum];
                for (int vm = 0; vm < virtualMethodNum; vm++)
                {
                    vTable[vm] = reader.ReadInt32();
                }

                if(foldoutanonymousStorey)
                {
                    EditorGUILayout.LabelField($"{i}: ctorId:{ctorId} fieldNum:{fieldNum} ctorParamNum:{ctorParamNum} vTable:[{string.Join(",", vTable)}]");
                    ++EditorGUI.indentLevel;
                    foreach (var itf in interfaces)
                    {
                        EditorGUILayout.LabelField($"itf:{itf.Item1} mthIds:[{string.Join(",", itf.Item2)}] [{externTypes[itf.Item1]}]");
                    }
                    --EditorGUI.indentLevel;
                }
            }
            --EditorGUI.indentLevel;

            var wrappersManagerImplName = reader.ReadString();
            var assemblyStr = reader.ReadString();
            // EditorGUILayout.LabelField($"wrappersManagerImplName:{wrappersManagerImplName}");
            // EditorGUILayout.LabelField($"assemblyStr:{assemblyStr}");

            int fixCount = reader.ReadInt32();
            foldoutFixCount = EditorGUILayout.Foldout(foldoutFixCount,$"fixMethod:{fixCount}", true);
            ++EditorGUI.indentLevel;
            for (int i = 0; i < fixCount; i++)
            {
                var fixMethod = IFix.Core.PatchManager.readMethod(reader, externTypes);
                var fixMethodId = reader.ReadInt32();
                if(foldoutFixCount)
                    EditorGUILayout.LabelField($"{i}: {fixMethodId}:{fixMethod}");
            }
            --EditorGUI.indentLevel;

            int newClassCount = reader.ReadInt32();
            foldoutNewClass = EditorGUILayout.Foldout(foldoutNewClass,$"newClass:{newClassCount}", true);
            ++EditorGUI.indentLevel;
            for (int i = 0; i < newClassCount; i++)
            {
                var newClassFullName = reader.ReadString();
                var newClassName = Type.GetType(newClassFullName);
                if(foldoutNewClass)
                    EditorGUILayout.LabelField($"{i}: {newClassName}");
            }
            --EditorGUI.indentLevel;
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
        finally
        {
            fileStream.Dispose();
            reader.Dispose();
        }

    }
}






[ScriptedImporter(2, new[] {"icfg"})]
public class IFixCfgImpot : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        var prefax = Path.GetExtension(ctx.assetPath).Substring(1);
        var asset = ScriptableObject.CreateInstance<IFixCfgAsset>();
        asset.data = File.ReadAllBytes(ctx.assetPath);
       
        ctx.AddObjectToAsset("main obj", asset, LoadIconTexture(prefax));
        ctx.SetMainObject(asset);
    }
    
    private Texture2D LoadIconTexture(string prefax)
    {
        return AssetDatabase.LoadAssetAtPath("Assets/IFix/Editor/ifix.png", typeof(Texture2D)) as Texture2D;
    }

}

[CustomEditor(typeof(IFixCfgAsset))]
public class IFixCfgEditor : Editor
{
    private IFixCfgAsset mTarget;
    public void OnEnable()
    {
        mTarget = target as IFixCfgAsset;
        
    }

    public void OnDestroy()
    {
    }

    private static bool mShowBase = false;
    public override void OnInspectorGUI()
    {
        mShowBase = EditorGUILayout.Toggle("ShowBase", mShowBase);
        // if (mShowBase)
        // {
        //     base.OnInspectorGUI();
        // }
        // else
        {
            DrawIFixCfg();
        }
    }

    void DrawIFixCfg()
    {
        var fileStream = new MemoryStream(mTarget.data);
        var reader = new BinaryReader(fileStream);
        // patchMethods
        Action<string> readMethods = ptype =>
        {
            var mgc = reader.ReadInt32();
            EditorGUILayout.IntField(ptype, mgc);
            ++EditorGUI.indentLevel;
            for (int i = 0; i < mgc; i++)
            {
                var name = reader.ReadString();
                var mc = reader.ReadInt32();
                EditorGUILayout.IntField($"{i}-{name}", mc);
                ++EditorGUI.indentLevel;
                for (int j = 0; j < mc; j++)
                {
                    var mname = reader.ReadString();
                    var returnt = reader.ReadString();
                    var pl = reader.ReadInt32();
                    var ps = new StringBuilder(128);
                    ++EditorGUI.indentLevel;
                    for (int k = 0; k < pl; k++)
                    {
                        var isout = reader.ReadBoolean();
                        if (isout)
                            ps.AppendFormat("{0} {1}", "out", reader.ReadString());
                        else
                            ps.AppendFormat("{0}", reader.ReadString());
                    }

                    --EditorGUI.indentLevel;
                    EditorGUILayout.LabelField($"{j}-{returnt} {mname}({ps})"); // name, return type
                }

                --EditorGUI.indentLevel;
            }

            --EditorGUI.indentLevel;
        };
        Action<string> readFields = (forp) =>
        {
            var fc = reader.ReadInt32();
            EditorGUILayout.IntField(forp, fc);
            ++EditorGUI.indentLevel;
            for (int i = 0; i < fc; i++)
            {
                var mname = reader.ReadString();
                var pl = reader.ReadInt32();
                EditorGUILayout.IntField($"{i}-{mname}", pl);
                var ps = new StringBuilder(128);
                ++EditorGUI.indentLevel;
                for (int k = 0; k < pl; k++)
                {
                    var fname = reader.ReadString();
                    var ftype = reader.ReadString();

                    EditorGUILayout.LabelField($"{k}-{fname}:{ftype}"); // name, return type            }
                }

                --EditorGUI.indentLevel;
            }
            --EditorGUI.indentLevel;
        };

        Action readNewClasses = () =>
        {
            var fc = reader.ReadInt32();
            EditorGUILayout.IntField("NewClasses", fc);
            ++EditorGUI.indentLevel;
            for (int i = 0; i < fc; i++)
            {
                var mname = reader.ReadString();
                EditorGUILayout.LabelField($"{i}-{mname}");
            }
            --EditorGUI.indentLevel;
        };
        
        readMethods("patchMethods");
        readMethods("newMethods");
        readFields("fieldGroups");
        readFields("properties");
        readNewClasses();
        
        fileStream.Dispose();
        reader.Dispose();
    }
}
