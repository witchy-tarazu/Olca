# Stage32 independent static review

必須未解決指摘: 0（対象2ファイルの静的レビュー範囲）。

管理HideでruntimeをDetachしてからView参照をCLR nullへ戻し、実View.Hideで入力/menu/presentation leaseを解放する。正常Back、別画面への切替、CloseCurrentの3経路で、閉じた実体の後続DestroyをController.HasLostViewの復旧要求に誤分類しなくなる。

表示中の自然DestroyはView.OnDisable→context.NotifyHiddenであり、登録Hide callbackを呼ばないため、破棄済みwrapperを残す従来のHasLostView回復は維持される。失敗/取消はViewManagerの既存rollbackを使い、同entry再生成では新実体を旧Hideで消さない既存処理とも整合する。

新回帰は3閉鎖経路、旧Back無作用、hub表示/focus、再生成回数、Root Core/gauge identityとframe不変を厳密に確認する。EditModeは明示OnDestroyを用いるため、自然Play破棄の保証ではない。

入力、Core計算、保存値・保存順の変更なし。Source hashesはsystem-stage32-independent-source-hashes.json参照。

限界: テスト/compile/native結果は本レビューで未確認。自然activeDestroy再生成、閉鎖後Destroy非再入、host再入と保存不変は実検証が必要。Unity操作・テスト起動・製品編集・git操作は行っていない。
