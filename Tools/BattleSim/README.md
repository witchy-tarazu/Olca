# Unityを起動しない最小バトル実行

`Assets/Olca/Core/**/*.cs` を直接コンパイルする。戦闘の模倣版やUnity型のスタブ、Unity DLLは使わない。ロジックの複製も行わない。

## 通常の.NET SDK

.NET 6参照パックを持つSDKで実行する（外部NuGet依存なし）。この作業VMにはSDKがなく、この経路自体は未実行。下の既存ランタイム経路で実コンパイル・実行を確認した。

```sh
dotnet run --project Tools/BattleSim/Olca.BattleSim.csproj -- --seed 123 --frames 240 --verify
```

標準出力は1 Stepあたり1行の状態・イベント列。`--batch 31 --work 2000` はフロントエンド側のフレームまとめ処理・計算負荷を変える。`--verify` は幅1/7/31/240と負荷を変えた結果を照合し、失敗なら終了コード1にする。経過時間・描画FPSはゲーム入力ではない。

## SDKがない既存環境

既存のMicrosoft .NET 6ランタイムとRoslyn 4.3（C#9）を明示する。ビルドスクリプトは専用出力ルートを必須にし、共通設定領域との包含・リンク・ソース領域への出力を拒否する。パッケージ取得やSDKの追加インストールはしない。

```sh
python3 Tools/BattleSim/build_existing_runtime.py \
  --runtime-root /path/to/microsoft-dotnet-runtime \
  --compiler-root /path/to/roslyn \
  --output-root /path/to/dedicated-proof \
  --forbidden-root /path/to/existing-game-settings-parent

/path/to/microsoft-dotnet-runtime/dotnet \
  /path/to/dedicated-proof/bin/Olca.BattleSim.dll --seed 123 --frames 240 --verify
```

作業VMでは、既存配布物のMicrosoftランタイム/コンパイラだけを `/workspace/work/Olca-pure-battle-proof-20261022/toolchain/` へ通常コピーした。そこにはUnity DLLがない。`DISPLAY`/`XAUTHORITY`を外し、`DOTNET_ROOT`をこのコピーへ限定してビルド・実行した。Unity Editorプロセスの起動やEditorアセンブリのロードは不要。既存Editorは保護のため停止していない。

## 最初のシナリオとAPI

`BattleSimulationSettings` はHPとタメ/間隔を明示し、`BattleSimulation(settings, seed)` に渡す。`Step(new BattleFrameCommand(logicalInput, userPaused))` は整数フレームを1回だけ処理する。`StepIndex`は要求したフレーム、`Battle.LogicalFrame`は停止・終了を除く論理フレーム。入力はコピーして使用する。

最初の外部検証シナリオは、HP100、敵HP120/土属性、位置(0,0)/(2,0)、既存ソード/回復の2枚、敵位置の草むら回復1、毒1/30フレーム/120フレームを明示した固定fixtureである。製品データや初期資源を変更するものではない。

- 1: 右移動。20/30: カード使用。
- 40: タメ開始。50〜64: ユーザーポーズ。65: 再開。70: 新しい押下でタメ再開、80: 解放。
- 100以降: 16フレーム間隔の短押し。240 Stepまで進める。
- 判定、HP合算、AI、毒、地形、前後隙は既存BattleSystemのまま。基本攻撃入力もUnityで使うものと同じCore実装。

`BattleFrameObservation` はStep/論理フレーム/HP/勝敗と、そのフレームのイベントを返す。`Canonical`は比較用の状態列（位置・敵・カード・行動相・地形・乱数状態等）。完全な保存DTOや全内部状態のハッシュではない。状態の保存復帰は既存機構を維持し、この入口では未対応。

## 境界と未完了

- 待機は純C# `FrameTask`。完了時に同じ所有スレッドで継続し、Unity PlayerLoop、同期Context、ThreadPool、壁時計を参照しない。並行Step/任意のバックグラウンド変更はサポートしない。`AsTask()`は外部観測用で、Core進行の時計には使わない。
- Unityは既存の入力サンプリング/描画/音/演出を担当。既存Applicationの基本攻撃入力型は互換アダプターでCoreを継承する。
- `.NET`側の`OLCA_PORTABLE`はCardMasterのDB注釈だけを外す。同じ実カードクラス/効果コードを使い、模倣属性は定義しない。Unity側のMasterMemory/MessagePack定義と保存形式は維持する。
- Core全ソースはコンパイル対象だが、CLIのコマンド入口はこの最小戦闘だけ。カスタム選択、変身選択、探索、報酬、製品DB読込、保存復帰、全カード/全AIデータは網羅していない。未対応カスタム入力は明示的に拒否する。
- 探索計画の既存`System.Random`や保存IDの`Guid`は今回の決定論的戦闘経路に含めない。全Coreや全製品進行の決定論を保証したとは扱わない。

## 確認結果

Unity MCPで同じCoreソースとUnity入力互換アダプターを実行し、240行をCLI出力と照合。幅1/7/31/240・追加計算負荷で全行一致した。

SHA256: `4597d866f2c40ce9f2624e4e61942ee73efd10ecd9872c9585457f4ef48d8edc`

最終行: Step240 / 論理199 / Victory / プレイヤーHP85 / 敵HP0。結果・参照DLL一覧・ソース一覧は `/workspace/.olca-cloud/results/pure-battle-20261022/` および専用出力ルートの `compile-manifest.json` に保存。Unityの既存全件回帰はこれと別に必須で実行する。

### カスタム操作の同一Coreリプレイ

同じビルド手順の出力DLLへ `--custom --verify` を指定すると、カスタムから戦闘復帰・カード使用までの固定fixtureを再生します。`--seed`、`--batch`、`--work`も利用可能です。`--custom`では57受付ステップの固定列を使い、`--frames`は使いません。通常シナリオの`--frames`は従来どおりです。

`BattleSimulation`の追加コンストラクターへ `Func<IRandomProvider, BattleSystem>` と初回カスタム開始の有無を渡すと、初期状態・加工定義を明示注入できます。入力は `BattleFrameCommand(custom: new CustomScreenCommand(kind, handles))`。コマンドStepは戦闘論理フレームを進めず、操作受付番号と結果イベントを出します。通常の時間Stepは `new BattleFrameCommand(input)` を別に渡します。同じStepに両者を混ぜず、カスタム操作と戦闘入力を別々に記録してください。

個体ハンドルは `Battle.CustomScreen.Commands.Observe()` の手札/選択観測から取得します。消費・Undoで別参照になった個体には新番号が付きます。ID・同名・定義違いの推測選択はしません。プレビューと加工取消は無消費、加工実行後は逆順Undo、確定後は `ReturnToBattle` で復帰します。生成カードの再加工も既存Coreが扱います。拒否時にもコードと理由が観測できます。

`CustomBattleReplay.Commands(seed)`は独立した同一Coreの計画用インスタンスで、復元コピーを含む対象ハンドルを確定した不変コマンド列を作ります。実再生側ではその列を変えず適用します。トレースには対象番号・受付結果・手札等の定義hashを含めます。fixtureは製品カード登録/ゲーム保存を変更しません。これはカスタム操作の第2区切りで、変身、製品DB投入、保存復帰コマンドは未移行です。

### 変身操作と自然解除のリプレイ

`--transformation --verify`で、変身の選択・加工連鎖取消・発動・演出停止・バリア/弱点解除と、通常のゲージ充填を使う3フェーズの自然終了を比較します。`--seed`、`--batch`、`--work`が使え、固定シナリオのため`--frames`は使用しません。製品データを追加せず、`TransformationBattleReplay`の明示fixtureだけを使います。

入力例は `new CustomScreenCommand(CustomCommandKind.PreviewTransformation, "fixture-a")`、取消は `CancelTransformation`。選択カードがあれば1枚解除し、次の取消でその画面の仮変身を戻す既存規則です。発動は`Confirm`、画面終了は`ReturnToBattle`です。確定済み変身の任意解除コマンドはありません。弱点命中・持続回数切れ・戦闘終了の解除は同じ戦闘Coreが行い、観測とイベントに出ます。

変身の残回数/使用済みID/演出残時間/耐性/攻撃性能は各受付・フレームの状態に含まれます。定義は作成時に`battle.Transformations.Configure(...)`で明示注入し、製品DB/保存復帰の投入経路はまだ別残件です。Unity側の通常Viewも同じ選択/取消コマンドを利用しますが、この検証でGUIコピーや物理実機を同期・操作したことにはなりません。

### 製品定義と既存戦闘開始セーブの輸送（最初の区切り）

`ExportProductBattle.cs`はUnity Editor側だけで実行する取得アダプターです。CLIのビルド対象には含めません。正規シーン`Assets/Scenes/DungeonScene.unity`を保存しないPreview Sceneで読み、実際のControllerの製品ファクトリー・マップ生成・定義登録を呼びます。値表をCLIへ手で複製しません。公開`Export(scenePath, sourceCheckpointPath, dedicatedRoot, replaySeed)`へ引数を明示できます。`Main`はこの作業の専用コピーと出力ルートを使う再現入口です。Unity MCP `run_script`のfileにこのファイル、entryに`ExportProductBattle.Main`を指定します。元セーブは読み取りのみで専用コピーを作り、出力は既存ValidationStorageGuardで制限。Play、Controller起動、Scene保存、設定操作はしません。

実証した出力は`/workspace/work/Olca-portable-product-proof-20261022/product-battle.json`です。既存手順でビルドしたDLLに `--package /workspace/work/Olca-portable-product-proof-20261022/product-battle.json --verify` を渡すと、Unity DLLなしで365ステップを再生します。`--batch`と`--work`が使えます。専用の比較シナリオのため`--frames`は使いません。CLIは入力を読みstdoutへトレースを出すだけで、ゲームの保存や設定を書きません。

DTOは正規カードの全コンストラクター値・Modifier・定義キー/hash、固定敵・耐性/強靭度/接敵定義、マップ/アイテム/ショップ/イベント/地形、必要な戦闘ルールを運びます。再構成したマップhash・カードカタログhashと既存CheckpointCodecの検証を通した後だけ開始します。同ID別定義はキー/hashで復元し、IDだけでは曖昧なDB検索を拒否します。保存の封筒codecもUnityと純C#で共通です。

**乱数の限界:** 旧チェックポイントにはゲーム用System.Randomのseed/内部状態がありません。初回手札・報酬候補は保存値を保ち再抽選せず、以後の比較用seedを輸送DTOに明示します。これは旧ライブセッションの全乱数列の復元ではありません。輸送DTOの`RandomState`/`RandomDraws`を明示した場合は、今回の純C#乱数状態から継続できます。元の保存形式にフィールドを追加したり、推測seedで元保存を書き換えたりしません。

**未対応:** BattleStart境界の通常／ダッシュ開始に対応します（下記参照）。製品変身定義、報酬受領後の攻略/永続保存への復帰コマンドは未対応です。欠損や未対応は例外/拒否として扱い、未定義データを作りません。任意の戦闘途中セーブは新設していません。既存保存層・DungeonSystemの開始時配置/地形/初回手札処理を共用し、通常経路の回帰はUnity MCPの全件テストで確認します。GUIコピーと実機は別の未確認です。

### 新規保存に含まれるRNG状態

新しく作るBattleStart計画は`RandomVersion=1`と`Random`状態を保存します。algorithm=`olca.xorshift32`/version=1、seed/state/Draws（32bit出力回数）が明示されます。マーカーがあるのに状態がない場合や未知版/不正値は拒否。必須JSONメンバー欠落も拒否します。初回手札・報酬の計画抽選は既存System.Randomのまま、戦闘専用streamの開始状態を別に固定し、保存タイミングは変えません。

新形式を`PortableBattlePackage.Capture`で輸送すると保存状態を取り込み、呼出元のfallback seedでは上書きしません。Unityの通常CheckpointBattleFactory復帰とCLIは同じ状態を復元します。カスタムを開く際の外部RNG指定も、新形式戦闘の追加ドローでは保存由来streamへ揃えます。現在の敵AIは優先順評価でRNG消費0であり、将来のためにAIContextへ同じstreamを渡すだけで新しい抽選を追加しません。

旧保存は状態/マーカーなしのまま互換読込し、Unityは従来外部RNG、CLIは明示fallbackを使います。旧ライブ列の完全復元はできません。元保存を書き換えず、保存済み手札・報酬を保持します。ライブstateを同じ境界ファイルへ自動書込する機能はなく、途中セーブを新設していません。

今回の新形式実証データは`/workspace/work/Olca-saved-battle-rng-proof-20261022/replay/product-battle.json`、隣接条件を明示した別fixtureは同ルートの`fixture/product-battle.json`です。既存DLL実行例の`--package`へ指定して`--verify`を付けます。製品配置での到達範囲と隣接fixtureを混同しないでください。旧GUIコピーは未同期です。

### ダッシュ接敵の開始効果

`CheckpointBattleFactory`は保存された接敵条件を`Battle.StartEffects`へ準備します。UnityのDungeonSystemは従来どおり、開始通知で設定・初回カスタムを準備した後に`Complete()`を呼びます。standaloneのBattleSimulationも同じ順序で同じCoreを呼びます。初期ダメージ・スタン・先制ゲージ・プレイヤー被害は既存の判定と量を使い、1インスタンスで再適用しません。

チェックポイントは効果適用前の開始境界を保つため、再開では新しいインスタンスへ一度だけ適用します。適用済みフラグをファイルへ書いてチェックポイント意味を変えることはしません。終了済みの戦闘には適用しません。旧保存の量省略は従来の15/180/0を維持し、旧ダッシュで効果自体が未指定なら新しい効果を補いません。

初撃のHP変化や終了イベントも観測できるよう`BattleSimulation.InitialObservation`を追加し、`PortableBattleReplay.Trace`の先頭に受付前のstep0を出します。通常365入力の場合は初期行を含め366行になります。開始条件・適用状態・スタン状態もトレースに含めます。新旧の保存値は変更しませんが、比較トレースの形式はこの追加分だけ拡張されます。

複数階層の封筒輸送は下記の入口で接続しました。階層移動コマンド、戦闘後保存、GUI同期は残件です。加工ルール輸送は下記に対応しました。

### 階層列のBattleStart輸送

`PortableBattlePackage.Capture(FloorSequenceDefinitions, bytes, rules, seed)`は、既存`floors.active.json`と同じチェックサム付き封筒を受け取る。`PortableFloorRoute`に全階層の正規マップ／カード定義と元封筒を保持し、既存`FloorSequenceDefinitions.TryRestore`でRouteId・Revision・RouteHash・FloorIndex・状態を照合する。単階層の旧パッケージも引き続き受け付ける。

Unity側で登録済みの階層列を渡し、各階層の定義を`PortableFloorDefinition.Capture`で変換する。CLI向けにカード表・敵表を別に記述しない。CLIは従来と同じ`--package path --verify`で選択階層の戦闘を再生する。階層移動コマンドや戦闘後保存は追加しない。加工ルールは下記の追加入口を使う。製品に登録されていない階層列を自動生成する機能でもない。

### 加工ルール輸送

`PortableProcessingCatalog.Capture(id, revision, cards, factory, fusionCatalog, tuningProvider)`は既存の登録表・ファクトリー・付与定義を値DTOへ変換する。ID/版/hash、定義hashによる材料・結果参照、通常融合表、特殊融合ID、登録済み超越先、調律条件・段階表を保持する。Restore後は既存FusionSystem／ExceedSystem／TuningSystemへ戻し、通常カスタムコマンドから使う。通常融合の計算や特殊優先をCLIに複製しない。未定義の上位カードを倍率計算で新しい製品登録にしない。

`PortableBattlePackage.Processing`へ設定してからSerializeする。`ExportProductBattle.cs`は本シーンのファクトリーとSimpleFusionRecipeProviderから取得し、カード表を別に書かない。現在製品の通常融合表は空なので、その空を維持する。旧パッケージはProcessingなしで従来どおり読む。定義付き調律素材と、チェックポイントの付与定義キー付き履歴も輸送する。

```sh
/path/to/dotnet /dedicated-proof/bin/Olca.BattleSim.dll \
  --package /dedicated-proof/package.json --processing --verify
```

`--processing`は手札で実行可能な超越・調律・融合についてプレビュー／取消／加工／消費済み再入力／Undoを通常コマンドで再生する。対象がない方式は実行しないため、実行数は手札と製品登録に依存する。任意途中保存・戦闘後保存・GUI同期は追加していない。
