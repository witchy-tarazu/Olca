# Stage37 independent static review

必須未解決指摘: 0（対象4 C#の表示差分）。

basic IDだけFormatDepartureStyleNameで「通常状態 / 無属性（カムイなし）」を表示し、他の登録名は保持。Choice formatterも同helperを利用し、Controllerの拠点/帰還2経路も同じ表示名へ接続。調べたstyle表示経路に旧SelectedStyle.Nameの残留はない。既存UI Text変換で漢字だけ読みへ変換し、ひらがなは保持する。

Core basic.Name/ID/Attack、Capture、保存DTO/hash・戦闘値は変更しない。更新された既存回帰は表示文字列、カタカナ変換、旧Capture不変、basic旧名/Attacknullと他の登録名/属性/解放条件を厳密に確認する。キック能力は追加/表示しない。

限界: compile/実テスト/native geometryは未確認。表示が長くなるため一覧/比較面/拠点帰還ラベルの実画面を3比率で確認する必要がある。レビュー役はUnity操作、製品編集、git操作を行っていない。

v2追加: Integrationの既存未解放表示/保存不変caseの選択スタイル文字列期待だけを新しい完全な表示へ更新。Containsの対象だけが変わり、未解放拒否/定義情報/選択/保存不変のassertは維持。期待緩和ではない。最終v2通常compile/テストは未確認。
