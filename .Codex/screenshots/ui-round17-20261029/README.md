# 保存待ち・空一覧・未解決定義からの復帰（2026-10-29）

View用の隔離データで通常／保存待ち／定義未解決／空一覧／復旧を切り替える。実保存障害を起こさず、製品の欠けた定義を補わない。PNGは切抜き・縮小・合成なしのGameView原本。寸法・SHA256はmanifest参照。

- unknown：無効になったカード詳細から「削除」へ焦点が移った状態を再現。修正後は有効な詳細→行情報→戻るの順で安全な既定へ戻す。未解決行の削除可否自体は変えない。
- ready：保存待ちの解除で消えたRetryの履歴を解除し、有効な既定のカード詳細へ戻す。
- 有効な選択は保持。空一覧から復旧した際の「戻る」も維持し、保存デッキ編成の既定Save／pendingのRetryは変えない。

| 対象 | 解像度 | 修正前 | 修正後 |
| --- | --- | --- | --- |
| unknown | 4by3 | [before](unknown-before-4by3.png) | [after](unknown-after-4by3.png) |
| unknown | fhd | [before](unknown-before-fhd.png) | [after](unknown-after-fhd.png) |
| unknown | 4k | [before](unknown-before-4k.png) | [after](unknown-after-4k.png) |
| ready | 4by3 | [before](ready-before-4by3.png) | [after](ready-after-4by3.png) |
| ready | fhd | [before](ready-before-fhd.png) | [after](ready-after-fhd.png) |
| ready | 4k | [before](ready-before-4k.png) | [after](ready-after-4k.png) |

[空一覧](empty-after.png)／[定義復旧後](restored-after.png)。

切替2周で無効な詳細・旧削除・旧Retryのcallback拒否、未解決行の実情報閲覧、空案内の消去、詳細の再有効化、既存Close焦点保持を確認。業務callback0・専用セーブハッシュ不変。実機入力・実保存障害・製品定義の復旧を確認した扱いにしない。

環境切断後、保存済み完了結果を回収して既存実行を重複起動していない。Unity Pipelineの実行要求中断ログを別記録として保持し、最終全件は復旧後に640×360固定で実施。結果は Planning/既存UI監査と図鑑表示修正-20261028.md の保存待ちと定義復旧節を参照。
