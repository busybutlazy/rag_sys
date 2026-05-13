from pydantic import BaseModel


class Message(BaseModel):
    role: str   # "system" | "user" | "assistant"
    content: str


class ChatRequest(BaseModel):
    messages: list[Message]
    model: str = "gpt-4o-mini"
    notebook_id: str | None = None  # reserved for Phase 4 RAG context injection


class AgentRunRequest(BaseModel):
    messages: list[Message]
    model: str = "gpt-4o-mini"
    notebook_id: str | None = None
