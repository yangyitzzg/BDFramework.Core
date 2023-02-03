﻿using BDFramework.Core.Tools;
using UnityEditor;

namespace BDFramework.Editor.Unity3dEx
{
    /// <summary>
    /// unity3d的editor扩展函数
    /// </summary>
    static public class EditorEx
    {
        /// <summary>
        /// 添加宏
        /// </summary>
        /// <param name="symbol"></param>
        public static void AddSymbols(string symbol)
        {
            foreach (var bt in BApplication.SupportBuildTargetGroups)
            {
                var symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(bt);
                if (!symbols.Contains(symbol))
                {
                    string str = "";
                    if (!string.IsNullOrEmpty(symbols))
                    {
                        if (!str.EndsWith(";"))
                        {
                            str = symbols + ";" + symbol;
                        }
                        else
                        {
                            str = symbols + symbol;
                        }
                    }
                    else
                    {
                        str = symbol;
                    }

                    PlayerSettings.SetScriptingDefineSymbolsForGroup(bt, str);
                }
            }
        }
    }
}
