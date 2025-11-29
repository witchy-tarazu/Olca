# Application Layer

ロジックとビューを仲介する制御層です。

## 役割

- Core層のロジックとPresentation層のViewを繋ぐ
- MonoBehaviourを継承したControllerクラスを配置
- Update()でロジックにdeltaTimeを渡す
- イベントを監視してViewを更新

## 実装ガイドライン

- 薄く保つ（ロジックは書かない）
- ロジックとビューの橋渡しのみ
