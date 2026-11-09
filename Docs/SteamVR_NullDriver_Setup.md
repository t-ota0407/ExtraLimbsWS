# SteamVRをヘッドセットなしで起動する（VIVEトラッカー専用）

Quest 3はQuest Link（Metaのランタイム）で使い、SteamVRはVIVEトラッカー（Tracker 3.0）を読むためだけに起動する。SteamVRはnullドライバ（仮想のヘッドセット）で動かし、Questを使わせない。Unityは描画しない補助アプリとしてSteamVRにつなぐ。

> 実験用PCでは2026-11-09に設定済み。元の設定は同じフォルダの `steamvr.vrsettings.bak_20261109` にある。

## 1. 設定ファイルをバックアップする

1. SteamVRを終了する（起動中に編集すると、終了時に上書きされて消える）。
2. `C:\Program Files (x86)\Steam\config\steamvr.vrsettings` を同じ場所にコピーし、`steamvr.vrsettings.bak_日付` のような名前にする。

## 2. steamvr.vrsettings を編集する

次の値を設定する。すでにあるセクションには項目を足し、なければセクションごと足す。

```json
"steamvr" : {
   "requireHmd" : false,
   "forcedDriver" : "null",
   "activateMultipleDrivers" : true
},
"driver_null" : {
   "enable" : true
},
"driver_oculus" : {
   "enable" : false
},
"power" : {
   "pauseCompositorOnStandby" : false
}
```

- `steamvr` の3項目と `driver_null`：仮想のヘッドセットで起動し、トラッカーのドライバも読み込む。`steamvr` の既存の項目はそのまま残す。
- `driver_oculus`：SteamVRがQuestを使おうとしないようにする（設定画面の「アドオンを管理」でOculusをオフにするのと同じ）。
- `pauseCompositorOnStandby`：ヘッドセットがない状態でVRが一時停止しないようにする。

## 3. 動作を確かめる

SteamVRを起動して、次の状態になればよい。

- SteamVRのウィンドウに、仮想のヘッドセット（null）とトラッカーが出る。**Questは検出されない（これで正しい）。**
- Questの中はMetaのLinkのホーム画面のままで、SteamVRの画面に切り替わらない。
- トラッカーのアイコンが緑色になる。

## 4. 実験時の起動の順番

1. Quest 3をQuest Linkでつなぐ（Questのクイック設定 → Quest Link）。
2. トラッカーのドングルをPC本体のUSB端子に直接さし（ハブは使わない）、トラッカーとベースステーションの電源を入れる。
3. SteamVRを起動し、トラッカーが緑色で認識されていることを確かめる。
4. Unityでシーンを再生する。

## Unity側のOpenXRランタイム

実験用PCでは、Windowsの標準のOpenXRランタイムがSteamVRになっている。そのままだと、UnityがSteamVR経由でQuestにつなごうとして失敗する（Editor.logに `Non-Oculus OpenXR runtime is not supported.` と出る）。Windowsの設定は変えず、プロジェクト側でMetaのランタイムを指定する。

- エディタ：`Assets/ExtraLimb/Editor/MetaOpenXRRuntimeSelector.cs` が、エディタの起動時とPlayの直前に、環境変数 `XR_RUNTIME_JSON` をMetaのランタイム（`C:\Program Files\Oculus\Support\oculus-runtime\oculus_openxr_64.json`）に設定する。
- 本番用にビルドしたアプリ：起動用のバッチファイルで同じ環境変数を設定してから起動する（実装方針のStep 6-1で用意する）。

## うまくいかないとき

| 症状 | 原因と対処 |
| --- | --- |
| Questの中がSteamVRの画面になる | 2の設定が入っていない。SteamVRを終了してから設定し直す |
| Unityのゲーム画面に「SteamVRが起動していません」と出る | SteamVRを起動してから再生し直す |
| トラッカーが出ない | ドングルがPCにささっていないか、トラッカーの電源が入っていない。ランプが青の点滅なら、SteamVRのメニュー → デバイス → コントローラーのペアリング → HTC VIVE Tracker でペアリングし直す |
| Questに映像が出ない（Editor.logに `XR_ERROR_FORM_FACTOR_UNAVAILABLE`） | Quest Linkがつながっていない。Questのクイック設定からQuest Linkを開始する |

## 元に戻す

この設定のままでは、SteamVRでヘッドセットを使うアプリ（研究室の他のSteamVR用アプリなど）が動かない。元に戻すときは、SteamVRを終了してから、バックアップを `steamvr.vrsettings` という名前に戻す。
