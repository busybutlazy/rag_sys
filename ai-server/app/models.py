from pydantic import BaseModel


class Message(BaseModel):
    role: str   # "system" | "user" | "assistant"
    content: str


class ChatRequest(BaseModel):
    messages: list[Message]
    model: str = "gpt-4o-mini"
    notebook_id: str | None = None  # reserved for Phase 4 RAG context injection
    request_id: str | None = None
    session_id: str | None = None


class AgentRunRequest(BaseModel):
    messages: list[Message]
    model: str = "gpt-4o-mini"
    notebook_id: str | None = None
    request_id: str | None = None
    session_id: str | None = None


class SessionStateUpdateRequest(BaseModel):
    request_id: str
    user_id: str | None = None
    notebook_id: str | None = None
    session_id: str
    prev_session_state: dict | None = None
    user_input: str
    assistant_response: str
