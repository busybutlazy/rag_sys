from fastapi import FastAPI

app = FastAPI(title="AI Server", version="0.1.0")


@app.get("/health")
async def health():
    return {"status": "ok", "service": "ai-server"}
