import time
from collections import defaultdict


class Metrics:
    def __init__(self):
        self.requests = 0
        self.errors = 0
        self.total_duration_ms = 0.0
        self.operations = defaultdict(int)

    def observe_request(self, duration_ms: float, is_error: bool) -> None:
        self.requests += 1
        self.errors += int(is_error)
        self.total_duration_ms += duration_ms

    def increment(self, operation: str) -> None:
        self.operations[operation] += 1

    def snapshot(self) -> dict:
        average = self.total_duration_ms / self.requests if self.requests else 0
        return {
            "requests": {
                "count": self.requests,
                "errors": self.errors,
                "average_duration_ms": round(average, 2),
            },
            "operations": dict(self.operations),
        }


metrics = Metrics()


async def observe_http(request, call_next):
    started = time.perf_counter()
    is_error = False
    try:
        response = await call_next(request)
        is_error = response.status_code >= 500
        return response
    except Exception:
        is_error = True
        raise
    finally:
        metrics.observe_request((time.perf_counter() - started) * 1000, is_error)
