# MCP サーバー設定

Claude Code に MCP サーバーを登録する方法は2種類あります．

## 1. プロジェクトレベル（推奨・このワークスペース用）

ワークスペースルートの `.mcp.json` が自動的に読み込まれます．

## 2. グローバルレベル（全プロジェクト共通）

```powershell
# Obsidian MCP をグローバル登録
claude mcp add obsidian -s user -- npx -y obsidian-mcp-server

# Home Assistant MCP をグローバル登録
claude mcp add homeassistant -s user -e HA_URL=https://YOUR_URL:8123 -e HA_TOKEN=YOUR_TOKEN -- uvx hass-mcp

# Sequential Thinking をグローバル登録
claude mcp add sequential-thinking -s user -- npx -y @modelcontextprotocol/server-sequential-thinking

# Memory をグローバル登録
claude mcp add memory -s user -e MEMORY_FILE_PATH=YOUR_PATH -- npx -y @modelcontextprotocol/server-memory

# GitHub をグローバル登録
claude mcp add github -s user -e GITHUB_PERSONAL_ACCESS_TOKEN=YOUR_TOKEN -- npx -y @modelcontextprotocol/server-github

# Brave Search をグローバル登録
claude mcp add brave-search -s user -e BRAVE_API_KEY=YOUR_KEY -- npx -y @modelcontextprotocol/server-brave-search

# Markitdown をグローバル登録
claude mcp add markitdown -s user -- uvx markitdown-mcp

# Filesystem をグローバル登録
claude mcp add filesystem -s user -- npx -y @modelcontextprotocol/server-filesystem YOUR_BASE_PATH

# Docker をグローバル登録
claude mcp add docker -s user -- npx -y @modelcontextprotocol/server-docker
```

---

## 各MCPサーバーのセットアップ手順

### obsidian（obsidian-mcp-server）

前提: Node.js / npm が必要

#### 準備: Obsidian REST API の設定
1. Obsidian で「Local REST API」プラグインをインストール・有効化
2. プラグイン設定で API Key を生成
3. 発行されたキーを `.mcp.json` の `OBSIDIAN_API_KEY` に設定

```powershell
# 動作確認（初回はパッケージが自動ダウンロードされる）
npx -y obsidian-mcp-server
```

#### `.mcp.json` の編集箇所
```json
"OBSIDIAN_API_KEY": "YOUR_API_KEY_HERE",
"OBSIDIAN_API_URL": "https://localhost:27124"
```

使用できる主なツール:
- ノートの検索、読み込み、作成、更新
- タグやフォルダの管理

---

### homeassistant（hass-mcp）

前提: Python / uvx（uv）が必要

#### 準備: Home Assistant でトークンを発行
1. Home Assistant の左下 → プロフィール
2. 「長期アクセストークン」→「トークンを作成」
3. 発行されたトークンを `.mcp.json` の `HA_TOKEN` に設定

```powershell
# uv が未インストールの場合
powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
```

#### `.mcp.json` の編集箇所
```json
"HA_URL": "https://YOUR_HOMEASSISTANT_URL:8123",
"HA_TOKEN": "YOUR_LONG_LIVED_ACCESS_TOKEN"
```

使用できる主なツール:
- `get_states` — エンティティの状態取得
- `call_service` — サービス（照明ON/OFF等）の実行
- `get_history` — 履歴の取得

---

### sequential-thinking

前提: Node.js / npm が必要

複雑な問題を段階的に考えるための思考支援ツール。設定不要。

```powershell
npx -y @modelcontextprotocol/server-sequential-thinking
```

---

### memory

前提: Node.js / npm が必要

会話の記憶を永続化するためのメモリサーバー。

#### `.mcp.json` の編集箇所
```json
"MEMORY_FILE_PATH": "YOUR_MEMORY_FILE_PATH"
```

使用できる主なツール:
- `create_memory` — 記憶の作成
- `retrieve_memory` — 記憶の取得
- `delete_memory` — 記憶の削除

---

### github

前提: Node.js / npm が必要

#### 準備: GitHub Personal Access Token の発行
1. GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
2. Generate new token で必要な権限を選択
3. 発行されたトークンを `.mcp.json` の `GITHUB_PERSONAL_ACCESS_TOKEN` に設定

#### `.mcp.json` の編集箇所
```json
"GITHUB_PERSONAL_ACCESS_TOKEN": "YOUR_GITHUB_TOKEN"
```

使用できる主なツール:
- リポジトリの検索・操作
- Issue/PR の管理
- ファイルの読み書き

---

### brave-search

前提: Node.js / npm が必要

#### 準備: Brave Search API Key の取得
1. [Brave Search API](https://brave.com/search/api/) でアカウント作成
2. API Key を発行
3. 発行されたキーを `.mcp.json` の `BRAVE_API_KEY` に設定

#### `.mcp.json` の編集箇所
```json
"BRAVE_API_KEY": "YOUR_BRAVE_API_KEY"
```

使用できる主なツール:
- Web検索
- ニュース検索

---

### markitdown

前提: Python / uvx（uv）が必要

様々なファイル形式をMarkdownに変換するツール。設定不要。

```powershell
uvx markitdown-mcp
```

使用できる主なツール:
- PDF, Word, Excel等をMarkdownに変換

---

### filesystem

前提: Node.js / npm が必要

指定したディレクトリ配下のファイルシステムにアクセス。

#### `.mcp.json` の編集箇所
```json
"args": [
  "-y",
  "@modelcontextprotocol/server-filesystem",
  "YOUR_BASE_PATH"
]
```

使用できる主なツール:
- ファイルの読み書き
- ディレクトリの操作

---

### docker

前提: Node.js / npm、Docker が必要

Dockerコンテナの管理ツール。設定不要。

```powershell
npx -y @modelcontextprotocol/server-docker
```

使用できる主なツール:
- コンテナの起動・停止
- イメージの管理
- ログの取得

---

## 登録済みMCPの確認

```powershell
claude mcp list
```

---

## 参考

- [obsidian-mcp-server (npm)](https://www.npmjs.com/package/obsidian-mcp-server)
- [hass-mcp (GitHub)](https://github.com/tevonsb/hass-mcp)
- [MCP Servers (Model Context Protocol)](https://github.com/modelcontextprotocol/servers)
- [Claude Code MCP 公式ドキュメント](https://docs.anthropic.com/claude-code/mcp)
