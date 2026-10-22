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
