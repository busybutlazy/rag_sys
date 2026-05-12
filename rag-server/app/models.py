from pydantic import BaseModel


class IngestRequest(BaseModel):
    source_id: str
    file_path: str
    mime_type: str
