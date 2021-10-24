﻿using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Collections;
using System.Linq;
using BDFramework.Core.Tools;
using Cysharp.Text;
using UnityEngine.U2D;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BDFramework.ResourceMgr.V2
{
    /// <summary>
    /// 加载资源的返回状态
    /// </summary>
    public enum LoadAssetState
    {
        Success = 0,
        Fail,
        IsLoding,
    }


    /// <summary>
    /// ab包管理器
    /// </summary>
    public class AssetBundleMgrV2 : IResMgr
    {
        /// <summary>
        /// 非Hash命名时，runtime目录的都放在一起，方便调试
        /// </summary>
        static readonly public string DEBUG_RUNTIME = "runtime/{0}";


        /// <summary>
        /// 全局的任务id
        /// </summary>
        private int taskIDCounter;

        /// <summary>
        /// 异步回调表
        /// </summary>
        private List<LoaderTaskGroup> allTaskGroupList = new List<LoaderTaskGroup>();

        /// <summary>
        /// 全局唯一的依赖
        /// </summary>
        private AssetbundleConfigLoder assetConfigLoder;

        /// <summary>
        /// 全局的assetbundle字典
        /// </summary>
        public Dictionary<string, AssetBundleWapper> AssetbundleMap { get; private set; } = new Dictionary<string, AssetBundleWapper>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 资源加载路径
        /// </summary>
        private string firstArtDirectory;

        //第二寻址路径
        private string secArtDirectory;

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="path"></param>
        public void Init(string path)
        {
            //多热更切换,需要卸载
            if (this.assetConfigLoder != null)
            {
                this.UnloadAllAsset();
            }

            var platformPath = BDApplication.GetPlatformPath(Application.platform);
            //1.设置加载路径  
            firstArtDirectory = ZString.Format("{0}/{1}/{2}", path, platformPath, BResources.ASSET_ROOT_PATH);
            //当路径为persistent时，第二路径生效
            secArtDirectory = ZString.Format("{0}/{1}/{2}", Application.streamingAssetsPath, platformPath, BResources.ASSET_ROOT_PATH); //
            //路径替换
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer:
                {
                    firstArtDirectory = firstArtDirectory.Replace("\\", "/");
                    secArtDirectory = secArtDirectory.Replace("\\", "/");
                }
                    break;
            }

            //加载Config
            var assetconfigPath = "";
            var assetTypePath = "";

            this.assetConfigLoder = new AssetbundleConfigLoder();
            if (Application.isEditor)
            {
                assetconfigPath = ZString.Format("{0}/{1}/{2}", path, platformPath, BResources.ASSET_CONFIG_PATH);
                assetTypePath = ZString.Format("{0}/{1}/{2}", path, platformPath, BResources.ASSET_TYPE_PATH);
            }
            else
            {
                //真机环境config在persistent，跟dll和db保持一致
                assetconfigPath = ZString.Format("{0}/{1}/{2}", Application.persistentDataPath, platformPath, BResources.ASSET_CONFIG_PATH);
                assetTypePath = ZString.Format("{0}/{1}/{2}", Application.persistentDataPath, platformPath, BResources.ASSET_TYPE_PATH);
            }

            this.assetConfigLoder.Load(assetconfigPath, assetTypePath);
        }


        #region 对外加载接口

        /// <summary>
        /// 同步加载
        /// </summary>
        /// <param name="fullPath"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T Load<T>(string path) where T : UnityEngine.Object
        {
            //非hash模式，需要debugRuntime
            if (!this.assetConfigLoder.IsHashName)
            {
                path = ZString.Format(DEBUG_RUNTIME, path);
            }

            //1.依赖路径
            var (assetBundleItem, dependAssetList) = assetConfigLoder.GetDependAssetsByName<T>(path);
            if (assetBundleItem != null)
            {
                //加载依赖
                foreach (var dependAssetBundle in dependAssetList)
                {
                    LoadAssetBundle(dependAssetBundle);
                }

                //加载主资源
                LoadAssetBundle(assetBundleItem.AssetBundlePath);
                //
                return LoadFormAssetBundle<T>(path, assetBundleItem);
            }

            return null;
        }

        /// <summary>
        /// load ALL TestAPI
        /// 这个有一定局限性，这里是返回某个Ab中的所有资源
        /// 简单处理一些简单资源情况：目前只解决图集
        /// 仅作为某些项目补坑用
        /// </summary>
        /// <param name="path"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public T[] LoadAll_TestAPI_2020_5_23<T>(string path) where T : Object
        {
            //非hash模式，需要debugRuntime
            if (!this.assetConfigLoder.IsHashName)
            {
                path = ZString.Format(DEBUG_RUNTIME, path);
            }


            var item = assetConfigLoder.GetAssetBundleData<T>(path);
            //加载assetbundle
            AssetBundle ab = LoadAssetBundle(item.AssetBundlePath);

            if (ab != null)
            {
                var assetNames = ab.GetAllAssetNames();
                string relname = "";
                if (assetNames.Length == 1)
                {
                    relname = assetNames[0];
                }
                else
                {
                    var f = path + ".";
                    relname = assetNames.First((s) => s.Contains(f));
                }

                return ab.LoadAssetWithSubAssets<T>(relname);
            }

            return null;
        }


        /// <summary>
        /// 异步加载接口
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="assetName"></param>
        /// <param name="callback"></param>
        /// <returns></returns>
        public int AsyncLoad<T>(string assetName, Action<T> callback) where T : UnityEngine.Object
        {
            //非hash模式，需要debugRuntime
            if (!this.assetConfigLoder.IsHashName)
            {
                assetName = ZString.Format(DEBUG_RUNTIME, assetName);
            }


            List<LoaderTaskData> taskQueue = new List<LoaderTaskData>();
            //获取依赖
            var (assetBundleItem, dependAssetList) = assetConfigLoder.GetDependAssetsByName<T>(assetName);
            if (assetBundleItem != null)
            {
                //依赖资源
                foreach (var dependAsset in dependAssetList)
                {
                    var task = new LoaderTaskData(dependAsset, typeof(Object));
                    taskQueue.Add(task);
                }

                //主资源，
                var mainTask = new LoaderTaskData(assetBundleItem.AssetBundlePath, typeof(Object), true);
                taskQueue.Add(mainTask);
                //添加任务组
                var taskGroup = new LoaderTaskGroup(this, assetName, assetBundleItem, taskQueue, (p, obj) =>
                {
                    //完成回调
                    callback(obj as T);
                });
                taskGroup.Id = this.taskIDCounter++;
                AddTaskGroup(taskGroup);

                //开始任务
                DoNextTask();
                return taskGroup.Id;
            }
            else
            {
                BDebug.LogError("资源不存在:" + assetName);
            }

            return -1;
        }


        /// <summary>
        /// 异步加载 多个
        /// </summary>
        /// <param name="assetNameList">资源</param>
        /// <param name="onLoadProcess">进度</param>
        /// <param name="onLoadComplete">加载结束</param>
        /// <returns>taskid</returns>
        public List<int> AsyncLoad(List<string> assetNameList, Action<int, int> onLoadProcess, Action<IDictionary<string, Object>> onLoadComplete)
        {
            var taskIdList = new List<int>();
            int taskCounter = 0;
            var loadAssetMap = new Dictionary<string, Object>();
            assetNameList = assetNameList.Distinct().ToList(); //去重
            int total = assetNameList.Count;
            //source
            foreach (var assetName in assetNameList)
            {
                var taskid = AsyncLoad<Object>(assetName, (o) =>
                {
                    loadAssetMap[assetName] = o;
                    //进度回调
                    onLoadProcess?.Invoke(loadAssetMap.Count, total);
                    //完成回调
                    if (loadAssetMap.Count == total)
                    {
                        onLoadComplete?.Invoke(loadAssetMap);
                    }
                });

                taskIdList.Add(taskid);
            }

            //开始任务
            DoNextTask();
            //
            return taskIdList;
        }


        /// <summary>
        /// 添加一个任务组
        /// </summary>
        /// <param name="taskGroup"></param>
        public void AddTaskGroup(LoaderTaskGroup taskGroup)
        {
            this.allTaskGroupList.Add(taskGroup);
        }

        /// <summary>
        /// 多路径寻址
        /// </summary>
        /// <param name="assetFileName"></param>
        /// <returns></returns>
        public string FindMultiAddressAsset(string assetFileName)
        {
            //第一地址
            var p = IPath.Combine(this.firstArtDirectory, assetFileName);
            //寻址到第二路径
            if (!File.Exists(p))
            {
                p = IPath.Combine(this.secArtDirectory, assetFileName);
            }

            return p;
        }

        #endregion

        #region 加载AssetsBundle

        /// <summary>
        /// 加载AssetBundle
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public AssetBundle LoadAssetBundle(string path)
        {
            AssetBundleWapper abw = null;
            if (AssetbundleMap.TryGetValue(path, out abw))
            {
                abw.Use();
                return abw.AssetBundle;
            }
            else
            {
                var p = FindMultiAddressAsset(path);
                var ab = AssetBundle.LoadFromFile(p);
                //添加
                this.AddAssetBundle(path, ab);
                return ab;
            }

            return null;
        }


        /// <summary>
        /// ab包计数器
        /// </summary>
        /// <param name="assetPath"></param>
        /// <param name="ab"></param>
        public void AddAssetBundle(string assetPath, AssetBundle ab)
        {
            AssetBundleWapper abw = null;
            //
            if (!AssetbundleMap.TryGetValue(assetPath, out abw))
            {
                abw = new AssetBundleWapper(ab);
                AssetbundleMap[assetPath] = abw;
            }

            abw.Use();
        }

        #endregion

        #region 从AB中加载资源

        /// <summary>
        /// 加载资源
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="abName"></param>
        /// <param name="objName"></param>
        /// <returns></returns>
        public T LoadFormAssetBundle<T>(string assetName, AssetBundleItem item) where T : UnityEngine.Object
        {
            if (item != null)
            {
                var gobj = LoadFormAssetBundle(assetName, item, typeof(T));
                return gobj as T;
            }

            BDebug.LogError("不存在:" + assetName);
            return null;
        }


        /// <summary>
        /// 加载资源
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="abName"></param>
        /// <param name="objName"></param>
        /// <returns></returns>
        private Object LoadFormAssetBundle(string assetName, AssetBundleItem item, Type t)
        {
            Object o = null;
            AssetBundleWapper abr = null;
            if (AssetbundleMap.TryGetValue(item.AssetBundlePath, out abr))
            {
                //var assetType = this.assetConfigLoder.AssetTypeList[item.AssetType];

                //优先处理图集
                //TODO 这里需要优化成int或者枚举判断，效率更高
                if (item.AssetType == this.assetConfigLoder.TYPE_SPRITE_ATLAS)
                {
                    o = abr.LoadTextureFormAtlas(assetName);
                }
                //其他需要处理的资源类型，依次判断.
                else
                {
                    o = abr.LoadAsset(assetName, t);
                }

                // switch ((AssetBundleItem.AssetTypeEnum)item.AssetType)
                // {
                //     //暂时需要特殊处理的只有一个
                //     case AssetBundleItem.AssetTypeEnum.SpriteAtlas:
                //     {
                //        
                //     }
                //         break;
                //     case AssetBundleItem.AssetTypeEnum.Prefab:
                //     case AssetBundleItem.AssetTypeEnum.Texture:
                //     case AssetBundleItem.AssetTypeEnum.Others:
                //     default:
                //     {
                //        
                //     }
                //         break;
                // }
            }
            else
            {
                BDebug.Log("资源不存在:" + assetName + " - " + item.AssetBundlePath, "red");

                return null;
            }

            return o;
        }

        #endregion

        #region 取消加载任务

        /// <summary>
        /// 取消load任务
        /// </summary>
        /// <param name="taskid"></param>
        public void LoadCancel(int taskid)
        {
            foreach (var tg in allTaskGroupList)
            {
                if (tg.Id == taskid)
                {
                    tg.Stop();
                    allTaskGroupList.Remove(tg);
                    break;
                }
            }
        }


        /// <summary>
        /// 取消所有load任务
        /// </summary>
        public void LoadAllCancel()
        {
            foreach (var tg in allTaskGroupList)
            {
                tg.Stop();
            }

            this.allTaskGroupList.Clear();
        }

        /// <summary>
        /// 获取路径下所有资源
        /// </summary>
        /// <param name="floder"></param>
        /// <param name="searchPattern"></param>
        /// <returns></returns>
        public string[] GetAssets(string floder, string searchPattern = null)
        {
            List<string> rets = new List<string>();
            string str;

            str = ZString.Concat(floder, "/");
            if (!this.assetConfigLoder.IsHashName)
            {
                str = ZString.Format(DEBUG_RUNTIME, str);
            }


            foreach (var abItem in this.assetConfigLoder.AssetbundleItemList)
            {
                if (abItem.LoadPath.StartsWith(str, StringComparison.OrdinalIgnoreCase))
                {
                    rets.Add(abItem.LoadPath);
                }
            }

            //寻找符合条件的
            if (!string.IsNullOrEmpty(searchPattern))
            {
                rets = rets.FindAll((r) =>
                {
                    var fileName = Path.GetFileName(r);

                    if (fileName.StartsWith(searchPattern, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    return false;
                });
            }

            if (!this.assetConfigLoder.IsHashName)
            {
                var count = "runtime/".Length;
                for (int i = 0; i < rets.Count; i++)
                {
                    rets[i] = rets[i].Substring(count);
                }
            }


            return rets.ToArray();
        }

        #endregion

        #region 核心任务驱动

        /// <summary>
        /// 当前执行的任务组
        /// </summary>
        private LoaderTaskGroup curDoTask = null;

        /// <summary>
        /// 核心功能,所有任务靠这个推进度
        /// 执行下个任务
        /// </summary>
        void DoNextTask()
        {
            if (this.allTaskGroupList.Count == 0)
            {
                return;
            }

            //当前任务组执行完毕，执行下一个
            if ((curDoTask == null || curDoTask.IsComplete) && this.allTaskGroupList.Count > 0)
            {
                curDoTask = this.allTaskGroupList[0];
                this.allTaskGroupList.RemoveAt(0);
                //开始task
                curDoTask.DoNextTask();
                //注册完成回调
                curDoTask.OnAllTaskCompleteCallback += (a, b) =>
                {
                    //
                    DoNextTask();
                };
            }
        }

        #endregion

        #region 卸载资源

        /// <summary>
        /// 卸载
        /// </summary>
        /// <param name="assetName">根据加载路径卸载</param>
        /// <param name="isForceUnload">强制卸载</param>
        public void UnloadAsset(string assetName, bool isForceUnload = false)
        {
            //非hash模式，需要debugRuntime
            if (!this.assetConfigLoder.IsHashName)
            {
                assetName = ZString.Format(DEBUG_RUNTIME, assetName);
            }

            var (assetBundleItem, dependAssetList) = assetConfigLoder.GetDependAssetsByName(assetName);
            //添加主资源一起卸载
            dependAssetList.Add(assetBundleItem.AssetBundlePath);
            //卸载
            for (int i = 0; i < dependAssetList.Count; i++)
            {
                var assetPath = dependAssetList[i];
                AssetBundleWapper abw = null;

                if (AssetbundleMap.TryGetValue(assetPath, out abw))
                {
                    if (isForceUnload)
                    {
                        abw.UnLoad();
                    }
                    else
                    {
                        abw.Unuse();
                    }
                }
            }
        }


        /// <summary>
        /// 卸载
        /// </summary>
        /// <param name="path"></param>
        public void UnloadAllAsset()
        {
            AssetBundle.UnloadAllAssetBundles(true);
            Resources.UnloadUnusedAssets();
        }

        #endregion
    }
}
