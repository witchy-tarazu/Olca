# 仮UI部品素材

`GenerateCyberUi.cs` はUnity MCP `run_script`（file=`Tools/UI/GenerateCyberUi.cs`, entry=`GenerateCyberUi.Main`）で実行する。Unity EditorのTexture2D/PNG encoderと正規TextureImporterだけで6部品を作る。素材に文字・カード絵・背景絵は含めない。

出力は `Assets/Resources/OlcaUI/`。256×256 RGBA、透明な切り欠き角、静かな内面、32pxの9-slice辺、mipmapなし、圧縮なし。window、description、通常/選択/押下/無効ボタンを同じ枠意匠にする。Image.Type.Slicedで使用し、カード絵/専用ミニアイコンは上書きしない。SpriteSwapで選択/フォーカス/無効を表現する。

`BgCustom` は対象外。Sceneに設定された既存Sprite・色・配置を維持する。カスタム画面の背景や戦闘背景をこの部品で置換しない。元背景の上に置くHP/コスト/説明は白文字、明るいボタン面上の文字は濃色にする。

現在の実画面確認はカスタム画面と同時表示のHUDを中心にFHD/4Kで実施。共通UiLook経由の部品へも適用するが、全画面の個別レイアウト最終確認という意味ではない。カード本絵未受領は別の残件で、この枠をカードイラストとして扱わない。
