# FortniteOffsetGrabber - UE6 x64

UE6 の Fortnite `.utoc/.ucas` から指定アセットを探し、次の3種類を保存します。

1. **展開済み `.uasset`**
   - CUE4Parse で読み出した通常のアセットデータ。
2. **`.block.bin`**
   - HxD の対象ブロック開始位置から `CompressedSize` 分だけ、そのままコピーした生の圧縮データ。
   - 今回の例なら `8C 0A 00 06 35 88 06 33 ...` から `0x63B` バイト。
3. **`.modslot.bin`**
   - HxD の対象ブロック開始位置から、**次の Compression Block の開始直前まで**コピーした元のブロックスロット。
   - 圧縮データの後ろにある `00 00 00 ...` のパディングも含めます。
   - Mod で元のUCAS領域を差し替える用途を想定したファイルです。

## HxDでの確認方法

たとえばログが次のようになった場合:

```text
[OFFSET] UCAS=0x2ED58800, Offset=0x2ED58800, Method=1
```

HxDでは `2ED58800` に移動します。
その場所から見える

```text
8C 0A 00 06 35 88 06 33 ...
```

が圧縮ブロックの先頭です。

`.block.bin` はこの圧縮データ本体だけを保存します。
`.modslot.bin` はさらにその後ろの `00` パディングを含め、**次のブロック直前まで**保存します。

Modで元のブロック領域を置き換える場合は、基本的に `.modslot.bin` 側を使うのが目的に近いです。

## 出力例

```text
Extracted\
└─ FortniteGame\
   └─ Content\
      └─ UI\
         └─ ExtensionWidgets\
            ├─ ExtensionSlot_Secondary.uasset
            ├─ ExtensionSlot_Secondary.uasset.block.bin
            └─ ExtensionSlot_Secondary.uasset.modslot.bin
```

実行すると `[MOD SLOT SAVED]` に以下を表示します。

- `Slot HxD Offset`
- `Compressed Size`
- `Full Slot Size`
- `Padding After Data`
- `Padding All Zero`
- `Next Block Offset`
- `First Bytes`
- `Last Bytes`

`Padding All Zero : True` なら、圧縮データの後ろが全部 `00` だったことを確認できます。

## 注意

- `.modslot.bin` は「元のUCAS上の物理領域」を保存するためのものです。CUE4Parseで展開した `.uasset` とは別物です。
- 圧縮ブロックは、そのままでは通常の `.uasset` として開けません。
- 差し替える圧縮データが元のスロットより大きくなる場合は、そのスロットに単純上書きできません。
- `.uasset` の完全なパッケージには `.uexp` / `.ubulk` 等が必要な場合があります。
