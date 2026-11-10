# Country flags

The Peers tab shows these next to each peer's country: Windows' emoji font draws flag emoji as two letters.

- Source: [flag-icons](https://github.com/lipis/flag-icons) by Panayiotis Lipiridis, version 7.6.0
  (commit 0d8909b00aa8418802a5abf2afa82356b129a7ef), MIT licence (`LICENSE` here).
- Only the 4×3 flags named by a two-letter ISO 3166-1 code (the app lowercases the code `CountryDatabase` returns), copied
  unchanged. Regions and organisations (`gb-eng`, `arab`, ...) are left out.
- None uses text, filters, masks, patterns, images or style sheets, which `SvgImageSource` (Direct2D) can't draw.
