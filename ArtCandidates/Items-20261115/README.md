# 毒消しアイコンの仮素材

作成日: 2026-11-15。ユーザー指示「素材と演出は仮で足りない部分はあったら作成して補ってほしい」に基づく補完。

- ファイル: `antidote_vial_v1.png`
- 用途: 毒・猛毒等を解除する所持アイテムの画像候補。毒状態アイコンとは別用途。
- 参照: `Assets/Art/UI/Pickups/heal_vial.png`の小瓶・輪郭を視覚言語の参照に使用。
- 実行方式: 組み込みimagegen。既存画像の置換ではなく新規生成。
- 実ファイル: 1254×1254、32bit ARGB、左上alpha0。緑の液体と葉ラベルで回復薬と区別する。
- SHA256: `4BF95823F429B0523ACEFB0C0D5FB137ADBEBF79CB0E1020E5CC5AF419A06C8B`
- 原画生成時は既存素材の上書き、画像の加工、.meta生成、C#変更を行っていない。生成原画はドット風の大判画像であり、32px単位の厳密なピクセル素材として検証済みとはしない。
- 追加対応で同一PNGバイトを`Assets/Resources/OlcaPickups/antidote_vial_v1.bytes`へ格納し、探索ショートカットHUDへ接続した。Pointフィルター・mipmap無効で表示する。実Playの640×360およびFHD画面で、既存回復薬との区別と下側の所持数表示を確認した。meta生成はユーザー側が担当する。

## 最終プロンプト

Create one production-ready game inventory icon: a small antidote vial for a retro pixel-art action RPG. Reference the immediately preceding shown heal_vial image for its visual language only: chunky clean stepped pixel silhouette, dark charcoal outline, tiny glass bottle with stopper, restrained white gray highlights. New design: vivid emerald green liquid in a squat glass antidote bottle, a small cream label with a simple dark green leaf silhouette (no text). Unambiguous readable at 32x32 or 48x48 pixels. One object only, centered, square canvas with generous transparent margin, no cast shadow, no glow, no frame, no letters, no skull, no character. Crisp pixel-art at a consistent pixel grid, nearest-neighbor appearance rather than smooth illustration. Transparent background with genuine alpha. This is a new icon, do not modify or replace the existing healing vial. Save the generated asset for use in the Olca Unity project.
