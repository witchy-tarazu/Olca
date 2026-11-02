# Stage33 independent static review

必須未解決: 1（変更6 C#の範囲）。

OutsideExplorationView.RenderのhandNamesへCard.Nameを直接代入している。登録可能な漢字名カードが新HUDでは生表示され、既存UIの漢字のみカタカナ表示要件を通らない。表示時CustomDisplay.Katakanaを適用し、元Card.Name不変/ひらがなLatin保持の回帰を追加する。

他の確認: DepartureDeckRulesは20枚/所持/同名とレギュレーションを検証しCard.Cloneで独立個体を作る。DrawCards3は既存random抽出で重複なし、Deck.Shuffleを変更しない。拒否時にはSession.Cardsを作らず、成功初回だけ代入し、表示中/再初期化は拒否する。Root Sessionで手札/引いた履歴を保持し、View再生成で再抽選しない。

Viewは名前/cost/miniのみread-only、raycastfalse。Bindingはicon resolverを借用しDisposeで返し、View.Hide/OnDisableでsprite参照を外す。Controllerの外界binding解放はCustomの所有art解放より先。原本/importSpriteを新たに破棄しない。

所持は既存(Id,Level)集計契約を共用。製品探索デッキの登録/選択/保存は未接続で、検証用20枚を製品デフォルトと扱わない。使用/補充/リロード/効果/消費順序も未実装。

限界: compile/test/native未確認。RNG例外時の外部RNG状態巻戻し保証は追加していない。実画面の長名/最大cost/3比率、Custom artwork owner終端後参照解除、host/Single再入を別途確認。Unity/test/git操作や製品編集は行っていない。
