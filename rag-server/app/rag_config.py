import os
from dataclasses import dataclass


@dataclass(frozen=True)
class RagConfig:
    chunk_size: int
    chunk_overlap: int
    embedding_model: str
    embedding_dimensions: int
    search_mode: str
    top_k: int
    hybrid_alpha: float

    def as_dict(self) -> dict:
        return {
            "chunk_size": self.chunk_size,
            "chunk_overlap": self.chunk_overlap,
            "embedding_model": self.embedding_model,
            "embedding_dimensions": self.embedding_dimensions,
            "search_mode": self.search_mode,
            "top_k": self.top_k,
            "hybrid_alpha": self.hybrid_alpha,
        }


def current_config() -> RagConfig:
    return RagConfig(
        chunk_size=int(os.environ.get("RAG_CHUNK_SIZE", "800")),
        chunk_overlap=int(os.environ.get("RAG_CHUNK_OVERLAP", "100")),
        embedding_model=os.environ.get("EMBEDDING_MODEL", "text-embedding-3-small"),
        embedding_dimensions=int(os.environ.get("EMBEDDING_DIMENSIONS", "1536")),
        search_mode=os.environ.get("RAG_SEARCH_MODE", "hybrid"),
        top_k=int(os.environ.get("RAG_TOP_K", "5")),
        hybrid_alpha=float(os.environ.get("RAG_HYBRID_ALPHA", "0.5")),
    )


def validate_config(cfg: RagConfig) -> None:
    if cfg.chunk_overlap >= cfg.chunk_size:
        raise SystemExit(
            f"RAG_CHUNK_OVERLAP ({cfg.chunk_overlap}) must be less than "
            f"RAG_CHUNK_SIZE ({cfg.chunk_size})"
        )
    if not (0.0 <= cfg.hybrid_alpha <= 1.0):
        raise SystemExit(
            f"RAG_HYBRID_ALPHA ({cfg.hybrid_alpha}) must be between 0.0 and 1.0"
        )
    if cfg.top_k < 1:
        raise SystemExit(f"RAG_TOP_K ({cfg.top_k}) must be at least 1")
