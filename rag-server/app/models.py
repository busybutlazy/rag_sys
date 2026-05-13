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


class BenchmarkResponse(BaseModel):
    query: str
    vector: list[ChunkResult]
    bm25: list[ChunkResult]
    hybrid: list[ChunkResult]


class SourceContentResponse(BaseModel):
    source_id: str
    notebook_id: str
    chunks: list[ChunkResult]
    text: str
