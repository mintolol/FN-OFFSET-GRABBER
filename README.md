# Fortnite Offset Grabber / Oodle Repacker

CUE4Parse を使って Fortnite (Unreal Engine 6) の `.utoc` / `.ucas` からアセットのオフセット情報を取得し、改変した `.uasset` をその場でリパックするツールです。

---

## 必要環境

| 項目 | バージョン |
|------|-----------|
| .NET Runtime | 10.0 以上 |
| OS | Windows x64 |
| Oodle DLL | `oo2core_*_win64.dll` または `oo2core.dll` |

### Oodle DLL の入手

Oodle DLL は別途用意する必要があります。

入手した DLL を **exe と同じフォルダに配置**するだけで自動認識されます。

---

## ビルド

```bash
dotnet publish -c Release -r win-x64
```

`bin\Release\net10.0\win-x64\publish\FortniteOffsetGrabber.exe` が生成されます。

---

## 使い方

### 1. ドロップモード（最も簡単）

`config.json` に `PaksDirectory` と `AssetPath` を設定しておいた状態で、改変済み `.uasset` を **exe にドロップ**するだけでリパックが完了します。

```
FortniteOffsetGrabber.exe
└─ oo2core.dll          ← Oodle DLL をここに置く
└─ config.json          ← PaksDirectory と AssetPath を設定
```

`.uasset` を exe アイコンにドラッグ＆ドロップ → 自動でリパック実行。

---

### 2. コマンドライン

```
FortniteOffsetGrabber.exe [オプション]
```

#### 主なオプション

| オプション | 説明 |
|-----------|------|
| `--paks="<パス>"` | Fortnite の Paks フォルダ |
| `--asset="<パス>"` | 対象アセットのゲーム内パス |
| `--repack` | リパックモードを有効化 |
| `--input="<パス>"` | 改変済み `.uasset` のパス |
| `--output="<パス>"` | 抽出先フォルダ（デフォルト: `./Extracted`） |
| `--export-compressed` | 圧縮済みブロックをファイルに書き出す |
| `--verify` | リパック後に再マウントして内容を検証 |
| `--oodle-dll="<パス>"` | Oodle DLL を明示的に指定 |
| `--oodle-compressor=Kraken` | 圧縮アルゴリズム（Kraken / Mermaid / Selkie / Leviathan） |
| `--oodle-level=Normal` | 圧縮レベル（HyperFast4 〜 Optimal9） |
| `--no-backup` | バックアップ作成をスキップ |

#### 例: オフセット確認のみ

```
FortniteOffsetGrabber.exe ^
  --paks="C:\Program Files\Epic Games\Fortnite\FortniteGame\Content\Paks" ^
  --asset="FortniteGame/Content/Athena/Items/Weapons/WID_Assault_AutoHigh.uasset"
```

#### 例: リパック

```
FortniteOffsetGrabber.exe ^
  --paks="C:\Program Files\Epic Games\Fortnite\FortniteGame\Content\Paks" ^
  --asset="FortniteGame/Content/Athena/Items/Weapons/WID_Assault_AutoHigh.uasset" ^
  --repack ^
  --input="C:\mod\WID_Assault_AutoHigh.uasset"
```

---

## config.json

初回起動時に exe と同じフォルダに自動生成されます。よく使う設定を保存しておけばコマンドライン引数を省略できます。

```json
{
  "PaksDirectory": "C:\\Program Files\\Epic Games\\Fortnite\\FortniteGame\\Content\\Paks",
  "AssetPath": "",
  "OutputDirectory": "",
  "OodleDll": "",
  "OodleCompressor": "Kraken",
  "OodleLevel": "Normal",
  "NoBackup": false
}
```

| キー | 説明 |
|-----|------|
| `PaksDirectory` | Fortnite の Paks フォルダパス |
| `AssetPath` | ゲーム内アセットパス（ドロップモードで参照） |
| `OutputDirectory` | 抽出先（空欄で `./Extracted`） |
| `OodleDll` | Oodle DLL の絶対パス（空欄で自動検索） |
| `OodleCompressor` | 圧縮アルゴリズム |
| `OodleLevel` | 圧縮レベル |
| `NoBackup` | `true` でバックアップ作成をスキップ |

---

## ログの見方

```
[FOUND]   アセットが Paks 内で見つかった
[INFO]    詳細情報（UTOC/UCAS パス、サイズ、ブロック番号など）
[OFFSET]  UCAS 内の物理オフセット（HxD などで直接確認する際に使用）
[REPACK]  リパック処理を開始
[OK]      処理成功
[SUCCESS] 検証・バックアップなどのサブ処理成功
[WARN]    非致命的なエラー（処理は継続）
[ERROR]   致命的なエラー（処理中断）
[DROP]    ドロップモードで起動していることを示す
```

### オフセット関連ログの詳細

```
[INFO] UTOC: pakchunk35-WindowsClient.utoc
[INFO] UCAS: pakchunk35-WindowsClient.ucas
[INFO] Size=4,740, Block=38,220, Offset=0x954C0000
```
- `Block` : 圧縮ブロックのインデックス
- `Offset` : ファイル内の論理オフセット

```
[OFFSET] UCAS=0x16116000, Offset=0x16116000, Method=1
```
- `UCAS=` : UCAS ファイル内の物理オフセット（HxD で直接参照できる値）
- `Method` : 圧縮方式のインデックス（1 = Oodle など）

```
[INFO] HxD Exact=0x...
```
- 非圧縮ブロックの場合のみ表示。HxD でそのオフセットに直接データがある。
- 圧縮ブロックの場合は `N/A (compressed block)` と表示。

---

## リパック後のファイル

リパックは **UCAS を直接書き換える**方式です。新しいファイルは生成されません。

```
書き換えられるファイル:
  [Paks]\pakchunkXX-WindowsClient.ucas

自動作成されるバックアップ:
  [Paks]\pakchunkXX-WindowsClient.ucas.backup
```

元に戻すには `.ucas.backup` を `.ucas` にリネームしてください。

---

## モード一覧

| モード | 起動方法 | 説明 |
|--------|---------|------|
| **オフセット確認** | フラグなし | アセットのオフセット・ブロック情報を表示して終了 |
| **リパック** | `--repack --input=` | 改変 uasset を Oodle 圧縮して UCAS に書き込む |
| **圧縮エクスポート** | `--export-compressed --input=` | 圧縮済みブロックをファイルに書き出す（UCAS は変更しない） |
| **検証** | `--repack --verify` | リパック後に再マウントして内容が一致するか確認 |
| **ドロップ** | uasset を exe にドロップ | `--repack` を自動設定して実行 |

---

## 注意事項

- このツールは **オフライン環境での研究・学習目的**で作成されています。
- Fortnite のオンライン対戦での使用はアカウント BANの原因になります。
- Oodle ライブラリは Epic Games / RAD Game Tools が権利を保有しています。DLL の再配布は行わないでください。

---

## 依存ライブラリ

- [CUE4Parse](https://github.com/FabianFG/CUE4Parse) — Unreal Engine アセットパーサー
