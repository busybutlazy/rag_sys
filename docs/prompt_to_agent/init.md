在這個專案中 我要建造一個rag server with arango db 和一個ai server  和 一個be server with sql db and front end
目標是練習rag 流程 , agent 流程 以及 graph rag複習 (未來會針對不同配置 進行實驗 測試效能)
[1] 專案以docker 為基底 使用docker compose 建立同一個內網 所有server 僅開放 front end 的前端port 5987 其餘的都在內網通訊 (因為都在內網 port 就用常見的8000 8001
3306等)
[2] 大致上的想法是 做出一個類似open notebook 的 介面 及 存儲 問答系統 與open notebook 不同的是 我希望能強化 ai agent的部分 以及未來要擴充 graphic rag [至於怎麼加強ai
agent 我們可以之後討論 graphic先不作 我們先專注在 vector search and sparse search (bm25)]
(開源專案位置 /Users/busybutlazy/gitproject/open-notebook 這個僅供參考 可擷取其優點進行摹寫) 
[3] 我希望你能先擬定計畫(ROADMAP.md) 分成不同階段(phase) 不需要規劃時程 我們會照著這個ROADMAP.md 逐步修正與完成 每個phase都會先 (1)擬定計畫 (2)開git branch
(3)逐步完成計畫 (4)開pr merge 回 main (5)開一個code review agent撰寫phase review報告 根據安全,效能,可維護性,邏輯 進行評估 (6)開phase patch branch (7)開pr merge
回main (8)更新ROADMAP.md
[4] llm 先使用 openai 我希望能撰寫 llm gateway 因為未來我可能會串接 vllm 或是其他供應商的llm
[5] 此專案雖為個人使用 但front end 需要有登入機制 帳號密碼 我會寫在.env 至於 .env.template 給你撰寫 我之後會照抄一份到.env 並填上真實 apikey 以及帳號密碼
並且未來可能會擴充為多用戶機制 (1) arango db可以一用戶一db (2) sql db則是多用戶一db 並建制users table 存放帳號密碼 且其餘表格需要關聯至users table
[6] db 的 id 部分 盡量使用uuid 長度 8 16 32 64 根據 你的經驗判斷
[7] 前端使用react .net 後端,ai,rag接使用 python fastapi
你現在的目標是 先擬定一個計畫 分析並撰寫
