from pydantic import BaseModel, Field


class RetrievalConfig(BaseModel):
    retrieval_version_id: str
    chunk_size: int
    chunk_overlap: int
    embedding_model: str
    embedding_dimensions: int
    search_mode: str
    top_k: int
    hybrid_alpha: float


class IngestRequest(BaseModel):
    source_id: str
    notebook_id: str
    user_id: str
    file_path: str
    mime_type: str
    retrieval: RetrievalConfig | None = None


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
    truncated: bool


class ExperimentConfig(BaseModel):
    modes: list[str] = Field(default_factory=lambda: ["vector", "bm25", "hybrid"])
    top_k: int = 5
    alpha: float = 0.5


class ExperimentRunRequest(BaseModel):
    notebook_id: str
    user_id: str
    name: str | None = None
    queries: list[str]
    config: ExperimentConfig = Field(default_factory=ExperimentConfig)


class ExperimentResultItem(BaseModel):
    source_id: str
    chunk_index: int


class ExperimentQueryResult(BaseModel):
    query: str
    mode: str
    latency_ms: int
    result_count: int
    results: list[ExperimentResultItem]


class ExperimentRecord(BaseModel):
    id: str
    notebook_id: str
    name: str
    config: ExperimentConfig
    queries: list[str]
    results: list[ExperimentQueryResult]
    created_at: str
