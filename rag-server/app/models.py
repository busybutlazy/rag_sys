from pydantic import BaseModel


class IngestRequest(BaseModel):
    source_id: str
    notebook_id: str
    file_path: str
    mime_type: str


class ChunkResult(BaseModel):
    source_id: str
    chunk_index: int
    text: str


class SearchResponse(BaseModel):
    results: list[ChunkResult]
