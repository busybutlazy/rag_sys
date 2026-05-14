# RAG System

繁體中文 | [English](./README.md)

> **Vibe Coding 練習專案** — 以 Claude Code 作為主要實作者，逐 Phase 完成整個系統。每個 Phase 遵循「計畫 → 實作 → 審查 → 修補」的固定循環，讓 AI agent 負責完整的開發流程，人類負責定義意圖與審核結果。

---

## 這是什麼

一個全端的 **檢索增強生成（RAG）系統**，以 **AI 輔助軟體開發**為核心練習目標。整個專案由 Roadmap（`ROADMAP.md`、`ROADMAP2.md`）驅動，Claude Code 自主完成每個 Phase——撰寫程式碼、自我審查、提交 patch branch、開 Pull Request。

最終產物是一個可運行的產品，同時也是一份 AI agent 如何在真實多服務架構下做出工程決策的完整紀錄。

---

## 系統架構

```
[瀏覽器]
    │  port 5987（唯一對外開放的埠）
    ▼
[Frontend: React + Vite + TS]
    │  內部網路
    ▼
[BE Server: .NET 8 Web API]  ──→  [MySQL 8]
    │  內部
    ▼
[AI Server: Python FastAPI]  ──→  [LLM Gateway → OpenAI]
    │  內部
    ▼
[RAG Server: Python FastAPI] ──→  [ArangoDB]  (向量 + BM25 + GraphRAG 規劃中)
```

### 服務一覽

| 服務        | 技術棧               | 內部 Port | 對外開放 |
|-------------|----------------------|-----------|---------|
| frontend    | React + Vite + Nginx | 80        | **5987** |
| be-server   | .NET 8 Web API       | 8001      | 否       |
| ai-server   | Python FastAPI       | 8002      | 否       |
| rag-server  | Python FastAPI       | 8003      | 否       |
| mysql       | MySQL 8              | 3306      | 否       |
| arangodb    | ArangoDB 3.12        | 8529      | 否       |

只有前端 port 對 host 開放，所有服務間的通訊都在 Docker 內部橋接網路上進行。

---

## 功能（已完成的 Phase）

| Phase | 功能 |
|-------|------|
| 0 | 基礎設施與 Docker 腳手架 |
| 1 | JWT 身份驗證（登入、受保護路由） |
| 2 | Notebook 與內容管理（CRUD、檔案上傳） |
| 3 | LLM Gateway + 串流對話（OpenAI、SSE） |
| 4 | RAG Pipeline（PDF/DOCX 攝取 → 分塊 → 嵌入 → ArangoDB 向量搜尋） |
| 5 | 混合搜尋（BM25 + 向量 RRF 融合、效能基準工具） |
| 6 | AI Agent 系統（ReAct 迴圈、工具調用、Notebook 範圍搜尋） |
| 7 | 實驗儀表板與 RAG 配置 A/B 測試 |

強化階段（Phase 8–15）定義在 `ROADMAP2.md`，涵蓋：Auth session 正確性、攝取可靠性、上傳安全、測試套件、API 可維護性、可擴展性、多用戶隔離與正式部署。

目前備註：聊天對話 session orchestration 已經在 `main` 完成（持久化 chat sessions、messages、requests、tasks，以及前端 session 切換）。這和 Phase 8 的 auth/session hardening 是不同範圍，Phase 8 仍未完成。

---

## Vibe Coding 工作流程

本專案的開發方式是讓 Claude Code 按照 Roadmap 逐 Phase 完成，每個 Phase 遵循固定流程：

```
1. 計畫    →  實作計畫寫入 docs/superpowers/plans/
2. 建立分支 →  git checkout -b phase-N-<name>
3. 實作    →  Claude Code 按計畫實作，在 feature branch 上 commit
4. 審查    →  agent 撰寫 docs/reviews/phase-N-review.md
5. Patch   →  git checkout -b phase-N-patch（從 feature branch 建立）
6. Rebase  →  patch rebase 回 feature branch，merged --ff-only
7. PR      →  單一乾淨的 PR：phase-N-<name> → main
8. 合併    →  更新 ROADMAP，記錄學習心得
```

**人類的角色：**
- 在 Roadmap 中定義意圖與範圍
- 審查 PR 與程式碼審查文件
- 當 agent 偏離方向時介入導正
- 核准合併

**Claude Code 的角色：**
- 閱讀 Roadmap phase，撰寫實作計畫
- 實作、commit、自我審查
- 根據審查結果產出 patch branch
- 開 Pull Request

---

## 安裝

### 前置需求

| 工具 | 最低版本 | 備註 |
|------|---------|------|
| Docker | 24+ | |
| Docker Compose | v2（plugin） | 使用 `docker compose`，而非 `docker-compose` |
| OpenAI API Key | — | 用於 embedding 與對話補全 |

不需要在本機安裝 .NET、Python 或 Node——所有執行環境都在容器內。

---

### 步驟一 — Clone 專案

```bash
git clone <repo-url>
cd rag_sys
```

---

### 步驟二 — 設定環境變數

```bash
cp .env.template .env
```

開啟 `.env`，填入以下必要值：

| 變數 | 必填 | 說明 |
|------|------|------|
| `OPENAI_API_KEY` | 是 | 你的 OpenAI API Key（`sk-...`） |
| `ADMIN_USERNAME` | 是 | 內建單一使用者的登入帳號 |
| `ADMIN_PASSWORD` | 是 | 登入密碼，請使用強密碼 |
| `JWT_SECRET` | 是 | JWT 簽署密鑰，**最少 32 個字元** |
| `INTERNAL_SECRET` | 是 | 服務間內部呼叫的共享密鑰 |
| `MYSQL_ROOT_PASSWORD` | 是 | MySQL root 密碼 |
| `MYSQL_PASSWORD` | 是 | MySQL 應用程式用戶密碼 |
| `ARANGO_ROOT_PASSWORD` | 是 | ArangoDB root 密碼 |
| `ARANGO_PASSWORD` | 是 | ArangoDB 應用程式用戶密碼 |

> 其餘變數（`MYSQL_DATABASE`、`MYSQL_USER`、各服務 port 等）在 `.env.template` 中已有合理預設值，通常不需要更動。

---

### 步驟三 — 啟動所有服務

```bash
docker compose up --build
```

第一次啟動需要幾分鐘——Docker 需要拉取映像並建置各服務。

<!-- TODO: 補上 docker compose up 輸出的截圖 -->

---

### 步驟四 — 驗證

所有容器健康後，打開瀏覽器前往：

```
http://localhost:5987
```

應該會看到登入頁面，使用 `.env` 中設定的 `ADMIN_USERNAME` / `ADMIN_PASSWORD` 登入。

<!-- TODO: 補上登入頁截圖 -->

<!-- TODO: 補上主畫面截圖 -->

也可以觀察 compose logs，確認所有容器都顯示 `Healthy` 狀態。

---

### 開發模式（熱重載）

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up --build
```

此模式將原始碼目錄掛載進容器，修改後無需完整重建：

- **Frontend** — Vite dev server（HMR）
- **AI server / RAG server** — Uvicorn `--reload`
- **BE server** — `dotnet watch`

---

## 專案結構

```
rag_sys/
├── frontend/          # React + Vite + TypeScript + Tailwind
├── be-server/         # .NET 8 Web API（驗證、Notebook、來源、筆記、對話）
├── ai-server/         # Python FastAPI（LLM Gateway、對話、Agent）
├── rag-server/        # Python FastAPI（攝取、分塊、嵌入、搜尋）
├── db/                # MySQL 初始化腳本
├── docs/
│   ├── superpowers/plans/   # Phase 實作計畫（由 Claude Code 撰寫）
│   └── reviews/             # Phase 程式碼審查（由 Claude Code 撰寫）
├── scripts/           # 工具腳本
├── ROADMAP.md         # 功能 Phase 0–7 + 未來待辦
├── ROADMAP2.md        # 強化 Phase 8–15
└── .env.template      # 所有環境變數說明
```

---

## Roadmap 狀態

功能階段請見 [`ROADMAP.md`](./ROADMAP.md)，強化階段請見 [`ROADMAP2.md`](./ROADMAP2.md)。

**Phase 0–7：** 已完成  
**聊天對話 sessions：** 已在 `main` 完成  
**Phase 8–15：** 已定義；下一步建議先做 Phase 8 auth/session hardening

---

## 未來待辦

- GraphRAG：以 ArangoDB 圖遍歷實現實體關係推理
- vLLM / 本地模型串接至 LLM Gateway
- 完整的多用戶管理 UI
- 組織 / 團隊共享模型
- Podcast 風格的音訊摘要
- 文件遮蔽與 PII 偵測
- RAG 實驗用評估資料集建構工具
- 管理員儀表板（攝取任務、失敗請求、使用量）
