using System;
using System.Collections.Generic;
using System.Linq;
using BDFramework.Editor.AssetBundle;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.AssetGraph;
using UnityEngine.AssetGraph.DataModel.Version2;

namespace BDFramework.Editor.AssetGraph.Node
{
    [CustomNode("BDFramework/[筛选]Group by Path", 10)]
    public class FilterGroupByPath : UnityEngine.AssetGraph.Node, IBDAssetBundleV2Node
    {
        public BuildInfo BuildInfo { get; set; }

        public override string ActiveStyle
        {
            get { return "node 2 on"; }
        }

        public override string InactiveStyle
        {
            get { return "node 2"; }
        }

        public override string Category
        {
            get { return "[筛选]Group by Path"; }
        }


        /// <summary>
        /// 输出路径的数据
        /// </summary>
        [Serializable]
        public class GroupPathData
        {
            public string OutputNodeId;
            public string GroupPath;
        }

        /// <summary>
        /// 所有输出路径
        /// </summary>
        [SerializeField] public List<GroupPathData> groupFilterPathDataList = new List<GroupPathData>();

        /// <summary>
        /// 路径list渲染对象
        /// </summary>
        ReorderableList e_groupList;

        private NodeGUI selfNodeData;

        public override void Initialize(NodeData data)
        {
            data.AddDefaultInputPoint();
        }

        public override UnityEngine.AssetGraph.Node Clone(NodeData newData)
        {
            var node = new FilterGroupByPath();
            newData.AddDefaultInputPoint();
            return node;
        }

        #region 渲染 list Inspector

        public override void OnInspectorGUI(NodeGUI node, AssetReferenceStreamManager streamManager, NodeGUIEditor editor, Action onValueChanged)
        {
            if (e_groupList == null)
            {
                e_groupList = new ReorderableList(groupFilterPathDataList, typeof(string), true, false, true, true);
                e_groupList.onReorderCallback = ReorderFilterEntryList;
                e_groupList.onAddCallback = AddToFilterEntryList;
                e_groupList.onRemoveCallback = RemoveFromFilterEntryList;
                e_groupList.onCanRemoveCallback = CanRemoveFilterEntry;
                e_groupList.drawElementCallback = DrawFilterEntryListElement;
                e_groupList.elementHeight = EditorGUIUtility.singleLineHeight + 8f;
                e_groupList.headerHeight = 3;

                e_groupList.index = this.groupFilterPathDataList.Count - 1;
            }

            this.selfNodeData = node;

            GUILayout.Label("路径:");
            e_groupList.DoLayoutList();
        }

        private bool CanRemoveFilterEntry(ReorderableList list)
        {
            return list.index > 0;
        }

        private void RemoveFromFilterEntryList(ReorderableList list)
        {
            if (list.index > 0)
            {
                this.groupFilterPathDataList.RemoveAt(this.groupFilterPathDataList.Count - 1);
                list.index--;
                list.onChangedCallback.Invoke(list);
            }
        }

        private void AddToFilterEntryList(ReorderableList list)
        {
            list.index++;
            var node = this.selfNodeData.Data.AddOutputPoint(list.index.ToString());
            this.groupFilterPathDataList.Add(new GroupPathData()
            {
                OutputNodeId = node.Id,
                GroupPath = list.index.ToString()
            });
        }

        private void DrawFilterEntryListElement(Rect rect, int index, bool isactive, bool isfocused)
        {
            //渲染数据
            var gp = this.groupFilterPathDataList[index];
            gp.GroupPath = EditorGUILayout.TextField(gp.GroupPath);
            //更新
            UpdateGroupPathData(index);
        }

        private void ReorderFilterEntryList(ReorderableList list)
        {
        }

        /// <summary>
        /// 更新数据
        /// </summary>
        private void UpdateGroupPathData(int idx)
        {
            var gpd = this.groupFilterPathDataList[idx];
            var outputNode = this.selfNodeData.Data.FindOutputPoint(gpd.OutputNodeId);
            outputNode.Label = gpd.GroupPath;
        }

        #endregion

        

        public override void Prepare(BuildTarget target, NodeData nodeData, IEnumerable<PerformGraph.AssetGroups> incoming, IEnumerable<ConnectionData> connectionsToOutput, PerformGraph.Output outputFunc)
        {
            if (incoming == null)
            {
                return;
            }

            this.BuildInfo = BDFrameworkAssetsEnv.BuildInfo;

            //初始化输出列表
            var outMap = new Dictionary<string, List<AssetReference>>();
            foreach (var group in this.groupFilterPathDataList)
            {
                if (!string.IsNullOrEmpty(group.GroupPath))
                {
                    outMap[group.GroupPath] = new List<AssetReference>();
                }
            }

            //在depend 和runtime内进行筛选
            foreach (var ags in incoming)
            {
                foreach (var group in ags.assetGroups)
                {
                    if (group.Key == nameof(BDFrameworkAssetsEnv.FloderType.Runtime) || group.Key == nameof(BDFrameworkAssetsEnv.FloderType.Depend))
                    {
                        var assetList = group.Value.ToList();
                        for (int i = assetList.Count - 1; i >= 0; i--)
                        {
                            var assetRef = assetList[i];

                            foreach (var groupFilter in this.groupFilterPathDataList)
                            {
                                if (!string.IsNullOrEmpty(groupFilter.GroupPath))
                                {
                                    //匹配路径
                                    if (assetRef.importFrom.StartsWith(groupFilter.GroupPath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        assetList.RemoveAt(i);
                                        //添加到输出
                                        outMap[groupFilter.GroupPath].Add(assetRef);
                                    }
                                }
                            }
                        }

                        outMap[group.Key] = assetList;
                    }
                }
            }


            //输出
            if (connectionsToOutput != null)
            {
                foreach (var outpointNode in connectionsToOutput)
                {
                    var groupFilter = this.groupFilterPathDataList.FirstOrDefault((gf) => gf.OutputNodeId == outpointNode.FromNodeConnectionPointId);
                    if (groupFilter != null)
                    {
                        var kv = new Dictionary<string, List<AssetReference>>()
                        {
                            {groupFilter.GroupPath, outMap[groupFilter.GroupPath]}
                        };
                        outputFunc(outpointNode, kv);
                    }
                }
            }
        }
    }
}
