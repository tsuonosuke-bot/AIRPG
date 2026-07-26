#!/usr/bin/env python3
"""公開後に書き換えたファイルの整合性ハッシュを service-worker-assets.js へ反映する。

Blazor WebAssembly のサービスワーカーは service-worker-assets.js に載っている
sha256 ハッシュ付きでアセットを取得する。GitHub Pages のサブパス対応で index.html の
<base href> を書き換えると、このハッシュと実ファイルが食い違ってインストールに失敗し、
オフライン動作とホーム画面への追加が効かなくなる。書き換えたファイルのハッシュを
計算し直して差し替える。

usage: fix_sw_integrity.py <wwwroot> <書き換えたファイル> [...]
"""

import base64
import hashlib
import json
import re
import sys
from pathlib import Path

ASSETS_FILE = "service-worker-assets.js"


def sha256_integrity(path: Path) -> str:
    return "sha256-" + base64.b64encode(hashlib.sha256(path.read_bytes()).digest()).decode()


def main(argv: list[str]) -> int:
    if len(argv) < 3:
        print(__doc__, file=sys.stderr)
        return 2

    wwwroot = Path(argv[1])
    changed = argv[2:]

    assets_path = wwwroot / ASSETS_FILE
    if not assets_path.exists():
        print(f"{assets_path} がないためスキップします")
        return 0

    text = assets_path.read_text(encoding="utf-8-sig")
    match = re.search(r"self\.assetsManifest\s*=\s*(\{.*\});?\s*$", text, re.S)
    if not match:
        print(f"{ASSETS_FILE} の形式を認識できませんでした", file=sys.stderr)
        return 1

    manifest = json.loads(match.group(1))
    by_url = {asset["url"].replace("\\/", "/"): asset for asset in manifest["assets"]}

    for name in changed:
        asset = by_url.get(name)
        if asset is None:
            print(f"警告: {name} は {ASSETS_FILE} に登録されていません", file=sys.stderr)
            continue
        target = wwwroot / name
        if not target.exists():
            print(f"警告: {target} がありません", file=sys.stderr)
            continue
        updated = sha256_integrity(target)
        print(f"{name}: {asset['hash']} -> {updated}")
        asset["hash"] = updated

    assets_path.write_text(
        "self.assetsManifest = " + json.dumps(manifest, ensure_ascii=False, indent=2) + ";\n",
        encoding="utf-8",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
