import uuid
from app.embedder import DIMENSIONS


def ensure_vector_index(db) -> None:
    col = db.collection("chunks")
    existing = {idx["type"] for idx in col.indexes()}
    if "vector" not in existing:
        col.add_index({
            "type": "vector",
            "fields": ["embedding"],
            "params": {
                "metric": "cosine",
                "dimension": DIMENSIONS,
                "nLists": 2,
            },
        })


def store_chunks(
    db,
    source_id: str,
    notebook_id: str,
    chunks: list[str],
    embeddings: list[list[float]],
) -> None:
    col = db.collection("chunks")
    docs = [
        {
            "_key": str(uuid.uuid4()).replace("-", ""),
            "source_id": source_id,
            "notebook_id": notebook_id,
            "chunk_index": i,
            "text": chunk,
            "embedding": embedding,
        }
        for i, (chunk, embedding) in enumerate(zip(chunks, embeddings))
    ]
    if docs:
        col.insert_many(docs)


def delete_chunks(db, source_id: str) -> None:
    db.aql.execute(
        "FOR doc IN chunks FILTER doc.source_id == @sid REMOVE doc IN chunks",
        bind_vars={"sid": source_id},
    )


def search_vector(
    db,
    query_embedding: list[float],
    notebook_id: str,
    top_k: int = 5,
) -> list[dict]:
    aql = """
    FOR doc IN chunks
      FILTER doc.notebook_id == @notebook_id
      SORT APPROX_NEAR_COSINE(doc.embedding, @query_vec)
      LIMIT @top_k
      RETURN {
        source_id: doc.source_id,
        chunk_index: doc.chunk_index,
        text: doc.text
      }
    """
    cursor = db.aql.execute(
        aql,
        bind_vars={
            "notebook_id": notebook_id,
            "query_vec": query_embedding,
            "top_k": top_k,
        },
    )
    return list(cursor)
