# ExtraLimbsWS

部位置換型Beyond Real Interaction（本来の身体部位のトラッキングで、別の部位から生えた追加肢を操作する）の探索的ワークショップ用Unityプロジェクト。

## 環境

- Unity 6000.3.10f1（6.3 LTS）、URP
- Meta Quest 3（Quest Link、Metaのランタイム）
- VIVEトラッカー（SteamVRをヘッドセットなしで起動し、OpenVR APIで読む）
- Final IK（VRIK、LimbIK）、Meta XR SDK

## フォルダ

- `Assets/ExtraLimb/` … 本プロジェクトで作るコード・シーン・プリセット
- `Assets/Models/` … アバターのFBX
- `Assets/Scenes/SampleScene.unity` … 試作のシーン（Final IKの設定例）

## 注意

- 実験データは `ExperimentData/` に保存し、リポジトリには入れない。
- Final IKはAsset Storeの有料アセットのため、リポジトリは非公開のままにする。
