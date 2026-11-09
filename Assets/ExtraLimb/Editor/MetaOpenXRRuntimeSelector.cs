using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ExtraLimb.EditorSupport
{
    /// <summary>
    /// エディタのPlay時に使うOpenXRランタイムを、Metaのランタイム（Quest Link）に固定する。
    /// Windowsの標準OpenXRランタイムがSteamVRのままでも、このプロジェクトだけはMetaのランタイムで動く。
    /// SteamVRはトラッカーを読むためだけに、ヘッドセットなしで起動しておく（Docs/SteamVR_NullDriver_Setup.md）。
    /// 本番用にビルドしたアプリでは、起動用バッチで環境変数 XR_RUNTIME_JSON を同じパスに設定する。
    /// </summary>
    [InitializeOnLoad]
    internal static class MetaOpenXRRuntimeSelector
    {
        // OpenXRローダーが読む環境変数
        const string RuntimeEnvKey = "XR_RUNTIME_JSON";
        // OpenXRパッケージの「Play Mode OpenXR Runtime」の選択が読む環境変数
        const string SelectedRuntimeEnvKey = "XR_SELECTED_RUNTIME_JSON";
        const string DefaultJson = @"C:\Program Files\Oculus\Support\oculus-runtime\oculus_openxr_64.json";
        const string LoggedKey = "ExtraLimb.MetaOpenXRRuntimeLogged";

        static MetaOpenXRRuntimeSelector()
        {
            Apply(!SessionState.GetBool(LoggedKey, false));
            SessionState.SetBool(LoggedKey, true);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Playに入る直前に毎回指定し直す（Play終了時にOpenXRパッケージが空にするため）
            if (state == PlayModeStateChange.ExitingEditMode) Apply(false);
        }

        static void Apply(bool log)
        {
            var json = FindMetaRuntimeJson();
            if (json == null)
            {
                Debug.LogWarning("[ExtraLimb] Metaのランタイム（oculus_openxr_64.json）が見つからないため、OpenXRランタイムを指定しませんでした。Meta Quest LinkのPCアプリが入っているか確認してください。");
                return;
            }
            Environment.SetEnvironmentVariable(SelectedRuntimeEnvKey, json);
            Environment.SetEnvironmentVariable(RuntimeEnvKey, json);
            if (log) Debug.Log("[ExtraLimb] Play時のOpenXRランタイムをMetaのランタイムに固定しました: " + json);
        }

        static string FindMetaRuntimeJson()
        {
#if UNITY_EDITOR_WIN
            try
            {
                // Meta Quest LinkのPCアプリのインストール先から探す
                var install = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Oculus", "InstallLocation", null) as string;
                if (!string.IsNullOrEmpty(install))
                {
                    var path = Path.Combine(install, @"Support\oculus-runtime\oculus_openxr_64.json");
                    if (File.Exists(path)) return path;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ExtraLimb] Metaのランタイムの場所をレジストリから読めませんでした: " + e.Message);
            }
            return File.Exists(DefaultJson) ? DefaultJson : null;
#else
            return null;
#endif
        }
    }
}
