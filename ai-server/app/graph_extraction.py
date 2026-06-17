import logging

from app.gateway.base import GatewayError, LLMGateway

logger = logging.getLogger(__name__)

# Phase 19 Gate B: the only module in ai-server allowed to perform LLM-based
# entity/fact extraction. rag-server stays deterministic; see
# docs/superpowers/plans/phase-19-graphrag-foundations.md.
_SCHEMA = {
    "name": "GraphExtraction",
    "schema": {
        "type": "object",
        "properties": {
            "mentions": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "entity_name": {"type": "string"},
                        "entity_type": {"type": "string"},
                    },
                    "required": ["entity_name", "entity_type"],
                    "additionalProperties": False,
                },
            },
            "facts": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "predicate": {"type": "string"},
                        "statement_text": {"type": "string"},
                        "confidence": {"type": "number"},
                        "participants": {
                            "type": "array",
                            "items": {
                                "type": "object",
                                "properties": {
                                    "entity_name": {"type": "string"},
                                    "role": {"type": "string"},
                                },
                                "required": ["entity_name", "role"],
                                "additionalProperties": False,
                            },
                        },
                    },
                    "required": ["predicate", "statement_text", "confidence", "participants"],
                    "additionalProperties": False,
                },
            },
        },
        "required": ["mentions", "facts"],
        "additionalProperties": False,
    },
}

_SYSTEM_PROMPT = (
    "Extract named entities and factual relationships from the given text chunk. "
    "Mentions are entities referenced in the text (people, organizations, concepts, "
    "products, etc). Facts are statements connecting two or more mentioned entities, "
    "each with a predicate, a short verbalized statement_text, a confidence between 0 "
    "and 1, and the participating entities with their role in the fact (e.g. subject, "
    "object). Only extract what is explicitly stated in the text; do not infer facts "
    "not present."
)


async def extract_chunk(gateway: LLMGateway, text: str, model: str) -> dict:
    """Extract mentions/facts for a single chunk of text.

    Never raises: a failed extraction degrades to an empty result so one bad
    chunk can't take down a whole ingestion batch. Callers that need to
    distinguish "nothing found" from "extraction failed" should check logs,
    not this return value.
    """
    try:
        result = await gateway.complete_structured(
            messages=[
                {"role": "system", "content": _SYSTEM_PROMPT},
                {"role": "user", "content": text},
            ],
            schema=_SCHEMA,
            model=model,
        )
        return {
            "mentions": result.get("mentions") or [],
            "facts": result.get("facts") or [],
        }
    except GatewayError as exc:
        logger.warning("graph extraction failed for chunk: %s", exc)
        return {"mentions": [], "facts": []}


async def extract_graph(gateway: LLMGateway, chunks: list[dict], model: str) -> list[dict]:
    """Extract mentions/facts for each chunk, preserving chunk_index."""
    results = []
    for chunk in chunks:
        extracted = await extract_chunk(gateway, chunk["text"], model)
        results.append(
            {
                "chunk_index": chunk["chunk_index"],
                "mentions": extracted["mentions"],
                "facts": extracted["facts"],
            }
        )
    return results
